using System.Collections;
using System.Collections.Generic;
using Kingmaker.RuleSystem.Rules;
namespace CombatPreview
{
    internal static class AttackCommandReader
    {
        internal static AttackRowLink Read(RuleAttackWithWeapon rule)
        {
            var result = new AttackRowLink { GlobalIndex = rule.AttackNumber, Penalty = rule.AttackBonusPenalty, Status = "ActiveCommandRuleNotFound" };
            var commands = CombatFields.Read(rule.Initiator?.Commands, "m_Commands") as IEnumerable;
            if (commands == null) { result.Status = "CommandSlotsUnavailable"; return result; }
            var seen = new HashSet<object>(); int commandCount = 0;
            foreach (var command in commands)
            {
                if (command == null || !seen.Add(command)) continue;
                if (++commandCount > 16) { result.Status = "CommandScanLimit"; return result; }
                var plan = CombatFields.Read(command, "m_AllAttacks") as IList;
                if (plan == null) continue;
                if (plan.Count > 128) { result.Status = "AttackPlanScanLimit"; continue; }
                int ordinal = 0;
                for (int index = 0; index < plan.Count; index++)
                {
                    var entry = plan[index];
                    var hand = CombatFields.Read(entry, "Hand");
                    var weapon = CombatFields.Bool(hand, "m_IsItemFake") ? CombatFields.Read(hand, "m_FakeItem") : BetaReader.SlotItem(hand);
                    bool exact = ReferenceEquals(CombatFields.Read(entry, "<Rule>k__BackingField"), rule);
                    if (exact)
                    {
                        result.CommandId = BetaIdentity.Id(command); result.PlanIndex = index;
                        result.WeaponOrdinal = ordinal;
                        result.Exact = ReferenceEquals(weapon, rule.Weapon)
                            && CombatFields.Read(entry, "AttackBonusPenalty") is int penalty && penalty == rule.AttackBonusPenalty;
                        result.Status = result.Exact ? "ExactCommandRuleAndWeapon" : "CommandRuleWeaponOrPenaltyChanged";
                        return result;
                    }
                    if (ReferenceEquals(weapon, rule.Weapon)) ordinal++;
                }
            }
            return result;
        }
    }
}
