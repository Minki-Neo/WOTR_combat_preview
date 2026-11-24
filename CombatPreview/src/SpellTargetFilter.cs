using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;

namespace CombatPreview
{
    internal sealed class SpellTargetFilter
    {
        readonly AbilityData ability;
        readonly UnitEntityData caster;
        readonly AbilityTargetsAround area;
        readonly bool uncertain;
        readonly int? areaType;
        internal SpellTargetFilter(AbilityData ability, UnitEntityData caster)
        {
            this.ability = ability; this.caster = caster;
            var selectors = ability.Blueprint.ComponentsArray.OfType<AbilityTargetsAround>().Take(2).ToArray();
            area = selectors.Length == 1 ? selectors[0] : null;
            uncertain = ability.MagicHackData != null || selectors.Length > 1
                || area != null && area.GetType() != typeof(AbilityTargetsAround)
                || ability.Blueprint.ComponentsArray.Any(component => component != null
                    && component.GetType().Name.StartsWith("AbilityTargets", System.StringComparison.Ordinal)
                    && !(component is AbilityTargetsAround));
            areaType = area == null ? null : CombatFields.Read(area, "m_TargetType") is TargetType type ? (int?)type : -1;
        }
        internal bool Allows(UnitEntityData target)
        {
            if (target == null) return false;
            var blueprint = ability.Blueprint;
            if (!SpellTargetEligibility.Allows(areaType, caster.IsEnemy(target), caster.IsAlly(target),
                ReferenceEquals(caster, target), blueprint.CanTargetPoint, blueprint.CanTargetEnemies,
                blueprint.CanTargetFriends, blueprint.CanTargetSelf, uncertain)) return false;
            if (!uncertain && area != null && CombatFields.Read(area, "m_Condition") is Kingmaker.ElementsSystem.ConditionsChecker conditions)
                return SpellPreviewRuntime.ReadSaveCondition(conditions, target, caster) != false;
            return true;
        }
    }
}
