using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Parts;
namespace CombatPreview
{
    // Reproduces base AutoMetamagic predicates from stored data. Never calls
    // ShouldApplyTo, rule triggers, reference.Get, or consumes a once-only buff.
    internal static class AutoMetamagicReader
    {
        internal sealed class Result
        {
            internal bool Available;
            internal Metamagic Mask;
            internal Dictionary<string, object>[] Evidence;
            internal string Reason;
        }
        internal static bool Resolve(AbilityData ability, UnitEntityData caster, Metamagic basis, out Metamagic resolved,
            out Dictionary<string, object>[] evidence, out string reason)
        {
            var key = (ability, caster, basis);
            if (!ScopedReadMemo<(AbilityData, UnitEntityData, Metamagic), Result>.TryGet(key, out var result))
            {
                result = new Result();
                result.Available = ResolveCore(ability, caster, basis, out result.Mask, out result.Evidence, out result.Reason);
                ScopedReadMemo<(AbilityData, UnitEntityData, Metamagic), Result>.Put(key, result);
            }
            resolved = result.Mask; evidence = result.Evidence; reason = result.Reason; return result.Available;
        }
        static bool ResolveCore(AbilityData ability, UnitEntityData caster, Metamagic basis, out Metamagic resolved,
            out Dictionary<string, object>[] evidence, out string reason)
        {
            resolved = basis; reason = null;
            var sources = new List<Dictionary<string, object>>();
            var entries = CombatFields.Read(caster.Get<UnitPartSpecialMetamagic>(), "m_MetamagicOnSpellList") as IEnumerable;
            int count = 0;
            if (entries != null) foreach (var entry in entries)
            {
                if (++count > 64) { reason = "AutoMetamagicSourceLimit"; break; }
                var rule = CombatFields.Read(entry, "Item2") as AutoMetamagic;
                var fact = CombatFields.Read(entry, "Item1");
                bool? applies = null;
                string problem = rule == null ? "AutoMetamagicRegistrationUnavailable" : rule.GetType() != typeof(AutoMetamagic)
                    ? "AutoMetamagicDerivedType:" + rule.GetType().Name : null;
                var filters = new List<Dictionary<string, object>>();
                if (problem == null)
                {
                    applies = Applies(rule, ability.Blueprint, ability, caster, 0, filters);
                    if (!applies.HasValue) problem = "AutoMetamagicFilterInputUnavailable:AutoMetamagic";
                }
                if (applies == true) resolved |= rule.Metamagic;
                sources.Add(new Dictionary<string, object> { { "source", CombatFields.Label(fact) },
                    { "type", rule?.GetType().Name }, { "mask", rule == null ? null : (object)(int)rule.Metamagic },
                    { "applicable", applies }, { "reason", problem }, { "filters", filters }, { "once", rule != null && rule.Once } });
                if (problem != null && reason == null) reason = problem;
            }
            evidence = sources.ToArray(); return reason == null;
        }
        static BlueprintAbility Alias(BlueprintAbility bp, AbilityData data, out bool loaded)
        {
            var converted = CombatFields.Read(data, "m_ConvertedFrom") as AbilityData;
            if (converted != null) { loaded = true; return converted.Blueprint; }
            var reference = CombatFields.Read(bp, "m_Parent");
            var guid = CombatFields.ReferenceGuid(reference);
            if (!guid.HasValue || guid.Value == Guid.Empty) { loaded = true; return bp; }
            var parent = LoadedBlueprintReader.Read(reference) as BlueprintAbility;
            loaded = parent != null; return parent;
        }
        static bool? Suitable(AutoMetamagic rule, BlueprintAbility bp, AbilityData data, int depth)
        {
            if (depth >= 16 || bp == null) return null;
            var alias = Alias(bp, data, out bool loaded);
            bool? parent = !loaded ? null : alias != bp ? Suitable(rule, alias, null, depth + 1) : false;
            var item = ItemSpellParameterReader.Item(data);
            int kind = item == null ? -1 : item.Blueprint is Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable usable
                ? (int)usable.Type : -2;
            // Non-usable source items must also fail the engine's item gate.
            if (item != null && kind < 0) kind = int.MaxValue;
            bool kinetic = bp.ComponentsArray.Any(c => c.GetType().Name == "AbilityKineticist");
            return AutoMetamagicRules.Or(parent, AutoMetamagicRules.Suitable(CombatFields.Int(rule, "m_AllowedAbilities"), (int)bp.Type, kind, kinetic));
        }
        static bool? Applies(AutoMetamagic rule, BlueprintAbility bp, AbilityData data, UnitEntityData caster, int depth, List<Dictionary<string, object>> filters)
        {
            if (depth >= 16 || bp == null) return null;
            var alias = Alias(bp, data, out bool loaded);
            bool? parent = !loaded ? null : alias != bp ? Applies(rule, alias, null, caster, depth + 1, filters) : false;
            if (parent == true) return true;
            var white = rule.Abilities;
            bool whitelist = white == null || white.Count == 0 || white.Any(r => CombatFields.Reference(r, bp));
            SpellDescriptor descriptor = rule.Descriptor;
            var spellDescriptor = bp.ComponentsArray.OfType<SpellDescriptorComponent>().FirstOrDefault();
            var blueprintDescriptor = CombatFields.Read(bp, "m_SpellDescriptor") as SpellDescriptor?
                ?? spellDescriptor?.Descriptor.Value ?? (SpellDescriptor)0;
            bool? descriptors = descriptor == 0 ? true : data != null && data.MagicHackData != null ? (bool?)null
                : (blueprintDescriptor & descriptor) != 0;
            // AbilityData descriptor/school can be dynamically replaced. Do not
            // pretend blueprint-only inputs cover those special transformations.
            bool? school = rule.School == SpellSchool.None ? true : data != null && data.MagicHackData != null ? (bool?)null
                : (bp.ComponentsArray.OfType<SpellComponent>().FirstOrDefault()?.School ?? SpellSchool.None) == rule.School;
            bool? level = rule.MaxSpellLevel <= 0 ? true : data == null ? true : SpellLevel(data, caster) is int n ? n <= rule.MaxSpellLevel : (bool?)null;
            bool? book = !rule.CheckSpellbook ? true : InBook(LoadedBlueprintReader.Read(CombatFields.Read(rule, "m_Spellbook")), bp);
            var include = CombatFields.Read(rule, "m_IncludeClasses") as IEnumerable;
            var exclude = CombatFields.Read(rule, "m_ExcludeClasses") as IEnumerable;
            var included = include?.Cast<object>().ToArray() ?? Array.Empty<object>();
            var excluded = exclude?.Cast<object>().ToArray() ?? Array.Empty<object>();
            if (included.Length + excluded.Length > 128) return null;
            bool Owns(object reference) => data != null && caster.Progression.Classes.Any(c => CombatFields.Reference(reference, c.CharacterClass));
            bool exclusions = !excluded.Any(Owns);
            bool? inclusions = included.Length == 0 ? true : !included.Any(Owns) ? false : (bool?)false;
            if (included.Length != 0 && included.Any(Owns)) foreach (var reference in included)
            {
                var cls = LoadedBlueprintReader.Read(reference);
                inclusions = AutoMetamagicRules.Or(inclusions, InBook(LoadedBlueprintReader.Read(CombatFields.Read(cls, "m_Spellbook")), bp));
            }
            var suitable = Suitable(rule, bp, data, depth);
            filters.Add(new Dictionary<string, object> { { "blueprint", bp.AssetGuid.ToString() }, { "aliasLoaded", loaded },
                { "suitable", suitable }, { "whitelist", whitelist }, { "descriptor", descriptors }, { "school", school },
                { "levelCap", level }, { "spellList", book }, { "excludedClasses", exclusions }, { "includedClasses", inclusions } });
            var own = AutoMetamagicRules.And(suitable, whitelist, descriptors, school, level, book, exclusions, inclusions);
            return AutoMetamagicRules.Or(parent, own);
        }
        static bool? InBook(object book, BlueprintAbility bp)
        {
            if (book == null) return null;
            var list = LoadedBlueprintReader.Read(CombatFields.Read(book, "m_SpellList"));
            if (!(CombatFields.Read(list, "SpellsByLevel") is IEnumerable levels)) return null;
            int count = 0;
            foreach (var level in levels)
                if (CombatFields.Read(level, "m_Spells") is IEnumerable refs) foreach (var reference in refs)
                { if (++count > 4096) return null; if (CombatFields.Reference(reference, bp)) return true; }
            return false;
        }
        internal static int? SpellLevel(AbilityData ability, UnitEntityData caster, int depth = 0)
        {
            if (depth >= 16) return null;
            if (ability.MagicHackData != null) return null;
            var value = CombatFields.Read(ability, "m_OverrideSpellLevel") as int?;
            if (value.HasValue) return value;
            if (CombatFields.Read(CombatFields.Read(ability, "m_Fact"), "<StickyTouch>k__BackingField") != null) return null;
            if (CombatFields.Read(ability, "m_ConvertedFrom") is AbilityData converted) return SpellLevel(converted, caster, depth + 1);
            var item = ItemSpellParameterReader.Item(ability);
            if (item?.Blueprint is Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipment definition)
            {
                var craft = item.Get<Kingmaker.Craft.CraftedItemPart>();
                return craft == null ? definition.SpellLevel : CombatFields.Int(craft, "<SpellLevel>k__BackingField")
                    + CombatFields.Int(CombatFields.Read(craft, "<MetamagicData>k__BackingField"), "<SpellLevelCost>k__BackingField");
            }
            value = CombatFields.Read(ability, "<SpellLevelInSpellbook>k__BackingField") as int?;
            var book = SpellCasterLevelReader.Book(ability, caster);
            if (!value.HasValue && CombatFields.Read(book, "m_KnownSpellLevels") is IDictionary levels
                && levels[ability.Blueprint] is IEnumerable<int> known && known.Any()) value = known.First();
            if (book == null) return null;
            // Spellbook.GetSpellLevel includes the selected metamagic slot cost.
            // This cost affects eligibility, not the independent base DC formula.
            return value.HasValue ? (int?)checked(value.Value + CombatFields.Int(CombatFields.Read(ability, "<MetamagicData>k__BackingField"), "<SpellLevelCost>k__BackingField")) : null;
        }
    }
}
