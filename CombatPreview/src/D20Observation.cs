using System.Collections.Generic;
using System.Linq;
using Kingmaker.RuleSystem.Rules;

namespace CombatPreview
{
    internal static class D20Observation
    {
        // Completed game-owned data only. A two-entry history does not by itself
        // identify advantage: record signed rerolls and their actual sources.
        internal static Dictionary<string, object> Capture(RuleRollDice roll)
        {
            if (roll == null) return null;
            return new Dictionary<string, object> {
                { "result", roll.Result }, { "rawResult", CombatFields.Read(roll, "m_Result") },
                { "history", roll.RollHistory?.Take(40).ToArray() },
                { "signedRerollCount", CombatFields.Read(roll, "m_RerollAmount") },
                { "replaceOneWithMax", roll.ReplaceOneWithMax }, { "resultOverride", CombatFields.Read(roll, "ResultOverride") },
                { "sources", roll.Rerolls?.Take(32).Select(reroll => new Dictionary<string, object> {
                    { "amount", reroll.Amount }, { "sourceId", BetaIdentity.Id(reroll.Source) },
                    { "source", reroll.Source == null ? null : CombatFields.Label(reroll.Source) },
                    { "blueprintId", (CombatFields.Blueprint(reroll.Source) as Kingmaker.Blueprints.SimpleBlueprint)?.AssetGuid.ToString() }
                }).ToArray() }
            };
        }
    }
}
