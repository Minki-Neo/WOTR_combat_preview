using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.ElementsSystem;

namespace CombatPreview
{
    internal static class SpellDamagePreview
    {
        sealed class Term { internal ContextActionDealDamage Action; internal bool Branch; internal ContextDiceValue[] Repeats; internal string Path; internal BlueprintComponent[] Components; }
        static object cachedComponents;
        static ActionList cachedClickedActions;
        static float scannedAt;
        internal static void Clear() { cachedComponents = null; cachedClickedActions = null; cachedTerms.Clear(); fields.Clear(); }
        static readonly List<Term> cachedTerms = new List<Term>();
        static bool truncated;
        static int unloadedLinks;
        static readonly Dictionary<Type, System.Reflection.FieldInfo[]> fields = new Dictionary<Type, System.Reflection.FieldInfo[]>();
        static System.Reflection.FieldInfo[] Lists(Type type)
        {
            System.Reflection.FieldInfo[] found;
            if (!fields.TryGetValue(type, out found)) { if (fields.Count >= 128) fields.Clear(); fields[type] = found = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .Where(f => f.FieldType == typeof(ActionList)).ToArray(); }
            return found;
        }
        static int I(object o, string name, int fallback = 0) { return CombatFields.Read(o, name) is int v ? v : fallback; }
        static bool B(object o, string name) { return CombatFields.Read(o, name) is bool v && v; }
        static int? Rank(object rank, BlueprintComponent[] components, int? cl, Metamagic? meta, UnitEntityData caster, int? rankBonus, int[] observedRanks = null)
        {
            var configs = components.OfType<ContextRankConfig>().Where(c => Equals(CombatFields.Read(c, "m_Type"), rank)).ToArray();
            if (configs.Length != 1) return null;
            var c = configs[0];
            int index = Convert.ToInt32(rank);
            if (observedRanks != null && index >= 0 && index < observedRanks.Length)
                return observedRanks[index];
            int? baseValue = RankBase(c, caster, cl, rankBonus);
            if (!baseValue.HasValue) return null;
            int max = I(c, "m_Max");
            if (B(c, "m_AffectedByIntensifiedMetamagic")) {
                if (!meta.HasValue) return null;
                if ((meta.Value & Metamagic.Intensified) != 0) max += 5;
            }
            string progression = CombatFields.Read(c, "m_Progression")?.ToString();
            if (progression == "Custom") {
                var entries = CombatFields.Read(c, "m_CustomProgression") as Array;
                if (entries == null || entries.Length == 0 || entries.Length > 128) return null;
                int value = 0;
                foreach (var entry in entries) { value = I(entry, "ProgressionValue"); if (baseValue.Value <= I(entry, "BaseValue")) break; }
                if (B(c, "m_UseMin")) value = Math.Max(value, I(c, "m_Min"));
                if (B(c, "m_UseMax")) value = Math.Min(value, max);
                return value;
            }
            return SpellDamageMath.Rank(baseValue.Value, progression, I(c, "m_StartLevel"), I(c, "m_StepLevel"), B(c, "m_UseMin") ? (int?)I(c, "m_Min") : null, B(c, "m_UseMax") ? (int?)max : null);
        }
        static int? RankBase(ContextRankConfig config, UnitEntityData caster, int? cl, int? rankBonus, int[] observedRanks = null)
        {
            string basis = CombatFields.Read(config, "m_BaseValueType")?.ToString();
            if (basis == "CasterLevel") return cl;
            if (caster == null) return null;
            switch (basis)
            {
                case "BaseAttack":
                    return CombatFields.Read(caster.Stats.BaseAttackBonus, "<ModifiedValue>k__BackingField") as int?;
                case "CharacterLevel":
                    return caster.Progression.CharacterLevel;
                case "MythicLevel":
                    return caster.Progression.MythicLevel;
                case "ClassLevel":
                    if (!rankBonus.HasValue || !(CombatFields.Read(config, "m_Class") is Array classes)) return null;
                    bool except = B(config, "m_ExceptClasses");
                    int level = rankBonus.Value;
                    foreach (var entry in caster.Progression.Classes)
                    {
                        bool listed = classes.Cast<object>().Any(reference => CombatFields.Reference(reference, entry.CharacterClass));
                        if (listed != except) level += entry.Level;
                    }
                    return level;
                default:
                    return null;
            }
        }

        internal static int? Value(ContextValue value, BlueprintComponent[] components, int? cl, Metamagic? meta, UnitEntityData caster, int? rankBonus, int[] observedRanks = null, int depth = 0)
        {
            if (value == null) return null;
            if (value.ValueType == ContextValueType.Simple) return value.Value;
            if (value.ValueType == ContextValueType.Rank) return Rank(value.ValueRank, components, cl, meta, caster, rankBonus, observedRanks);
            if (value.ValueType != ContextValueType.Shared) return null;
            return Shared(value.ValueShared, components, cl, meta, caster, rankBonus, observedRanks, depth);
        }

        static int? Shared(AbilitySharedValue slot, BlueprintComponent[] components, int? cl, Metamagic? meta,
            UnitEntityData caster, int? rankBonus, int[] observedRanks, int depth)
        {
            if (depth >= 4) return null;
            var sources = components.OfType<ContextCalculateSharedValue>()
                .Where(source => source.ValueType == slot).Take(2).ToArray();
            if (sources.Length != 1 || sources[0].Value == null) return null;
            var shared = sources[0];
            int? dice = Value(shared.Value.DiceCountValue, components, cl, meta, caster, rankBonus, observedRanks, depth + 1);
            int? bonus = Value(shared.Value.BonusValue, components, cl, meta, caster, rankBonus, observedRanks, depth + 1);
            if (dice != 0 || !bonus.HasValue) return null;
            double result = bonus.Value * shared.Modifier;
            return !double.IsNaN(result) && result >= int.MinValue && result <= int.MaxValue ? (int?)result : null;
        }
        static double? SharedDamageMean(AbilitySharedValue slot, BlueprintComponent[] components, int? cl,
            Metamagic? meta, UnitEntityData caster, int? rankBonus, int[] observedRanks, bool half)
        {
            if (!meta.HasValue) return null;
            var sources = components.OfType<ContextCalculateSharedValue>()
                .Where(source => source.ValueType == slot).Take(2).ToArray();
            if (sources.Length != 1 || sources[0].Value == null) return null;
            var shared = sources[0];
            int? dice = Value(shared.Value.DiceCountValue, components, cl, meta, caster, rankBonus, observedRanks, 1);
            int? bonus = Value(shared.Value.BonusValue, components, cl, meta, caster, rankBonus, observedRanks, 1);
            if (!dice.HasValue || !bonus.HasValue) return null;
            int sides = Math.Max(1, Kingmaker.RuleSystem.DiceTypeExtension.Sides(shared.Value.DiceType));
            return SpellDamageMath.MeanContextDice(dice.Value, sides, bonus.Value,
                (meta.Value & Metamagic.Maximize) != 0, (meta.Value & Metamagic.Empower) != 0,
                shared.Modifier, half ? .5 : 1, 1);
        }
        static void Walk(ActionList list, bool branch, ContextDiceValue[] repeats, string path, BlueprintComponent[] components, HashSet<object> active, int depth, ref int budget)
        {
            if (list?.Actions == null) return;
            if (depth > 8 || budget <= 0) { truncated = true; return; }
            if (!active.Add(list)) { truncated = true; return; }
            foreach (var action in list.Actions) {
                if (--budget < 0) { truncated = true; break; }
                if (action == null) continue;
                var damage = action as ContextActionDealDamage;
                if (damage != null) { cachedTerms.Add(new Term { Action = damage, Branch = branch, Repeats = repeats, Path = path, Components = components }); continue; }
                var repeat = action as ContextActionRepeatedActions;
                if (repeat != null) {
                    var nestedRepeats = repeats.Concat(new[] { repeat.Value }).ToArray();
                    Walk(repeat.Actions, branch, nestedRepeats, path + "/repeat", components, active, depth + 1, ref budget);
                    continue;
                }
                WalkLinked(action, path, active, depth, ref budget);
                foreach (var field in Lists(action.GetType()))
                    Walk(field.GetValue(action) as ActionList, true, repeats, path + "/" + action.GetType().Name + "." + field.Name, components, active, depth + 1, ref budget);
            }
            active.Remove(list);
        }
        static void WalkLinked(object node, string path, HashSet<object> active, int depth, ref int budget)
        {
            string name = node.GetType().Name;
            string field = name == "ContextActionApplyBuff" || name == "AbilityAreaEffectBuff" ? "m_Buff"
                : name == "ContextActionSpawnAreaEffect" || name == "AddAreaEffect" ? "m_AreaEffect" : null;
            if (field == null || budget <= 0 || depth >= 8) return;
            var blueprint = LoadedBlueprintReader.Read(CombatFields.Read(node, field)) as BlueprintScriptableObject;
            if (blueprint == null) { unloadedLinks++; return; }
            if (!active.Add(blueprint)) { truncated = true; return; }
            foreach (var component in blueprint.ComponentsArray)
            {
                if (component == null) continue;
                if (--budget <= 0) { truncated = true; break; }
                string stage = path + "/" + name + "/" + component.GetType().Name;
                foreach (var actions in Lists(component.GetType()))
                    Walk(actions.GetValue(component) as ActionList, true, new ContextDiceValue[0],
                        stage + "." + actions.Name, blueprint.ComponentsArray, active, depth + 1, ref budget);
                WalkLinked(component, stage, active, depth + 1, ref budget);
            }
            active.Remove(blueprint);
        }

        static double? RepeatMean(ContextDiceValue[] repeats, BlueprintComponent[] components, int? cl, Metamagic? meta, UnitEntityData caster, int? rankBonus, int[] observedRanks, out bool fixedCount)
        {
            fixedCount = true;
            double total = 1;
            foreach (var repeat in repeats)
            {
                int? dice = Value(repeat?.DiceCountValue, components, cl, meta, caster, rankBonus, observedRanks);
                int? bonus = Value(repeat?.BonusValue, components, cl, meta, caster, rankBonus, observedRanks);
                if (!dice.HasValue || !bonus.HasValue) return null;
                int sides = Math.Max(1, Kingmaker.RuleSystem.DiceTypeExtension.Sides(repeat.DiceType));
                bool empower = meta.HasValue && (meta.Value & Metamagic.Empower) != 0;
                bool maximize = meta.HasValue && (meta.Value & Metamagic.Maximize) != 0;
                double? mean = sides > 1
                    ? SpellDamageMath.MeanContextDice(dice.Value, sides, bonus.Value, maximize, empower, 1, 1, 0)
                    : SpellDamageMath.Mean(dice.Value, sides, bonus.Value);
                fixedCount &= dice == 0 || sides == 1 || maximize;
                long maxCount = (long)dice.Value * sides + bonus.Value;
                if (empower && sides > 1) maxCount += maxCount / 2;
                if (maxCount > 100) return null;
                if (!mean.HasValue || total * mean.Value > 1000) return null;
                total *= mean.Value;
            }
            return total;
        }

        internal static void Append(BlueprintComponent[] components, int? cl, Metamagic? meta, List<string> lines, Dictionary<string, object> metadata, Kingmaker.EntitySystem.Entities.UnitEntityData target = null,
            SpellDamageConditions conditions = null, UnitEntityData caster = null, int? rankBonus = null, int[] observedRanks = null,
            bool compositeApplication = false, ActionList clickedActions = null, ContextActionDealDamage resolvedDamage = null,
            AbilityData sourceAbility = null)
        {
            using (PreviewPerformance.Measure("spell-damage-preview")) {
                if (!ReferenceEquals(cachedComponents, components) || !ReferenceEquals(cachedClickedActions, clickedActions) || UnityEngine.Time.unscaledTime < scannedAt || UnityEngine.Time.unscaledTime - scannedAt >= .25f) {
                    cachedComponents = components; cachedClickedActions = clickedActions; scannedAt = UnityEngine.Time.unscaledTime; cachedTerms.Clear(); truncated = false; unloadedLinks = 0; int budget = 256;
                    if (clickedActions != null)
                        Walk(clickedActions, true, new ContextDiceValue[0], "clicked-target", components, new HashSet<object>(), 0, ref budget);
                    else
                        foreach (var run in components.OfType<AbilityEffectRunAction>())
                            Walk(run.Actions, false, new ContextDiceValue[0], "cast", components, new HashSet<object>(), 0, ref budget);
                }
                var details = new List<Dictionary<string, object>>();
                var projectiles = components.OfType<AbilityDeliverProjectile>().ToArray();
                int? count = projectiles.Length == 0 ? (int?)1 : null;
                string delivery = projectiles.Length == 0 ? "직접 효과" : "발사체";
                bool simple = projectiles.Length == 0;
                if (projectiles.Length == 1) {
                    var p = projectiles[0]; var refs = CombatFields.Read(p, "m_Projectiles") as Array;
                    count = refs == null ? null : SpellDamageMath.Projectiles(refs.Length, p.UseMaxProjectilesCount, p.UseMaxProjectilesCount ? Rank(p.MaxProjectilesCountRank, components, cl, meta, caster, rankBonus, observedRanks) : null);
                    simple = p.Type.ToString() == "Simple";
                    if (!simple) { count = 1; delivery = "범위 효과 (대상별)"; }
                }
                if (clickedActions != null) { count = 1; delivery = "클릭 대상 후속 효과"; }
                else if (compositeApplication) { count = 1; delivery = "합성 적용당"; }
                metadata["spellDamage"] = new Dictionary<string, object> { { "casterLevel", cl }, { "deliveryCount", count }, { "deliveryKind", delivery }, { "scanTruncated", truncated }, { "unloadedLinks", unloadedLinks }, { "terms", details }, { "basis", "static-definition/basic-CL-scaling; before defenses and external modifiers" } };
                lines.Add("피해 · CL " + (cl.HasValue ? cl.Value.ToString() : "미확인") + " · " + delivery + " " + (count.HasValue ? count.ToString() : "미확인") + "회");
                bool completeTotal = !truncated && cachedTerms.Count > 0 && cachedTerms.Count <= 12
                    && components.OfType<AbilityEffectRunAction>().Count() == 1;
                double total = 0;
                var selectedTerms = resolvedDamage == null ? cachedTerms : cachedTerms.Where(t => ReferenceEquals(t.Action, resolvedDamage));
                var directHp = selectedTerms.Where(t => ReferenceEquals(t.Components, components) && CombatFields.Read(t.Action, "m_Type")?.ToString() == "Damage").ToArray();
                double targetVulnerability = SwarmDefenseReader.SpellMultiplier(sourceAbility, target);
                var areaHalfSources = SpellTargetScalingReader.AreaHalfSources(target);
                var energyScaling = new SpellEnergyScalingReader(caster, target);
                foreach (var term in selectedTerms.Take(12)) {
                    var damage = term.Action;
                    var sourceDamage = damage;
                    bool sharedRaw = false;
                    if (damage.ReadPreRolledFromSharedValue && term.Path == "cast")
                    {
                        var prior = cachedTerms.TakeWhile(candidate => !ReferenceEquals(candidate, term)).LastOrDefault();
                        var roots = components.OfType<AbilityEffectRunAction>().Take(2).ToArray();
                        var rootActions = roots.Length == 1 ? roots[0].Actions?.Actions : null;
                        int position = rootActions == null ? -1 : Array.IndexOf(rootActions, damage);
                        if (prior != null && prior.Path == "cast" && ReferenceEquals(prior.Components, term.Components)
                            && position > 0 && ReferenceEquals(rootActions[position - 1], prior.Action)
                            && prior.Action.WriteResultToSharedValue && prior.Action.WriteRawResultToSharedValue
                            && prior.Action.ResultSharedValue == damage.PreRolledSharedValue
                            && !prior.Action.ReadPreRolledFromSharedValue && prior.Repeats.Length == 0)
                        {
                            sourceDamage = prior.Action;
                            sharedRaw = true;
                        }
                    }
                    bool conditional = term.Branch && !ReferenceEquals(damage, resolvedDamage);
                    var termRanks = ReferenceEquals(term.Components, components) ? observedRanks : null;
                    int? dice = Value(sourceDamage.Value?.DiceCountValue, term.Components, cl, meta, caster, rankBonus, termRanks), bonus = Value(sourceDamage.Value?.BonusValue, term.Components, cl, meta, caster, rankBonus, termRanks);
                    int sides = sourceDamage.Value == null ? 0 : Kingmaker.RuleSystem.DiceTypeExtension.Sides(sourceDamage.Value.DiceType);
                    bool hp = CombatFields.Read(damage, "m_Type")?.ToString() == "Damage";
                    int? preRolled = null;
                    if (damage.ReadPreRolledFromSharedValue && !sharedRaw)
                    {
                        preRolled = Shared(damage.PreRolledSharedValue, term.Components, cl, meta, caster, rankBonus, termRanks, 0);
                        if (preRolled.HasValue) { dice = 0; bonus = preRolled; sides = 1; }
                    }
                    var repeats = RepeatMean(term.Repeats, term.Components, cl, meta, caster, rankBonus, termRanks, out bool fixedRepeats);
                    var bonusSources = new List<string>();
                    int? baseBonus = bonus;
                    // Pre-rolled results can already contain external modifiers. Do
                    // not add them a second time without its complete write/read semantics.
                    if (hp && dice.HasValue && bonus.HasValue && !damage.IgnoreUnitModifiers && !damage.ReadPreRolledFromSharedValue && !damage.WriteRawResultToSharedValue)
                    {
                        bonus += SpellBonusReader.Bonus(sourceAbility, caster, dice.Value, term.Components, cl, meta, rankBonus, bonusSources, termRanks);
                        bonus += SpellBonusReader.TargetBonus(caster, target, bonusSources);
                    }
                    bool supported = (!damage.ReadPreRolledFromSharedValue || sharedRaw || preRolled.HasValue) && dice.HasValue && bonus.HasValue && dice.Value >= 0 && dice.Value <= 1000 && (dice.Value == 0 || sides > 0);
                    string reason = damage.ReadPreRolledFromSharedValue && !preRolled.HasValue ? "PreRolledSharedValue" : !supported ? "DynamicRankOrValueUnavailable" : null;
                    string diceText = supported ? (dice == 0 ? bonus.ToString() : dice + "D" + sides + (bonus == 0 ? "" : bonus > 0 ? "+" + bonus : bonus.ToString())) : "주사위 미확인";
                    string type = hp
                        ? (damage.DamageType == null ? "Unknown" : DefenseReader.Classify(damage.DamageType).ToString())
                        : CombatFields.Read(damage, "m_Type")?.ToString() ?? "Unknown";
                    double? mean = hp && damage.ReadPreRolledFromSharedValue && !preRolled.HasValue
                        ? SharedDamageMean(damage.PreRolledSharedValue, term.Components, cl, meta, caster,
                            rankBonus, termRanks, damage.Half && !damage.AlreadyHalved)
                        : null;
                    if (mean.HasValue)
                    {
                        diceText = "공유 주사위";
                        reason = "ConditionalSharedInitializer";
                    }
                    bool empower = !preRolled.HasValue && meta.HasValue && (meta.Value & Metamagic.Empower) != 0;
                    bool maximize = !preRolled.HasValue && meta.HasValue && (meta.Value & Metamagic.Maximize) != 0;
                    bool half = damage.Half && !damage.AlreadyHalved;
                    if (supported)
                        mean = hp ? SpellDamageMath.MeanHitPoints(dice.Value, Math.Max(1, sides), bonus.Value, maximize, empower, half)
                            : SpellDamageMath.Mean(dice.Value, Math.Max(1, sides), bonus.Value);
                    var data = new Dictionary<string, object> { { "dice", dice }, { "sides", sides }, { "bonus", bonus }, { "formula", diceText }, { "damageType", type }, { "meanPerApplicationRaw", mean }, { "meanRepeats", repeats }, { "empower", empower }, { "maximize", maximize }, { "hpDamage", hp }, { "branchCandidate", conditional }, { "path", term.Path }, { "usesSharedDefinition", damage.ReadPreRolledFromSharedValue || damage.Value?.DiceCountValue?.ValueType == ContextValueType.Shared || damage.Value?.BonusValue?.ValueType == ContextValueType.Shared }, { "half", damage.Half }, { "halfIfSaved", damage.HalfIfSaved }, { "unavailableReason", reason }, { "totalRaw", null } };
                    details.Add(data);
                    data["actionId"] = BetaIdentity.Id(damage);
                    data["baseBonus"] = baseBonus;
                    data["additionalDamageSources"] = bonusSources.ToArray();
                    lines.AddRange(bonusSources);
                    data["sharedRollGroup"] = cachedTerms.FindIndex(candidate => ReferenceEquals(candidate.Action, sourceDamage));
                    if (sharedRaw)
                    {
                        data["usesSharedDefinition"] = false;
                        data["sharedRawRoll"] = true;
                        data["unavailableReason"] = null;
                    }
                    data["effectiveHalf"] = half;
                    data["alreadyHalved"] = damage.AlreadyHalved;
                    data["minimumHpAfterDamage"] = damage.UseMinHPAfterDamage ? (int?)damage.MinHPAfterDamage : null;
                    data["physicalForm"] = CombatFields.Int(damage.DamageType?.Physical, "Form");
                    bool directContext = ReferenceEquals(term.Components, components);
                    bool areaDamage = directContext && SpellTargetScalingReader.IsArea(damage, sourceAbility);
                    double durability = SpellTargetScaling.Durability(areaDamage, areaHalfSources.Length);
                    double swarm = directContext ? targetVulnerability : 1;
                    var scale = energyScaling.Read(hp ? damage.DamageType : null,
                        Equals(metadata.TryGetValue("directDamageNoAttackRoll", out var noAttack) ? noAttack : null, true),
                        swarm, durability, out int? percent, out double energyVulnerability, out string scaleReason, out var scaleSources);
                    if (!directContext && scaleSources.Length > 0)
                    { scale = null; scaleReason = "LinkedEnergyScalingContextUnavailable"; }
                    double vulnerability = scale ?? double.NaN;
                    data["targetVulnerability"] = directContext ? targetVulnerability : 1;
                    data["isAreaDamage"] = areaDamage;
                    data["targetDurability"] = durability;
                    data["targetDamageMultiplier"] = scale;
                    data["energyBonusPercent"] = percent;
                    data["energyVulnerability"] = energyVulnerability;
                    data["energyScalingSources"] = scaleSources;
                    data["targetScalingUnavailableReason"] = scaleReason;
                    data["areaHalfSources"] = areaDamage ? areaHalfSources : new Dictionary<string, object>[0];
                    if (durability < 1) lines.Add("범위 피해 반감 반영 ×" + durability.ToString("0.####"));
                    if (swarm > 1) lines.Add("군체: 범위 피해 취약 ×" + swarm.ToString("0.##"));
                    if (percent.HasValue && percent != 0) lines.Add("에너지 피해 보정 " + percent.Value.ToString("+0;-0;0") + "%");
                    if (energyVulnerability > 1) lines.Add("에너지 취약 ×" + energyVulnerability.ToString("0.##"));
                    if (scaleReason != null) lines.Add("에너지 배율 미확인: " + scaleReason);
                    if (hp && supported && percent.HasValue)
                        mean = scaleReason == null ? SpellDamageMath.MeanHitPoints(dice.Value, Math.Max(1, sides), bonus.Value,
                            maximize, empower, half, bonusPercent: percent.Value) : null;
                    data["meanPerApplicationRaw"] = mean;
                    if (scaleReason != null) data["unavailableReason"] = scaleReason;
                    if (Equals(data["usesSharedDefinition"], true) && (supported || mean.HasValue))
                        lines.Add("공유 값 초기 정의 기준 · 시전 중 덮어쓰기 별도");
                    lines.Add((ReferenceEquals(term.Components, components) ? "피해 " : "후속 피해 ") + diceText + " · " + type + (mean.HasValue ? " · 평균 " + mean.Value.ToString("0.##") : (supported ? " · 기본 평균 미확정" : " · CL/동적 값 판독 대기")) + (conditional ? " · 조건 분기 후보" : "") + (repeats != 1 ? " · 평균 반복 " + (repeats.HasValue ? repeats.Value.ToString("0.##") : "미확인") : "") + (damage.Half ? " · 기본 반감" : "") + (damage.HalfIfSaved ? " · 내성 성공 시 피해 감소" : ""));
                    if (target != null && directHp.Contains(term)
                        && !conditional && !Equals(data["usesSharedDefinition"], true) && mean.HasValue && meta.HasValue && damage.DamageType != null
                        && (damage.DamageType.Type.ToString() == "Energy" || damage.DamageType.Type.ToString() == "Physical")
                        && Enum.TryParse(type, out DamageKind kind)
                        && kind != DamageKind.Unknown)
                    {
                        bool singleApplication = count == 1 && term.Repeats.Length == 0;
                        var physicalTraits = new WeaponTraits { Known = true,
                            Form = CombatFields.Int(damage.DamageType.Physical, "Form"),
                            Materials = CombatFields.Int(damage.DamageType.Physical, "Material"),
                            Enhancement = CombatFields.Int(damage.DamageType.Physical, "Enhancement"),
                            EnhancementTotal = CombatFields.Int(damage.DamageType.Physical, "EnhancementTotal") };
                        var profile = AutomaticDefenseReader.Read(target, null, physicalTraits, out _, energyOnly: kind != DamageKind.Physical,
                            readEnergyPools: true, singleApplication: kind == DamageKind.Physical && singleApplication && directHp.Length == 1,
                            incomingKind: kind);
                        if (profile.PhysicalPool.HasValue)
                        { profile.Complete = false; profile.UnavailableReason += "; 비무기 물리 연속 피해와 보호막"; }
                        if (directHp.Length > 1 && (profile.HasConsumablePool || directHp.Any(other => other.Branch)))
                            profile.Complete = false;
                        if (profile.Complete)
                        {
                            profile.Reduction.TryGetValue(kind, out int resistance);
                            bool hasPool = profile.EnergyPools.TryGetValue(kind, out var pool);
                            bool overlappingPool = hasPool && profile.MultipleEnergyPools.Contains(kind);
                            profile.ConstantEnergyResistance.TryGetValue(kind, out int constantResistance);
                            double decline = profile.IncorporealEnergyDivisor && kind != DamageKind.PositiveEnergy
                                && kind != DamageKind.NegativeEnergy && kind != DamageKind.Holy
                                && kind != DamageKind.Unholy && kind != DamageKind.Divine ? .5 : 1;
                            if (hasPool && singleApplication && !overlappingPool)
                                resistance = Math.Max(constantResistance, Math.Min(pool.Remaining, pool.PerHit));
                            bool immune = profile.Immunities.Contains(kind);
                            double? defended = immune ? 0 : overlappingPool ? (double?)null
                                : SpellDamageMath.MeanHitPoints(dice.Value, Math.Max(1, sides), bonus.Value, maximize, empower, half, resistance, decline, vulnerability, percent.GetValueOrDefault());
                            data["meanWithRegisteredResistance"] = defended;
                            data["registeredResistance"] = resistance;
                            data["damageDeclineFactor"] = decline;
                            if (decline < 1) lines.Add("비실체 피해 반감 반영");
                            data["singleApplicationPool"] = hasPool;
                            if (hasPool) data["poolRemaining"] = pool.Remaining;
                            if (defended.HasValue) lines.Add("등록 저항·면역 적용 시 1회 평균 " + defended.Value.ToString("0.##"));
                            bool perDelivery = count > 1 && term.Repeats.Length == 0
                                && projectiles.Length == 1 && projectiles[0].GetType() == typeof(AbilityDeliverProjectile)
                                && projectiles[0].Type.ToString() == "Simple";
                            if (conditions != null && completeTotal && count.HasValue && repeats.HasValue && defended.HasValue
                                && !damage.AlreadyHalved && (!conditions.HasSave || damage.HalfIfSaved)
                                && (singleApplication || !hasPool || profile.Immunities.Contains(kind)))
                            {
                                double? failed = immune ? 0 : conditions.ImprovedEvasion
                                    ? SpellDamageMath.MeanHitPoints(dice.Value, Math.Max(1, sides), bonus.Value, maximize, empower, half, resistance, conditions.SavedMultiplier * decline, vulnerability, percent.GetValueOrDefault())
                                    : defended;
                                double? passed = immune || conditions.Evasion ? 0
                                    : SpellDamageMath.MeanHitPoints(dice.Value, Math.Max(1, sides), bonus.Value, maximize, empower, half, resistance, conditions.SavedMultiplier * decline, vulnerability, percent.GetValueOrDefault());
                                if (failed.HasValue && passed.HasValue)
                                {
                                    double hit = perDelivery ? conditions.MeanProjectileHit(count.Value) : conditions.Hit;
                                    double expected = SpellDamageMath.Expected(count.Value * repeats.Value, hit,
                                        conditions.Penetration, conditions.SaveFailure, failed.Value, passed.Value);
                                    data["expectedSupportedModel"] = expected;
                                    data["probabilityInputs"] = conditions.Data(failed.Value, passed.Value);
                                    data["meanHitPerApplication"] = hit;
                                    data["registeredEnergyImmunity"] = immune;
                                    data["singleDamageApplication"] = singleApplication;
                                    if (!hasPool && !conditions.HasSave && conditions.Penetration == 1
                                        && conditions.MirrorImages == 0 && perDelivery)
                                        data["independentDamageApplications"] = count.Value;
                                    lines.Add("기본 판정·등록 저항 기준 기댓값 ~" + expected.ToString("0.##") + " HP");
                                }
                            }
                            bool sharedDelivery = count == 1 && repeats > 1 && fixedRepeats;
                            if (hasPool && vulnerability == 1 && percent == 0 && completeTotal && (perDelivery || sharedDelivery)
                                && conditions != null && !damage.AlreadyHalved && (!conditions.HasSave || damage.HalfIfSaved)
                                && (!perDelivery || conditions.MirrorImages == 0)
                                && !profile.Immunities.Contains(kind) && profile.EnergySequenceSources.TryGetValue(kind, out var sources)
                                && sources.All(source => source.Order >= 0)
                                && sources.Select(source => source.Order).Distinct().Count() == sources.Count)
                            {
                                var ordered = sources.OrderBy(source => source.Order).Select(source => (source.Remaining, source.PerHit)).ToArray();
                                double failedFactor = conditions.ImprovedEvasion ? conditions.SavedMultiplier : 1;
                                double passedFactor = conditions.Evasion ? 0 : conditions.SavedMultiplier;
                                int applications = perDelivery ? count.Value : (int)repeats.Value;
                                double? PerSequence(double hit, double failure, double failed, double passed)
                                    => SpellDamageMath.ExpectedThroughOrderedPools(dice.Value, Math.Max(1, sides), bonus.Value,
                                        maximize, empower, half, applications, hit, failure, failed * decline, passed * decline, ordered);
                                double? expected;
                                if (perDelivery)
                                    expected = PerSequence(conditions.Hit * conditions.Penetration,
                                        conditions.SaveFailure, failedFactor, passedFactor);
                                else if (!conditions.HasSave)
                                    expected = conditions.Hit * conditions.Penetration * PerSequence(1, 1, 1, 1);
                                else
                                    expected = conditions.Hit * conditions.Penetration *
                                        (conditions.SaveFailure * PerSequence(1, 1, failedFactor, failedFactor)
                                         + (1 - conditions.SaveFailure) * PerSequence(1, 1, passedFactor, passedFactor));
                                data["orderedPoolSources"] = sources.OrderBy(source => source.Order).Select(source =>
                                    new Dictionary<string, object> { { "order", source.Order }, { "remaining", source.Remaining }, { "perHit", source.PerHit } }).ToArray();
                                data["poolCheckScope"] = perDelivery ? "per-delivery" : "shared-by-repeats";
                                data["poolSequenceStatus"] = expected.HasValue ? "OrderedSequence" : "DistributionBudget";
                                if (expected.HasValue)
                                {
                                    data["expectedSupportedModel"] = expected;
                                    data["poolSequenceApplications"] = applications;
                                    data["poolSequenceHitChanceAcOnly"] = conditions.Hit;
                                    data["poolSequencePenetration"] = conditions.Penetration;
                                    data["poolSequenceSaveFailure"] = conditions.SaveFailure;
                                    lines.Add((perDelivery ? "타격별 판정" : "반복 공유 판정") + " · 보호막 적용 기댓값 ~" + expected.Value.ToString("0.##") + " HP");
                                }
                            }
                            else if (hasPool && !singleApplication && !profile.Immunities.Contains(kind))
                            {
                                data["poolSequenceStatus"] = "OverlappingOrConditionalPool";
                                if (vulnerability == 1 && percent == 0 && !profile.MultipleEnergyPools.Contains(kind) && completeTotal && count > 1
                                    && term.Repeats.Length == 0 && conditions != null && !conditions.HasSave
                                    && conditions.MirrorImages == 0
                                    && conditions.Penetration == 1 && !damage.AlreadyHalved)
                                {
                                    double? expected = SpellDamageMath.ExpectedThroughPool(dice.Value, Math.Max(1, sides),
                                        bonus.Value, maximize, empower, half, count.Value, conditions.Hit, pool.Remaining, pool.PerHit, constantResistance, decline);
                                    data["poolSequenceStatus"] = expected.HasValue ? "Computed" : "DistributionBudgetOrPriorityTie";
                                    if (expected.HasValue)
                                    {
                                        data["expectedSupportedModel"] = expected;
                                        data["poolPerHit"] = pool.PerHit;
                                        data["constantEnergyResistance"] = constantResistance;
                                        data["poolSequenceHitChanceAcOnly"] = conditions.Hit;
                                        data["poolSequenceApplications"] = count.Value;
                                        lines.Add("보호막 잔량 " + pool.Remaining + " · 연속 타격 기댓값 ~" + expected.Value.ToString("0.##") + " HP");
                                    }
                                }
                                else lines.Add("보호막 잔량 " + pool.Remaining + " · 중첩 또는 조건부 연속 타격");
                            }
                        }
                        else
                        {
                            data["resistanceUnavailableReason"] = profile.UnavailableReason;
                            lines.Add("등록 저항 계산 보류 · " + profile.UnavailableReason);
                        }
                    }
                    if (!conditional && hp && supported && mean.HasValue && !damage.AlreadyHalved && repeats.HasValue && count.HasValue && meta.HasValue) {
                        double applications = count.Value * repeats.Value;
                        data["totalRaw"] = mean * applications;
                        total += mean.Value * applications;
                    } else if (hp && ReferenceEquals(term.Components, components)) {
                        completeTotal = false;
                    }
                }
                if (completeTotal && details.Any(d => d["totalRaw"] != null))
                    lines.Add("직접 피해 적용 시 평균 " + total.ToString("0.##") + " · 명중·내성·저항 적용 전");
                var hpDetails = details.Where(detail => Equals(detail["hpDamage"], true)).ToArray();
                if (completeTotal && cachedTerms.Count <= 12 && hpDetails.Length > 0
                    && hpDetails.All(detail => detail.TryGetValue("expectedSupportedModel", out var expected) && expected is double))
                {
                    double expectedTotal = hpDetails.Sum(detail => (double)detail["expectedSupportedModel"]);
                    ((Dictionary<string, object>)metadata["spellDamage"])["expectedSupportedTotal"] = expectedTotal;
                    lines.Add("전체 피해 성분 기댓값 ~" + expectedTotal.ToString("0.##") + " HP");
                }
                if (cachedTerms.Count == 0) lines.Add("읽을 수 있는 피해 효과 없음");
                if (unloadedLinks > 0 && resolvedDamage == null) lines.Add("아직 로드되지 않은 장판·버프 효과 " + unloadedLinks + "개");
                if (truncated || cachedTerms.Count > 12) lines.Add("피해 효과 탐색/표시 한도 초과 · 합계 미확정");
                if (meta.HasValue && (meta.Value & (Metamagic.Empower | Metamagic.Maximize)) != 0) lines.Add("강화/극대 반영 · 주사위식은 기본값");
                lines.Add("피해는 기본식 추정 · 추가 피해/랭크 보너스/특수 상한 해제·방어 효과는 별도");
            }
        }
    }
}
