using System;
using System.Collections;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CombatPreview
{
    internal static class SpellCasterLevelReader
    {
        // Independent base CL when the game has not calculated parameters yet.
        // No rule dispatch, Spellbook getter/cache write, tooltip or HCO output.
        internal static int? Read(AbilityData ability, UnitEntityData caster)
        {
            if (ability == null || caster == null || ability.MagicHackData != null) return null;
            var fact = CombatFields.Read(ability, "m_Fact");
            if (ItemSpellParameterReader.Item(ability) != null)
                return ItemSpellParameterReader.TryRead(ability, caster, out _, out int itemLevel, out _, out _, out _) ? (int?)itemLevel : null;
            if (CombatFields.Read(fact, "<OverrideParams>k__BackingField") is AbilityParams overridden)
                return overridden.CasterLevel;
            if (ability.Blueprint.ComponentsArray.Any(component => component is ContextAbilityParamsCalculator
                    || component is ContextSetAbilityParams)) return null;
            if (CombatFields.Read(ability, "<OverrideCasterLevel>k__BackingField") is int replacement) return Math.Max(1, replacement);
            var book = Book(ability, caster);
            var blueprint = CombatFields.Read(ability, "m_SpellbookBlueprint");
            if (book == null) return blueprint == null ? Math.Max(1, caster.Progression.CharacterLevel) : (int?)null;
            return ReadBook(book, caster);
        }
        internal static int? ReadBook(Spellbook book, UnitEntityData caster)
        {
            if (book == null || caster == null) return null;
            if (!(CombatFields.Read(book, "m_BaseLevelInternal") is int baseLevel)
                || !(CombatFields.Read(book, "m_MythicLevelInternal") is int mythic)) return null;
            var lastMythic = caster.Progression.Classes.LastOrDefault(entry => entry.CharacterClass.IsMythic)?.CharacterClass;
            if (caster.Progression.MythicLevel <= 3 && lastMythic?.DontAddCasterLevels == true) mythic = 0;
            if (!(CombatFields.Read(book, "MaxCasterLevel") is int maximum)) return null;
            int level = Math.Max(0, Math.Min(maximum, baseLevel + book.Blueprint.CasterLevelModifier)) + mythic;
            if (CombatFields.Read(caster.Stats.BonusCasterLevel, "<ModifiedValue>k__BackingField") is int bonus) level += bonus;
            return Math.Max(1, level);
        }
        internal static Spellbook Book(AbilityData ability, UnitEntityData caster)
        {
            var book = CombatFields.Read(ability, "<OverrideSpellbook>k__BackingField") as Spellbook
                ?? CombatFields.Read(ability, "m_CachedSpellbook") as Spellbook;
            var blueprint = CombatFields.Read(ability, "m_SpellbookBlueprint");
            if (book == null && blueprint != null && CombatFields.Read(caster.Descriptor, "m_Spellbooks") is IDictionary books)
                foreach (DictionaryEntry entry in books)
                    if (entry.Value is Spellbook candidate && ReferenceEquals(candidate.Blueprint, blueprint)) { book = candidate; break; }
            return book;
        }
    }
}
