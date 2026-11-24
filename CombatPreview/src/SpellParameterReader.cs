using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal static class SpellParameterReader
    {
        internal sealed class ReadResult
        { internal bool Available; internal int Dc, Level, RankBonus; internal string Reason; }
        internal sealed class SchoolInputs
        {
            internal object[] Features;
            internal readonly HashSet<int> Expanded = new HashSet<int>();
            internal bool Complete = true;
            internal string Stamp;
        }
        static SpellSchool? School(object fact) => CombatFields.Read(CombatFields.Read(fact, "<Param>k__BackingField"), "SpellSchool") as SpellSchool?;
        static SchoolInputs ReadSchools(UnitEntityData caster)
        {
            if (ScopedReadMemo<UnitEntityData, SchoolInputs>.TryGet(caster, out var cached)) return cached;
            using (PreviewPerformance.Measure("spell-school-inputs"))
            {
                var inputs = new SchoolInputs { Features = caster.Progression.Features.Enumerable.Cast<object>().ToArray() };
                var part = caster.Get<UnitPartExpandedArsenal>();
                if (part != null)
                {
                    if (CombatFields.Read(part, "Entries") is IEnumerable entries)
                        foreach (var entry in entries)
                            if (CombatFields.Read(entry, "School") is SpellSchool school) inputs.Expanded.Add((int)school);
                            else inputs.Complete = false;
                    else inputs.Complete = false;
                }
                inputs.Stamp = inputs.Complete + ":" + string.Join(",", inputs.Expanded.OrderBy(value => value)) + "|" + string.Join(";",
                    inputs.Features.Select(feature => BetaIdentity.Id(feature) + ":" + School(feature) + ":"
                        + BetaIdentity.Id(CombatFields.Read(CombatFields.Read(feature, "<Param>k__BackingField"), "Blueprint"))));
                ScopedReadMemo<UnitEntityData, SchoolInputs>.Put(caster, inputs); return inputs;
            }
        }
        static int FocusBonus(object fact, SpellFocusParametrized rule, SchoolInputs inputs)
        {
            var source = School(fact);
            if (!source.HasValue) return rule.BonusDC;
            var mythicSchools = inputs.Features.Where(feature => CombatFields.Reference(CombatFields.Read(rule, "m_MythicFocus"), CombatFields.Blueprint(feature)))
                .Select(School).Where(school => school.HasValue).Select(school => (int)school.Value);
            return SpellSchoolMath.Focus(rule.BonusDC, (int)source.Value, mythicSchools, inputs.Expanded);
        }
        static readonly Dictionary<Type, bool> parameterHandlers = new Dictionary<Type, bool>();
        internal static bool IsHandler(Type type)
        {
            if (!parameterHandlers.TryGetValue(type, out bool found))
                parameterHandlers[type] = found = type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Any(method => method.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(RuleCalculateAbilityParams)));
            return found;
        }
        static bool Stable(Type type) => type == typeof(MetamagicRodMechanics) || type == typeof(Kingmaker.UnitLogic.Buffs.Components.NegativeLevelComponent) || type == typeof(AddCasterLevel) || type == typeof(AddCasterLevelForAbility)
            || type == typeof(IncreaseCasterLevel) || type == typeof(IncreaseAllSpellsDC)
            || type == typeof(IncreaseSpellSchoolDC) || type == typeof(SpellFocusParametrized)
            || type == typeof(AddCasterLevelForSpellbook) || type == typeof(IncreaseSpellSpellbookDC)
            || type == typeof(IncreaseSpellSchoolCasterLevel) || type == typeof(IncreaseSpellDC)
            || type == typeof(IncreaseSpellDescriptorDC) || type == typeof(ReplaceAbilitiesStat)
            || type == typeof(ExpandedArsenalMagicSchools)
            || type == typeof(SpellSpecializationParametrized) || type == typeof(SchoolMasteryParametrized)
            || type == typeof(ArcaneBloodlineArcana) || type == typeof(ConcentrationBonus)
            // These handlers only change concentration, not the DC/CL/rank/meta
            // values retained by this cache, even when they query nearby allies.
            || type == typeof(ConcentrationBonusOnArmorType) || type == typeof(AlliedSpellcaster) || type == typeof(ShieldedCaster);
        static AbilityParams InheritedParameters(AbilityData ability)
        {
            var fact = CombatFields.Read(ability, "m_Fact");
            // Item parameters precede OverrideParams in CalculateParams.
            return CombatFields.Read(ability, "m_ConvertedFrom") == null && CombatFields.Read(fact, "<SourceItem>k__BackingField") == null
                ? CombatFields.Read(fact, "<OverrideParams>k__BackingField") as AbilityParams : null;
        }
        internal static Dictionary<string, object>[] NegativeLevelSources(UnitEntityData caster)
        {
            var sources = new List<Dictionary<string, object>>();
            foreach (var fact in caster.Facts.List.Where(fact => fact.Active)) foreach (var runtime in fact.Components)
                if (CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                    && CombatFields.Component(runtime)?.GetType() == typeof(Kingmaker.UnitLogic.Buffs.Components.NegativeLevelComponent))
                {
                    var count = CombatFields.Read(CombatFields.Read(runtime, "m_Data"), "Count") as int?;
                    sources.Add(new Dictionary<string, object> { { "sourceId", BetaIdentity.Id(fact) }, { "source", CombatFields.Label(fact) },
                        { "count", count }, { "casterLevelAdjustment", count.HasValue && count >= 0 ? (object)-count.Value : null } });
                }
            return sources.ToArray();
        }
        internal static string Source(AbilityData ability) => ItemSpellParameterReader.Item(ability) != null ? "readonly-item-parameters" : InheritedParameters(ability) != null
            ? "inherited-source-parameters" : "readonly-parameter-model";
        static AbilityParams ContextParameters(object fact) => ContextParameterRules.Read(CombatFields.Read(fact, "m_Context"), CombatFields.Read) as AbilityParams;
        internal static string DependencyStamp(AbilityData ability, UnitEntityData caster)
        {
            if (ItemSpellParameterReader.Item(ability) != null)
                return ItemSpellParameterReader.TryRead(ability, caster, out _, out _, out _, out var itemEvidence, out var itemReason)
                    ? "item-parameters:" + DamageTrace.Json(itemEvidence) : "item-parameters-unavailable:" + itemReason + ":" + BetaReader.PairStamp(caster, caster);
            var inherited = InheritedParameters(ability);
            if (inherited != null) return "inherited-parameters:" + BetaIdentity.Id(CombatFields.Read(ability, "m_Fact"))
                + ":" + inherited.DC + ":" + inherited.CasterLevel + ":" + inherited.RankBonus + ":" + (int)inherited.Metamagic;
            // Keep the strict geometry key for unknown handlers. Only audited
            // position-independent adapters can reuse DC/CL across movement.
            bool strict = caster.Get<UnitPartChangeSpellElementalDamage>() != null || ability.MagicHackData != null || CombatFields.Read(ability, "m_Fact") != null
                || CombatFields.Read(ability, "m_ConvertedFrom") != null || ability.Blueprint.ComponentsArray.Any(component =>
                component is ContextAbilityParamsCalculator || component is ContextSetAbilityParams);
            var contextDependencies = new List<string>();
            foreach (var fact in caster.Facts.List.Where(fact => fact.Active))
            foreach (var runtime in fact.Components)
            {
                var component = CombatFields.Component(runtime);
                if (component != null && CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                    && IsHandler(component.GetType()) && !Stable(component.GetType())) strict = true;
                if (component?.GetType() == typeof(MetamagicRodMechanics))
                    contextDependencies.Add("rod-swift:" + RodMetamagicReader.SwiftState(caster));
                if (component?.GetType() == typeof(Kingmaker.UnitLogic.Buffs.Components.NegativeLevelComponent)
                    && CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField"))
                    contextDependencies.Add("negative-level:" + BetaIdentity.Id(fact) + ":" + CombatFields.Read(CombatFields.Read(runtime, "m_Data"), "Count"));
                if (component?.GetType() == typeof(ReplaceAbilityParamsWithContext)
                    && CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                    && CombatFields.Reference(CombatFields.Read(component, "m_Ability"), ability.Blueprint))
                {
                    var parameters = ContextParameters(fact);
                    contextDependencies.Add(BetaIdentity.Id(fact) + ":" + (parameters == null ? "missing" : parameters.DC + ":"
                        + parameters.CasterLevel + ":" + parameters.SpellLevel + ":" + (int)parameters.Metamagic));
                }
            }
            if (strict) return "strict:" + BetaReader.NativeStamp(caster, null) + "|context-parameters:" + string.Join(";", contextDependencies);
            var book = SpellCasterLevelReader.Book(ability, caster);
            return "parameter-dependencies:" + BetaIdentity.Id(caster) + ":" + caster.Progression.CharacterLevel + ":" + caster.Progression.MythicLevel
                + ":" + string.Join(":", new[] { caster.Stats.Strength, caster.Stats.Dexterity, caster.Stats.Constitution,
                    caster.Stats.Intelligence, caster.Stats.Wisdom, caster.Stats.Charisma, caster.Stats.BonusCasterLevel,
                    caster.Stats.BaseAttackBonus }.Select(CombatFields.CachedStat))
                + "|" + string.Join(";", caster.Facts.List.Select(CombatFields.FactStamp))
                + "|book:" + BetaIdentity.Id(book) + ":" + CombatFields.Read(book, "m_BaseLevelInternal") + ":" + CombatFields.Read(book, "m_MythicLevelInternal")
                + ":" + CombatFields.Read(book, "m_Type") + "|equipment:" + BetaReader.EquipmentStamp(caster)
                + "|spell-level:" + CombatFields.Read(ability, "<SpellLevelInSpellbook>k__BackingField")
                + "|source:" + BetaIdentity.Id(CombatFields.Read(ability, "m_Fact"))
                + "|school-inputs:" + ReadSchools(caster).Stamp + "|runtime-parameters:" + string.Join(";", contextDependencies);
        }
        internal static bool TryRead(AbilityData ability, UnitEntityData caster, Metamagic meta,
            out int dc, out int level, out int rankBonus, out string reason)
        {
            var key = (ability, caster, meta);
            if (!ScopedReadMemo<(AbilityData, UnitEntityData, Metamagic), ReadResult>.TryGet(key, out var result))
            {
                result = new ReadResult();
                result.Available = TryReadCore(ability, caster, meta, out result.Dc, out result.Level, out result.RankBonus, out result.Reason);
                ScopedReadMemo<(AbilityData, UnitEntityData, Metamagic), ReadResult>.Put(key, result);
            }
            dc = result.Dc; level = result.Level; rankBonus = result.RankBonus; reason = result.Reason; return result.Available;
        }
        static bool TryReadCore(AbilityData ability, UnitEntityData caster, Metamagic meta,
            out int dc, out int level, out int rankBonus, out string reason)
        {
            dc = level = rankBonus = 0; reason = null;
            if (ItemSpellParameterReader.Item(ability) != null)
                return ItemSpellParameterReader.TryRead(ability, caster, out dc, out level, out _, out _, out reason);
            if (!AutoMetamagicReader.Resolve(ability, caster, meta, out meta, out _, out reason)) return false;
            if (!RodMetamagicReader.Resolve(ability, caster, meta, out meta, out _, out _, out reason)) return false;
            var inherited = InheritedParameters(ability);
            if (inherited != null)
            {
                if (!SpellParameterRules.InheritedParametersValid(inherited.DC, inherited.CasterLevel, (int)inherited.Metamagic, (int)meta))
                { reason = "InheritedParametersOrMetamagicUnavailable"; return false; }
                dc = inherited.DC; level = inherited.CasterLevel; rankBonus = inherited.RankBonus; return true;
            }
            var book = SpellCasterLevelReader.Book(ability, caster);
            var baseLevel = SpellCasterLevelReader.Read(ability, caster);
            if (book == null || !baseLevel.HasValue || ability.MagicHackData != null || caster.IsPlayersEnemy || caster.Get<UnitPartSpellFool>() != null
                || ability.Blueprint.ComponentsArray.Any(component => component is ContextAbilityParamsCalculator || component is ContextSetAbilityParams))
            { reason = "SpecialOrUnresolvedParameterSource"; return false; }
            if (CombatFields.Read(CombatFields.Read(ability, "m_Fact"), "<SourceItem>k__BackingField") != null)
            { reason = "ItemParameters"; return false; }
            if (CombatFields.Read(ability, "m_Fact") != null || CombatFields.Read(ability, "m_ConvertedFrom") != null)
            { reason = "InheritedOrOverriddenParameters"; return false; }
            var ranks = CombatFields.Read(book, "m_KnownSpellLevels") as IDictionary;
            int? spellLevel = CombatFields.Read(ability, "<SpellLevelInSpellbook>k__BackingField") as int?;
            if (!spellLevel.HasValue && ranks?[ability.Blueprint] is IEnumerable<int> levels && levels.Any()) spellLevel = levels.First();
            if (CombatFields.Read(ability, "m_OverrideSpellLevel") is int replaced) spellLevel = replaced;
            if (!spellLevel.HasValue || spellLevel < 0) { reason = "SpellLevelUnavailable"; return false; }
            if (!new[] { "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma" }.Contains(book.Blueprint.CastingAttribute.ToString())
                && !(CombatFields.Read(ability, "<OverrideCasterModifier>k__BackingField") is int))
            { reason = "CastingAttributeUnavailable"; return false; }
            var metaData = CombatFields.Read(ability, "<MetamagicData>k__BackingField");
            int heighten = (meta & Metamagic.Heighten) != 0 ? CombatFields.Int(metaData, "<HeightenLevel>k__BackingField") : 0;
            // Known spell levels exclude the slot cost; RuleCalculateAbilityParams
            // subtracts that cost again before adding Heighten.
            int effectiveSpellLevel = spellLevel.Value + heighten;
            if (CombatFields.Read(ability, "m_OverrideSpellLevel") is int overridden)
                effectiveSpellLevel = overridden;
            int attribute = CombatFields.Read(ability, "<OverrideCasterModifier>k__BackingField") is int modifier ? modifier
                : BetaReader.AbilityModifier(caster, book.Blueprint.CastingAttribute.ToString());
            int baseDc = 10 + effectiveSpellLevel + (CombatFields.Int(book, "m_Type") == 2 ? caster.Progression.MythicLevel : attribute);
            if (CombatFields.Read(ability, "<OverrideDC>k__BackingField") is int replacement) baseDc = replacement;
            var dcBonuses = new List<ProjectedModifier>(); var clBonuses = new List<ProjectedModifier>();
            var schoolInputs = ReadSchools(caster);
            string replacementStat = null;
            ContextParameterSnapshot? contextReplacement = null;
            foreach (var fact in caster.Facts.List.Where(fact => fact.Active))
            foreach (var runtime in fact.Components)
            {
                var component = CombatFields.Component(runtime);
                if (component == null || !CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField") || !IsHandler(component.GetType())) continue;
                int value = 0; ModifierDescriptor descriptor = (ModifierDescriptor)25; bool cl = false;
                if (component.GetType() == typeof(Kingmaker.UnitLogic.Buffs.Components.NegativeLevelComponent))
                {
                    if (!(CombatFields.Read(CombatFields.Read(runtime, "m_Data"), "Count") is int count) || count < 0)
                    { reason = "NegativeLevelCountUnavailable"; return false; }
                    value = -count; cl = true; descriptor = (ModifierDescriptor)25;
                }
                else if (component.GetType() == typeof(AddCasterLevel)) { var rule = (AddCasterLevel)component; value = rule.Bonus; descriptor = rule.Descriptor; cl = true; }
                else if (component.GetType() == typeof(AddCasterLevelForAbility))
                { var rule = (AddCasterLevelForAbility)component; if (!CombatFields.Reference(CombatFields.Read(rule, "m_Spell"), ability.Blueprint)) continue; value = rule.Bonus; descriptor = rule.Descriptor; cl = true; }
                else if (component.GetType() == typeof(AddCasterLevelForSpellbook))
                {
                    var rule = (AddCasterLevelForSpellbook)component;
                    if (!(CombatFields.Read(rule, "m_Spellbooks") is IEnumerable books) || !books.Cast<object>().Any(reference => CombatFields.Reference(reference, book.Blueprint))) continue;
                    value = rule.Bonus; descriptor = rule.Descriptor; cl = true;
                }
                else if (component.GetType() == typeof(IncreaseCasterLevel))
                { var rule = (IncreaseCasterLevel)component; if (!CombatFields.Scalar(rule.Value, fact, out value)) { reason = "DynamicCasterLevel"; return false; } descriptor = rule.Descriptor; cl = true; }
                else if (component.GetType() == typeof(IncreaseAllSpellsDC))
                { var rule = (IncreaseAllSpellsDC)component; if (!CombatFields.Scalar(rule.Value, fact, out value)) { reason = "DynamicDC"; return false; } descriptor = rule.Descriptor; }
                else if (component.GetType() == typeof(IncreaseSpellDescriptorDC))
                {
                    var rule = (IncreaseSpellDescriptorDC)component;
                    var spellDescriptor = ability.Blueprint.ComponentsArray.OfType<SpellDescriptorComponent>().FirstOrDefault();
                    if (spellDescriptor == null) continue;
                    // The engine's replacement helper calls FixNullErrors and can
                    // mutate its part. Never call it from a read-only preview.
                    if (spellDescriptor.Descriptor.Value.IsElementalDescriptor() && caster.Get<UnitPartChangeSpellElementalDamage>() != null)
                    { reason = "ElementalDescriptorReplacement"; return false; }
                    if (!SpellParameterRules.DescriptorMatches((ulong)spellDescriptor.Descriptor.Value, (ulong)rule.Descriptor.Value)) continue;
                    value = rule.BonusDC; descriptor = rule.ModifierDescriptor;
                }
                else if (component.GetType() == typeof(ReplaceAbilitiesStat))
                {
                    var rule = (ReplaceAbilitiesStat)component;
                    if (!(CombatFields.Read(rule, "m_Ability") is IEnumerable abilities)
                        || !abilities.Cast<object>().Any(reference => CombatFields.Reference(reference, ability.Blueprint))) continue;
                    // Explicit caster modifier wins over ReplaceStat in OnTrigger.
                    if (CombatFields.Read(ability, "<OverrideCasterModifier>k__BackingField") is int) continue;
                    if (ability.Blueprint.IsDomainAbility) { reason = "ReplacementDomainStat"; return false; }
                    if (!SpellParameterRules.TrySelectStat(ref replacementStat, rule.Stat.ToString()))
                    { reason = "ConflictingOrUnsupportedReplacementStat"; return false; }
                    continue;
                }
                else if (component.GetType() == typeof(IncreaseSpellDC))
                {
                    var rule = (IncreaseSpellDC)component;
                    if (CombatFields.Bool(rule, "m_AllMagicHackSpells")) continue; // this fallback excludes magic hacks
                    var reference = CombatFields.Read(rule, "m_Spell");
                    if (reference != null && !CombatFields.Reference(reference, ability.Blueprint)) continue;
                    value = rule.HalfMythicRank ? caster.Progression.MythicLevel / 2 : rule.BonusDC;
                    if (rule.UseContextBonus && CombatFields.Read(fact, "m_Context") != null && !CombatFields.Scalar(rule.Value, fact, out value))
                    { reason = "DynamicSpellDC"; return false; }
                    descriptor = rule.Descriptor;
                }
                else if (component.GetType() == typeof(IncreaseSpellSchoolDC))
                { var rule = (IncreaseSpellSchoolDC)component; if (rule.School != SpellSchool.None && rule.School != ability.SpellSchool) continue; value = rule.BonusDC; descriptor = rule.Descriptor; }
                else if (component.GetType() == typeof(IncreaseSpellSchoolCasterLevel))
                { var rule = (IncreaseSpellSchoolCasterLevel)component; if (rule.School != SpellSchool.None && rule.School != ability.SpellSchool) continue; value = rule.BonusLevel; descriptor = rule.Descriptor; cl = true; }
                else if (component.GetType() == typeof(IncreaseSpellSpellbookDC))
                {
                    var rule = (IncreaseSpellSpellbookDC)component;
                    if (!(CombatFields.Read(rule, "m_Spellbooks") is IEnumerable books) || !books.Cast<object>().Any(reference => CombatFields.Reference(reference, book.Blueprint))) continue;
                    value = rule.BonusDC; descriptor = rule.Descriptor;
                }
                else if (component.GetType() == typeof(SpellFocusParametrized))
                {
                    var rule = (SpellFocusParametrized)component;
                    if (!schoolInputs.Complete) { reason = "ExpandedSchoolEntriesUnavailable"; return false; }
                    if (schoolInputs.Expanded.Contains((int)ability.SpellSchool) || School(fact) != ability.SpellSchool) continue;
                    value = FocusBonus(fact, rule, schoolInputs); descriptor = rule.Descriptor;
                }
                else if (component.GetType() == typeof(SchoolMasteryParametrized))
                {
                    if (!schoolInputs.Complete) { reason = "ExpandedSchoolEntriesUnavailable"; return false; }
                    if (schoolInputs.Expanded.Contains((int)ability.SpellSchool) || School(fact) != ability.SpellSchool) continue;
                    value = 1; cl = true;
                }
                else if (component.GetType() == typeof(ExpandedArsenalMagicSchools))
                {
                    if (!schoolInputs.Complete) { reason = "ExpandedSchoolEntriesUnavailable"; return false; }
                    int school = (int)ability.SpellSchool;
                    if (school == 0 || !schoolInputs.Expanded.Contains(school)) continue;
                    var focusSources = new List<(int School, int Bonus)>();
                    var masterySources = new List<(int School, int Bonus)>();
                    foreach (var feature in schoolInputs.Features)
                    {
                        var donor = School(feature);
                        if (!donor.HasValue || donor.Value == ability.SpellSchool) continue;
                        bool Matches(string field) => CombatFields.Read(component, field) is IEnumerable references
                            && references.Cast<object>().Any(reference => CombatFields.Reference(reference, CombatFields.Blueprint(feature)));
                        var components = (CombatFields.Blueprint(feature) as Kingmaker.Blueprints.BlueprintScriptableObject)?.ComponentsArray;
                        if (Matches("m_Focuses"))
                        {
                            var focus = components?.OfType<SpellFocusParametrized>().FirstOrDefault();
                            if (focus == null || focus.GetType() != typeof(SpellFocusParametrized))
                            { reason = "ExpandedFocusDefinitionUnavailable"; return false; }
                            // GetBonusDC reads Param from the current runtime
                            // event context (the arsenal fact), not its donor.
                            focusSources.Add(((int)donor.Value, FocusBonus(fact, focus, schoolInputs)));
                        }
                        if (Matches("m_SchoolMastery"))
                        {
                            var mastery = components?.OfType<SchoolMasteryParametrized>().FirstOrDefault();
                            if (mastery == null || mastery.GetType() != typeof(SchoolMasteryParametrized))
                            { reason = "ExpandedMasteryDefinitionUnavailable"; return false; }
                            masterySources.Add(((int)donor.Value, 1));
                        }
                    }
                    dcBonuses.Add(new ProjectedModifier { Value = SpellSchoolMath.Transfer(school, focusSources), Descriptor = 25, Stacks = true });
                    clBonuses.Add(new ProjectedModifier { Value = SpellSchoolMath.Transfer(school, masterySources), Descriptor = 25, Stacks = true });
                    continue;
                }
                else if (component.GetType() == typeof(SpellSpecializationParametrized))
                {
                    var selected = CombatFields.Read(CombatFields.Read(fact, "<Param>k__BackingField"), "Blueprint");
                    if (selected == null || !ReferenceEquals(selected, ability.Blueprint)
                        && !CombatFields.Reference(CombatFields.Read(ability.Blueprint, "m_Parent"), selected)) continue;
                    value = 2; cl = true;
                }
                else if (component.GetType() == typeof(ReplaceAbilityParamsWithContext))
                {
                    if (!CombatFields.Reference(CombatFields.Read(component, "m_Ability"), ability.Blueprint)) continue;
                    var parameters = ContextParameters(fact);
                    if (parameters == null || !ContextParameterRules.Merge(ref contextReplacement, new ContextParameterSnapshot {
                        Dc = parameters.DC, Level = parameters.CasterLevel, SpellLevel = parameters.SpellLevel,
                        Metamagic = (int)parameters.Metamagic }, (int)meta))
                    { reason = "ContextParametersMissingConflictingOrNewMetamagic"; return false; }
                    continue;
                }
                else if (component.GetType() == typeof(ArcaneBloodlineArcana))
                { if ((int)ability.Blueprint.Type != 0 || meta == 0 || (meta & Metamagic.Heighten) != 0) continue; value = 1; }
                else if (component.GetType() == typeof(MetamagicRodMechanics) || component.GetType() == typeof(AutoMetamagic)) continue; // Resolved before numeric DC/CL calculation.
                else if (component.GetType() == typeof(ConcentrationBonus) || component.GetType() == typeof(ConcentrationBonusOnArmorType)
                    || component.GetType() == typeof(AlliedSpellcaster) || component.GetType() == typeof(ShieldedCaster)) continue;
                else { reason = "UnhandledParameterHandler:" + component.GetType().Name; return false; }
                (cl ? clBonuses : dcBonuses).Add(new ProjectedModifier { Value = value, Descriptor = (int)descriptor, Stacks = descriptor.IsStackable() });
            }
            if (replacementStat != null && CombatFields.Int(book, "m_Type") != 2
                && !(CombatFields.Read(ability, "<OverrideDC>k__BackingField") is int))
            {
                int replacementAttribute;
                if (replacementStat == "BaseAttackBonus")
                {
                    var cached = CombatFields.CachedStat(caster.Stats.BaseAttackBonus);
                    if (!cached.HasValue) { reason = "ReplacementStatUnavailable"; return false; }
                    replacementAttribute = cached.Value;
                }
                else replacementAttribute = BetaReader.AbilityModifier(caster, replacementStat);
                baseDc = checked(baseDc - attribute + replacementAttribute);
            }
            rankBonus = RuleBonusMath.Sum(clBonuses);
            dc = checked((contextReplacement?.Dc ?? baseDc) + (ability.IgnoreDCBonuses ? 0 : RuleBonusMath.Sum(dcBonuses)));
            level = checked((contextReplacement?.Level ?? baseLevel.Value) + rankBonus);
            return true;
        }
    }
}
