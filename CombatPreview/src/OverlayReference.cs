using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Items;
using Kingmaker.EntitySystem.Stats;
using UnityEngine;

namespace CombatPreview
{
    // Optional observer. Never invokes HCO calculations, getters, rules or actions.
    internal static class OverlayReference
    {
        const string Owner = "CombatPreview.HCOReference";
        static Harmony harmony;
        static string version, mvid;
        internal static string Status = "NotInstalled";
        [ThreadStatic] static Stack<Batch> scopes;
        internal static bool Active { get { return scopes != null && scopes.Count > 0; } }
        static bool ObserveResults => Main.Settings.CompareBeta;
        internal sealed class Batch
        {
            internal UnitEntityData Actor, Target;
            internal AbilityData Ability;
            internal string Key, Before, After;
            internal bool InArea, Complete;
            internal float Time;
            internal readonly List<Dictionary<string, object>> Checks = new List<Dictionary<string, object>>();
            internal string[] Lines;
        }
        sealed class Local { internal string Stamp; internal float Time; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BetaSnapshot, Local> locals = new System.Runtime.CompilerServices.ConditionalWeakTable<BetaSnapshot, Local>();
        internal static void Bind(BetaSnapshot snapshot, UnitEntityData actor, UnitEntityData target)
        {
            if (harmony == null || !Main.Settings.CompareBeta) return;
            try { using (ScopedReadMemo<UnitEntityData, string>.Begin()) {
                var value = locals.GetValue(snapshot, _ => new Local()); value.Stamp = Stamp(actor, target); value.Time = Time.unscaledTime;
            } } catch (Exception e) { Main.Error("HCO local stamp unavailable", e); }
        }
        static readonly Dictionary<string, Batch> recent = new Dictionary<string, Batch>();
        static string Key(UnitEntityData actor, UnitEntityData target, AbilityData ability)
        { return BetaIdentity.Id(actor) + "|" + BetaIdentity.Id(target) + "|" + BetaIdentity.Id(ability); }
        static string Stamp(UnitEntityData actor, UnitEntityData target, AbilityData ability = null)
        {
            string pair = BetaReader.PairStamp(actor, target);
            if (ability == null) return pair;
            var meta = CombatFields.Read(ability, "<MetamagicData>k__BackingField");
            return pair + "|spell:" + BetaIdentity.Id(ability) + ":" + CombatFields.Read(ability, "<OverrideDC>k__BackingField")
                + ":" + CombatFields.Read(ability, "m_OverrideSpellLevel") + ":" + CombatFields.Read(ability, "<OverrideCasterLevel>k__BackingField")
                + ":" + CombatFields.Read(meta, "<MetamagicMask>k__BackingField") + ":" + CombatFields.Read(meta, "<HeightenLevel>k__BackingField");
        }
        static object Field(object o, string name) { return CombatFields.Read(o, "<" + name + ">k__BackingField"); }
        internal static void Install()
        {
            if (harmony != null) return;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "HitChanceOverlay");
            if (assembly == null) { Status = "NotInstalled"; return; }
            // Fail closed for unknown layouts. The audited 0.4.0 has these entry points.
            try {
                if (assembly.ManifestModule.ModuleVersionId.ToString() != "80ea8375-0d21-45f4-8533-0064c3011a00") throw new NotSupportedException("Unaudited HCO build");
                var calculator = assembly.GetType("HitChanceOverlay.OddsCalculator", true);
                var rule = assembly.GetType("HitChanceOverlay.RulePreview", true);
                var entries = new[] { "ForWeapon", "ForAbility" }.Select(n => AccessTools.Method(calculator, n)).ToArray();
                var attack = AccessTools.Method(calculator, "CalculateAttack");
                var save = AccessTools.Method(rule, "SavingThrow");
                var sr = AccessTools.Method(rule, "SpellPenetration");
                var threshold = AccessTools.Method(calculator, "GetSpellResistance");
                if (entries.Any(e => e == null) || attack == null || save == null || sr == null || threshold == null
                    || entries[0].GetParameters().Length != 2 || entries[1].GetParameters().Length != 3
                    || attack.GetParameters().Length != 10 || save.GetParameters().Length != 4 || sr.GetParameters().Length != 2 || threshold.GetParameters().Length != 4)
                    throw new NotSupportedException("HCO method layout changed");
                version = assembly.GetName().Version.ToString(); mvid = assembly.ManifestModule.ModuleVersionId.ToString();
                var instance = new Harmony(Owner);
                try {
                    for (int i = 0; i < entries.Length; i++) instance.Patch(entries[i],
                        prefix: new HarmonyMethod(typeof(OverlayReference), i == 0 ? nameof(BeginWeapon) : nameof(BeginAbility)),
                        postfix: new HarmonyMethod(typeof(OverlayReference), nameof(Complete)) { priority = Priority.Last },
                        finalizer: new HarmonyMethod(typeof(OverlayReference), nameof(End)));
                    instance.Patch(attack, postfix: new HarmonyMethod(typeof(OverlayReference), nameof(Attack)));
                    instance.Patch(save, postfix: new HarmonyMethod(typeof(OverlayReference), nameof(Save)));
                    instance.Patch(sr, postfix: new HarmonyMethod(typeof(OverlayReference), nameof(Resistance)));
                    instance.Patch(threshold, postfix: new HarmonyMethod(typeof(OverlayReference), nameof(ResistanceThreshold)));
                } catch { instance.UnpatchAll(Owner); throw; }
                harmony = instance; Status = "ObservingExistingResults";
                Main.Log?.Log("HitChanceOverlay reference observer installed: " + version + " MVID=" + mvid);
            } catch (Exception e) { Status = "UnsupportedOrPatchFailed"; Main.Error("HCO comparison unavailable", e); }
        }
        static float nextInstall;
        internal static void Tick()
        {
            if (!BetaRuntime.Enabled) { if (harmony != null) Remove(); return; }
            if (harmony == null && (Status == "NotInstalled" || Status == "Stopped") && Time.unscaledTime >= nextInstall) {
                nextInstall = Time.unscaledTime + 10; Install();
            }
        }
        internal static void Remove()
        { harmony?.UnpatchAll(Owner); harmony = null; recent.Clear(); scopes?.Clear(); Status = "Stopped"; }
        // Harmony 2.0.4 recognizes positional arguments but does not inject __args.
        static void BeginWeapon(UnitEntityData __0, UnitEntityData __1, out Batch __state)
        { BeginCore(__0, __1, null, false, out __state); }
        static void BeginAbility(AbilityData __0, UnitEntityData __1, bool __2, out Batch __state)
        { BeginCore(null, __1, __0, __2, out __state); }
        static void BeginCore(UnitEntityData actor, UnitEntityData target, AbilityData ability, bool inArea, out Batch state)
        {
            state = null;
            if (!BetaRuntime.Enabled) return;
            using (PreviewPerformance.Measure("hco-capture-start"))
            try
            {
                if (ability != null) actor = SpellPreviewRuntime.Caster(ability);
                if (actor == null || target == null) return;
                var batch = new Batch { Actor = actor, Target = target, Ability = ability, InArea = inArea };
                if (ObserveResults)
                {
                    batch.Key = Key(actor, target, ability);
                    batch.Before = Stamp(actor, target, ability);
                }
                if (scopes == null) scopes = new Stack<Batch>();
                scopes.Push(batch);
                state = batch;
            }
            catch (Exception e) { Main.Error("HCO observation scope unavailable", e); }
        }
        static void Complete(Batch __state, object __result)
        { using (PreviewPerformance.Measure("hco-capture-end")) CompleteCore(__state, __result); }
        static void CompleteCore(Batch __state, object __result)
        {
            if (__state == null || __state.Key == null || __result == null || !ObserveResults) return;
            try {
                __state.After = Stamp(__state.Actor, __state.Target, __state.Ability); __state.Time = Time.unscaledTime;
                var lines = CombatFields.Read(__result, "Lines") as IEnumerable;
                var text = new List<string>();
                var headline = Field(CombatFields.Read(__result, "Headline"), "Text") as string;
                if (headline != null) text.Add(headline);
                if (lines != null) foreach (var line in lines) { if (text.Count >= 16) break; text.Add(Field(line, "Text") as string); }
                __state.Lines = text.ToArray(); __state.Complete = true;
                if (recent.Count >= 128 && !recent.ContainsKey(__state.Key)) recent.Clear();
                recent[__state.Key] = __state;
            } catch (Exception e) { Main.Error("HCO result unavailable", e); }
        }
        static Exception End(Batch __state, Exception __exception)
        {
            // Preserve the original exception and always unwind our own scope.
            if (__state != null && scopes != null && scopes.Count > 0 && ReferenceEquals(scopes.Peek(), __state)) scopes.Pop();
            return __exception;
        }
        static void Add(string kind, string type, object result, params string[] fields)
        {
            if (!Active || !ObserveResults || result == null || scopes.Peek().Checks.Count >= 24) return;
            var item = new Dictionary<string, object> { { "kind", kind }, { "type", type } };
            foreach (var field in fields) item[field] = Field(result, field);
            scopes.Peek().Checks.Add(item);
        }
        static void Attack(ItemEntityWeapon __2, int __3, bool __4, int __5, bool __7, bool __8, object __result)
        {
            try {
                if (!Active || !ObserveResults || scopes.Peek().Checks.Count >= 24) return;
                Add("attack", "Attack", __result, "AttackBonus", "TargetAC", "RollPercent", "MissPercent", "MirrorImages", "AutoHit", "TouchAC", "PendingToggles", "DamageImmune", "MissNote");
                var entry = scopes.Peek().Checks.LastOrDefault();
                if (entry == null) return;
                entry["weaponId"] = BetaIdentity.Id(__2); entry["penalty"] = __3;
                entry["weaponAttack"] = __4; entry["extraBonus"] = __5; entry["firstAttack"] = __7; entry["charge"] = __8;
            } catch (Exception e) { Main.Error("HCO attack result unavailable", e); }
        }
        static void Save(SavingThrowType __2, object __result)
        { try { Add("save", __2.ToString(), __result, "Bonus", "DC", "AutoPass", "SuccessBonus", "Precise"); } catch (Exception e) { Main.Error("HCO save result unavailable", e); } }
        static void Resistance(object __result)
        { try { Add("sr", "SpellResistance", __result, "Penetration", "Precise"); } catch (Exception e) { Main.Error("HCO SR result unavailable", e); } }
        static void ResistanceThreshold(int __result)
        {
            try {
                if (!Active || !ObserveResults || scopes.Peek().Checks.Count >= 24) return;
                scopes.Peek().Checks.Add(new Dictionary<string, object> { { "kind", "sr-threshold" }, { "type", "SpellResistance" }, { "value", __result } });
            } catch (Exception e) { Main.Error("HCO SR threshold unavailable", e); }
        }
        static Dictionary<string, object> Read(UnitEntityData actor, UnitEntityData target, AbilityData ability, IEnumerable<SpellCheckRecord> local, string weapon = null)
        {
            using (PreviewPerformance.Measure("hco-compare"))
            try { return ReadCore(actor, target, ability, local, weapon); }
            catch (Exception e) { Main.Error("HCO comparison unavailable", e); return new Dictionary<string, object> { { "status", "ReadUnavailable" } }; }
        }
        static Dictionary<string, object> ReadCore(UnitEntityData actor, UnitEntityData target, AbilityData ability, IEnumerable<SpellCheckRecord> local, string weapon)
        {
            Batch batch; recent.TryGetValue(Key(actor, target, ability), out batch);
            float age = batch == null ? -1 : Time.unscaledTime - batch.Time;
            string status = ReferenceMatchPolicy.Status(batch != null, true, age,
                batch != null && batch.After == Stamp(actor, target, ability), batch != null && batch.Before == batch.After);
            var output = new Dictionary<string, object> { { "observerStatus", Status }, { "status", status }, { "referenceVersion", version }, { "referenceMvid", mvid },
                { "ageMs", batch == null ? null : (object)(age * 1000) }, { "independentGroundTruth", false },
                { "authority", "ReferenceOnly" }, { "validationBasis", "Completed game ruling and raw inputs; HCO difference is not a bug verdict" },
                { "scope", "first/basic check values only; HCO final effect percent has different semantics" } };
            if (batch == null) return output;
            output["referenceLines"] = batch.Lines; output["referenceChecks"] = batch.Checks.ToArray(); output["referenceInArea"] = batch.InArea;
            var differences = new List<Dictionary<string, object>>();
            if (status == "ComparablePartialModel") foreach (var check in local ?? new SpellCheckRecord[0]) {
                var candidates = batch.Checks.Where(c => (string)c["kind"] == check.Kind && (check.Kind == "attack" || (string)c["type"] == check.Type)).ToArray();
                if (candidates.Length != 1) { differences.Add(new Dictionary<string, object> { { "kind", check.Kind }, { "status", candidates.Length == 0 ? "ReferenceCheckMissing" : "MultipleReferenceChecks" } }); continue; }
                var reference = candidates[0];
                if (weapon != null && check.Kind == "attack" && (!Equals(reference["weaponId"], weapon) || !Equals(reference["firstAttack"], true))) {
                    differences.Add(new Dictionary<string, object> { { "kind", "attack" }, { "status", "WeaponOrSequenceMismatch" } }); continue;
                }
                string bonus = check.Kind == "attack" ? "AttackBonus" : check.Kind == "save" ? "Bonus" : "Penetration";
                string threshold = check.Kind == "attack" ? "TargetAC" : check.Kind == "save" ? "DC" : null;
                object thresholdValue = null;
                if (threshold != null) reference.TryGetValue(threshold, out thresholdValue);
                if (check.Kind == "sr") {
                    var srValues = batch.Checks.Where(c => (string)c["kind"] == "sr-threshold").ToArray();
                    if (srValues.Length == 1) thresholdValue = srValues[0]["value"];
                }
                differences.Add(new Dictionary<string, object> { { "kind", check.Kind }, { "type", check.Type }, { "status", "RawValuesOnly" },
                    { "bonusDelta", check.Bonus.HasValue && reference.ContainsKey(bonus) && reference[bonus] is int ? (object)((long)check.Bonus.Value - (int)reference[bonus]) : null },
                    { "thresholdDelta", check.Threshold.HasValue && thresholdValue is int ? (object)((long)check.Threshold.Value - (int)thresholdValue) : null },
                    { "localProbability", check.Probability }, { "referenceRollPercent", check.Kind == "attack" ? reference["RollPercent"] : null },
                    { "localD20Model", check.D20Model }, { "localPersistentSave", check.PersistentSave },
                    { "probabilityEquivalenceVerified", false },
                    { "verdict", "ReferenceDifferenceOnly" },
                    { "probabilityComparisonScope", "local modeled rerolls versus reference reported chance; difference is not proof that either model is correct" },
                    { "rawRollProbabilityDelta", check.Kind == "attack" && check.AcOnlyProbability.HasValue && reference["RollPercent"] is int ? (object)(check.AcOnlyProbability.Value - (int)reference["RollPercent"] / 100.0) : null },
                    { "missProbabilityDelta", check.Kind == "attack" && check.VisibilityMiss.HasValue && ReferenceMiss(reference) is double miss
                        ? (object)(check.VisibilityMiss.Value - miss) : null },
                    { "mirrorImageCountDelta", check.Kind == "attack" && check.MirrorImages.HasValue && reference["MirrorImages"] is int images
                        ? (object)(check.MirrorImages.Value - images) : null },
                    { "finalEffectProbabilityDelta", null } }); // No invented equivalence to HCO combined effect odds.
            }
            output["differences"] = differences.ToArray();
            return output;
        }
        internal static Dictionary<string, object> Spell(AbilityData ability, UnitEntityData actor, UnitEntityData target, IEnumerable<SpellCheckRecord> checks)
        { using (ScopedReadMemo<UnitEntityData, string>.Begin()) return Read(actor, target, ability, checks); }

        static double? ReferenceMiss(Dictionary<string, object> attack)
        {
            if (!attack.TryGetValue("MissPercent", out var value)) return null;
            double percent = value is double d ? d : value is int n ? n : double.NaN;
            return percent >= 0 && percent <= 100 ? percent / 100 : (double?)null;
        }
        internal static Dictionary<string, object> Weapon(BetaSnapshot snapshot)
        {
            var batch = recent.Values.LastOrDefault(b => b.Ability == null && BetaIdentity.Id(b.Actor) == snapshot.Input.Actor && BetaIdentity.Id(b.Target) == snapshot.Input.Target);
            if (batch == null) return new Dictionary<string, object> { { "observerStatus", Status }, { "status", "NotObserved" } };
            var input = snapshot.Input;
            int baseAB = input.AttackBonus - input.AttackPenalties[0];
            var row = snapshot.Rows[0];
            double? roll = row.RollProbabilityAvailable ? (double?)row.HitAcOnly : null;
            var checks = new[] { new SpellCheckRecord { Kind = "attack", Type = "Attack",
                Bonus = baseAB, Threshold = input.AC, AcOnlyProbability = roll,
                Probability = row.RollProbabilityAvailable ? (double?)row.Hit : null,
                VisibilityMiss = input.ConcealmentMiss, MirrorImages = input.MirrorImages, D20Model = input.AttackRollModel?.Data() } };
            using (ScopedReadMemo<UnitEntityData, string>.Begin()) {
                var output = Read(batch.Actor, batch.Target, null, checks, snapshot.Input.Weapon);
                Local local;
                if (!locals.TryGetValue(snapshot, out local) || Time.unscaledTime < local.Time || Time.unscaledTime - local.Time > .35f || local.Stamp != batch.After) {
                    output["status"] = "LocalSnapshotExpiredOrChanged"; output.Remove("differences");
                }
                return output;
            }
        }
        internal static string LogKey(Dictionary<string, object> reference)
        {
            // Exclude age: unchanged numeric results should not log every refresh.
            return DamageTrace.Json(reference.Where(p => p.Key != "ageMs").ToDictionary(p => p.Key, p => p.Value));
        }
    }
}
