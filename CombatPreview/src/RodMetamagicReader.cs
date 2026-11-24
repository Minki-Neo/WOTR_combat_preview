using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
namespace CombatPreview
{
    internal static class RodMetamagicReader
    {
        internal static object SwiftState(UnitEntityData caster)
            => CombatFields.Read(CombatFields.Read(caster?.CombatState, "Cooldown"), "<SwiftAction>k__BackingField");
        internal static bool Resolve(AbilityData ability, UnitEntityData caster, Metamagic basis, out Metamagic resolved,
            out bool ignoreSr, out Dictionary<string, object>[] evidence, out string reason)
        {
            resolved = basis; ignoreSr = false; reason = null;
            var sources = new List<Dictionary<string, object>>(); evidence = null;
            if (ItemSpellParameterReader.Item(ability) != null) { evidence = sources.ToArray(); return true; }
            var book = SpellCasterLevelReader.Book(ability, caster);
            int? level = AutoMetamagicReader.SpellLevel(ability, caster);
            bool sticky = CombatFields.Read(CombatFields.Read(ability, "m_Fact"), "<StickyTouch>k__BackingField") != null;
            foreach (var fact in caster.Facts.List.Where(fact => fact.Active)) foreach (var runtime in fact.Components)
            {
                if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                    || CombatFields.Component(runtime)?.GetType() != typeof(MetamagicRodMechanics)) continue;
                var rod = (MetamagicRodMechanics)CombatFields.Component(runtime);
                int mask = (int)rod.Metamagic;
                bool alreadyPresent = ((int)resolved & mask) == mask;
                if (alreadyPresent && !rod.ForceIgnoreSpellResist)
                {
                    sources.Add(new Dictionary<string, object> { { "sourceId", BetaIdentity.Id(fact) }, { "source", CombatFields.Label(fact) },
                        { "mask", mask }, { "alreadyPresent", true }, { "parameterHandlerNoOp", true } });
                    continue;
                }
                var swift = SwiftState(caster);
                if ((mask & 4) != 0 && !(swift is float)) { reason = "RodSwiftCooldownUnavailable"; return false; }
                var white = CombatFields.Read(rod, "m_AbilitiesWhiteList") as IEnumerable;
                var parent = CombatFields.ReferenceGuid(CombatFields.Read(ability.Blueprint, "m_Parent"));
                bool whitelisted = white != null && white.Cast<object>().Any(reference => CombatFields.Reference(reference, ability.Blueprint)
                    || parent.HasValue && CombatFields.ReferenceGuid(reference) == parent);
                if (!alreadyPresent && book != null && ability.Blueprint.Type == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityType.Spell
                    && !level.HasValue && !whitelisted) { reason = "RodSpellLevelUnavailable"; return false; }
                bool applicable = RodMetamagicRules.Applies(mask, (int)ability.Blueprint.AvailableMetamagic,
                    swift is float used && used > 0, sticky, book != null && (int)ability.Blueprint.Type == 0, level, rod.MaxSpellLevel, whitelisted);
                if (applicable) { resolved |= rod.Metamagic; ignoreSr |= rod.ForceIgnoreSpellResist; }
                sources.Add(new Dictionary<string, object> { { "sourceId", BetaIdentity.Id(fact) }, { "source", CombatFields.Label(fact) },
                    { "mask", mask }, { "alreadyPresent", alreadyPresent }, { "applicable", applicable },
                    { "maximumSpellLevel", rod.MaxSpellLevel }, { "spellLevel", level }, { "whitelisted", whitelisted },
                    { "swiftCooldown", swift }, { "ignoreSpellResistance", applicable && rod.ForceIgnoreSpellResist } });
            }
            evidence = sources.ToArray(); return true;
        }
    }
}
