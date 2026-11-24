using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    // Detached value copies from existing events/fields. Never trigger a rule,
    // evaluate a ContextValue, load a reference or recalculate a live stat.
    internal static class CompletedAttackEvidence
    {
        static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
        static FieldInfo[] Layout(Type type)
        {
            lock (fields)
            {
                if (fields.TryGetValue(type, out var found)) return found;
                var result = new List<FieldInfo>();
                for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                    result.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                return fields[type] = result.ToArray();
            }
        }
        static object Read(object value, string name) => CombatFields.Read(value, name);
        static object Auto(object value, string name) => Read(value, "<" + name + ">k__BackingField");
        internal static object Fact(object fact)
        {
            var bp = CombatFields.Blueprint(fact) as SimpleBlueprint;
            return fact == null ? null : new Dictionary<string, object> {
                { "instanceId", BetaIdentity.Id(fact) }, { "blueprintId", bp?.AssetGuid.ToString() }, { "label", CombatFields.Label(fact) }
            };
        }
        static object Scalar(object value)
        {
            if (value == null || value is string || value is bool || value is int || value is long || value is float || value is double) return value;
            if (value is Enum) return value.ToString();
            if (value is BlueprintReferenceBase) return new Dictionary<string, object> { { "blueprintId", CombatFields.ReferenceGuid(value)?.ToString("N") } };
            if (value is EntityFact) return Fact(value);
            if (value is Kingmaker.RuleSystem.DiceFormula) return value.ToString();
            return null;
        }
        internal static Dictionary<string, object> Fields(object value, bool contextValues = false)
        {
            if (value == null) return null;
            var result = new Dictionary<string, object>(); var omitted = new List<string>();
            var layout = Layout(value.GetType());
            result["fieldsTruncated"] = layout.Length > 64;
            foreach (var field in layout.Take(64))
            {
                var type = field.DeclaringType;
                var item = field.GetValue(value); var copied = Scalar(item);
                if (item == null || copied != null) result[type.Name + "." + field.Name] = copied;
                else if (item is Array array && (array.GetType().GetElementType().IsEnum
                    || array.GetType().GetElementType().IsPrimitive || array.GetType().GetElementType() == typeof(string)
                    || typeof(BlueprintReferenceBase).IsAssignableFrom(array.GetType().GetElementType())))
                    result[type.Name + "." + field.Name] = new Dictionary<string, object> {
                        { "values", array.Cast<object>().Take(64).Select(Scalar).ToArray() },
                        { "count", array.Length }, { "truncated", array.Length > 64 }
                    };
                else if (contextValues && item is Kingmaker.UnitLogic.Mechanics.ContextValue) result[type.Name + "." + field.Name] = Fields(item);
                else omitted.Add(type.Name + "." + field.Name);
            }
            result["omittedNonScalarFields"] = omitted.ToArray(); return result;
        }
        static object StatModifiers(object raw)
        {
            if (!(raw is IEnumerable entries)) return null;
            var rows = new List<object>(); bool truncated = false;
            foreach (var item in entries)
            {
                if (rows.Count == 64) { truncated = true; break; }
                rows.Add(new Dictionary<string, object> {
                    { "value", Read(item, "ModValue") }, { "descriptor", Read(item, "ModDescriptor")?.ToString() },
                    { "stackMode", Read(item, "StackMode")?.ToString() }, { "source", Fact(Read(item, "Source")) },
                    { "sourceComponent", Read(item, "SourceComponent") }, { "itemSourceId", Read(item, "ItemSource") == null ? null : BetaIdentity.Id(Read(item, "ItemSource")) }
                });
            }
            return new Dictionary<string, object> { { "entries", rows }, { "truncated", truncated } };
        }
        internal static object RuleBonuses(object raw)
        {
            if (!(raw is ModifiableBonus bonus)) return null;
            return new Dictionary<string, object> {
                { "total", bonus.TotalValue }, { "truncated", bonus.Modifiers.Take(65).Count() > 64 },
                { "entries", bonus.Modifiers.Take(64).Select(modifier => new Dictionary<string, object> {
                    { "value", modifier.Value }, { "descriptor", modifier.Descriptor.ToString() }, { "source", Fact(modifier.Fact) }
                }).ToArray() }
            };
        }
        static object Rule(object rule) => rule == null ? null : new Dictionary<string, object> {
            { "eventId", BetaIdentity.Id(rule) }, { "result", Auto(rule, "Result") },
            { "ruleBonuses", RuleBonuses(Read(rule, "m_ModifiableBonus")) },
            { "statModifiersAtTheMoment", StatModifiers(Auto(rule, "StatModifiersAtTheMoment")) },
            { "temporaryModifiersRetainedByRule", StatModifiers(Read(rule, "m_TemporaryModifiers")) },
            { "scalarFields", Fields(rule) }
        };
        internal static object RuleDetails(object rule)
        {
            try { return Rule(rule); }
            catch (Exception error) { return new Dictionary<string, object> { { "captureFailure", error.GetType().Name } }; }
        }
        internal static Dictionary<string, object> Capture(RuleAttackRoll evt)
        {
            try
            {
                using (PreviewPerformance.Measure("raw-attack-evidence"))
                {
                    var ab = Auto(evt, "AttackBonusRule"); var ac = Auto(evt, "ACRule");
                    var stats = evt.WeaponStats;
                    return new Dictionary<string, object> {
                        { "version", 1 }, { "captureStage", "completed-real-RuleAttackRoll" },
                        { "finalAB", evt.AttackBonus }, { "finalAC", evt.TargetAC },
                        { "attackOutcomeFields", Fields(evt) },
                        { "d20", D20Observation.Capture(evt.D20) },
                        { "criticalConfirmationD20", D20Observation.Capture(Auto(evt, "CriticalConfirmationD20") as RuleRollDice) },
                        { "attackType", evt.AttackType.ToString() }, { "autoHit", evt.AutoHit }, { "autoMiss", evt.AutoMiss },
                        { "attackPenalty", evt.AttackBonusPenalty }, { "attackRule", Rule(ab) },
                        { "baseAttackRule", Rule(Read(ab, "m_InnerRule")) }, { "armorClassRule", Rule(ac) },
                        { "targetACBaseValueNow", Read(evt.Target?.Stats.AC, "m_BaseValue") },
                        { "targetACCachedNow", Auto(evt.Target?.Stats.AC, "ModifiedValue") },
                        { "weaponStatsRule", Rule(stats) }, { "weaponDamageModifiers", RuleBonuses(stats?.DamageModifiers) },
                        { "weaponDiceFields", Fields(stats == null ? null : Auto(stats, "WeaponDamageDice")) },
                        { "weaponDiceModifiers", DiceModifiers(stats == null ? null : Auto(stats, "WeaponDamageDice")) },
                        { "weaponDamageDescriptions", Descriptions(stats?.DamageDescription) },
                        { "missing", new[] { "Unknown handler internals and non-scalar fields are not reconstructed; see omitted/truncated fields", "*Now stat fields are post-event values, not substituted for engine-captured AtTheMoment modifiers" } }
                    };
                }
            }
            catch (Exception error) { return new Dictionary<string, object> { { "captureFailure", error.GetType().Name }, { "complete", false } }; }
        }
        internal static object DiceModifiers(object dice)
        {
            if (!(Read(dice, "m_Modifications") is IEnumerable entries)) return null;
            var values = entries.Cast<object>().Take(65).ToArray();
            return new Dictionary<string, object> { { "entries", values.Take(64).Select(value => Fields(value)).ToArray() }, { "truncated", values.Length > 64 } };
        }
        static object Descriptions(IEnumerable descriptions)
        {
            if (descriptions == null) return null;
            var values = descriptions.Cast<object>().Take(33).ToArray();
            return new Dictionary<string, object> { { "entries", values.Take(32).Select(value => Fields(value, true)).ToArray() }, { "truncated", values.Length > 32 } };
        }
        internal static Dictionary<string, object> Defense(UnitEntityData target)
        {
            try
            {
                using (PreviewPerformance.Measure("raw-defense-evidence"))
                {
                    var part = target?.Get<UnitPartDamageReduction>();
                    var chunks = Read(part, "m_Chunks") as IList;
                    var rows = new List<object>();
                    for (int i = 0; chunks != null && i < Math.Min(chunks.Count, 64); i++)
                    {
                        var chunk = chunks[i]; var runtime = Auto(chunk, "DR"); var fact = Auto(runtime, "Fact") ?? Auto(chunk, "Source");
                        var context = Read(fact, "m_Context");
                        rows.Add(new Dictionary<string, object> {
                            { "registrationIndex", i }, { "source", Fact(fact) }, { "chunkFields", Fields(chunk) },
                            { "remainingPoolStored", Read(runtime, "m_RemainPool") },
                            { "componentType", CombatFields.Component(runtime)?.GetType().FullName },
                            { "componentDefinition", Fields(CombatFields.Component(runtime), true) },
                            { "contextRanks", (Read(context, "m_Ranks") as int[])?.Take(32).ToArray() },
                            { "contextSharedValues", (Read(context, "m_SharedValues") as int[])?.Take(32).ToArray() }
                        });
                    }
                    return new Dictionary<string, object> {
                        { "partPresent", part != null }, { "registeredChunks", rows }, { "chunkCount", chunks?.Count },
                        { "penaltyEntries", Descriptions(Read(part, "PenaltyEntries") as IEnumerable) },
                        { "chunksTruncated", chunks != null && chunks.Count > 64 },
                        { "scope", "Raw registered chunks/configuration and pool state; selected actual reduction is in completed damage components, not inferred from these candidates" }
                    };
                }
            }
            catch (Exception error) { return new Dictionary<string, object> { { "captureFailure", error.GetType().Name }, { "complete", false } }; }
        }
    }
}
