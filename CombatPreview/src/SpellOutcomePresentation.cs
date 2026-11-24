using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CombatPreview
{
    internal sealed class SpellOutcomeRow
    {
        internal string Path, Effect;
        internal double? Chance, ExpectedHpLoss;
        internal bool Lethal;
        internal string ChanceText => Chance.HasValue ? "~" + Chance.Value.ToString("0.#%", CultureInfo.InvariantCulture) : "—";
        internal string EffectText => Effect + (ExpectedHpLoss.HasValue && ExpectedHpLoss.Value > 0
            ? "\n기대 HP 감소 ~" + ExpectedHpLoss.Value.ToString("0.##", CultureInfo.InvariantCulture) : "");
    }
    internal sealed class SpellOutcomeTable
    {
        internal readonly List<SpellOutcomeRow> Rows = new List<SpellOutcomeRow>();
        internal string ProbabilityHeading = "시전당 확률";
        internal string OutcomeHeading = "최종 분기";
        internal string Note;
        internal string ChecksCaption;
    }
    // Presentation only: consumes existing structured outcomes and probabilities.
    // It does not parse prose, roll dice, query HCO or manufacture missing checks.
    internal static class SpellOutcomePresentation
    {
        static object Get(Dictionary<string, object> data, string key)
            => data != null && data.TryGetValue(key, out var value) ? value : null;
        static double? Number(object value) => value is double d && !double.IsNaN(d) && !double.IsInfinity(d) ? (double?)d
            : value is int i ? i : value is long l ? l : (double?)null;
        static double? Probability(object value)
        { var p = Number(value); return p >= 0 && p <= 1 ? p : null; }
        static IEnumerable<Dictionary<string, object>> Entries(object value)
            => value as IEnumerable<Dictionary<string, object>> ?? Enumerable.Empty<Dictionary<string, object>>();
        static string Texts(object value) => value is string text ? text : value is IEnumerable<string> values ? string.Join(" · ", values) : "";
        static double? Through(double? a, double? b) => a == 0 || b == 0 ? 0 : a * b;
        static string Save(string type) => type == "Fortitude" ? "인내" : type == "Reflex" ? "반사" : type == "Will" ? "의지" : type;
        static SpellOutcomeRow Row(Dictionary<string, object> data, bool general)
        {
            string damage = Texts(Get(data, general ? "damageFormula" : "damage"));
            string effect = Texts(Get(data, general ? "effectAttempts" : "effect"));
            var chance = Probability(Get(data, "chancePerCast"));
            double? loss = Number(Get(data, "hpLossIfReached"));
            return new SpellOutcomeRow {
                Path = (Get(data, "path") as string ?? "효과 적용").Trim(' ', '→'), Chance = chance,
                Effect = string.Join(" · ", new[] { damage, effect }.Where(text => !string.IsNullOrWhiteSpace(text)).Distinct()),
                ExpectedHpLoss = Through(chance, loss),
                Lethal = effect.Contains("즉사") || damage.Contains("즉사")
            };
        }
        internal static SpellOutcomeTable Build(Dictionary<string, object> metadata, IList<SpellCheckRecord> checks)
        {
            var table = new SpellOutcomeTable();
            if (Get(metadata, "stagedOutcome") is Dictionary<string, object> stages)
                table.ChecksCaption = Save(Get(stages, "firstSaveType") as string) + " 실패 " + Chance(Probability(Get(stages, "firstSaveFailure")))
                    + " → " + Save(Get(stages, "secondSaveType") as string) + " 실패 " + Chance(Probability(Get(stages, "secondSaveFailureIfReached"))) + " (도달 시)";
            var staged = Entries(Get(metadata, "spellOutcomes")).ToArray();
            if (staged.Length > 0)
                table.Rows.AddRange(staged.Select(data => Row(data, false)));
            else if (Get(metadata, "branchPreview") is Dictionary<string, object> branch)
            {
                AddBlocked(table, checks);
                table.Rows.AddRange(Entries(Get(branch, "outcomes")).Select(data => Row(data, true)));
            }
            else if (Get(metadata, "spellDamage") is Dictionary<string, object> damage)
            {
                var terms = Entries(Get(damage, "terms")).Where(term => Equals(Get(term, "hpDamage"), true)).ToArray();
                // Unknown conditional trees must not be presented as a certain
                // final damage effect. Retain the existing summary in that case.
                if (terms.Any(term => Equals(Get(term, "branchCandidate"), true))) return table;
                var direct = terms.Where(term => Equals(Get(term, "path"), "cast")).ToArray();
                if (direct.Length == 0) return table;
                var identified = checks.Where(check => check.Kind == "attack" && check.Status != "NotIdentified").ToArray();
                if (identified.Length > 1) return table;
                double? hit = identified.Length == 0 ? 1 : identified[0].Probability;
                double? sr = Penetration(checks);
                var saves = checks.Where(check => check.Kind == "save" && check.Status != "NotIdentified").ToArray();
                if (saves.Length > 0) return table; // Structured save outcomes take precedence.
                AddBlocked(table, checks);
                string formula = string.Join(" + ", direct.Select(term => Equals(Get(term, "registeredEnergyImmunity"), true)
                    ? Texts(Get(term, "formula")) + " (피해 면역)" : Texts(Get(term, "formula"))));
                table.Rows.Add(new SpellOutcomeRow { Path = identified.Length == 0 ? "효과 적용" : "효과 적중", Chance = Through(hit, sr), Effect = formula });
                if (Get(damage, "deliveryCount") is int count && count > 1)
                {
                    table.ProbabilityHeading = "타격당 확률";
                    table.OutcomeHeading = "타격 분기";
                    table.Note = count + "회 전달 · 위 체력바는 전체 시전 기댓값";
                    if (identified.Length == 1 && identified[0].MirrorImages > 0)
                    {
                        table.ProbabilityHeading = "첫 타격 확률";
                        table.Note += "\n후속 확률은 거울상 소모에 따라 변함";
                    }
                }
            }
            foreach (var row in table.Rows)
                if (string.IsNullOrWhiteSpace(row.Effect)) row.Effect = "피해·상태 효과 없음";
            if (Get(metadata, "secondaryTargetEffects") is Dictionary<string, object> secondary && Equals(Get(secondary, "status"), "Unmodeled"))
            {
                table.ProbabilityHeading = "주 대상 직접 판정";
                table.OutcomeHeading = "직접 분기 · 주변 효과 별도";
                table.Note = (table.Note == null ? "" : table.Note + "\n") + "주변 전파 확률·추가 효과·HP 영향 미계산";
            }
            if (table.Rows.Any(row => !row.Chance.HasValue))
                table.Note = (table.Note == null ? "" : table.Note + "\n") + "— 확률 미확인 · 0%가 아님";
            return table;
        }
        static string Chance(double? value) => value.HasValue ? "~" + value.Value.ToString("0.#%", CultureInfo.InvariantCulture) : "—";
        static double? Penetration(IList<SpellCheckRecord> checks)
        {
            var sr = checks.FirstOrDefault(check => check.Kind == "sr");
            return sr?.Status == "NotRequired" || sr?.Status == "NoRegisteredSR" ? 1 : sr?.Probability;
        }
        static void AddBlocked(SpellOutcomeTable table, IList<SpellCheckRecord> checks)
        {
            var attack = checks.FirstOrDefault(check => check.Kind == "attack" && check.Status != "NotIdentified");
            double? hit = attack == null ? 1 : attack.Probability;
            if (attack != null) table.Rows.Add(new SpellOutcomeRow { Path = "빗나감", Chance = 1 - hit, Effect = "피해·효과 없음", ExpectedHpLoss = 0 });
            var sr = checks.FirstOrDefault(check => check.Kind == "sr");
            if (sr != null && sr.Status != "NotRequired" && sr.Status != "NoRegisteredSR")
                table.Rows.Add(new SpellOutcomeRow { Path = sr.Status == "Immune" ? "주문 면역" : "SR 차단",
                    Chance = Through(hit, 1 - sr.Probability), Effect = "피해·효과 없음", ExpectedHpLoss = 0 });
        }
    }
}
