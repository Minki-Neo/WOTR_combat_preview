using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.Enums.Damage;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace CombatPreview
{
    internal static class BetaIdentity
    {
        sealed class Token { internal long Id; }
        static readonly ConditionalWeakTable<object, Token> ids = new ConditionalWeakTable<object, Token>();
        static long next;
        internal static string Id(object value)
        { return value == null ? "none" : ids.GetValue(value, v => new Token { Id = Interlocked.Increment(ref next) }).Id.ToString(CultureInfo.InvariantCulture); }
    }
    internal static class BetaReader
    {
        internal const string KnownMvid = "90a9869c-2792-4c7b-bfb7-5a8b33da7c82";
        internal static bool Supported { get { return typeof(RuleAttackRoll).Module.ModuleVersionId.ToString() == KnownMvid; } }
        static int Cached(ModifiableValue value)
        {
            if (value == null) throw new NotSupportedException("저장된 능력치가 없습니다.");
            var field = AccessTools.Field(typeof(ModifiableValue), "<ModifiedValue>k__BackingField");
            if (field == null) throw new NotSupportedException("능력치 필드 구성이 다릅니다.");
            return (int)field.GetValue(value);
        }
        static string Number(int value) { return value.ToString(CultureInfo.InvariantCulture); }
        internal static string HealthLabel(UnitEntityData target)
        {
            if (!Supported || target?.Stats == null) return "대상 체력: 확인 불가";
            try
            {
                int maximum = Cached(target.Stats.HitPoints);
                if (maximum <= 0) return "대상 체력: 확인 불가";
                long current = Math.Max(0L, (long)maximum - target.Damage);
                int temporary = Cached(target.Stats.TemporaryHitPoints);
                return string.Format("대상 체력: {0} / {1} HP{2}", current, maximum,
                    temporary > 0 ? string.Format("  |  임시 HP: {0}", temporary) : "");
            }
            catch (NotSupportedException) { return "대상 체력: 확인 불가"; }
        }
        static int Ability(UnitEntityData unit, string stat)
        {
            switch (stat)
            {
                case "Strength": return Cached(unit.Stats.Strength);
                case "Dexterity": return Cached(unit.Stats.Dexterity);
                case "Constitution": return Cached(unit.Stats.Constitution);
                case "Intelligence": return Cached(unit.Stats.Intelligence);
                case "Wisdom": return Cached(unit.Stats.Wisdom);
                case "Charisma": return Cached(unit.Stats.Charisma);
                default: throw new NotSupportedException("명중 능력치를 판독할 수 없습니다.");
            }
        }
        static string State(UnitEntityData unit)
        {
            var p = unit.Position;
            return string.Join(",", new[] { p.x.ToString("R", CultureInfo.InvariantCulture), p.y.ToString("R", CultureInfo.InvariantCulture),
                p.z.ToString("R", CultureInfo.InvariantCulture), Number(unit.Damage),
                string.Join(";", unit.Facts.List.Select(f => BetaIdentity.Id(f) + ":" + f.Active).OrderBy(s => s).ToArray()) });
        }
        internal static ItemEntityWeapon Primary(UnitEntityData unit)
        {
            // Do not call MaybeWeapon/EmptyHandWeapon (unarmed creation) or
            // EntityRef.Entity (proxy cache refresh). Read the existing slot's
            // cached reference/proxy only; unavailable data stays unavailable.
            var slot = unit?.Body?.PrimaryHand;
            if (slot == null) return null;
            if (AccessTools.Field(slot.GetType(), "m_IsItemFake")?.GetValue(slot) as bool? == true) return null;
            var reference = AccessTools.Field(slot.GetType(), "m_ItemRef")?.GetValue(slot);
            var proxy = reference == null ? null : AccessTools.Field(reference.GetType(), "m_Proxy")?.GetValue(reference);
            var item = proxy == null ? null : AccessTools.Field(proxy.GetType(), "<Entity>k__BackingField")?.GetValue(proxy) as ItemEntityWeapon;
            if (item != null && AccessTools.Field(item.GetType(), "<RemoveFromSlotInProgress>k__BackingField")?.GetValue(item) as bool? == true) return null;
            return item;
        }
        internal static BetaInput Capture(UnitEntityData unit, UnitEntityData target, bool full)
        {
            if (!Supported) throw new NotSupportedException("이 게임 DLL은 읽기 전용 베타 판독이 검증되지 않았습니다.");
            var weapon = Primary(unit);
            if (unit == null || target == null || weapon == null) throw new NotSupportedException("장착된 주무기가 필요합니다.");
            var bp = weapon.Blueprint;
            var typeReference = AccessTools.Field(bp.GetType(), "m_Type")?.GetValue(bp);
            if (typeReference == null || AccessTools.Field(typeReference.GetType(), "<Cached>k__BackingField")?.GetValue(typeReference) == null)
                throw new NotSupportedException("무기 종류 데이터가 아직 로드되지 않았습니다. 게임의 장비 화면을 연 뒤 다시 확인하세요.");
            if (bp.IsNatural || bp.IsUnarmed || bp.Double) throw new NotSupportedException("이번 베타는 주무기 일반 무기만 지원합니다. 자연/비무장/이중 무기는 미지원입니다.");
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            if (turn == null) throw new NotSupportedException("턴제 전투에서 사용할 수 있습니다.");
            string stat = bp.AttackBonusStat.ToString();
            if (stat == "Unknown") stat = bp.IsRanged ? "Dexterity" : "Strength";
            int ability = (int)Math.Floor((Ability(unit, stat) - 10) / 2.0);
            int bab = Cached(unit.Stats.BaseAttackBonus), extra = Cached(unit.Stats.AdditionalAttackBonus);
            int ac = Cached(target.Stats.AC), strength = (int)Math.Floor((Cached(unit.Stats.Strength) - 10) / 2.0);
            int damageExtra = Cached(unit.Stats.AdditionalDamage);
            int bonus = damageExtra + (bp.IsRanged ? 0 : (bp.IsTwoHanded && strength > 0 ? strength * 3 / 2 : strength));
            var dice = bp.BaseDamage;
            int rolls = dice.Rolls, sides = rolls == 0 ? 1 : dice.Dice.Sides();
            bool known = bp.DamageType != null && bp.DamageType.Type.ToString() == "Physical";
            bool defensePart = target.Get<UnitPartDamageReduction>() != null;
            string actor = BetaIdentity.Id(unit), victim = BetaIdentity.Id(target), item = BetaIdentity.Id(weapon), turnId = BetaIdentity.Id(turn);
            string values = string.Join("|", new[] { actor, victim, item, turnId, full.ToString(), Number(bab), Number(ability), Number(extra),
                Number(ac), Number(rolls), Number(sides), Number(bonus), Number(bp.CriticalRollEdge), Number(bp.CriticalModifier.IntValue()),
                State(unit), State(target), defensePart.ToString() });
            string signature;
            using (var sha = SHA256.Create()) signature = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(values))).Replace("-", "");
            return new BetaInput(actor, victim, item, turnId, signature, bab, checked(bab + ability + extra), ac,
                rolls, sides, bonus, bp.CriticalRollEdge, bp.CriticalModifier.IntValue(), full, known, defensePart);
        }
    }
    internal static class BetaRuntime
    {
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly BetaTracker tracker = new BetaTracker();
        static readonly ObservedPreviewCache observed = new ObservedPreviewCache();
        static bool observedFaulted;
        static BetaSnapshot built;
        static long sequence;
        static int mainThread;
        internal static string Alert;
        static long alertUntil, lastAlert = -10000;
        internal static long DifferenceCount;
        internal static bool Enabled { get { return Main.Settings != null && Main.Settings.Enabled && Main.Settings.ShowOnHover && Compatibility.CanPreview; } }
        internal static void Initialize() { mainThread = Thread.CurrentThread.ManagedThreadId; }
        internal static BetaSnapshot Capture(UnitEntityData actor, UnitEntityData target, bool full)
        {
            var input = CaptureInput(actor, target, full);
            if (built == null || built.Input.Signature != input.Signature)
                built = BetaModel.Build(input, "beta-" + (++sequence).ToString(CultureInfo.InvariantCulture));
            return built;
        }
        static BetaInput CaptureInput(UnitEntityData actor, UnitEntityData target, bool full)
        {
            var input = BetaReader.Capture(actor, target, full);
            return Main.Settings.UseObservedPreview && !observedFaulted ? observed.Read(input, clock.ElapsedMilliseconds) : input;
        }
        internal static void ObserveGameEstimate(RuleAttackRoll evt)
        {
            if (!Enabled || !Main.Settings.UseObservedPreview || observedFaulted || evt == null
                || Thread.CurrentThread.ManagedThreadId != mainThread) return;
            try
            {
                if (!evt.IsFake || evt.AttackBonusPenalty != 0 || evt.Initiator == null || evt.Target == null
                    || evt.Initiator != TurnBased.Controllers.CombatController.SelectedUnit
                    || evt.Weapon == null || evt.Weapon != BetaReader.Primary(evt.Initiator)) return;
                var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
                if (turn == null) return;
                var input = BetaReader.Capture(evt.Initiator, evt.Target, turn.GetEnabledFullAttack(evt.Initiator));
                var descriptions = evt.WeaponStats?.DamageDescription;
                if (descriptions == null || descriptions.Count == 0 || descriptions.Count > 32) return;
                var parts = new List<DamageComponentSnapshot>();
                bool known = true;
                foreach (var d in descriptions)
                {
                    if (d == null || d.EvaluatedBonus != null) return; // Never execute an evaluator.
                    var dice = d.ModifiedDiceFormula;
                    var kind = DefenseReader.Classify(d.TypeDescription);
                    known &= kind != DamageKind.Unknown;
                    parts.Add(new DamageComponentSnapshot(new DamageTerm(dice.Rolls,
                        dice.Rolls == 0 ? 1 : dice.Dice.Sides(), d.Bonus, parts.Count == 0) { Kind = kind }, false, null));
                }
                var first = parts[0];
                string signature = input.Signature + "|observed:" + evt.AttackBonus + ":" + evt.TargetAC + ":"
                    + string.Join(";", parts.Select(p => p.Kind + ":" + p.Rolls + ":" + p.Sides + ":" + p.Bonus).ToArray());
                var copy = new BetaInput(input.Actor, input.Target, input.Weapon, input.Turn, signature,
                    input.BAB, evt.AttackBonus, evt.TargetAC, first.Rolls, first.Sides, first.Bonus,
                    input.CriticalEdge, input.Multiplier, input.FullAttack, known, input.DefensePartPresent,
                    parts, "observed-existing-estimate/basic-primary-iteratives");
                observed.Store(input, copy, clock.ElapsedMilliseconds);
            }
            catch (NotSupportedException) { return; }
            catch (Exception e)
            {
                observed.Clear(); observedFaulted = true;
                Main.Error("Observed game estimate unavailable; using basic model", e);
            }
        }
        internal static void Shown(BetaSnapshot snapshot)
        {
            if (!Enabled) return;
            tracker.Shown(snapshot, clock.ElapsedMilliseconds);
            Main.Diagnostics?.PreviewShown(snapshot);
        }
        internal static void Invalidate() { tracker.Invalidate(); observed.Clear(); built = null; }
        internal static bool AlertVisible { get { return Enabled && Main.Settings.BetaAlerts && clock.ElapsedMilliseconds < alertUntil; } }
        internal static void Before(RuleAttackWithWeapon evt)
        {
            if (!Enabled || !Main.Settings.CompareBeta || Thread.CurrentThread.ManagedThreadId != mainThread || evt == null) return;
            if (!tracker.HasPair(BetaIdentity.Id(evt.Initiator), BetaIdentity.Id(evt.Target))) return;
            try
            {
                var input = CaptureInput(evt.Initiator, evt.Target, evt.IsFullAttack);
                tracker.Begin(BetaIdentity.Id(evt), input, evt.AttackNumber, evt.IsFullAttack, clock.ElapsedMilliseconds, BetaIdentity.Id(evt.Weapon));
            }
            catch (Exception e) { Main.Error("Beta comparison input capture", e); tracker.Cancel(BetaIdentity.Id(evt)); }
        }
        internal static BetaComparison After(RuleAttackRoll evt)
        {
            if (!Enabled || !Main.Settings.CompareBeta || Thread.CurrentThread.ManagedThreadId != mainThread || evt?.RuleAttackWithWeapon == null || evt.IsFake) return null;
            var damage = evt.WeaponStats?.DamageDescription;
            var components = damage == null || damage.Count > 32 || damage.Any(d => d == null || d.EvaluatedBonus != null)
                ? null : damage.Select((d, i) => {
                    var formula = d.ModifiedDiceFormula;
                    return new DamageComponentSnapshot(new DamageTerm(formula.Rolls, formula.Rolls == 0 ? 1 : formula.Dice.Sides(),
                        d.Bonus, i == 0) { Kind = DefenseReader.Classify(d.TypeDescription) }, false, null);
                }).ToArray();
            int? rolls = null, sides = null, bonus = null;
            if (damage != null && damage.Count > 0)
            {
                var dice = damage[0].ModifiedDiceFormula;
                rolls = dice.Rolls; sides = dice.Rolls == 0 ? 1 : dice.Dice.Sides(); bonus = damage[0].Bonus;
            }
            var result = tracker.Finish(BetaIdentity.Id(evt.RuleAttackWithWeapon), BetaIdentity.Id(evt.Initiator), BetaIdentity.Id(evt.Target),
                BetaIdentity.Id(evt.Weapon), GameRules.Die(evt) > 0, evt.AttackBonus, evt.TargetAC, rolls, sides, bonus,
                clock.ElapsedMilliseconds, damage == null ? -1 : damage.Count, components);
            if (result != null && result.HasDifference)
            {
                DifferenceCount++;
                long now = clock.ElapsedMilliseconds;
                if (now - lastAlert >= 3000)
                {
                    Alert = "베타 모델 차이: " + string.Join(", ", result.Differences.ToArray()) + " (미반영 효과 가능; 로그 기록)";
                    alertUntil = now + 7000; lastAlert = now;
                }
            }
            return result;
        }
        internal static void End(RuleAttackWithWeapon evt)
        { if (evt != null && Enabled) tracker.Cancel(BetaIdentity.Id(evt)); }
    }
    // Runs at the beginning of the REAL attack's before-event phase. No new rule
    // is created, triggered, copied or cleaned up by this hook.
    internal sealed class BetaAttackHook : IRulebookEventAboutToTriggerHook, IGlobalRulebookHandler<RuleAttackWithWeapon>
    {
        public void OnBeforeEventAboutToTrigger(RulebookEvent evt) { BetaRuntime.Before(evt as RuleAttackWithWeapon); }
        public void OnEventAboutToTrigger(RuleAttackWithWeapon evt) { }
        public void OnEventDidTrigger(RuleAttackWithWeapon evt) { BetaRuntime.End(evt); }
    }
}
