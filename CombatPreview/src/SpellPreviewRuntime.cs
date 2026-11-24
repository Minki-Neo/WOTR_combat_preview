using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace CombatPreview
{
    // Observe game-owned calculations/targeting. Never call CalculateParams,
    // WouldCurrentlyTarget, action evaluators or Rulebook.Trigger ourselves.
    internal static class SpellPreviewRuntime
    {
        internal static AbilityData SelectedAbility => Game.Instance?.TurnBasedCombatController?.CurrentTurn?.CurrentAbility
            ?? Game.Instance?.SelectedAbilityHandler?.SelectedAbility;
        static string lastPanelState;
        internal static void PanelState(AbilityData ability, UnitEntityData target, string state)
        {
            if (Main.Settings == null || !(Main.Settings.CompareBeta || Main.Settings.LocalDiagnostics)) return;
            string key = BetaIdentity.Id(ability) + "|" + BetaIdentity.Id(target) + "|" + state;
            if (key == lastPanelState) return;
            lastPanelState = key;
            Main.Log?.Log("Spell panel: " + ability.Blueprint.name + " / " + state
                + " / target=" + (target?.CharacterName ?? "none") + " / selection="
                + (Game.Instance?.TurnBasedCombatController?.CurrentTurn?.CurrentAbility != null ? "turn-current-ability" : "selected-ability-handler"));
        }
        sealed class Parameters { internal int DC, Level, RankBonus; internal Metamagic Metamagic; internal string Stamp, Origin; internal float Time; }
        static readonly Dictionary<AbilityData, Parameters> parameters = new Dictionary<AbilityData, Parameters>();
        static readonly Dictionary<AbilityData, long> revisions = new Dictionary<AbilityData, long>();
        internal static long Revision(AbilityData ability) => ability != null && revisions.TryGetValue(ability, out long value) ? value : 0;
        static void Advance(AbilityData ability)
        { if (revisions.Count >= 64 && !revisions.ContainsKey(ability)) revisions.Clear(); revisions[ability] = Revision(ability) + 1; }
        sealed class AttackEstimate { internal int AB, AC; internal bool AutoHit; internal float Time; internal string Stamp; }
        static readonly Dictionary<string, AttackEstimate> attacks = new Dictionary<string, AttackEstimate>();
        internal static void ObserveAttackEstimate(Kingmaker.RuleSystem.Rules.RuleAttackRoll evt)
        {
            var ability = SelectedAbility;
            if (OverlayReference.Active || !BetaRuntime.Enabled || ability == null || evt == null || !evt.IsFake || evt.Initiator == null || evt.Target == null
                || (evt.AttackType != Kingmaker.RuleSystem.AttackType.Touch && evt.AttackType != Kingmaker.RuleSystem.AttackType.RangedTouch)) return;
            using (PreviewPerformance.Measure("spell-game-estimate"))
            using (ScopedReadMemo<UnitEntityData, string>.Begin())
            try
            {
                var source = PassiveAttackDiagnostics.Source(evt);
                if (!ReferenceEquals(source.Ability, ability) || Caster(ability) != evt.Initiator
                    || !evt.AutoHit && GameRules.Die(evt) <= 0) return;
                if (attacks.Count > 64) attacks.Clear();
                string key = BetaIdentity.Id(ability) + "|" + BetaIdentity.Id(evt.Target) + "|" + evt.AttackType;
                string stamp = ParameterStamp(ability, evt.Initiator) + "|" + BetaReader.PairStamp(evt.Initiator, evt.Target);
                if (!attacks.TryGetValue(key, out var previous) || previous.AB != evt.AttackBonus || previous.AC != evt.TargetAC
                    || previous.AutoHit != evt.AutoHit || previous.Stamp != stamp) Advance(ability);
                attacks[key] = new AttackEstimate {
                    AB = evt.AttackBonus, AC = evt.TargetAC, AutoHit = evt.AutoHit, Time = Time.unscaledTime,
                    Stamp = stamp
                };
            }
            catch (Exception e) { Main.Error("Spell attack estimate unavailable", e); }
        }
        static AttackEstimate ObservedAttack(AbilityData ability, UnitEntityData caster, UnitEntityData target, bool ranged)
        {
            AttackEstimate found;
            if (!attacks.TryGetValue(BetaIdentity.Id(ability) + "|" + BetaIdentity.Id(target) + "|" + (ranged ? "RangedTouch" : "Touch"), out found)
                || Time.unscaledTime < found.Time || Time.unscaledTime - found.Time > .25f) return null;
            return found.Stamp == ParameterStamp(ability, caster) + "|" + BetaReader.PairStamp(caster, target) ? found : null;
        }
        static readonly SpellTargetWindow<UnitEntityData> affected = new SpellTargetWindow<UnitEntityData>();
        static readonly Dictionary<AbilityData, long> targetExclusions = new Dictionary<AbilityData, long>();
        internal static long TargetExclusions(AbilityData ability) => targetExclusions.TryGetValue(ability, out var count) ? count : 0;
        internal static UnitEntityData Caster(AbilityData ability)
        {
            var reference = CombatFields.Read(ability, "m_CasterRef");
            var proxy = CombatFields.Read(reference, "m_Proxy");
            return CombatFields.Read(proxy, "<Entity>k__BackingField") as UnitEntityData;
        }
        static string ParameterStamp(AbilityData ability, UnitEntityData caster)
        {
            if (ScopedReadMemo<AbilityData, string>.TryGet(ability, out var cached)) return cached;
            var meta = CombatFields.Read(ability, "<MetamagicData>k__BackingField");
            string stamp = SpellParameterReader.DependencyStamp(ability, caster) + "|" + ability.IgnoreDCBonuses + "|" + string.Join(":", new[] {
                CombatFields.Read(ability, "<OverrideDC>k__BackingField"), CombatFields.Read(ability, "m_OverrideSpellLevel"),
                CombatFields.Read(ability, "<OverrideCasterLevel>k__BackingField"), CombatFields.Read(ability, "<OverrideCasterModifier>k__BackingField"),
                CombatFields.Read(meta, "<MetamagicMask>k__BackingField"), CombatFields.Read(meta, "<SpellLevelCost>k__BackingField"),
                CombatFields.Read(meta, "<HeightenLevel>k__BackingField") }.Select(value => value?.ToString() ?? "none"));
            ScopedReadMemo<AbilityData, string>.Put(ability, stamp); return stamp;
        }
        internal static string InputStamp(AbilityData ability, UnitEntityData caster, UnitEntityData target)
            => ParameterStamp(ability, caster) + "|" + BetaReader.PairStamp(caster, target)
                + "|target-stats:" + Cached(target.Stats.AC) + ":" + Cached(target.Stats.SaveFortitude)
                + ":" + Cached(target.Stats.SaveReflex) + ":" + Cached(target.Stats.SaveWill)
                + "|caster-attack:" + Cached(caster.Stats.AdditionalAttackBonus);
        sealed class ObservedRankValues
        {
            internal int[] Values;
            internal string Stamp, Origin;
            internal float Time;
        }
        static readonly Dictionary<string, ObservedRankValues> observedRanks = new Dictionary<string, ObservedRankValues>();

        internal static void ObserveRanks(Kingmaker.UnitLogic.Mechanics.MechanicsContext __instance)
        {
            if (OverlayReference.Active || !BetaRuntime.Enabled || !(__instance is AbilityExecutionContext execution)) return;
            try
            {
                var ability = execution.Ability;
                if (ability == null || SelectedAbility != ability
                    || ability.MagicHackData != null || __instance.AssociatedBlueprint != ability.Blueprint) return;
                var target = __instance.MainTarget?.Unit;
                var caster = Caster(ability);
                if (target == null || caster == null
                    || !(CombatFields.Read(__instance, "m_Ranks") is int[] values)
                    || values.Length == 0 || values.Length > 64) return;
                using (ScopedReadMemo<UnitEntityData, string>.Begin())
                {
                    if (observedRanks.Count >= 64) observedRanks.Clear();
                    string key = BetaIdentity.Id(ability) + "|" + BetaIdentity.Id(target);
                    string stamp = ParameterStamp(ability, caster) + "|" + BetaReader.PairStamp(caster, target);
                    if (!observedRanks.TryGetValue(key, out var previous) || previous.Stamp != stamp || !previous.Values.SequenceEqual(values)) Advance(ability);
                    observedRanks[key] = new ObservedRankValues
                    {
                        Values = (int[])values.Clone(), Time = Time.unscaledTime,
                        Stamp = stamp,
                        Origin = "existing-game-context"
                    };
                }
            }
            catch (Exception e) { Main.Error("Spell rank observer unavailable", e); }
        }

        internal static void Clear() { parameters.Clear(); revisions.Clear(); observedRanks.Clear(); affected.Clear(); targetExclusions.Clear(); attacks.Clear(); lastComponents = null; lastScan = null; lastPanelState = null; SpellDamagePreview.Clear(); }
        internal static void ObserveParameters(AbilityData __instance, AbilityParams __result)
        {
            if (OverlayReference.Active || !BetaRuntime.Enabled || __result == null) return;
            try
            {
                var caster = Caster(__instance); if (caster == null) return;
                if (parameters.Count >= 64 && !parameters.ContainsKey(__instance)) parameters.Clear();
                string stamp = ParameterStamp(__instance, caster);
                if (!parameters.TryGetValue(__instance, out var previous) || previous.DC != __result.DC || previous.Level != __result.CasterLevel
                    || previous.RankBonus != __result.RankBonus || previous.Metamagic != __result.Metamagic || previous.Stamp != stamp) Advance(__instance);
                parameters[__instance] = new Parameters { DC = __result.DC, Level = __result.CasterLevel, RankBonus = __result.RankBonus, Metamagic = __result.Metamagic,
                    Stamp = stamp, Origin = "game-calculation", Time = Time.unscaledTime };
            }
            catch (Exception e) { Main.Error("Spell parameter observer", e); }
        }
        internal static void ObserveTarget(AbilityData __instance, Vector3 __0, UnitEntityData __1, bool __result)
        {
            if (OverlayReference.Active || !BetaRuntime.Enabled || SelectedAbility != __instance || __1 == null) return;
            bool eligible = __result;
            if (eligible)
            {
                var caster = Caster(__instance);
                if (caster != null) eligible = new SpellTargetFilter(__instance, caster).Allows(__1);
            }
            if (__result && !eligible)
            {
                if (targetExclusions.Count >= 64 && !targetExclusions.ContainsKey(__instance)) targetExclusions.Clear();
                targetExclusions[__instance] = TargetExclusions(__instance) + 1;
            }
            affected.Observe(__instance, __0, __1, eligible, Time.unscaledTime);
        }
        internal static UnitEntityData[] Targets(AbilityData ability)
        {
            return affected.Read(ability, Time.unscaledTime).Where(unit => unit.IsVisibleForPlayer && !unit.Descriptor.State.IsDead).ToArray();
        }

        static int Cached(ModifiableValue value)
        { var v = CombatFields.CachedStat(value); if (!v.HasValue) throw new NotSupportedException("능력치 캐시 미확인"); return v.Value; }
        internal static List<string> Describe(AbilityData ability, UnitEntityData target)
        { List<SpellCheckRecord> checks; Dictionary<string, object> metadata; return Describe(ability, target, out checks, out metadata); }
        static SpellCheckRecord Check(string kind, string type, string status, string reason = null, double? probability = null, int? bonus = null, int? threshold = null)
        { return new SpellCheckRecord { Kind = kind, Type = type, Status = status, Reason = reason, Probability = probability, Bonus = bonus, Threshold = threshold, Source = "cached-basic-partial-model" }; }
        internal static List<string> Describe(AbilityData ability, UnitEntityData target, out List<SpellCheckRecord> checks, out Dictionary<string, object> metadata, bool cursorTarget = false)
        {
            checks = new List<SpellCheckRecord>(); metadata = new Dictionary<string, object>();
            var rows = new List<string>(); var caster = Caster(ability);
            if (caster == null) { rows.Add("시전자 정보 미확인"); return rows; }
            Parameters p;
            bool current = parameters.TryGetValue(ability, out p) && Time.unscaledTime >= p.Time
                && p.Stamp == ParameterStamp(ability, caster);
            metadata["blueprintId"] = ability.Blueprint.AssetGuid.ToString();
            if (Main.Settings.CompareBeta || Main.Settings.LocalDiagnostics)
                metadata["negativeLevelSources"] = SpellParameterReader.NegativeLevelSources(caster);
            metadata["parameterStatus"] = current ? "Current" : p == null ? "NotObserved" : Time.unscaledTime < p.Time ? "ClockRegression" : "StateChanged";
            metadata["parameterSource"] = p == null ? null : p.Origin;
            metadata["parameterAgeMs"] = p == null ? null : (object)((Time.unscaledTime - p.Time) * 1000);
            metadata["dc"] = current ? (object)p.DC : null; metadata["casterLevel"] = current ? (object)p.Level : null;
            var meta = CombatFields.Read(ability, "<MetamagicData>k__BackingField");
            var mask = CombatFields.Read(meta, "<MetamagicMask>k__BackingField");
            Metamagic? selectedMeta = meta == null ? (Metamagic)0 : mask is Metamagic ? (Metamagic?)mask : null;
            if (ItemSpellParameterReader.Item(ability) != null)
            {
                bool itemReady = ItemSpellParameterReader.TryRead(ability, caster, out int itemDc, out int itemLevel,
                    out Metamagic itemMeta, out var itemEvidence, out var itemReason);
                metadata["itemParameters"] = itemEvidence;
                metadata["itemParameterStatus"] = itemReady ? "ReadOnlyCalculated" : itemReason;
                if (!current && itemReady)
                {
                    current = true; selectedMeta = itemMeta;
                    p = new Parameters { DC = itemDc, Level = itemLevel, RankBonus = 0, Metamagic = itemMeta,
                        Origin = "readonly-item-parameters", Time = Time.unscaledTime };
                    metadata["parameterStatus"] = "ReadOnlyCalculated"; metadata["parameterSource"] = p.Origin;
                    metadata["dc"] = itemDc; metadata["parameterAgeMs"] = 0;
                }
            }
            if (selectedMeta.HasValue && ItemSpellParameterReader.Item(ability) == null)
            {
                bool autoKnown = AutoMetamagicReader.Resolve(ability, caster, selectedMeta.Value, out var resolvedAuto,
                    out var autoSources, out var autoReason);
                metadata["automaticMetamagicSources"] = autoSources;
                metadata["automaticMetamagicStatus"] = autoKnown ? "Resolved" : autoReason;
                selectedMeta = autoKnown ? (Metamagic?)resolvedAuto : null;
            }
            if (selectedMeta.HasValue && ItemSpellParameterReader.Item(ability) == null)
            {
                bool rodsKnown = RodMetamagicReader.Resolve(ability, caster, selectedMeta.Value, out var resolvedMeta,
                    out bool ignoreSr, out var rodSources, out var rodReason);
                metadata["rodMetamagic"] = rodSources; metadata["rodMetamagicStatus"] = rodsKnown ? "Resolved" : rodReason;
                metadata["rodIgnoreSpellResistance"] = ignoreSr;
                selectedMeta = rodsKnown ? (Metamagic?)resolvedMeta : null;
            }
            if (current && selectedMeta.HasValue && (Main.Settings.LocalDiagnostics || Main.Settings.CompareBeta))
            {
                bool available = SpellParameterReader.TryRead(ability, caster, selectedMeta.Value, out int ownDc, out int ownLevel, out int ownRankBonus, out string reason);
                metadata["readonlyParameterComparison"] = new Dictionary<string, object> {
                    { "available", available }, { "reason", reason },
                    { "source", available ? SpellParameterReader.Source(ability) : null },
                    { "dcDelta", available ? (object)((long)ownDc - p.DC) : null },
                    { "casterLevelDelta", available ? (object)((long)ownLevel - p.Level) : null },
                    { "rankBonusDelta", available ? (object)((long)ownRankBonus - p.RankBonus) : null },
                    { "scope", "independent readonly model minus observed game parameters at tracked state" }
                };
            }
            if (!current && selectedMeta.HasValue)
            {
                if (SpellParameterReader.TryRead(ability, caster, selectedMeta.Value, out int dc, out int level, out int rankBonus, out string reason))
                {
                    current = true;
                    p = new Parameters { DC = dc, Level = level, Metamagic = selectedMeta.Value, RankBonus = rankBonus,
                        Origin = SpellParameterReader.Source(ability), Time = Time.unscaledTime };
                    metadata["parameterStatus"] = "ReadOnlyCalculated"; metadata["parameterSource"] = p.Origin;
                    metadata["dc"] = dc; metadata["parameterAgeMs"] = 0;
                }
                else metadata["parameterFallbackReason"] = reason;
            }
            Metamagic? previewMeta = current ? p.Metamagic : selectedMeta;
            int? casterLevel = current ? p.Level : SpellCasterLevelReader.Read(ability, caster);
            metadata["rankBonus"] = current ? (object)p.RankBonus : null;
            metadata["parameterKeyScope"] = p?.Stamp?.StartsWith("strict:", StringComparison.Ordinal) == true
                ? "strict-combat-state-for-unhandled-parameter-dependencies" : "audited-parameter-dependencies";
            metadata["casterLevel"] = casterLevel;
            metadata["casterLevelSource"] = current ? p.Origin : "readonly-base-caster-level; per-ability modifiers separate";
            metadata["persistentSave"] = previewMeta.HasValue ? (object)((previewMeta.Value & Metamagic.Persistent) != 0) : null;
            metadata["damageMetamagicKnown"] = previewMeta.HasValue;
            metadata["effectiveMetamagic"] = previewMeta.HasValue ? (object)(int)previewMeta.Value : null;
            var components = ability.Blueprint.ComponentsArray;
            var sticky = components.OfType<AbilityEffectStickyTouch>().FirstOrDefault();
            if (sticky != null)
            {
                var reference = CombatFields.Read(sticky, "m_TouchDeliveryAbility");
                var delivery = LoadedBlueprintReader.Read(reference) as Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility;
                if (delivery != null) components = delivery.ComponentsArray;
                else rows.Add("접촉 전달 주문의 상세 효과가 아직 로드되지 않았습니다.");
            }
            if (ability.MagicHackData != null)
            {
                var parts = new List<Dictionary<string, object>>();
                metadata["compositeParts"] = parts;
                var composite = ability.MagicHackData;
                metadata["compositeSaveType"] = composite.SavingThrowType.ToString();
                if (composite.AdditionalAoeBlueprint != null)
                {
                    metadata["compositeAdditionalAreaBlueprint"] = composite.AdditionalAoeBlueprint.AssetGuid.ToString();
                    rows.Add("추가 범위 조준 · " + composite.AdditionalAoeBlueprint.Name);
                }
                var clickedParts = new List<Dictionary<string, object>>();
                metadata["compositeClickedEffects"] = clickedParts;
                metadata["compositeCursorTargetCandidate"] = cursorTarget;
                rows.Add("합성 주문 · 내성 " + composite.SavingThrowType);
                ModifiableValue compositeSave = composite.SavingThrowType == SavingThrowType.Fortitude ? target.Stats.SaveFortitude
                    : composite.SavingThrowType == SavingThrowType.Reflex ? target.Stats.SaveReflex
                    : composite.SavingThrowType == SavingThrowType.Will ? target.Stats.SaveWill : null;
                if (current && compositeSave != null)
                {
                    int bonus = Cached(compositeSave);
                    var failure = ActiveD20Reader.SaveFailure(target, caster, ability, composite.SavingThrowType, bonus, p.DC,
                        (p.Metamagic & Metamagic.Persistent) != 0, out var rolls);
                    var check = Check("save", composite.SavingThrowType.ToString(), failure.HasValue ? "Estimated" : "Unavailable",
                        "CompositeConfiguredSaveType", failure, bonus, p.DC);
                    check.D20Model = rolls.Data(); checks.Add(check);
                    rows.Add("합성 기본 내성 실패 " + SpellLethalPreview.Percent(failure) + " · DC " + p.DC + " / 보정 " + bonus);
                }
                metadata["compositeSaveScope"] = "shared-by-components-within-effect-application";
                int order = 0;
                foreach (var spell in new[] { composite.Spell1, composite.Spell2 })
                {
                    order++;
                    if (spell == null) continue;
                    var effectComponents = spell.ComponentsArray;
                    if (!effectComponents.OfType<AbilityEffectRunAction>().Any())
                    {
                        var stickyEffect = effectComponents.OfType<AbilityEffectStickyTouch>().FirstOrDefault();
                        var deliveryEffect = LoadedBlueprintReader.Read(CombatFields.Read(stickyEffect, "m_TouchDeliveryAbility"))
                            as Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility;
                        if (deliveryEffect != null) effectComponents = deliveryEffect.ComponentsArray;
                    }
                    var part = new Dictionary<string, object> { { "blueprintId", spell.AssetGuid.ToString() }, { "name", spell.Name }, { "applicationOrder", order } };
                    rows.Add("적용 " + order + " · " + spell.Name);
                    SpellDamagePreview.Append(effectComponents, casterLevel,
                        current ? (Metamagic?)p.Metamagic : selectedMeta, rows, part,
                        caster: caster, rankBonus: current ? (int?)p.RankBonus : null, compositeApplication: true);
                    var run = effectComponents.OfType<AbilityEffectRunAction>().FirstOrDefault();
                    if (run != null)
                        part["nestedSaves"] = DescribeNestedSaves(run.Actions, target, caster, effectComponents, current ? p : null,
                            current ? (Metamagic?)p.Metamagic : selectedMeta, composite.SavingThrowType, true, rows, ability);
                    parts.Add(part);

                    var clicked = spell.ComponentsArray.OfType<AdditionalAbilityEffectRunActionOnClickedTarget>().FirstOrDefault();
                    if (clicked?.Action != null)
                    {
                        var clickedPart = new Dictionary<string, object> {
                            { "blueprintId", spell.AssetGuid.ToString() }, { "orderAfterDelivery", order },
                            { "target", "clicked-target-only" }, { "cursorTargetCandidate", cursorTarget } };
                        clickedParts.Add(clickedPart);
                        if (cursorTarget)
                        {
                            var clickedRows = new List<string>();
                            SpellDamagePreview.Append(spell.ComponentsArray, casterLevel,
                                current ? (Metamagic?)p.Metamagic : selectedMeta, clickedRows, clickedPart,
                                caster: caster, rankBonus: current ? (int?)p.RankBonus : null, clickedActions: clicked.Action);
                            clickedPart["lines"] = clickedRows;
                            clickedPart["nestedSaves"] = DescribeNestedSaves(clicked.Action, target, caster, spell.ComponentsArray, current ? p : null,
                                current ? (Metamagic?)p.Metamagic : selectedMeta, SavingThrowType.Unknown, true, clickedRows, ability);
                        }
                    }
                }
                foreach (var clickedPart in clickedParts)
                {
                    if (!(clickedPart.TryGetValue("lines", out object clickedLines) && clickedLines is List<string> details)) continue;
                    rows.Add("기본 전달 뒤 클릭 대상 후속 " + clickedPart["orderAfterDelivery"] + " · 커서 대상 후보");
                    rows.AddRange(details);
                }
                rows.Add("같은 효과 적용의 두 구성은 기본 내성 결과를 공유");
                if (!cursorTarget && clickedParts.Count > 0)
                    rows.Add("클릭 대상 후속 효과 " + clickedParts.Count + "개");
                return rows;
            }
            var projectile = components.OfType<AbilityDeliverProjectile>().FirstOrDefault();
            bool touch = components.OfType<AbilityDeliverTouch>().Any();
            var weaponReference = CombatFields.Read(projectile, "m_Weapon");
            var projectileWeapon = LoadedBlueprintReader.Read(weaponReference) as Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon;
            var weaponType = LoadedBlueprintReader.Read(CombatFields.Read(projectileWeapon, "m_Type"));
            var typeValue = CombatFields.Read(weaponType, "m_AttackType");
            var attackType = typeValue is Kingmaker.RuleSystem.AttackType ? (Kingmaker.RuleSystem.AttackType?)typeValue : null;
            bool knownTouchProjectile = attackType == Kingmaker.RuleSystem.AttackType.Touch || attackType == Kingmaker.RuleSystem.AttackType.RangedTouch;
            metadata["projectileNeedsAttackRoll"] = projectile?.NeedAttackRoll;
            metadata["deliveryWeaponStatus"] = projectileWeapon == null ? "NotLoadedOrBusy" : weaponType == null ? "TypeNotLoaded" : "ResolvedExisting";
            metadata["componentTypes"] = components.Take(32).Select(c => c?.GetType().Name).ToArray();
            string replacementStat = null;
            if (projectile?.ReplaceAttackRollBonusStat == true)
            {
                replacementStat = caster.State.Features.DomainsByIntelligenceFeature
                    ? "Intelligence" : projectile.AttackRollBonusStat.ToString();
            }
            bool supportedAttackStat = replacementStat == null || new[] {
                "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"
            }.Contains(replacementStat);
            if (projectile != null && projectile.NeedAttackRoll && (!knownTouchProjectile || projectile.IsHandOfTheApprentice || !supportedAttackStat))
            {
                rows.Add("주문 공격 명중 — 특수 무기/능력치 판정 미지원");
                checks.Add(Check("attack", "Projectile", "Unavailable", !knownTouchProjectile ? "DeliveryWeaponNotResolvedOrNotTouch" : "SpecialAttackStat"));
            }
            if (touch && !selectedMeta.HasValue) { rows.Add("접촉 명중 — 선택된 메타매직 판독 대기"); checks.Add(Check("attack", "Touch", "Unavailable", "MetamagicUnknown")); }
            if (touch && selectedMeta.HasValue || projectile != null && projectile.NeedAttackRoll && knownTouchProjectile && !projectile.IsHandOfTheApprentice && supportedAttackStat)
            {
                bool ranged = touch ? (selectedMeta.Value & Metamagic.Reach) != 0 : attackType == Kingmaker.RuleSystem.AttackType.RangedTouch;
                string stat = !touch && replacementStat != null ? replacementStat : ranged ? "Dexterity" : "Strength";
                int ab = Cached(caster.Stats.BaseAttackBonus) + BetaReader.AbilityModifier(caster, stat)
                    + Cached(caster.Stats.AdditionalAttackBonus);
                metadata["attackAbilityStat"] = stat;
                var targetNotes = new List<string>();
                ab += TargetCombatReader.Attack(caster, target, null, ranged, targetNotes);
                object ac = TargetCombatReader.Armor(caster, target, ranged, true, targetNotes);
                metadata["targetAttackModifiers"] = targetNotes.ToArray();
                var estimate = ObservedAttack(ability, caster, target, ranged);
                bool observed = estimate != null;
                if (observed) { ab = estimate.AB; ac = estimate.AC; }
                metadata["attackBasis"] = observed ? "observed-game-estimate/AC-only" : "cached-stats/basic-touch";
                if (estimate?.AutoHit == true)
                {
                    var automatic = Check("attack", ranged ? "RangedTouch" : "Touch", "Estimated", "ObservedGameAutomaticHit", 1);
                    automatic.AutomaticHit = true;
                    automatic.Source = "observed-game-estimate";
                    checks.Add(automatic);
                    rows.Add("게임 관측 · 자동 명중");
                }
                else if (ac is int) {
                    var rollModel = ActiveD20Reader.Read(caster, target, "attack", ability: ability);
                    double? hit = rollModel.Complete ? (double?)rollModel.Attack(ab, (int)ac, GameRules.AlwaysChance(caster)) : null;
                    rows.Add((ranged ? "원거리 접촉" : "근접 접촉") + " 명중 " + SpellLethalPreview.Percent(hit) + " (" + (observed ? "게임 관측 AB " : "기본 AB ") + ab + " / 접촉 AC " + ac + ")");
                    double? miss = AttackConcealment.MissChance(caster, target);
                    var check = Check("attack", ranged ? "RangedTouch" : "Touch", hit.HasValue ? "Estimated" : "Unavailable", "ActiveD20Model",
                        hit * (1 - miss.GetValueOrDefault()), ab, (int)ac);
                    check.AcOnlyProbability = hit;
                    check.D20Model = rollModel.Data();
                    if (rollModel.ConsumesEffect && projectile != null)
                    { check.Probability = null; check.Status = "Unavailable"; check.Reason = "ConsumableRerollAcrossDeliverySequence"; }
                    check.VisibilityMiss = miss;
                    check.VisibilitySource = miss.HasValue ? "game-basic-concealment" : null;
                    check.Source = (string)metadata["attackBasis"]; checks.Add(check);
                }
                else { rows.Add("접촉 명중 — 접촉 AC 미확인"); checks.Add(Check("attack", "Touch", "Unavailable", "TouchACUnknown")); }
            }
            if (!checks.Any(c => c.Kind == "attack"))
            {
                bool directNoAttack = !touch && projectile?.NeedAttackRoll != true
                    && components.OfType<AbilityEffectRunAction>().Any()
                    && !components.Any(component => component != null && component.GetType().Name.StartsWith("AbilityDeliver", StringComparison.Ordinal)
                        && !(component is AbilityDeliverProjectile) && !(component is AbilityDeliverDelay));
                metadata["directDamageNoAttackRoll"] = directNoAttack;
                rows.Add(directNoAttack ? "피해 전달 · 명중 굴림 없음 (내성은 각 효과별 판정)"
                    : "명중 굴림 — 직접 공격 판정 미발견 · 내성/SR 판정과 별개");
                checks.Add(Check("attack", directNoAttack ? "NoAttackRoll" : "Unidentified",
                    "NotIdentified", directNoAttack ? "NoAttackRollRequiredByDirectDelivery" : "NoDirectAttackComponent"));
            }
            var scan = ScanEffects(components);
            var saves = scan.Direct; var unsafeSaves = scan.DirectUnsafe;
            var linked = scan.Linked; var linkedUnsafe = scan.LinkedUnsafe;
            int budget = scan.Budget, missing = scan.Missing;
            metadata["effectScanTruncated"] = budget <= 0;
            metadata["linkedScanTruncated"] = scan.LinkedBudget <= 0;
            metadata["linkedSaveTypes"] = linked.Distinct().Select(t => t.ToString()).ToArray();
            metadata["unloadedEffectReferences"] = missing;
            foreach (var type in linked.Distinct().Where(t => !saves.Contains(t))) {
                string label = type == SavingThrowType.Fortitude ? "인내" : type == SavingThrowType.Reflex ? "반사" : type == SavingThrowType.Will ? "의지" : type.ToString();
                if (!current || linkedUnsafe.Contains(type)) {
                    rows.Add(label + " 내성: 장판/버프의 후속 판정 · DC 또는 조건 미확인");
                    checks.Add(Check("save", type.ToString(), "Unavailable", "LinkedEffectDCOrConditionUnverified"));
                } else {
                    ModifiableValue stat = type == SavingThrowType.Fortitude ? target.Stats.SaveFortitude : type == SavingThrowType.Reflex ? target.Stats.SaveReflex : target.Stats.SaveWill;
                    int bonus = Cached(stat);
                    var failure = ActiveD20Reader.SaveFailure(target, caster, ability, type, bonus, p.DC, (p.Metamagic & Metamagic.Persistent) != 0, out var rolls);
                    rows.Add(label + " 후속 내성 실패 " + SpellLethalPreview.Percent(failure) + " (장판/버프 분기 후보 · DC " + p.DC + ")");
                    var check = Check("save", type.ToString(), failure.HasValue ? "Estimated" : "Unavailable", "LinkedEffectBranchCandidateNotImmediateCast", failure, bonus, p.DC);
                    check.D20Model = rolls.Data(); checks.Add(check);
                }
            }
            if (budget <= 0) { foreach (var type in saves) unsafeSaves.Add(type); rows.Add("내성 효과 탐색 한도 초과 · 일부 분기 미확인"); }
            if (saves.Count == 0 && linked.Count == 0) { rows.Add("내성 — 직접 내성 판정을 찾지 못함 (없음으로 확정하지 않음)"); checks.Add(Check("save", "Unidentified", "NotIdentified", "NoDirectSaveFound")); }
            foreach (var type in saves.Distinct())
            {
                ModifiableValue stat = type == SavingThrowType.Fortitude ? target.Stats.SaveFortitude : type == SavingThrowType.Reflex ? target.Stats.SaveReflex : type == SavingThrowType.Will ? target.Stats.SaveWill : null;
                string label = type == SavingThrowType.Fortitude ? "인내" : type == SavingThrowType.Reflex ? "반사" : type == SavingThrowType.Will ? "의지" : type.ToString();
                if (unsafeSaves.Contains(type)) { rows.Add(label + " 내성 실패 — 별도/조건부 DC가 있어 계산 불가"); checks.Add(Check("save", type.ToString(), "Unavailable", "ConditionalOrCustomDC")); continue; }
                if (!current || stat == null) { rows.Add(label + " 내성 실패 — 주문 DC의 최신 게임 계산 결과 대기"); checks.Add(Check("save", type.ToString(), "Unavailable", !current ? (string)metadata["parameterStatus"] : "SaveBonusUnknown")); continue; }
                int bonus = Cached(stat);
                var failure = ActiveD20Reader.SaveFailure(target, caster, ability, type, bonus, p.DC, (p.Metamagic & Metamagic.Persistent) != 0, out var rollModel);
                rows.Add(label + " 내성 실패 " + SpellLethalPreview.Percent(failure) + " (DC " + p.DC + " / 내성 " + bonus.ToString("+0;-0;0") + ")");
                string saveReason = failure.HasValue ? rollModel.ConsumesEffect ? "TrackedSingleSaveConsumption" : "ActiveD20Model"
                    : !rollModel.Complete ? "UnsupportedActiveD20Effects" : "UnavailableSaveLifecycleOrBudget";
                var saveCheck = Check("save", type.ToString(), failure.HasValue ? "Estimated" : "Unavailable", saveReason, failure, bonus, p.DC);
                saveCheck.D20Model = rollModel.Data(); checks.Add(saveCheck);
            }
            if (!ability.Blueprint.SpellResistance) { rows.Add("주문 저항: 적용하지 않는 주문 (효과 면역은 별도)"); checks.Add(Check("sr", "SpellResistance", "NotRequired", "AbilityDoesNotCheckSR")); }
            else
            {
                var srPart = target.Get<UnitPartSpellResistance>();
                var srList = CombatFields.Read(srPart, "m_SRs") as IList;
                var immunities = CombatFields.Read(srPart, "m_SpellImmunities") as IList;
                if (srPart == null) { rows.Add("주문 저항: 등록된 SR 없음 (면역 여부는 별도)"); checks.Add(Check("sr", "SpellResistance", "NoRegisteredSR", "ImmunityStillSeparate")); }
                else if (srList == null)
                { rows.Add("주문 저항 관통 — 시전자 레벨 또는 조건부 SR 미확인"); checks.Add(Check("sr", "SpellResistance", "Unavailable", "CasterLevelOrConditionalSRUnknown")); }
                else
                {
                    int value = srList.Cast<object>().Where(s => SpellResistanceReader.Applies(s, ability, caster))
                        .Select(s => (int)CombatFields.Read(s, "Value")).DefaultIfEmpty(0).Max();
                    var bonuses = CombatFields.Read(srPart, "Bonuses") as IDictionary;
                    if (bonuses != null) foreach (DictionaryEntry bonus in bonuses) value += (int)bonus.Value;
                    value = Math.Max(0, value - (int)(CombatFields.Read(srPart, "<AllSRPenalty>k__BackingField") ?? 0)
                        - (previewMeta.HasValue && (previewMeta.Value & Metamagic.Piercing) != 0 ? 5 : 0));
                    if (metadata.TryGetValue("rodIgnoreSpellResistance", out var ignoreByRod) && Equals(ignoreByRod, true))
                        checks.Add(Check("sr", "SpellResistance", "Bypassed", "ActiveMetamagicRod", 1));
                    else if (value == 0) checks.Add(Check("sr", "SpellResistance", "NoRegisteredSR", "NoPositiveResistance"));
                    else if (!current)
                        checks.Add(Check("sr", "SpellResistance", "Unavailable", "CasterLevelNotObserved", threshold: value));
                    else
                    {
                        int bonus = SpellPenetrationPreview.Bonus(caster, out bool unsupported);
                        int penetration = checked(p.Level + bonus);
                        var rollModel = ActiveD20Reader.Read(caster, target, "sr", ability: ability);
                        double? pass = rollModel.Complete && !unsupported ? (double?)rollModel.Resistance(penetration, value) : null;
                        var srCheck = Check("sr", "SpellResistance", pass.HasValue ? "Estimated" : "Unavailable",
                            unsupported ? "SomePenetrationModifiersUnavailable" : "BasicPenetrationWithSupportedBonuses",
                            pass, penetration, value);
                        srCheck.D20Model = rollModel.Data(); checks.Add(srCheck);
                        rows.Add("SR " + value + " · 관통 " + SpellLethalPreview.Percent(pass)
                            + " · CL " + p.Level + " / 활성 보정 " + bonus.ToString("+0;-0;0"));
                    }
                }
            }

            bool? spellImmune = SpellResistanceReader.Immune(ability, caster, target);
            metadata["registeredSpellImmunity"] = spellImmune;
            if (spellImmune == true)
            {
                checks.RemoveAll(check => check.Kind == "sr");
                checks.Add(Check("sr", "SpellResistance", "Immune", "RegisteredSpellImmunity", 0));
                rows.Add("대상 주문 면역 · 적용 확률 0%");
            }

            var nestedSaves = new List<Dictionary<string, object>>();
            foreach (var run in components.OfType<AbilityEffectRunAction>())
                nestedSaves.AddRange(DescribeNestedSaves(run.Actions, target, caster, components, current ? p : null,
                    current ? (Metamagic?)p.Metamagic : selectedMeta, run.SavingThrowType, false, rows, ability));
            if (nestedSaves.Count > 0) metadata["nestedSaves"] = nestedSaves;

            var attackCheck = checks.FirstOrDefault(check => check.Kind == "attack" && check.AcOnlyProbability.HasValue);
            if (attackCheck != null)
            {
                int images = attackCheck.MirrorImages ?? AttackConcealment.MirrorImages(caster, target);
                attackCheck.MirrorImages = images;
                attackCheck.HitBeforeImages = attackCheck.Probability;
                if (images > 0 && attackCheck.Bonus.HasValue && attackCheck.Threshold.HasValue)
                {
                    attackCheck.Probability /= images + 1.0;
                    var rolls = ActiveD20Reader.Read(caster, target, "attack", ability: ability);
                    int ab = attackCheck.Bonus.Value, ac = attackCheck.Threshold.Value;
                    attackCheck.MirrorNearMiss = rolls.Complete ? (double?)rolls.Probability(
                        die => !SpellPreviewMath.AttackHits(die, ab, ac, GameRules.AlwaysChance(caster)),
                        (die, extra) => !SpellPreviewMath.AttackHits(die, ab + extra, ac, GameRules.AlwaysChance(caster))
                            && (long)ac - ab - extra - die <= 5) * (1 - attackCheck.VisibilityMiss.GetValueOrDefault()) : null;
                }
                rows.Add("AC 기준 " + attackCheck.AcOnlyProbability.Value.ToString("P0")
                    + " · 빗나감 " + SpellLethalPreview.Percent(attackCheck.VisibilityMiss)
                    + (images > 0 ? " · 거울상 " + images + "개 · 첫 타격" : "")
                    + " · 적용 명중률 " + SpellLethalPreview.Percent(attackCheck.Probability));
            }
            if (Main.Settings.CompareBeta)
                metadata["hcoReference"] = OverlayReference.Spell(ability, caster, target, checks);
            var damageRows = new List<string>();
            var resolvedDamage = EarPiercingScreamDamage.Resolve(ability, caster, components, out bool enhancedDamage, out int dazeBaseRounds);
            if (resolvedDamage != null)
            {
                metadata["resolvedDamageBranch"] = enhancedDamage ? "caster-fact-present" : "standard";
                metadata["dazeBaseRounds"] = dazeBaseRounds;
                rows.Add(enhancedDamage ? "시전자 특성 피해식 적용" : "기본 피해식 적용");
            }
            else if (missing > 0) rows.Add("장판/버프 효과 일부 미로드 · 후속 내성 탐색 미완료");
            if ((missing == 0 || resolvedDamage != null) && saves.Distinct().Count() == 1 && linked.Count == 0 && budget > 0)
                SpellOutcome.ReadDirectSaveEffects(components, metadata);
            var conditions = SpellDamageConditions.Create(ability, caster, target, checks, components,
                touch || projectile?.NeedAttackRoll == true, saves, linked.Count > 0 || budget <= 0 || missing > 0 && resolvedDamage == null,
                ability.Blueprint.SpellResistance, out string expectedStatus);
            metadata["expectedDamageConditions"] = expectedStatus;
            ObservedRankValues rankSnapshot = null;
            if (ReferenceEquals(components, ability.Blueprint.ComponentsArray)
                && observedRanks.TryGetValue(BetaIdentity.Id(ability) + "|" + BetaIdentity.Id(target), out var observedRank)
                && Time.unscaledTime >= observedRank.Time && Time.unscaledTime - observedRank.Time <= .35f
                && observedRank.Stamp == ParameterStamp(ability, caster) + "|" + BetaReader.PairStamp(caster, target))
                rankSnapshot = observedRank;
            metadata["damageRankSource"] = rankSnapshot?.Origin ?? "static-rank-definition";
            SpellDamagePreview.Append(components, casterLevel, current ? (Metamagic?)p.Metamagic : selectedMeta, damageRows, metadata, target, conditions, caster, current ? (int?)p.RankBonus : null, rankSnapshot?.Values, resolvedDamage: resolvedDamage, sourceAbility: ability);
            SpellSequenceProjection.Append(metadata, target, conditions);
            SpellBranchPreview.Append(ability, caster, target, components, checks, metadata);
            metadata["inputStamp"] = InputStamp(ability, caster, target);
            PitSpellPreview.Append(ability, caster, target, components, current ? (int?)p.DC : null,
                casterLevel, previewMeta, current ? (int?)p.RankBonus : null, metadata);
            rows.InsertRange(0, damageRows);
            foreach (var check in checks.Where(check => check.Kind == "save"))
                check.PersistentSave = previewMeta.HasValue ? (bool?)((previewMeta.Value & Metamagic.Persistent) != 0) : null;
            return rows;
        }
        static List<Dictionary<string, object>> DescribeNestedSaves(Kingmaker.ElementsSystem.ActionList actions,
            UnitEntityData target, UnitEntityData caster, Kingmaker.Blueprints.BlueprintComponent[] components,
            Parameters parameters, Metamagic? metamagic, SavingThrowType rootType, bool composite, List<string> lines, AbilityData sourceAbility = null)
        {
            var result = new List<Dictionary<string, object>>();
            int remaining = 512;
            int? baseDc = parameters?.DC;
            Scan(actions, 0, rootType, rootType != SavingThrowType.Unknown ? baseDc : null, true);
            return result;

            int? ResolveDcValue(object value)
                => SpellDamagePreview.Value(value as Kingmaker.UnitLogic.Mechanics.ContextValue, components,
                    parameters?.Level, metamagic, caster, parameters?.RankBonus);

            void Scan(Kingmaker.ElementsSystem.ActionList list, int depth, SavingThrowType parentType, int? parentDc, bool sameTarget)
            {
                if (list?.Actions == null || depth > 12 || remaining <= 0) return;
                foreach (var action in list.Actions)
                {
                    if (--remaining <= 0) return;
                    if (action == null) continue;
                    bool hasParentSave = parentType != SavingThrowType.Unknown;
                    var nestedType = parentType;
                    int? nestedDc = parentDc;
                    if (action is ContextActionSavingThrow save)
                    {
                        bool shared = composite && hasParentSave && !save.MakeSaveAlsoForMagicHack;
                        var increases = CombatFields.Read(save, "m_ConditionalDCIncrease") as Array;
                        bool hasConditions = increases != null && increases.Length > 0;
                        var dc = shared ? parentDc
                            : save.UseDCFromContextSavingThrow && hasParentSave ? parentDc
                            : save.HasCustomDC ? ResolveDcValue(save.CustomDC) : baseDc;
                        int? lower = dc, upper = dc;
                        var conditionResults = new List<Dictionary<string, object>>();
                        if (!shared && !(save.UseDCFromContextSavingThrow && hasParentSave) && hasConditions)
                        {
                            foreach (var increase in increases)
                            {
                                bool? active = ReadSaveCondition(CombatFields.Read(increase, "Condition")
                                    as Kingmaker.ElementsSystem.ConditionsChecker, sameTarget ? target : null, caster);
                                int? definedAdjustment = ResolveDcValue(CombatFields.Read(increase, "Value"));
                                int? adjustment = active == false ? 0 : definedAdjustment;
                                conditionResults.Add(new Dictionary<string, object> { { "active", active }, { "adjustment", adjustment }, { "definedAdjustment", definedAdjustment } });
                                if (!adjustment.HasValue) { lower = upper = null; continue; }
                                lower = AddDc(lower, active == true ? adjustment.Value : Math.Min(0, adjustment.Value));
                                upper = AddDc(upper, active == true ? adjustment.Value : Math.Max(0, adjustment.Value));
                            }
                        }

                        var type = shared ? parentType : save.Type;
                        bool exactBaseDc = lower.HasValue && lower == upper;
                        {
                            var entry = new Dictionary<string, object> {
                                { "actionId", BetaIdentity.Id(save) },
                                { "type", type.ToString() }, { "source", shared ? "existing-shared-save" : "nested-save" },
                                { "customDc", save.HasCustomDC }, { "usesExistingSaveDc", save.UseDCFromContextSavingThrow },
                                { "baseDcMinimum", lower }, { "baseDcMaximum", upper },
                                { "unadjustedDc", dc },
                                { "conditionalDc", hasConditions }, { "conditionResults", conditionResults },
                                { "sameTargetKnown", sameTarget }, { "branchCandidate", true } };
                            result.Add(entry);
                            if (!shared)
                            {
                                string label = "내부 내성 후보 · " + type;
                                if (conditionResults.Any(condition => condition["active"] is bool)) label += " · 현재 상태 기준";
                                if (lower.HasValue && upper.HasValue)
                                    label += exactBaseDc ? " · 기본 DC " + lower.Value : " · 조건별 기본 DC " + lower.Value + "–" + upper.Value;
                                else label += " · 동적 DC";
                                var stat = type == SavingThrowType.Fortitude ? target.Stats.SaveFortitude
                                    : type == SavingThrowType.Reflex ? target.Stats.SaveReflex
                                    : type == SavingThrowType.Will ? target.Stats.SaveWill : null;
                                if (sameTarget && stat != null && lower.HasValue && upper.HasValue && metamagic.HasValue)
                                {
                                    int bonus = Cached(stat);
                                    var actualAbility = sourceAbility ?? SelectedAbility;
                                    double? failure = ActiveD20Reader.SaveFailure(target, caster, actualAbility, type, bonus, lower.Value,
                                        (metamagic.Value & Metamagic.Persistent) != 0, out var rollModel);
                                    if (hasParentSave && rollModel.ConsumesEffect) failure = null;
                                    entry["d20Model"] = rollModel.Data();
                                    entry["bonus"] = bonus;
                                    if (exactBaseDc)
                                    {
                                        entry["failureChance"] = failure;
                                        label += " · 실패 " + SpellLethalPreview.Percent(failure);
                                    }
                                    else
                                    {
                                        double? upperFailure = failure.HasValue && rollModel.Complete ? (double?)rollModel.SaveFailure(bonus, upper.Value,
                                            (metamagic.Value & Metamagic.Persistent) != 0) : null;
                                        entry["failureChanceMinimum"] = failure;
                                        entry["failureChanceMaximum"] = upperFailure;
                                        label += " · 실패 " + SpellLethalPreview.Percent(failure) + "–" + SpellLethalPreview.Percent(upperFailure);
                                    }
                                }
                                lines.Add(label);
                            }
                        }
                        nestedType = type;
                        nestedDc = exactBaseDc ? lower : null;
                    }
                    foreach (var field in action.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                        if (field.FieldType == typeof(Kingmaker.ElementsSystem.ActionList))
                            Scan(field.GetValue(action) as Kingmaker.ElementsSystem.ActionList, depth + 1, nestedType, nestedDc,
                                sameTarget && (action is ContextActionSavingThrow || action is ContextActionConditionalSaved
                                    || action is ContextActionRepeatedActions || action is Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional));
                }
            }
        }

        internal static bool? ReadSaveCondition(Kingmaker.ElementsSystem.ConditionsChecker checker, UnitEntityData target, UnitEntityData caster = null,
            ProjectedEffectState projectedTarget = null, ProjectedEffectState projectedCaster = null)
        {
            if (checker == null) return null;
            var conditions = checker.Conditions;
            if (conditions == null || conditions.Length == 0) return true;
            if (conditions.Length > 32) return null;
            int operation = (int)checker.Operation;
            if (operation != 0 && operation != 1) return null;
            bool hasUnknown = false;
            foreach (var condition in conditions)
            {
                if (condition == null) continue;
                bool? value = null;
                if (condition.GetType() == typeof(Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionIsAlly))
                    value = caster != null && target != null && caster.IsAlly(target);
                if (caster != null && condition.GetType() == typeof(Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionCasterHasFact))
                    value = projectedCaster != null ? projectedCaster.HasFact(caster, CombatFields.Read(condition, "m_Fact"))
                        : CombatFields.HasFact(caster, CombatFields.Read(condition, "m_Fact"));
                if (condition is Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionAlignment alignment)
                {
                    var unit = alignment.CheckCaster ? caster : target;
                    if (unit != null) value = Kingmaker.Enums.AlignmentExtension.HasComponent(unit.Alignment.ValueRaw, alignment.Alignment);
                }
                else if (condition is Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionCharacterClass characterClass)
                {
                    var unit = characterClass.CheckCaster ? caster : target;
                    if (unit != null) value = unit.Progression.Classes.Any(entry => entry.Level >= characterClass.MinLevel
                        && CombatFields.Reference(CombatFields.Read(characterClass, "m_Class"), entry.CharacterClass));
                }
                else if (target != null && condition.GetType() == typeof(Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHasFact))
                {
                    var fact = CombatFields.Read(condition, "m_Fact");
                    if (fact != null) value = projectedTarget != null ? projectedTarget.HasFact(target, fact) : CombatFields.HasFact(target, fact);
                }
                else if (target != null && condition.GetType() == typeof(Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHasBuff))
                {
                    var buff = CombatFields.Read(condition, "m_Buff");
                    if (buff != null) value = projectedTarget != null ? projectedTarget.HasFact(target, buff) : CombatFields.HasFact(target, buff);
                }

                if (!value.HasValue) { hasUnknown = true; continue; }
                bool passed = condition.Not ? !value.Value : value.Value;
                if (operation == 0 && !passed) return false;
                if (operation == 1 && passed) return true;
            }
            return hasUnknown ? (bool?)null : operation == 0;
        }

        static int? AddDc(int? dc, int adjustment)
        {
            if (!dc.HasValue) return null;
            long result = (long)dc.Value + adjustment;
            return result < int.MinValue || result > int.MaxValue ? (int?)null : (int)result;
        }

        sealed class EffectScan
        {
            internal readonly List<SavingThrowType> Direct = new List<SavingThrowType>(), Linked = new List<SavingThrowType>();
            internal readonly HashSet<SavingThrowType> DirectUnsafe = new HashSet<SavingThrowType>(), LinkedUnsafe = new HashSet<SavingThrowType>();
            internal int Budget = 512, LinkedBudget = 512, Missing;
        }
        static object lastComponents;
        static float lastScanTime;
        static EffectScan lastScan;
        static EffectScan ScanEffects(Kingmaker.Blueprints.BlueprintComponent[] components)
        {
            float now = Time.unscaledTime;
            // Static definitions only; no target stats/DC are cached. Retry missing
            // loaded references after 100ms, share one scan across area targets.
            if (ReferenceEquals(lastComponents, components) && lastScan != null && now >= lastScanTime && now - lastScanTime < .1f) return lastScan;
            var result = new EffectScan();
            foreach (var run in components.OfType<AbilityEffectRunAction>()) {
                if (run.SavingThrowType != SavingThrowType.Unknown) result.Direct.Add(run.SavingThrowType);
                FindSaves(run.Actions, result.Direct, result.DirectUnsafe, 0, ref result.Budget);
            }
            var visited = new HashSet<object>();
            foreach (var component in components) FindLinkedSaves(component, result.Linked, result.LinkedUnsafe, visited, 0, ref result.LinkedBudget, ref result.Missing);
            if (result.LinkedBudget <= 0) foreach (var type in result.Linked) result.LinkedUnsafe.Add(type);
            lastComponents = components; lastScanTime = now; lastScan = result;
            return result;
        }
        static void FindLinkedSaves(object node, List<SavingThrowType> saves, HashSet<SavingThrowType> unsafeSaves,
            HashSet<object> visited, int depth, ref int budget, ref int missing)
        {
            if (node == null || !visited.Add(node)) return;
            if (--budget <= 0 || depth > 10) { budget = 0; return; }
            var actionList = node as Kingmaker.ElementsSystem.ActionList;
            if (actionList != null) {
                foreach (var action in actionList.Actions ?? new Kingmaker.ElementsSystem.GameAction[0])
                    FindLinkedSaves(action, saves, unsafeSaves, visited, depth + 1, ref budget, ref missing);
                return;
            }
            var save = node as ContextActionSavingThrow;
            if (save != null) {
                var conditional = CombatFields.Read(save, "m_ConditionalDCIncrease") as Array;
                saves.Add(save.Type);
                if (save.HasCustomDC || save.FromBuff || save.UseDCFromContextSavingThrow || conditional?.Length > 0)
                    unsafeSaves.Add(save.Type);
            }
            string name = node.GetType().Name;
            string referenceField = name == "ContextActionApplyBuff" || name == "AbilityAreaEffectBuff" ? "m_Buff"
                : name == "ContextActionSpawnAreaEffect" || name == "AddAreaEffect" ? "m_AreaEffect" : null;
            if (referenceField != null) {
                var bp = LoadedBlueprintReader.Read(CombatFields.Read(node, referenceField)) as Kingmaker.Blueprints.BlueprintScriptableObject;
                if (bp == null) missing++;
                else foreach (var component in bp.ComponentsArray)
                    FindLinkedSaves(component, saves, unsafeSaves, visited, depth + 1, ref budget, ref missing);
                if (name == "ContextActionApplyBuff" && (bool)(CombatFields.Read(node, "IgnoreParentContext") ?? false))
                    foreach (var type in saves) unsafeSaves.Add(type);
            }
            foreach (var field in node.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                if (field.FieldType == typeof(Kingmaker.ElementsSystem.ActionList))
                    FindLinkedSaves(field.GetValue(node), saves, unsafeSaves, visited, depth + 1, ref budget, ref missing);
        }
        static void FindSaves(Kingmaker.ElementsSystem.ActionList actions, List<SavingThrowType> saves, HashSet<SavingThrowType> unsafeSaves, int depth, ref int budget)
        {
            if (actions?.Actions == null) return;
            bool complete = ActionGraphScan.Visit(actions.Actions, Children, action => {
                if (!(action is ContextActionSavingThrow save)) return;
                saves.Add(save.Type);
                var conditional = CombatFields.Read(save, "m_ConditionalDCIncrease") as Array;
                if (save.HasCustomDC || save.UseDCFromContextSavingThrow || save.FromBuff || conditional?.Length > 0)
                    unsafeSaves.Add(save.Type);
            }, out int visited, maximumNodes: Math.Max(0, budget));
            budget = complete ? budget - visited : 0;
        }
        static IEnumerable<Kingmaker.ElementsSystem.GameAction> Children(Kingmaker.ElementsSystem.GameAction action)
        {
            foreach (var field in action.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                if (field.FieldType == typeof(Kingmaker.ElementsSystem.ActionList)
                    && field.GetValue(action) is Kingmaker.ElementsSystem.ActionList list && list.Actions != null)
                    foreach (var child in list.Actions) if (child != null) yield return child;
        }
    }
}
