using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    public enum RulesImplementation { Unknown, Vanilla, TabletopTweaks }
    public enum SnapshotOrigin { Unavailable, Fixture, Target, Manual }
    public enum PredictionQuality { SupportedModel, PartialEstimate, Unavailable }
    public enum BypassOperator { Never, Trait, All, Any }

    // Input observations contain value data only, never Unity/fact/runtime objects.
    // Traits use explicit names (e.g. material:adamantine, enhancement:3,
    // alignment:good, form:slashing); the live reader must certify their semantics.
    public sealed class BypassCondition
    {
        public BypassOperator Operator { get; private set; }
        public string Trait { get; private set; }
        public IReadOnlyList<BypassCondition> Children { get; private set; }
        public BypassCondition(BypassOperator op, string trait = null, params BypassCondition[] children)
        {
            if (!Enum.IsDefined(typeof(BypassOperator), op)) throw new ArgumentOutOfRangeException("op");
            var copy = (children ?? new BypassCondition[0]).ToArray();
            if (copy.Any(c => c == null) || (op == BypassOperator.Trait && string.IsNullOrWhiteSpace(trait))
                || ((op == BypassOperator.All || op == BypassOperator.Any) && copy.Length == 0)
                || ((op == BypassOperator.Never || op == BypassOperator.Trait) && copy.Length != 0))
                throw new ArgumentException("Incomplete bypass expression");
            Operator = op; Trait = trait; Children = Array.AsReadOnly(copy);
            int enhancement;
            if (op == BypassOperator.Trait && trait.StartsWith("enhancement:", StringComparison.Ordinal)
                && (!int.TryParse(trait.Substring(12), out enhancement) || enhancement < 0 || enhancement > 100))
                throw new ArgumentException("Invalid enhancement bypass threshold");
        }
        public bool Matches(ISet<string> traits)
        {
            if (traits == null) throw new ArgumentNullException("traits");
            if (Operator == BypassOperator.Trait)
            {
                if (!Trait.StartsWith("enhancement:", StringComparison.Ordinal)) return traits.Contains(Trait);
                int threshold = int.Parse(Trait.Substring(12), System.Globalization.CultureInfo.InvariantCulture);
                return traits.Any(t => {
                    int amount;
                    return t.StartsWith("enhancement:", StringComparison.Ordinal)
                        && int.TryParse(t.Substring(12), out amount) && amount >= threshold;
                });
            }
            if (Operator == BypassOperator.All) return Children.All(c => c.Matches(traits));
            if (Operator == BypassOperator.Any) return Children.Any(c => c.Matches(traits));
            return false;
        }
    }

    public sealed class ReductionObservation
    {
        public DamageKind Kind { get; private set; }
        public int Amount { get; private set; }
        // Maximum within a group, sum across groups. Only an audited reader may
        // supply distinct groups for explicitly stacking DR; never infer stacking.
        public string StackGroup { get; private set; }
        public BypassCondition Bypass { get; private set; }
        public ReductionObservation(DamageKind kind, int amount, string stackGroup, BypassCondition bypass)
        {
            if (!Enum.IsDefined(typeof(DamageKind), kind) || kind == DamageKind.Unknown || amount < 0 || amount > 1000000
                || string.IsNullOrWhiteSpace(stackGroup) || bypass == null) throw new ArgumentException("Invalid reduction");
            Kind = kind; Amount = amount; StackGroup = stackGroup; Bypass = bypass;
        }
    }

    public sealed class DamageComponentSnapshot
    {
        public DamageKind Kind { get; private set; }
        public int Rolls { get; private set; }
        public int Sides { get; private set; }
        public int Bonus { get; private set; }
        public bool Multiplies { get; private set; }
        public bool BypassKnown { get; private set; }
        public IReadOnlyList<string> BypassTraits { get; private set; }
        public DamageComponentSnapshot(DamageTerm term, bool bypassKnown, IEnumerable<string> traits)
        {
            if (term == null) throw new ArgumentNullException("term");
            Kind = term.Kind; Rolls = term.Rolls; Sides = term.Sides; Bonus = term.Bonus; Multiplies = term.Multiplies;
            BypassKnown = bypassKnown;
            var copy = (traits ?? new string[0]).ToArray();
            if (copy.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Empty bypass trait");
            BypassTraits = Array.AsReadOnly(copy);
        }
        public DamageTerm ToTerm() { return new DamageTerm(Rolls, Sides, Bonus, Multiplies) { Kind = Kind }; }
    }

    // Mutable collection input is copied into the snapshot. Complete flags are
    // intentionally false by default; absence of data never certifies zero defense.
    public sealed class DefenseObservation
    {
        public RulesImplementation Rules;
        public SnapshotOrigin Origin = SnapshotOrigin.Fixture;
        public string GameBuild, RuleEvidence, TargetId, Scope;
        public bool Complete, GroupingVerified, TraitsComplete;
        public bool HasConsumablePool, HasSuppression, HasUnknownModifiers;
        public readonly List<string> Missing = new List<string>();
        public readonly List<string> ObservedDetails = new List<string>();
        public readonly List<ReductionObservation> Reductions = new List<ReductionObservation>();
        public readonly HashSet<DamageKind> Immunities = new HashSet<DamageKind>();
    }

    public sealed class DefenseSnapshot
    {
        readonly Dictionary<DamageKind, int> reductions;
        readonly HashSet<DamageKind> immunities;
        public string Source { get; private set; }
        public string GameBuild { get; private set; }
        public string TargetId { get; private set; }
        public string Scope { get; private set; }
        public RulesImplementation Rules { get; private set; }
        public SnapshotOrigin Origin { get; private set; }
        public PredictionQuality Quality { get; private set; }
        public IReadOnlyList<string> Missing { get; private set; }
        public IReadOnlyList<string> ObservedDetails { get; private set; }
        internal DefenseSnapshot(DefenseObservation o, string source, IEnumerable<string> missing,
            Dictionary<DamageKind, int> values = null)
        {
            Source = source + " | build=" + o.GameBuild + " | target=" + o.TargetId + " | scope=" + o.Scope;
            Rules = o.Rules; Origin = o.Origin; GameBuild = o.GameBuild;
            TargetId = o.TargetId; Scope = o.Scope;
            Missing = Array.AsReadOnly(missing.Distinct().ToArray());
            ObservedDetails = Array.AsReadOnly(o.ObservedDetails.ToArray());
            Quality = Missing.Count == 0 ? PredictionQuality.SupportedModel : PredictionQuality.Unavailable;
            reductions = values == null ? new Dictionary<DamageKind, int>() : new Dictionary<DamageKind, int>(values);
            immunities = new HashSet<DamageKind>(o.Immunities);
        }
        public DefenseProfile ToProfile()
        {
            var p = new DefenseProfile { Complete = Quality == PredictionQuality.SupportedModel,
                Source = Source, UnavailableReason = string.Join("; ", Missing.ToArray()) };
            foreach (var item in reductions) p.Reduction.Add(item.Key, item.Value);
            foreach (var kind in immunities) p.Immunities.Add(kind);
            return p;
        }
    }

    // This is the pure projection contract, not a live game reader. Version-
    // specific readers must provide audited observations and evidence first.
    public interface IDefenseSnapshotProvider
    {
        DefenseSnapshot Read(DefenseObservation observation, IReadOnlyList<DamageComponentSnapshot> components);
    }
    public sealed class VanillaDefenseSnapshotProvider : IDefenseSnapshotProvider
    {
        public DefenseSnapshot Read(DefenseObservation o, IReadOnlyList<DamageComponentSnapshot> c)
        { return DefenseProjection.Read(o, c, RulesImplementation.Vanilla); }
    }
    public sealed class TabletopTweaksDefenseSnapshotProvider : IDefenseSnapshotProvider
    {
        public DefenseSnapshot Read(DefenseObservation o, IReadOnlyList<DamageComponentSnapshot> c)
        { return DefenseProjection.Read(o, c, RulesImplementation.TabletopTweaks); }
    }
    static class DefenseProjection
    {
        internal static DefenseSnapshot Read(DefenseObservation o, IReadOnlyList<DamageComponentSnapshot> c, RulesImplementation expected)
        {
            if (o == null) throw new ArgumentNullException("observation");
            var missing = new List<string>(o.Missing);
            if (o.Rules != expected || string.IsNullOrWhiteSpace(o.RuleEvidence)) missing.Add("Rule implementation/activation is not established");
            if (string.IsNullOrWhiteSpace(o.GameBuild) || string.IsNullOrWhiteSpace(o.TargetId) || string.IsNullOrWhiteSpace(o.Scope))
                missing.Add("Missing build, target or application scope");
            if (o.Origin != SnapshotOrigin.Fixture && o.Origin != SnapshotOrigin.Target) missing.Add("Invalid automatic snapshot origin");
            if (!o.Complete || !o.TraitsComplete) missing.Add("Defense or bypass observation is incomplete");
            if (!o.GroupingVerified) missing.Add("Damage grouping/stacking/minimum rules are unverified");
            if (o.HasConsumablePool) missing.Add("Consumable protection requires sequential simulation");
            if (o.HasSuppression) missing.Add("Immunity/resistance suppression is unsupported");
            if (o.HasUnknownModifiers) missing.Add("Vulnerability, difficulty or additional modifiers are unresolved");
            if (c == null || c.Count == 0 || c.Any(x => x == null || x.Kind == DamageKind.Unknown
                || !Enum.IsDefined(typeof(DamageKind), x.Kind) || !x.BypassKnown)) missing.Add("Unknown damage type or bypass traits");
            if (c != null && (c.Count > 64 || c.Any(x => x != null && (x.Rolls < 0 || x.Rolls > 100 || x.Sides < 1 || x.Sides > 100))))
                missing.Add("Invalid damage component or model budget exceeded");
            if (o.Reductions.Any(x => x == null) || o.Immunities.Any(x => x == DamageKind.Unknown || !Enum.IsDefined(typeof(DamageKind), x)))
                missing.Add("Invalid defense type");
            var values = new Dictionary<DamageKind, int>();
            if (missing.Count == 0)
            {
                foreach (var group in c.GroupBy(x => x.Kind))
                {
                    var firstTraits = new HashSet<string>(group.First().BypassTraits, StringComparer.Ordinal);
                    if (group.Any(x => !firstTraits.SetEquals(x.BypassTraits)))
                    { missing.Add("Mixed bypass profiles within one damage type are unsupported"); continue; }
                    var effective = o.Reductions.Where(x => x.Kind == group.Key && !x.Bypass.Matches(firstTraits));
                    long amount = effective.GroupBy(x => x.StackGroup).Sum(g => (long)g.Max(x => x.Amount));
                    if (amount > 1000000) { missing.Add("Reduction exceeds model budget"); continue; }
                    values[group.Key] = (int)amount;
                }
            }
            return new DefenseSnapshot(o, expected + " | " + o.Origin + " | " + o.RuleEvidence, missing, values);
        }
    }
}
