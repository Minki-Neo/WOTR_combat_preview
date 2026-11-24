using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace CombatPreview
{
    // Completed real/provenance-labelled events only; no roll or rule is executed.
    internal sealed partial class PassiveAttackDiagnostics : IGlobalRulebookHandler<RuleSavingThrow>, IGlobalRulebookHandler<RuleSpellResistanceCheck>
    {
        internal sealed class SpellSource { internal AbilityData Ability; internal BlueprintAbility Blueprint; internal UnitEntityData Caster; internal string ContextId; internal object MechanicsContext; }
        sealed class SpellStageInput { internal LoggedSpellPreview Preview; internal string Stamp; internal long Age; }
        sealed class SpellCastLink { internal SpellStageInput First; internal int Ordinal; }
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<RulebookEvent, SpellStageInput> spellStageInputs
            = new System.Runtime.CompilerServices.ConditionalWeakTable<RulebookEvent, SpellStageInput>();
        readonly Dictionary<string, SpellCastLink> spellCastLinks = new Dictionary<string, SpellCastLink>();
        void CaptureSpellInput(RulebookEvent evt, UnitEntityData target, object context = null)
        {
            if (evt == null || target == null || !CanObserve() || DryRun.Active) return;
            try
            {
                var source = Source(evt, context);
                var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
                if (source.Ability == null || source.Caster == null || source.ContextId == null || turn == null) return;
                if (!recentSpellPreviews.TryGetValue(SpellPreviewKey(Id(source.Caster), Id(target), Id(source.Ability), Id(turn)), out var preview)) return;
                long age = clock.ElapsedMilliseconds - preview.Time;
                if (age < 0 || age > 10000) return;
                using (PreviewPerformance.Measure("spell-log-input-association"))
                using (ScopedReadMemo<UnitEntityData, string>.Begin())
                {
                    spellStageInputs.Remove(evt);
                    spellStageInputs.Add(evt, new SpellStageInput { Preview = preview, Age = age,
                        Stamp = SpellPreviewRuntime.InputStamp(source.Ability, source.Caster, target) });
                }
            }
            catch (Exception e) { Main.Error("Spell input association unavailable", e); }
        }
        static UnitEntityData CachedUnit(object reference)
        { return CombatFields.Read(CombatFields.Read(reference, "m_Proxy"), "<Entity>k__BackingField") as UnitEntityData; }
        internal static SpellSource Source(RulebookEvent evt, object suppliedContext = null)
        {
            var result = new SpellSource();
            var reason = CombatFields.Read(evt, "<Reason>k__BackingField");
            object context = suppliedContext;
            for (int i = 0; reason != null && i < 4; i++)
            {
                result.Ability = result.Ability ?? CombatFields.Read(reason, "<Ability>k__BackingField") as AbilityData;
                result.Caster = result.Caster ?? CombatFields.Read(reason, "<Caster>k__BackingField") as UnitEntityData;
                context = context ?? CombatFields.Read(reason, "<Context>k__BackingField");
                // Periodic area/buff saves often carry a source fact instead of
                // AbilityData directly. Read its existing mechanics context only.
                var fact = CombatFields.Read(reason, "<Fact>k__BackingField");
                context = context ?? CombatFields.Read(fact, "m_Context");
                var parent = CombatFields.Read(reason, "<Rule>k__BackingField");
                reason = CombatFields.Read(parent, "<Reason>k__BackingField");
            }
            result.MechanicsContext = context;
            for (int i = 0; context != null && i < 4; i++)
            {
                if (context is AbilityExecutionContext) result.ContextId = BetaIdentity.Id(context);
                result.Ability = result.Ability ?? CombatFields.Read(context, "Ability") as AbilityData;
                result.Blueprint = result.Blueprint ?? CombatFields.Read(context, "AssociatedBlueprint") as BlueprintAbility;
                result.Caster = result.Caster ?? CachedUnit(CombatFields.Read(context, "m_CasterRef"));
                context = CombatFields.Read(context, "<ParentContext>k__BackingField");
            }
            if (result.Ability != null)
            { result.Blueprint = result.Ability.Blueprint; result.Caster = result.Caster ?? SpellPreviewRuntime.Caster(result.Ability); }
            return result;
        }
        void SpellObservation(RulebookEvent evt, SpellSource source, UnitEntityData target, Dictionary<string, object> values)
        {
            if (target == null) return;
            values["blueprintId"] = source.Blueprint?.AssetGuid.ToString();
            values["abilityInstanceId"] = source.Ability == null ? null : Id(source.Ability);
            values["sourceResolved"] = source.Caster != null && source.Blueprint != null;
            values["castContextId"] = source.ContextId;
            values["ruleEventId"] = Id(evt);
            values["rawRuleInputs"] = CompletedAttackEvidence.RuleDetails(evt);
            if (source.ContextId != null)
            {
                string castKey = source.ContextId + "|" + Id(target);
                if (!spellCastLinks.TryGetValue(castKey, out var link))
                {
                    if (spellCastLinks.Count >= 128) spellCastLinks.Clear();
                    spellStageInputs.TryGetValue(evt, out var first);
                    spellCastLinks[castKey] = link = new SpellCastLink { First = first };
                }
                values["stageOrdinalForCastTarget"] = ++link.Ordinal;
                if (link.First != null)
                {
                    values["frozenPreviewId"] = link.First.Preview.Id;
                    values["frozenDisplayedInputs"] = link.First.Preview.RawSnapshot;
                    values["frozenPreviewLoggedSeparately"] = link.First.Preview.LoggedSeparately;
                    values["frozenPreviewAgeMsAtFirstStage"] = link.First.Age;
                    values["association"] = link.First.Preview.InputStamp == null ? "PreviewStampMissing"
                        : link.First.Preview.InputStamp == link.First.Stamp ? "TrackedInputsMatchAtFirstRule" : "TrackedInputsChangedBeforeFirstRule";
                    values["associationScope"] = "same cast/target, frozen first rule; tracked inputs only, not proof of every game condition";
                }
            }
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var previewKey = SpellPreviewKey(Id(source.Caster), Id(target), Id(source.Ability), Id(turn));
            if (source.Ability != null && source.Caster != null && source.ContextId != null && turn != null
                && recentSpellPreviews.TryGetValue(previewKey, out var preview))
            {
                long age = clock.ElapsedMilliseconds - preview.Time;
                if (age >= 0 && age <= 2000)
                {
                    values["previewCandidateId"] = preview.Id;
                    values["candidateDisplayedInputs"] = preview.RawSnapshot;
                    values["previewAgeMs"] = age;
                    string kind = (string)values["checkKind"];
                    var matching = preview.Checks?.Where(c => c.Kind == kind && c.Status != "NotIdentified"
                        && (kind != "save" || Equals(c.Type, values["saveType"]))).Take(2).ToArray();
                    if (matching?.Length == 1) values["previewCheckCandidate"] = matching[0].Data();
                }
            }
            recorder.RecordSpellObservation("spell:" + values["checkKind"] + ":" + Id(evt), new DiagnosticContext(Id(source.Caster), Id(target),
                source.Blueprint?.AssetGuid.ToString() ?? "unresolved-ability", Id(turn), "unknown", -1, "", false), values);
        }
        void ObserveSpellDamage(Kingmaker.RuleSystem.Rules.Damage.RuleDealDamage evt)
        {
            try
            {
                var source = Source(evt);
                if (source.Blueprint == null && evt.AttackRoll != null) source = Source(evt.AttackRoll);
                if (source.Blueprint == null) return;
                source.Caster = source.Caster ?? evt.Initiator;
                SpellObservation(evt, source, evt.Target, new Dictionary<string, object>
                {
                    { "checkKind", "damage" },
                    { "damageInitiatorId", Id(evt.Initiator) },
                    { "actualContextSharedValues", (CombatFields.Read(source.MechanicsContext, "m_SharedValues") as int[])?.Take(32).ToArray() },
                    { "actualContextRanks", (CombatFields.Read(source.MechanicsContext, "m_Ranks") as int[])?.Take(32).ToArray() },
                    { "rollEventId", evt.AttackRoll == null ? null : Id(evt.AttackRoll) },
                    { "reportedDamage", evt.Result },
                    { "rawDamage", evt.RawResult },
                    { "beforeDifficulty", evt.DamageBeforeDifficulty },
                    { "damageTrace", CaptureDamageTrace(evt, null) }
                });
            }
            catch (Exception e) { Main.Error("Spell damage diagnostic unavailable", e); }
        }
        void ObserveSpellAttack(RuleAttackRoll evt)
        {
            try
            {
                var source = Source(evt);
                if (source.Blueprint == null) return;
                SpellObservation(evt, source, evt.Target, new Dictionary<string, object> {
                    { "checkKind", "attack" }, { "attackType", evt.AttackType.ToString() },
                    { "ab", evt.AttackBonus }, { "ac", evt.TargetAC }, { "die", GameRules.Die(evt) },
                    { "hit", evt.IsHit }, { "autoHit", evt.AutoHit }, { "autoMiss", evt.AutoMiss },
                    { "ignoreConcealment", evt.IgnoreConcealment }, { "visibility", AttackConcealment.Observed(evt) }, { "isFake", false }
                    , { "rawInputs", CompletedAttackEvidence.Capture(evt) }
                });
            }
            catch (Exception e) { Main.Error("Spell attack diagnostic unavailable", e); }
        }
        public void OnEventAboutToTrigger(RuleSavingThrow evt) { if (evt?.D20?.IsFake != true) CaptureSpellInput(evt, evt?.Initiator); }
        static Dictionary<string, object> SavingThrowModifiers(RuleSavingThrow evt)
        {
            var captured = CombatFields.Read(evt, "<StatModifiersAtTheMoment>k__BackingField") as System.Collections.IEnumerable;
            if (captured == null) return null;
            var entries = captured.Cast<object>().Take(33).ToArray();
            return new Dictionary<string, object> {
                { "truncated", entries.Length > 32 },
                { "statType", CombatFields.Read(evt, "<StatType>k__BackingField")?.ToString() },
                { "entries", entries.Take(32).Select(modifier => {
                    var source = CombatFields.Read(modifier, "Source");
                    return new Dictionary<string, object> {
                        { "value", CombatFields.Read(modifier, "ModValue") }, { "descriptor", CombatFields.Read(modifier, "ModDescriptor")?.ToString() },
                        { "component", CombatFields.Read(modifier, "SourceComponent") }, { "sourceId", source == null ? null : BetaIdentity.Id(source) },
                        { "blueprintId", (CombatFields.Blueprint(source) as Kingmaker.Blueprints.SimpleBlueprint)?.AssetGuid.ToString() },
                        { "source", source == null ? null : CombatFields.Label(source) }
                    };
                }).ToArray() },
                { "scope", "engine-captured stat modifiers; base attribute and separate rule bonuses are not an exhaustive source decomposition" }
            };
        }
        public void OnEventDidTrigger(RuleSavingThrow evt)
        {
            if (!CanObserve() || evt == null || DryRun.Active || evt.D20?.IsFake == true) return;
            try
            {
                SpellObservation(evt, Source(evt), evt.Initiator, new Dictionary<string, object> {
                    { "checkKind", "save" }, { "saveType", evt.Type.ToString() }, { "dc", evt.DifficultyClass },
                    { "bonus", evt.StatValue }, { "die", evt.D20 == null ? null : (object)evt.D20.Result },
                    { "passed", evt.IsPassed }, { "autoPass", evt.AutoPass }, { "persistent", evt.PersistentSpell },
                    { "successBonus", evt.SuccessBonus }, { "alternativePassed", evt.IsAlternativePassed },
                    { "rollHistory", evt.D20?.RollHistory?.Take(8).ToArray() },
                    { "d20", D20Observation.Capture(evt.D20) },
                    { "statModifiers", SavingThrowModifiers(evt) },
                    { "simulationProvenance", evt.D20 == null ? "NoD20: unverified" : "D20-not-fake" }
                });
            }
            catch (Exception e) { Main.Error("Spell save diagnostic unavailable", e); }
        }
        public void OnEventAboutToTrigger(RuleSpellResistanceCheck evt) { if (evt?.Roll?.IsFake != true) CaptureSpellInput(evt, evt?.Target, evt?.Context); }
        public void OnEventDidTrigger(RuleSpellResistanceCheck evt)
        {
            if (!CanObserve() || evt == null || DryRun.Active || evt.Roll?.IsFake == true) return;
            try
            {
                var source = Source(evt, evt.Context); source.Blueprint = source.Blueprint ?? evt.Ability;
                source.Caster = source.Caster ?? evt.Initiator;
                SpellObservation(evt, source, evt.Target, new Dictionary<string, object> {
                    { "checkKind", "sr" }, { "penetration", evt.SpellPenetration }, { "sr", evt.SpellResistance },
                    { "die", evt.Roll == null ? null : (object)evt.Roll.Result }, { "resisted", evt.IsSpellResisted },
                    { "d20", D20Observation.Capture(evt.Roll) },
                    { "targetImmune", evt.TargetIsImmune }, { "ignoreSR", evt.IgnoreSpellResistance },
                    { "simulationProvenance", evt.Roll == null ? "NoD20: unverified" : "D20-not-fake" }
                });
            }
            catch (Exception e) { Main.Error("Spell SR diagnostic unavailable", e); }
        }
    }
}
