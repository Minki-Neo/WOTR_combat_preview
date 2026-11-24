using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CombatPreview
{
    internal sealed class ProjectedBuffContext
    {
        internal int? CasterLevel, RankBonus, PowerfulChange = 0;
        internal Metamagic? Metamagic;
        internal static ProjectedBuffContext Read(AbilityData ability, UnitEntityData caster, Dictionary<string, object> metadata)
        {
            var result = new ProjectedBuffContext();
            if (metadata.TryGetValue("parameterStatus", out var status) && (Equals(status, "Current") || Equals(status, "ReadOnlyCalculated")))
            {
                result.CasterLevel = metadata["casterLevel"] as int?;
                result.RankBonus = metadata["rankBonus"] as int?;
                if (metadata.TryGetValue("effectiveMetamagic", out var metamagic) && metamagic is int mask) result.Metamagic = (Metamagic)mask;
            }
            int power = (caster.State.Features.PowerfulChange ? 2 : 0) + (caster.State.Features.ImprovedPowerfulChange ? 2 : 0);
            if (power > 0 && ability.SpellSchool == Kingmaker.Blueprints.Classes.Spells.SpellSchool.Transmutation && ability.Blueprint.IsSpell)
            {
                var book = SpellCasterLevelReader.Book(ability, caster);
                result.PowerfulChange = book != null ? book.Blueprint.IsArcanist ? power : 0
                    : CombatFields.Read(ability, "m_SpellbookBlueprint") == null ? (int?)0 : null;
            }
            return result;
        }
        internal int? Bonus(AddContextStatBonus rule, BlueprintBuff buff, UnitEntityData caster)
        {
            if (!RankIndependent(rule, buff)) return null;
            int? amount = SpellDamagePreview.Value(rule.Value, buff.ComponentsArray, CasterLevel, Metamagic, caster, RankBonus);
            return BuffStatMath.ContextBonus(amount, rule.Multiplier, rule.Stat.IsAttribute() ? PowerfulChange : 0,
                rule.HasMinimal ? (int?)rule.Minimal : null);
        }
        internal static bool RankIndependent(AddContextStatBonus rule, BlueprintBuff buff)
        {
            var property = CombatFields.Read(CombatFields.Read(rule, "Restrictions"), "Property");
            if (property != null && (!(CombatFields.Read(property, "Getters") is Array getters) || getters.Length != 0)) return false;
            var value = rule.Value;
            if (value?.ValueType == ContextValueType.Rank)
            {
                var configs = buff.ComponentsArray.OfType<ContextRankConfig>().Where(config => Equals(CombatFields.Read(config, "m_Type"), value.ValueRank)).ToArray();
                if (configs.Length != 1) return false;
                string basis = CombatFields.Read(configs[0], "m_BaseValueType")?.ToString();
                // These bases do not read an unprojected buff/attribute state.
                if (basis != "CasterLevel" && basis != "ClassLevel" && basis != "CharacterLevel" && basis != "MythicLevel") return false;
            }
            else if (value?.ValueType != ContextValueType.Simple) return false;
            return true;
        }
    }
}
