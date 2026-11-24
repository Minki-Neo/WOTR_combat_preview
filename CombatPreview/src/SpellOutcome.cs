using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CombatPreview
{
    internal sealed class SpellOutcome
    {
        readonly string path, damage, effect;
        readonly double? chance;
        readonly double? hpLossIfReached;

        internal SpellOutcome(string path, double? chance, string damage, string effect = null, double? hpLossIfReached = null)
        {
            this.path = path;
            this.chance = chance;
            this.damage = damage;
            this.effect = effect;
            this.hpLossIfReached = hpLossIfReached;
        }

        internal static List<string> Describe(string stages, Dictionary<string, object> metadata, List<SpellOutcome> outcomes)
        {
            metadata["spellOutcomes"] = outcomes.Select(outcome => new Dictionary<string, object> {
                { "path", outcome.path }, { "chancePerCast", outcome.chance },
                { "damage", outcome.damage }, { "effect", outcome.effect }, { "hpLossIfReached", outcome.hpLossIfReached }
            }).ToArray();
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(stages)) lines.Add(stages);
            lines.Add("시전당 결과");
            foreach (var outcome in outcomes)
                lines.Add(outcome.path + "  " + (outcome.chance.HasValue ? "~" + outcome.chance.Value.ToString("0.##%") : "—")
                    + "  ·  " + outcome.damage + (outcome.effect == null ? "" : "  ·  " + outcome.effect));
            return lines;
        }

        internal static List<string> DescribeSingleSave(List<SpellCheckRecord> checks, Dictionary<string, object> metadata)
        {
            if (!metadata.TryGetValue("spellDamage", out var raw) || !(raw is Dictionary<string, object> model)
                || !Equals(model["deliveryCount"], 1) || !Equals(model["scanTruncated"], false)
                || !(model["terms"] is List<Dictionary<string, object>> terms) || terms.Count != 1)
                return null;

            var term = terms[0];
            if (!Equals(term["hpDamage"], true) || !Equals(term["branchCandidate"], false)
                || !Equals(term["meanRepeats"], 1.0) || !Equals(term["halfIfSaved"], true)) return null;
            if (!metadata.ContainsKey("resolvedDamageBranch")
                && (!Equals(model["unloadedLinks"], 0) || !Equals(term["path"], "cast"))) return null;
            var saves = checks.Where(check => check.Kind == "save" && check.Status != "NotIdentified").Take(2).ToArray();
            if (saves.Length != 1 || !metadata.TryGetValue("directDamageSaveType", out var rootSave)
                || !Equals(rootSave, saves[0].Type)) return null;
            var attacks = checks.Where(check => check.Kind == "attack" && check.Status != "NotIdentified").Take(2).ToArray();
            if (attacks.Length > 1) return null;
            var sr = checks.FirstOrDefault(check => check.Kind == "sr");
            bool checksSr = sr != null && sr.Status != "NotRequired" && sr.Status != "NoRegisteredSR";
            double? penetration = checksSr ? sr.Probability : 1;
            double? hit = attacks.Length == 1 ? attacks[0].Probability : 1;
            double? failure = saves[0].Probability;
            string save = SpellLethalPreview.SaveLabel(saves[0].Type);
            string stages = (attacks.Length == 1 ? "명중 " + SpellLethalPreview.Percent(hit) + "  ·  " : "")
                + (checksSr ? "SR 관통 " + SpellLethalPreview.Percent(penetration) + "  ·  " : "")
                + save + " 실패 " + SpellLethalPreview.Percent(failure);
            var outcomes = new List<SpellOutcome>();
            if (attacks.Length == 1) outcomes.Add(new SpellOutcome("빗나감", 1 - hit, "피해 없음"));
            if (checksSr) outcomes.Add(new SpellOutcome("SR 차단", SpellDamageMath.Through(hit, 1 - penetration), "피해 없음"));
            var gate = SpellDamageMath.Through(hit, penetration);
            string formula = term["formula"] as string ?? "피해 주사위 미확인";
            var inputs = term.TryGetValue("probabilityInputs", out var data) ? data as Dictionary<string, object> : null;
            bool evasion = inputs != null && Equals(inputs["evasion"], true);
            bool improvedEvasion = inputs != null && Equals(inputs["improvedEvasion"], true);
            double? factor = inputs == null ? (double?)null : (double)inputs["savedMultiplier"];
            string reduced = factor == .75 ? " (75%)" : factor.HasValue ? " (반감)" : " (내성으로 감소)";
            bool immune = term.TryGetValue("registeredEnergyImmunity", out var immunity) && Equals(immunity, true);
            string daze = metadata.TryGetValue("dazeBaseRounds", out var rounds)
                ? "멍해짐 시도" + (rounds is int count && count > 0 ? " (기본 " + count + "라운드)" : "")
                : ReadEffect(metadata, "saveFailedEffects");
            outcomes.Add(new SpellOutcome(save + " 실패", SpellDamageMath.Through(gate, failure),
                immune ? "피해 면역" : formula + (improvedEvasion ? reduced : ""), daze));
            outcomes.Add(new SpellOutcome(save + " 성공", SpellDamageMath.Through(gate, 1 - failure),
                immune ? "피해 면역" : evasion ? "회피로 피해 없음" : formula + reduced,
                ReadEffect(metadata, "savePassedEffects")));
            return Describe(stages, metadata, outcomes);
        }

        internal static void ReadDirectSaveEffects(BlueprintComponent[] components, Dictionary<string, object> metadata)
        {
            var runs = components.OfType<AbilityEffectRunAction>().Take(2).ToArray();
            if (runs.Length != 1 || runs[0].SavingThrowType == SavingThrowType.Unknown) return;
            var run = runs[0];
            metadata["directDamageSaveType"] = run.SavingThrowType.ToString();
            if (run.Actions?.Actions == null || run.Actions.Actions.Length > 16) return;
            var branches = run.Actions.Actions.OfType<ContextActionConditionalSaved>().Take(2).ToArray();
            if (branches.Length != 1) return;
            SetEffects("saveFailedEffects", branches[0].Failed);
            SetEffects("savePassedEffects", branches[0].Succeed);

            void SetEffects(string key, ActionList actions)
            {
                if (actions?.Actions == null || actions.Actions.Length > 16) return;
                var effects = actions.Actions.OfType<ContextActionApplyBuff>()
                    .Select(action => LoadedBlueprintReader.Read(CombatFields.Read(action, "m_Buff")) as BlueprintBuff)
                    .Where(buff => buff != null && !string.IsNullOrWhiteSpace(buff.Name))
                    .Select(buff => buff.Name).Distinct().Take(2).ToArray();
                if (effects.Length > 0) metadata[key] = effects;
            }
        }

        static string ReadEffect(Dictionary<string, object> metadata, string key)
            => metadata.TryGetValue(key, out var value) && value is string[] effects
                ? string.Join(", ", effects) + " 시도" : null;
    }
}
