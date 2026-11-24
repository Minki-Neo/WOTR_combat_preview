using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Craft;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Parts;
namespace CombatPreview
{
    internal static class ItemSpellParameterReader
    {
        internal static ItemEntity Item(AbilityData ability)
        {
            // AbilityData.Fact prefers the oldest non-null converted-from fact.
            // Read existing fields only; do not invoke conversion/fact getters.
            if (ability == null) return null;
            if (CombatFields.Read(ability, "m_ConvertedFrom") == null)
                return CombatFields.Read(CombatFields.Read(ability, "m_Fact"), "<SourceItem>k__BackingField") as ItemEntity;
            object fact = null; var visited = new HashSet<AbilityData>();
            for (var current = ability; current != null; current = CombatFields.Read(current, "m_ConvertedFrom") as AbilityData)
            {
                if (visited.Count >= 16 || !visited.Add(current)) return null;
                var candidate = CombatFields.Read(current, "m_Fact");
                if (candidate != null) fact = candidate;
            }
            return CombatFields.Read(fact, "<SourceItem>k__BackingField") as ItemEntity;
        }
        internal static bool TryRead(AbilityData ability, UnitEntityData caster, out int dc, out int level,
            out Metamagic meta, out Dictionary<string, object> evidence, out string reason)
        {
            dc = level = 0; meta = 0; reason = null; evidence = null;
            var item = Item(ability);
            if (!(item?.Blueprint is BlueprintItemEquipment definition) || caster == null)
            { reason = "ItemDefinitionUnavailable"; return false; }
            var craft = item.Get<CraftedItemPart>();
            var craftedMeta = CombatFields.Read(craft, "<MetamagicData>k__BackingField");
            int spellLevel = craft == null ? definition.SpellLevel : CombatFields.Int(craft, "<SpellLevel>k__BackingField")
                + CombatFields.Int(craftedMeta, "<SpellLevelCost>k__BackingField");
            int kind = definition is BlueprintItemEquipmentUsable usable ? (int)usable.Type : -1;
            var specialization = caster.Get<UnitPartScrollSpecialization>();
            int? specialized = kind == 2 && specialization != null
                ? SpellCasterLevelReader.ReadBook(specialization.SpecializedClassSpellbook, caster) : null;
            if (kind == 2 && specialization != null && !specialized.HasValue)
            { reason = "ScrollSpecializationBookUnavailable"; return false; }
            var potion = caster.Get<UnitPartEnhancePotion>();
            var entries = CombatFields.Read(caster.Get<UnitPartItemCasterLevelBonus>(), "Entries") as IEnumerable;
            var bonusSources = new List<Dictionary<string, object>>(); int bonus = 0, visited = 0;
            if (entries != null) foreach (var entry in entries)
            {
                if (++visited > 64) { reason = "ItemCasterLevelSourceLimit"; return false; }
                if (CombatFields.Int(entry, "ItemType") != kind) continue;
                int value = CombatFields.Int(entry, "Bonus"); bonus = checked(bonus + value);
                bonusSources.Add(new Dictionary<string, object> { { "bonus", value }, { "type", kind } });
            }
            var features = caster.State.Features;
            var input = new ItemSpellInput {
                Kind = kind, Dc = craft == null ? definition.DC : CombatFields.Int(craft, "<AbilityDC>k__BackingField"),
                Level = craft == null ? definition.CasterLevel : CombatFields.Int(craft, "<CasterLevel>k__BackingField"),
                SpellLevel = spellLevel, Crafted = craft != null,
                Intelligence = BetaReader.AbilityModifier(caster, "Intelligence"), Charisma = BetaReader.AbilityModifier(caster, "Charisma"),
                ScrollMastery = features.ScrollMastery, WandMastery = features.WandMastery, EldritchWandMastery = features.EldritchWandMastery,
                SpecializedLevel = specialized, PotionLevel = kind == 3 && potion?.classData != null ? (int?)potion.classData.Level : null,
                TricksterBoost = features.TricksterUseMagicDeviceCasterLevelBoost, Mythic = caster.Progression.MythicLevel,
                ItemLevelBonus = bonus
            };
            if (!ItemSpellRules.Calculate(input, out dc, out level)) { reason = "ItemParameterBounds"; return false; }
            meta = (Metamagic)CombatFields.Int(craftedMeta, "<MetamagicMask>k__BackingField");
            if (kind == 3 && features.ExtendPotion) meta |= Metamagic.Extend;
            // The engine applies registered AutoMetamagic to items, but skips
            // rods and next-spell metamagic. Resolve only audited predicates.
            if (!AutoMetamagicReader.Resolve(ability, caster, meta, out meta, out var automatic, out reason))
            {
                evidence = new Dictionary<string, object> { { "itemId", BetaIdentity.Id(item) },
                    { "automaticMetamagicSources", automatic }, { "unavailableReason", reason } };
                return false;
            }
            evidence = new Dictionary<string, object> {
                { "itemId", BetaIdentity.Id(item) }, { "itemBlueprintId", definition.AssetGuid.ToString() },
                { "kind", kind }, { "crafted", craft != null }, { "spellLevel", spellLevel },
                { "itemDc", input.Dc }, { "itemCasterLevel", input.Level },
                { "scrollMastery", input.ScrollMastery }, { "specializedCasterLevel", specialized },
                { "itemCasterLevelBonuses", bonusSources }, { "tricksterMythicBonus", input.TricksterBoost ? input.Mythic : 0 },
                { "dc", dc }, { "casterLevel", level }, { "metamagic", (int)meta }, { "automaticMetamagicSources", automatic },
                { "scope", "Independent audited item parameter path; item params bypass ordinary spellbook DC/CL handlers and ability overrides." }
            };
            return true;
        }
    }
}
