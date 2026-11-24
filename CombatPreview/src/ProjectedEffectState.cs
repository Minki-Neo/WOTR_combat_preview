using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.Designers.Mechanics.Facts;

namespace CombatPreview
{
    internal sealed partial class ProjectedEffectState
    {
        readonly Dictionary<StatType, List<ProjectedModifier>> additions = new Dictionary<StatType, List<ProjectedModifier>>();
        readonly HashSet<string> buffs = new HashSet<string>();
        ProjectedBuffLedger ledger;
        readonly Dictionary<string, BlueprintBuff> definitions = new Dictionary<string, BlueprintBuff>();
        internal bool Unresolved;
        internal bool HasStatProjectionChanges => additions.Any(pair => pair.Value.Count > 0)
            || ledger?.HasRemovals == true || ledger?.HasRankChanges == true || discardedStatusModifiers.Count > 0;
        internal readonly HashSet<string> Reasons = new HashSet<string>();
        internal void Invalidate(string reason) { Unresolved = true; Reasons.Add(reason); }
        internal ProjectedEffectState Copy()
        {
            var copy = new ProjectedEffectState { Unresolved = Unresolved };
            foreach (var entry in additions) copy.additions[entry.Key] = new List<ProjectedModifier>(entry.Value);
            foreach (var id in buffs) copy.buffs.Add(id);
            CopyConditions(copy);
            copy.ledger = ledger?.Copy();
            foreach (var entry in definitions) copy.definitions[entry.Key] = entry.Value;
            foreach (var reason in Reasons) copy.Reasons.Add(reason);
            return copy;
        }
        void Initialize(UnitEntityData target)
        {
            if (ledger != null) return;
            ledger = new ProjectedBuffLedger();
            foreach (var buff in target.Buffs.Enumerable)
            {
                string id = buff.Blueprint.AssetGuid.ToString();
                definitions[id] = buff.Blueprint;
                var descriptor = CombatFields.Read(buff.Context, "<SpellDescriptor>k__BackingField");
                ledger.Seed(BetaIdentity.Id(buff), id, BetaIdentity.Id(buff.Context?.MaybeCaster), CombatFields.Rank(buff),
                    descriptor is Kingmaker.Blueprints.Classes.Spells.SpellDescriptor mask ? (ulong?)unchecked((ulong)mask) : null, buff.Active);
            }
            InitializeConditions(target);
        }
        internal IEnumerable<BlueprintComponent> EventComponents(UnitEntityData target)
        {
            Initialize(target);
            foreach (var fact in target.Facts.List)
            {
                if (!fact.Active || ledger.Removed(BetaIdentity.Id(fact))) continue;
                foreach (var runtime in fact.Components)
                    if (CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField"))
                        yield return CombatFields.Component(runtime) as BlueprintComponent;
            }
            foreach (var entry in ledger.Active.Where(entry => !entry.Live))
                foreach (var component in definitions[entry.Blueprint].ComponentsArray) yield return component;
        }
        internal void Remove(BlueprintBuff buff, UnitEntityData target, UnitEntityData caster, bool onlyCaster, bool rankOnly)
        {
            if (buff == null) { Invalidate("RemoveBuffDefinitionNotLoaded"); return; }
            Initialize(target);
            var removed = ledger.RemoveFirst(buff.AssetGuid.ToString(), onlyCaster ? BetaIdentity.Id(caster) : null, rankOnly);
            if (removed == null) return;
            if (!removed.Removed)
            {
                RefreshRank(removed, buff, target);
                return;
            }
            FinishRemoval(removed, buff, target);
        }
        void RemoveEntry(ProjectedBuffEntry entry, BlueprintBuff buff, UnitEntityData target)
        { if (ledger.RemoveToken(entry.Token) != null) FinishRemoval(entry, buff, target); }
        void FinishRemoval(ProjectedBuffEntry removed, BlueprintBuff buff, UnitEntityData target)
        {
            if (!ledger.Active.Any(entry => entry.Blueprint == removed.Blueprint)) buffs.Remove(removed.Blueprint);
            // Unknown deactivate actions/condition sources can change more than
            // the detached modifier list. Do not certify those downstream saves.
            foreach (var component in buff.ComponentsArray)
                if (removed.Active && component?.GetType() == typeof(AddCondition))
                    ChangeCondition(((AddCondition)component).Condition, -1, false, target, null);
                else if (removed.Active && component?.GetType() == typeof(AddConditionImmunity))
                    ChangeCondition(((AddConditionImmunity)component).Condition, -1, true, target, null);
                else if (component != null && component.GetType() != typeof(AddCondition) && component.GetType() != typeof(AddConditionImmunity)
                    && component.GetType() != typeof(AddStatBonus) && component.GetType() != typeof(AddContextStatBonus) && !Passive(component)) Invalidate("BuffDeactivation:" + component.GetType().Name);
        }
        void RefreshRank(ProjectedBuffEntry entry, BlueprintBuff buff, UnitEntityData target)
        {
            if (!entry.Active) return;
            foreach (var component in buff.ComponentsArray)
                if (component != null && component.GetType() != typeof(AddStatBonus) && !Passive(component)
                    && component.GetType() != typeof(AddCondition) && component.GetType() != typeof(AddConditionImmunity)
                    && !(component.GetType() == typeof(AddContextStatBonus) && ProjectedBuffContext.RankIndependent((AddContextStatBonus)component, buff)))
                    Invalidate("RankRefresh:" + component.GetType().Name);
            foreach (int direction in new[] { -1, 1 })
                foreach (var component in buff.ComponentsArray)
                    if (component?.GetType() == typeof(AddCondition))
                        ChangeCondition(((AddCondition)component).Condition, direction, false, target, direction > 0 ? buff : null);
                    else if (component?.GetType() == typeof(AddConditionImmunity))
                        ChangeCondition(((AddConditionImmunity)component).Condition, direction, true, target, direction > 0 ? buff : null);
        }
        static bool Passive(BlueprintComponent component) => component.GetType().Name == "SpellDescriptorComponent"
            || component.GetType() == typeof(SpecificBuffImmunity) || component.GetType() == typeof(BuffSubstitutionOnApply)
            || component.GetType() == typeof(Kingmaker.UnitLogic.Mechanics.Components.ContextRankConfig);
        internal void RemoveDescriptor(Kingmaker.Blueprints.Classes.Spells.SpellDescriptor descriptor, UnitEntityData target, UnitEntityData caster)
        {
            Initialize(target);
            // This branch interpreter runs direct ability actions, outside a
            // Buff.Data/area activation scope: NotSelf has no current buff here.
            var snapshot = ledger.DescriptorSnapshot(unchecked((ulong)descriptor), out bool unknown);
            if (unknown) Invalidate("BuffDescriptorContextUnavailable");
            foreach (var entry in snapshot)
            {
                // The game's snapshot selects by context descriptor, then calls
                // GetBuff(blueprint).Remove, which removes the first current copy.
                Remove(definitions[entry.Blueprint], target, caster, false, false);
            }
        }
        internal ProjectedBuffEntry Apply(BlueprintBuff buff, UnitEntityData target, UnitEntityData caster = null,
            Kingmaker.Blueprints.Classes.Spells.SpellDescriptor descriptor = Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None,
            ProjectedBuffContext context = null)
        {
            if (buff == null) { Invalidate("ApplyBuffDefinitionNotLoaded"); return null; }
            Initialize(target);
            string id = buff.AssetGuid.ToString();
            bool present = ledger.Active.Any(entry => entry.Blueprint == id);
            string stacking = CombatFields.Read(buff, "Stacking")?.ToString();
            if (present && (stacking == "Prolong" || stacking == "Ignore")) return ledger.Active.First(entry => entry.Blueprint == id);
            if (present && stacking == "Rank")
            {
                var changed = ledger.AddRank(id, 1, buff.Ranks);
                if (changed != null) RefreshRank(changed, buff, target);
                return ledger.Active.First(entry => entry.Blueprint == id);
            }
            if (present && stacking == "Replace") Remove(buff, target, caster, false, false);
            else if (present && stacking != "Stack") { Invalidate("BuffStacking:" + stacking); return null; }
            buffs.Add(id);
            definitions[id] = buff;
            var ownDescriptor = buff.ComponentsArray.OfType<Kingmaker.Blueprints.Classes.Spells.SpellDescriptorComponent>().FirstOrDefault();
            var source = ledger.Add(id, BetaIdentity.Id(caster), BuffStatMath.Descriptor(unchecked((ulong)descriptor),
                ownDescriptor == null ? 0 : unchecked((ulong)(Kingmaker.Blueprints.Classes.Spells.SpellDescriptor)ownDescriptor.Descriptor)));
            context = context ?? new ProjectedBuffContext();
            if (buffs.Count > 32) { Invalidate("ProjectedBuffBudget"); return null; }
            foreach (var component in buff.ComponentsArray)
            {
                if (component?.GetType() == typeof(AddStatBonus))
                {
                    var bonus = (AddStatBonus)component;
                    int amount = bonus.Value;
                    if (bonus.ScaleByBasicAttackBonus)
                    {
                        if (!(CombatFields.Read(target.Stats.BaseAttackBonus, "<ModifiedValue>k__BackingField") is int bab)) { Invalidate("BuffBaseAttackBonusUnavailable"); continue; }
                        amount = SupportedCombatRules.AttackModifier(amount, bab, 1, true);
                    }
                    int? adjusted = BuffStatMath.ContextBonus(amount, 1, bonus.Stat.IsAttribute() ? context.PowerfulChange : 0, null);
                    if (!adjusted.HasValue) { Invalidate("BuffPowerfulChangeUnavailable"); continue; }
                    if (!additions.TryGetValue(bonus.Stat, out var list)) additions[bonus.Stat] = list = new List<ProjectedModifier>();
                    list.Add(new ProjectedModifier { Descriptor = (int)bonus.Descriptor, Value = adjusted.Value, Stacks = bonus.Descriptor.IsStackable(), Source = source.Token, Component = bonus.name });
                }
                else if (component?.GetType() == typeof(AddContextStatBonus))
                {
                    var bonus = (AddContextStatBonus)component;
                    int? amount = context.Bonus(bonus, buff, caster);
                    if (!amount.HasValue) { Invalidate("BuffContextStatValueOrRestrictionUnavailable:" + bonus.name); continue; }
                    if (!additions.TryGetValue(bonus.Stat, out var list)) additions[bonus.Stat] = list = new List<ProjectedModifier>();
                    list.Add(new ProjectedModifier { Descriptor = (int)bonus.Descriptor, Value = amount.Value,
                        Stacks = bonus.Descriptor.IsStackable(), Source = source.Token, Component = bonus.name });
                }
                else if (component?.GetType() == typeof(AddCondition)) ChangeCondition(((AddCondition)component).Condition, 1, false, target, buff);
                else if (component?.GetType() == typeof(AddConditionImmunity)) ChangeCondition(((AddConditionImmunity)component).Condition, 1, true, target, buff);
                else if (component != null && !Passive(component)) Invalidate("BuffActivation:" + component.GetType().Name);
            }
            return source;
        }
        internal bool? HasFact(UnitEntityData target, object reference)
        {
            if (reference == null) return false;
            Initialize(target);
            var loaded = LoadedBlueprintReader.Read(reference);
            if (loaded != null && buffs.Contains(loaded.AssetGuid.ToString())) return true;
            string guid = CombatFields.Read(reference, "guid") as string;
            if (guid != null && buffs.Any(id => string.Equals(id.Replace("-", ""), guid.Replace("-", ""), StringComparison.OrdinalIgnoreCase))) return true;
            return target.Facts.List.Any(fact => fact.Active && !ledger.Removed(BetaIdentity.Id(fact))
                && CombatFields.Reference(reference, CombatFields.Blueprint(fact))) ? true : Unresolved ? (bool?)null : false;
        }
        void Add(StatType stat, int value, ModifierDescriptor descriptor, string source = null)
        {
            if (!additions.TryGetValue(stat, out var list)) additions[stat] = list = new List<ProjectedModifier>();
            list.Add(new ProjectedModifier { Descriptor = (int)descriptor, Value = value, Stacks = descriptor.IsStackable(), Source = source });
        }
        internal int? Save(UnitEntityData target, SavingThrowType type)
        {
            if (Unresolved) return null;
            var stat = type == SavingThrowType.Reflex ? target.Stats.SaveReflex : type == SavingThrowType.Fortitude
                ? target.Stats.SaveFortitude : type == SavingThrowType.Will ? target.Stats.SaveWill : null;
            if (stat == null) return null;
            int? value = Value(stat);
            var attribute = type == SavingThrowType.Reflex ? target.Stats.Dexterity
                : type == SavingThrowType.Fortitude ? target.Stats.Constitution : target.Stats.Wisdom;
            // Respect the current save's actual base attribute/replacement.
            var actualAttribute = CombatFields.Read(stat, "BaseStat") as ModifiableValueAttributeStat;
            if (actualAttribute != null) attribute = actualAttribute;
            if (!(Value(attribute) is int projected) || !(CombatFields.CachedStat(attribute) is int current)) return null;
            value += (int)Math.Floor((projected - 10) / 2.0) - (int)Math.Floor((current - 10) / 2.0);
            if (type == SavingThrowType.Reflex && (NewHelplessStatus || LiveHelplessStatus))
            {
                if (!(Value(target.Stats.Dexterity) is int changedDex)) return null;
                if (NewHelplessStatus) value += -5 - (int)Math.Floor((changedDex - 10) / 2.0);
                else
                {
                    if (!(CombatFields.CachedStat(target.Stats.Dexterity) is int oldDex)) return null;
                // The live Helpless handler updates its Reflex modifier when Dexterity
                // changes. Cancel that same delta in the projected copy as well.
                    value -= (int)Math.Floor((changedDex - 10) / 2.0) - (int)Math.Floor((oldDex - 10) / 2.0);
                }
            }
            return Unresolved ? null : value;
        }
        int? Value(ModifiableValue stat)
        {
            if (!(CombatFields.CachedStat(stat) is int current)) return null;
            if (!additions.TryGetValue(stat.Type, out var added)) added = new List<ProjectedModifier>();
            if (added.Count == 0 && ledger?.HasRemovals != true && ledger?.HasRankChanges != true && discardedStatusModifiers.Count == 0) return current;
            if (!(CombatFields.Read(stat, "ModifierList") is IDictionary groups)) return null;
            var existing = new List<ProjectedModifier>();
            foreach (DictionaryEntry group in groups)
                foreach (var modifier in (IEnumerable)group.Value)
                {
                    var descriptor = (ModifierDescriptor)CombatFields.Read(modifier, "ModDescriptor");
                    int mode = CombatFields.Int(modifier, "StackMode");
                    existing.Add(new ProjectedModifier { Descriptor = (int)descriptor, Source = BetaIdentity.Id(CombatFields.Read(modifier, "Source")),
                        Value = CombatFields.Int(modifier, "ModValue"), Component = CombatFields.Read(modifier, "SourceComponent") as string,
                        Stacks = mode == 1 || mode == 0 && descriptor.IsStackable(), Identity = BetaIdentity.Id(modifier) });
                }
            bool Allowed(ProjectedModifier modifier) => (!stat.MoraleBonusesDisabled || modifier.Value < 0 || modifier.Descriptor != 15)
                && (!stat.InsightBonusesDisabled || (modifier.Value < 0 || modifier.Descriptor != 15)
                    && modifier.Descriptor != 13 && modifier.Descriptor != 3);
            if (stat is ModifiableValueAttributeStat && existing.Concat(added).Any(modifier => modifier.Descriptor == 40 && !SourceRemoved(modifier.Source)
                && !discardedStatusModifiers.Contains(modifier.Identity ?? BetaIdentity.Id(modifier)))) return 0;
            if (!(CombatFields.Read(stat, "<ModifiedValue>k__BackingField") is int unfiltered)) return null;
            ProjectedModifier ReplaceRank(ProjectedModifier original)
            {
                if (discardedStatusModifiers.Contains(original.Identity ?? BetaIdentity.Id(original))) return new ProjectedModifier {
                    Descriptor = original.Descriptor, Source = original.Source, Component = original.Component,
                    Stacks = original.Stacks, Identity = original.Identity, Value = 0 };
                var source = original.Source == null ? null : ledger?.Find(original.Source);
                if (source == null || source.Removed || source.Rank == source.OriginalRank) return original;
                var contextual = definitions[source.Blueprint].ComponentsArray.OfType<AddContextStatBonus>()
                    .Where(bonus => bonus.GetType() == typeof(AddContextStatBonus) && bonus.Stat == stat.Type
                        && (int)bonus.Descriptor == original.Descriptor && bonus.name == original.Component).ToArray();
                if (contextual.Length == 1)
                {
                    if (ProjectedBuffContext.RankIndependent(contextual[0], definitions[source.Blueprint])) return original;
                    Invalidate("RankContextStatRecalculationUnavailable"); return original;
                }
                var candidates = definitions[source.Blueprint].ComponentsArray.OfType<AddStatBonus>()
                    .Where(bonus => bonus.GetType() == typeof(AddStatBonus) && bonus.Stat == stat.Type && (int)bonus.Descriptor == original.Descriptor).ToArray();
                // AddModifierUnique identifies a particular source component.
                // With a single matching component the source/descriptor is sufficient.
                if (candidates.Length > 1) candidates = candidates.Where(bonus => bonus.name == original.Component).ToArray();
                if (candidates.Length != 1) { Invalidate("RankModifierSourceAmbiguous"); return original; }
                var bonus = candidates[0];
                int perRank = bonus.Value;
                if (bonus.ScaleByBasicAttackBonus)
                {
                    // Reuse the cached BAB, never recalculate a live stat.
                    var stats = CombatFields.Read(stat, "m_Stats") as CharacterStats;
                    var bab = stats?.BaseAttackBonus;
                    if (!(CombatFields.CachedStat(bab) is int value)) { Invalidate("RankBaseAttackBonusUnavailable"); return original; }
                    perRank = checked(perRank * (1 + value / 4));
                }
                // Powerful Change is an additive constant stored by the live
                // component. Preserve it by changing only the per-rank portion.
                var valueAfterRank = BuffStatMath.RankValue(original.Value, perRank, source.OriginalRank, source.Rank);
                if (!valueAfterRank.HasValue) { Invalidate("RankStatOverflowOrInvalidRank"); return original; }
                return new ProjectedModifier { Descriptor = original.Descriptor, Source = original.Source,
                    Component = original.Component, Stacks = original.Stacks, Identity = original.Identity,
                    Value = valueAfterRank.Value };
            }
            int projected = ProjectedStatMath.ProjectFiltered(unfiltered, existing, added.Select(ReplaceRank), Allowed, SourceRemoved, ReplaceRank);
            if (Unresolved) return null;
            return stat is ModifiableValueAttributeStat ? Math.Max(0, projected) : projected;
        }
        internal Dictionary<string, object> Diagnostics() => new Dictionary<string, object> {
            { "conditions", ConditionDiagnostics() },
            { "buffChanges", ledger?.Changes.Select(entry => new Dictionary<string, object> {
                { "source", entry.Token }, { "blueprintId", entry.Blueprint }, { "caster", entry.Caster },
                { "rank", entry.Rank }, { "originalRank", entry.OriginalRank }, { "removed", entry.Removed },
                { "live", entry.Live }, { "contextDescriptor", entry.Descriptor?.ToString("X") }
            }).ToArray() },
            { "addedStatModifiers", additions.SelectMany(pair => pair.Value.Select(modifier => new Dictionary<string, object> {
                { "stat", pair.Key.ToString() }, { "initialValue", modifier.Value }, { "descriptor", modifier.Descriptor },
                { "source", modifier.Source }, { "component", modifier.Component }
            })).ToArray() }, { "unresolved", Unresolved }
        };
    }
}
