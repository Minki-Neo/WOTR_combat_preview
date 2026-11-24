using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;

namespace CombatPreview
{
    internal sealed partial class ProjectedEffectState
    {
        sealed class StatusBinding
        {
            internal readonly string Kind, Token;
            internal StatusBinding(string kind, string token) { Kind = kind; Token = token; }
        }
        ProjectedConditionCounts conditionCounts;
        readonly Dictionary<UnitCondition, StatusBinding> statusBindings = new Dictionary<UnitCondition, StatusBinding>();
        readonly Dictionary<UnitCondition, string[]> liveStatusModifiers = new Dictionary<UnitCondition, string[]>();
        readonly HashSet<string> discardedStatusModifiers = new HashSet<string>();
        readonly HashSet<string> removedStatusSources = new HashSet<string>();
        int statusSequence, statusUpdates;
        bool statusSnapshotAvailable;
        bool LiveHelplessStatus => statusBindings.TryGetValue(UnitCondition.Helpless, out var binding) && binding.Token == "live:helpless";
        bool NewHelplessStatus => statusBindings.TryGetValue(UnitCondition.Helpless, out var binding) && binding.Kind == "helpless" && binding.Token != "live:helpless";
        bool SourceRemoved(string token) => token != null && (ledger?.Removed(token) == true || removedStatusSources.Contains(token));

        void InitializeConditions(UnitEntityData target)
        {
            var counts = CombatFields.Read(target.State, "m_Conditions") as sbyte[];
            var immunities = CombatFields.Read(target.State, "m_ConditionsImmunity") as sbyte[];
            if (counts != null && immunities != null && counts.Length == immunities.Length)
                conditionCounts = new ProjectedConditionCounts(counts, immunities);
            if (!(CombatFields.Read(target.State, "m_StatusEffects") is IDictionary statuses)) return;
            statusSnapshotAvailable = true;
            foreach (DictionaryEntry pair in statuses)
            {
                if (!(pair.Key is UnitCondition condition) || pair.Value == null) continue;
                string type = pair.Value.GetType().Name;
                if (type == "UnitStatusEffectFatigued")
                {
                    statusBindings[condition] = new StatusBinding("fatigue", "live:fatigue");
                    liveStatusModifiers[condition] = new[] { CombatFields.Read(pair.Value, "m_ModifierStrength"), CombatFields.Read(pair.Value, "m_ModifierDexterity") }
                        .Where(value => value != null).Select(BetaIdentity.Id).ToArray();
                }
                else if (type == "UnitStatusEffectHelpless") statusBindings[condition] = new StatusBinding("helpless", "live:helpless");
                else if (type == "UnitStatusBuff")
                {
                    var buff = CombatFields.Read(pair.Value, "m_Buff");
                    statusBindings[condition] = new StatusBinding("buff", buff == null ? null : BetaIdentity.Id(buff));
                }
                else statusBindings[condition] = new StatusBinding("other", null);
            }
        }
        void CopyConditions(ProjectedEffectState copy)
        {
            copy.conditionCounts = conditionCounts?.Copy(); copy.statusSequence = statusSequence; copy.statusUpdates = statusUpdates;
            copy.statusSnapshotAvailable = statusSnapshotAvailable;
            foreach (var pair in statusBindings) copy.statusBindings[pair.Key] = pair.Value;
            foreach (var pair in liveStatusModifiers) copy.liveStatusModifiers[pair.Key] = pair.Value;
            foreach (string value in discardedStatusModifiers) copy.discardedStatusModifiers.Add(value);
            foreach (string value in removedStatusSources) copy.removedStatusSources.Add(value);
        }
        internal bool? HasCondition(UnitEntityData target, UnitCondition condition)
        { Initialize(target); return Unresolved ? null : conditionCounts?.Active((int)condition); }
        internal bool? HasConditionImmunity(UnitEntityData target, UnitCondition condition)
        { Initialize(target); return Unresolved ? null : conditionCounts?.Immune((int)condition); }

        void ChangeCondition(UnitCondition condition, int direction, bool immunity, UnitEntityData target, BlueprintBuff source)
        {
            if (!statusSnapshotAvailable || conditionCounts == null || !conditionCounts.Active((int)condition).HasValue)
            { Invalidate("ConditionCountLayoutUnavailable"); return; }
            bool before = conditionCounts.Active((int)condition).Value;
            if (!conditionCounts.Change((int)condition, direction, immunity))
            { Invalidate("ConditionCounterOverflowOrInvalid"); return; }
            bool active = conditionCounts.Active((int)condition).Value;
            RefreshStatus(condition, active, target, source);
            active = conditionCounts.Active((int)condition).Value;
            if (before != active)
                foreach (var trigger in EventComponents(target).OfType<AddConditionTrigger>())
                    if (trigger.Conditions?.Contains(condition) == true && trigger.Action?.Actions?.Length > 0)
                        Invalidate("ConditionTransitionAction:" + condition);
        }
        static string StatusBuffName(UnitCondition condition) => condition == UnitCondition.DeathDoor ? "DeathDoor"
            : condition == UnitCondition.Petrified ? "Petrified" : condition == UnitCondition.Entangled ? "Entangled"
            : condition == UnitCondition.Shaken ? "Shaken" : condition == UnitCondition.Frightened ? "Frightened"
            : condition == UnitCondition.Sickened ? "Sickened" : condition == UnitCondition.Stunned ? "Stunned"
            : condition == UnitCondition.Cowering ? "Cowering" : null;
        void RefreshStatus(UnitCondition condition, bool active, UnitEntityData target, BlueprintBuff source)
        {
            statusBindings.TryGetValue(condition, out var existing);
            string name = StatusBuffName(condition);
            var reference = name == null ? null : CombatFields.Read(Game.Instance.BlueprintRoot.SystemMechanics, "m_" + name + "Buff");
            bool isSelf = reference != null && source != null && CombatFields.Reference(reference, source);
            var transition = ProjectedConditionCounts.Transition(active, existing != null, isSelf);
            if (transition == StatusTransition.None) return;
            if (++statusUpdates > 64) { Invalidate("StatusTransitionBudget"); return; }
            if (transition == StatusTransition.Deactivate)
            {
                statusBindings.Remove(condition);
                if (existing.Kind == "fatigue")
                {
                    if (existing.Token == "live:fatigue")
                    {
                        if (!liveStatusModifiers.TryGetValue(condition, out var modifiers) || modifiers.Length != 2)
                            Invalidate("FatigueModifierIdentityUnavailable");
                        else foreach (string id in modifiers) discardedStatusModifiers.Add(id);
                    }
                    else removedStatusSources.Add(existing.Token);
                }
                else if (existing.Kind == "helpless") ClearReflexStatusModifiers(target);
                else if (existing.Kind == "buff" && existing.Token != null) RemoveStatusBuff(existing.Token, target);
                else if (existing.Kind == "other" && condition != UnitCondition.Prone && condition.ToString() != "Dazzled")
                    Invalidate("StatusDeactivation:" + condition);
                return;
            }
            string token = "status:" + (int)condition + ":" + statusSequence++;
            if (condition == UnitCondition.Fatigued)
            {
                statusBindings[condition] = new StatusBinding("fatigue", token);
                Add(StatType.Strength, -2, ModifierDescriptor.Fatigued, token);
                Add(StatType.Dexterity, -2, ModifierDescriptor.Fatigued, token);
            }
            else if (condition == UnitCondition.Helpless)
            {
                ClearReflexStatusModifiers(target);
                statusBindings[condition] = new StatusBinding("helpless", token);
            }
            else if (name != null)
            {
                var blueprint = LoadedBlueprintReader.Read(reference) as BlueprintBuff;
                if (blueprint == null) { Invalidate("StatusBuffNotLoaded:" + name); return; }
                // UnitState stores the status object before activating its buff.
                // Keep that placeholder during nested AddCondition calls.
                statusBindings[condition] = new StatusBinding("buff", null);
                BuffApplicationReader.Apply(null, target, target, blueprint, this, this, out string applied);
                if (statusBindings.ContainsKey(condition)) statusBindings[condition] = new StatusBinding("buff", applied);
            }
            else if (condition == UnitCondition.Prone || condition.ToString() == "Dazzled")
                statusBindings[condition] = new StatusBinding("other", null);
            else Invalidate("ConditionEffectOutsideSaveProjection:" + condition);
        }
        void RemoveStatusBuff(string token, UnitEntityData target)
        {
            var entry = ledger.Find(token);
            if (entry == null || entry.Removed) return;
            // A status owns a particular buff instance, not the first blueprint
            // match. Remove its detached identity exactly once.
            RemoveEntry(entry, definitions[entry.Blueprint], target);
        }
        void ClearReflexStatusModifiers(UnitEntityData target)
        {
            if (CombatFields.Read(target.Stats.SaveReflex, "ModifierList") is IDictionary groups)
            {
                foreach (DictionaryEntry group in groups)
                    foreach (var modifier in (IEnumerable)group.Value)
                        if (CombatFields.Int(modifier, "ModDescriptor") == 40) discardedStatusModifiers.Add(BetaIdentity.Id(modifier));
            }
            else { Invalidate("HelplessModifierListUnavailable"); return; }
            if (additions.TryGetValue(StatType.SaveReflex, out var pending))
                foreach (var modifier in pending.Where(modifier => modifier.Descriptor == 40)) discardedStatusModifiers.Add(BetaIdentity.Id(modifier));
        }
        object ConditionDiagnostics() => conditionCounts?.Touched.Select(index => new Dictionary<string, object> {
            { "condition", ((UnitCondition)index).ToString() }, { "sources", conditionCounts.Sources(index) },
            { "immune", conditionCounts.Immune(index) }, { "active", conditionCounts.Active(index) },
            { "statusKind", statusBindings.TryGetValue((UnitCondition)index, out var binding) ? binding.Kind : null }
        }).ToArray();
    }
}
