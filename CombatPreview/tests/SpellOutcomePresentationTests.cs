using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CombatPreview;
static class SpellOutcomePresentationTests
{
    static int assertions;
    static void Check(bool value, string reason) { assertions++; if (!value) throw new Exception(reason); }
    static void Near(double actual, double expected, string reason) => Check(Math.Abs(actual - expected) < 1e-9, reason);
    static Dictionary<string, object> Outcome(string path, double? chance, string effect, double? hp = null)
        => new Dictionary<string, object> { { "path", path }, { "chancePerCast", chance }, { "damage", effect }, { "hpLossIfReached", hp } };
    public static void Run()
    {
        var checks = new List<SpellCheckRecord>();
        var metadata = new Dictionary<string, object> { { "spellOutcomes", new[] {
            Outcome("SR 차단", .2, "피해 없음", 0), Outcome("의지 성공", .32, "피해 없음", 0),
            Outcome("의지 실패 → 인내 실패", .192, "즉사 (100 HP)", 100),
            Outcome("의지 실패 → 인내 성공", .288, "3D6", 10.5) } } };
        var table = SpellOutcomePresentation.Build(metadata, checks);
        Check(table.Rows.Count == 4, "every final lethal-spell branch is shown");
        Near(table.Rows.Sum(row => row.Chance.Value), 1, "mutually exclusive final branches retain per-cast probabilities");
        Near(table.Rows[2].ExpectedHpLoss.Value, 19.2, "instant death contributes remaining HP times final branch chance");
        Near(table.Rows[3].ExpectedHpLoss.Value, 3.024, "damage branch contribution uses its final probability, not the conditional save chance");
        Check(table.Rows[2].Lethal && !table.Rows[3].Lethal, "instant death can be visually distinguished from dice damage");
        Check(table.Rows[3].EffectText.Contains("3D6"), "damage dice remain visible");
        metadata["spellOutcomes"] = new[] { Outcome("내성 실패", null, "멍해짐 시도"), Outcome("불가능한 분기", 0, "3D6") };
        table = SpellOutcomePresentation.Build(metadata, checks);
        Check(table.Rows[0].ChanceText == "—" && table.Rows[1].ChanceText == "~0%", "unknown and zero chance stay distinct");
        Check(table.Rows[0].ExpectedHpLoss == null && table.Note.Contains("0%가 아님"), "missing probabilities do not turn into invented HP contributions");

        var branchMetadata = new Dictionary<string, object> { { "branchPreview", new Dictionary<string, object> { { "outcomes", new[] {
            new Dictionary<string, object> { { "path", " → 반사 실패" }, { "chancePerCast", .6 },
                { "damageFormula", new[] { "4D6" } }, { "effectAttempts", new[] { "실명 적용" } }, { "hpLossIfReached", 12.0 } }
        } } } } };
        var sr = new List<SpellCheckRecord> { new SpellCheckRecord { Kind = "sr", Status = "Estimated", Probability = .6 } };
        table = SpellOutcomePresentation.Build(branchMetadata, sr);
        Check(table.Rows.Count == 2 && table.Rows[0].Path == "SR 차단", "generic branch tables include the blocked delivery outcome");
        Near(table.Rows.Sum(row => row.Chance.Value), 1, "blocked and reached generic branches form one per-cast distribution");
        Check(table.Rows[1].Effect.Contains("4D6") && table.Rows[1].Effect.Contains("실명"), "damage and status share one outcome cell");
        Near(table.Rows[1].ExpectedHpLoss.Value, 7.2, "generic branch preserves HP contribution");

        branchMetadata["secondaryTargetEffects"] = new Dictionary<string, object> { { "status", "Unmodeled" } };
        var directBranch = ((Dictionary<string, object>)branchMetadata["branchPreview"])["outcomes"] as Dictionary<string, object>[];
        directBranch[0]["hpLossIfReached"] = null;
        table = SpellOutcomePresentation.Build(branchMetadata, sr);
        Near(table.Rows[1].Chance.Value, .6, "known direct branch survives unknown collateral action");
        Check(table.Rows[1].ExpectedHpLoss == null, "unmodeled collateral cannot become zero or a direct-only HP forecast");
        Check(table.ProbabilityHeading == "주 대상 직접 판정", "direct-only odds have an explicit scope");
        Check(table.Note.Contains("주변 전파") && table.Note.Contains("HP"), "unmodeled collateral is visible to user");
        Check(table.OutcomeHeading.Contains("별도"), "table does not claim exhaustive spell outcomes");

        var directTerm = new Dictionary<string, object> {
            { "hpDamage", true }, { "branchCandidate", false }, { "path", "cast" }, { "formula", "4D6" } };
        var directModel = new Dictionary<string, object> { { "deliveryCount", 3 }, { "terms", new[] { directTerm } } };
        var direct = new Dictionary<string, object> { { "spellDamage", directModel } };
        var attacks = new List<SpellCheckRecord> { new SpellCheckRecord { Kind = "attack", Status = "Estimated", Probability = .7 },
            new SpellCheckRecord { Kind = "sr", Status = "Estimated", Probability = .8 } };
        table = SpellOutcomePresentation.Build(direct, attacks);
        Check(table.ProbabilityHeading == "타격당 확률" && table.Note.Contains("3회"), "multiple rays are not mislabeled as mutually exclusive whole-cast outcomes");
        Near(table.Rows[0].Chance.Value, .3, "ray miss probability");
        Near(table.Rows[1].Chance.Value, .14, "SR blocked probability includes reaching SR after hit");
        Near(table.Rows[2].Chance.Value, .56, "ray damage outcome includes hit and SR gates");
        Check(table.Rows[2].ExpectedHpLoss == null, "whole-spell mean is not distributed arbitrarily across rays");
        attacks[0].MirrorImages = 2;
        table = SpellOutcomePresentation.Build(direct, attacks);
        Check(table.ProbabilityHeading == "첫 타격 확률" && table.Note.Contains("후속"), "mirror depletion is not advertised as a constant per-ray probability");
        directTerm["branchCandidate"] = true;
        Check(SpellOutcomePresentation.Build(direct, attacks).Rows.Count == 0, "unresolved conditional trees keep their existing uncertainty summary");

        using (var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PhantasmalKiller.outcomes.json"))))
        {
            object Read(JsonElement element) => element.ValueKind == JsonValueKind.Object
                ? element.EnumerateObject().ToDictionary(property => property.Name, property => Read(property.Value))
                : element.ValueKind == JsonValueKind.Array ? (object)element.EnumerateArray().Select(Read).ToArray()
                : element.ValueKind == JsonValueKind.Number ? element.GetDouble()
                : element.ValueKind == JsonValueKind.String ? element.GetString()
                : element.ValueKind == JsonValueKind.True ? true : element.ValueKind == JsonValueKind.False ? false : null;
            var source = (Dictionary<string, object>)Read(fixture.RootElement.GetProperty("metadata"));
            source["spellOutcomes"] = ((object[])source["spellOutcomes"]).Cast<Dictionary<string, object>>().ToArray();
            table = SpellOutcomePresentation.Build(source, checks);
            Check(table.Rows.Count == fixture.RootElement.GetProperty("expectedRows").GetInt32(), "actual logged lethal spell produces all table rows");
            Near(table.Rows.Sum(row => row.Chance.Value), 1, "actual logged per-cast branches keep total probability");
            Check(table.Rows.Any(row => row.Lethal) && table.Rows.Any(row => row.Effect.Contains("3D6")), "actual lethal/damage alternatives remain visible");
            Check(table.ChecksCaption.Contains("도달 시"), "second save is explicitly conditional in the stage caption");
            Check(table.Rows.All(row => row.ExpectedHpLoss == null || row.ExpectedHpLoss == 0), "legacy log without per-branch HP does not invent contributions");
        }
        Console.WriteLine("PASS: " + assertions + " spell outcome table/actual-log presentation assertions");
    }
}
