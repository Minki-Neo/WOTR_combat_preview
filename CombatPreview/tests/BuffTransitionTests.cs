using System;
using System.Linq;
using CombatPreview;
static class BuffTransitionTests
{
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    public static void Run()
    {
        var ledger = new ProjectedBuffLedger();
        ledger.Seed("a", "fear", "caster1", 1);
        ledger.Seed("b", "fear", "caster2", 2);
        ledger.Seed("c", "fear", "caster1", 1);
        var otherBranch = ledger.Copy();
        Check(ledger.RemoveFirst("fear", "caster2", true).Token == "b", "OnlyFromCaster selects the matching caster");
        Check(ledger.Active.First(entry => entry.Token == "b").Rank == 1, "rank removal keeps a multi-rank buff present");
        Check(ledger.HasRankChanges && ledger.Find("b").OriginalRank == 2, "rank refresh retains the live original rank");
        Check(!ledger.HasRemovals, "a rank decrement does not remove its identity");
        Check(ledger.RemoveFirst("fear").Token == "a", "plain removal selects only the first matching instance");
        Check(ledger.Removed("a") && ledger.Active.Count() == 2, "later stacks remain active");
        Check(otherBranch.Active.Count() == 3 && otherBranch.Active.First(entry => entry.Token == "b").Rank == 2,
            "branches do not share removal/rank mutations");
        Check(ledger.RemoveFirst("missing") == null && ledger.RemoveFirst("fear", "caster3") == null, "absent matches leave state unchanged");
        Check(ledger.RemoveFirst("fear", "caster2", true).Removed, "last rank removal removes the buff");
        var pending = ledger.Add("fear", "caster1");
        Check(ledger.RemoveFirst("fear").Token == "c", "pre-existing matching stacks precede a projected application");
        Check(ledger.RemoveFirst("fear").Token == pending.Token && !ledger.Active.Any(), "a later removal can remove a projected buff");
        var copyAfterRemoval = ledger.Copy();
        Check(copyAfterRemoval.Removed("a") && copyAfterRemoval.HasRemovals, "copied branches retain tombstones");
        Check(copyAfterRemoval.Add("courage", "caster2").Token != pending.Token, "copy preserves unique projected-source sequence");

        for (ulong incoming = 0; incoming < 16; incoming++) for (ulong required = 0; required < 16; required++)
        {
            bool overlap = (incoming & required) != 0;
            Check(BuffTransitionPolicy.Matches(true, incoming, required, false, false, true) == overlap,
                "descriptor substitutions test bit overlap, not equality or buff identity");
            Check(!BuffTransitionPolicy.Matches(true, incoming, required, true, true, true), "condition immunity blocks substitution when requested");
            Check(BuffTransitionPolicy.Matches(false, incoming, required, true, false, true), "buff-identity substitutions ignore descriptor overlap");
        }
        bool?[] options = { false, true, null };
        foreach (var ignore in options) foreach (var fact in options)
        {
            bool? expected = ignore == true || fact == false ? false : ignore == false && fact == true ? true : (bool?)null;
            Check(BuffTransitionPolicy.Blocks(ignore, fact) == expected, "unknown projected facts stay unknown unless an exception decides immunity");
        }

        var primary = new ProjectedModifier { Value = 4, Descriptor = 22, Source = "primary" };
        var weaker = new ProjectedModifier { Value = 2, Descriptor = 22, Source = "weaker" };
        Check(ProjectedStatMath.Project(23, new[] { primary, weaker }, new ProjectedModifier[0], source => source == "primary") == 21,
            "removing the strongest typed bonus reveals the weaker existing source");
        Check(ProjectedStatMath.Project(23, new[] { primary, weaker }, new ProjectedModifier[0], source => source == "weaker") == 23,
            "removing a suppressed typed bonus leaves the stat unchanged");
        var penalty = new ProjectedModifier { Value = -4, Descriptor = 25, Stacks = true, Source = "pending" };
        Check(ProjectedStatMath.Project(23, new[] { primary }, new[] { penalty }, source => source == "pending") == 23,
            "applying then removing a projected debuff leaves no penalty");
        Check(ProjectedStatMath.Project(23, new[] { penalty }, new ProjectedModifier[0], source => source == "pending") == 27,
            "removing a live debuff restores its cached contribution");
        var negativeInsight = new ProjectedModifier { Value = -4, Descriptor = 13, Source = "insight" };
        Check(ProjectedStatMath.ProjectFiltered(19, new[] { negativeInsight }, new[] { penalty }, modifier => modifier.Descriptor != 13, null) == 15,
            "filtered cache remains capped at unfiltered value after adding a penalty");
        Check(ProjectedStatMath.ProjectFiltered(19, new[] { negativeInsight }, new ProjectedModifier[0], modifier => modifier.Descriptor != 13,
            source => source == "insight") == 23, "removing a filtered-out negative bonus restores both caches");
        var rankSource = new ProjectedModifier { Value = 8, Descriptor = 22, Source = "ranked" };
        var rival = new ProjectedModifier { Value = 6, Descriptor = 22, Source = "rival" };
        var independentBranch = new ProjectedModifier { Value = 8, Descriptor = 22, Source = "ranked" };
        var changed = ProjectedStatMath.ProjectFiltered(28, new[] { rankSource, rival }, new ProjectedModifier[0], modifier => true, null,
            modifier => modifier.Source == "ranked" ? new ProjectedModifier { Value = 4, Descriptor = 22, Source = modifier.Source } : modifier);
        Check(changed == 26 && rankSource.Value == 8 && independentBranch.Value == 8,
            "decrementing the dominant rank reveals a competing bonus and preserves snapshots");
        changed = ProjectedStatMath.ProjectFiltered(14, new[] { new ProjectedModifier { Value = -6, Descriptor = 25, Stacks = true, Source = "ranked" } },
            new ProjectedModifier[0], modifier => true, null,
            modifier => new ProjectedModifier { Value = -4, Descriptor = 25, Stacks = true, Source = modifier.Source });
        Check(changed == 16, "a rank decrement reduces a live stackable penalty in the baseline");
        var descriptors = new ProjectedBuffLedger();
        descriptors.Seed("live", "same-buff", "a", 1, 1);
        var projectedBuff = descriptors.Add("same-buff", "b", BuffStatMath.Descriptor(4, 8));
        var untouchedBranch = descriptors.Copy();
        var removals = descriptors.DescriptorSnapshot(4, out bool unknown);
        Check(!unknown && removals.Length == 1 && removals[0].Token == projectedBuff.Token,
            "new buff inherits the parent descriptor as well as its own descriptor");
        foreach (var removal in removals) descriptors.RemoveFirst(removal.Blueprint);
        Check(descriptors.Removed("live") && descriptors.Active.Single().Token == projectedBuff.Token,
            "descriptor selection uses its snapshot but game GetBuff removes the first current blueprint copy");
        Check(untouchedBranch.Active.Count() == 2 && !untouchedBranch.HasRemovals, "descriptor removal does not mutate sibling branches");
        removals = descriptors.DescriptorSnapshot(8, out unknown);
        foreach (var removal in removals) descriptors.RemoveFirst(removal.Blueprint);
        Check(!descriptors.Active.Any(), "a projected buff can later be removed by its own descriptor");
        descriptors.Add("unknown", "a");
        Check(descriptors.DescriptorSnapshot(1, out unknown).Length == 0 && unknown,
            "missing context descriptors are unknown rather than assumed absent");
        Check(descriptors.DescriptorSnapshot(0, out unknown).Length == 0 && !unknown, "empty removal mask cannot remove any buff");
        for (int value = -6; value <= 6; value++)
        for (int multiplier = -2; multiplier <= 3; multiplier++)
        foreach (int power in new[] { 0, 2, 4 })
        foreach (int? minimum in new int?[] { null, 0, 3 })
        {
            int expected = value * multiplier;
            if (expected >= 0) expected += power;
            if (minimum.HasValue && expected < minimum) expected = minimum.Value;
            Check(BuffStatMath.ContextBonus(value, multiplier, power, minimum) == expected,
                "context buff multiplier, Powerful Change sign gate and minimum match audited operation order");
        }
        Check(BuffStatMath.ContextBonus(-2, 1, null, null) == -2, "unknown Powerful Change cannot change a negative penalty");
        Check(BuffStatMath.ContextBonus(0, 1, null, null) == null, "zero bonus can receive Powerful Change and must preserve uncertainty");
        Check(BuffStatMath.ContextBonus(null, 0, 0, null) == null, "unknown context evaluation is not manufactured even with zero multiplier");
        Check(BuffStatMath.ContextBonus(int.MaxValue, 2, 0, null) == null, "overflow does not wrap into a penalty");
        var contextual = new ProjectedModifier { Descriptor = 22, Value = BuffStatMath.ContextBonus(3, 2, 2, 0).Value,
            Source = "projected-context" };
        Check(ProjectedStatMath.Project(20, new ProjectedModifier[0], new[] { contextual }) == 28, "context buff contributes to later saves");
        Check(ProjectedStatMath.Project(20, new ProjectedModifier[0], new[] { contextual }, source => source == "projected-context") == 20,
            "applying then removing a context buff cancels its entire contribution");
        var ranked = new ProjectedBuffLedger();
        var first = ranked.Add("ranked-buff", "caster", 4);
        Check(ranked.AddRank("ranked-buff", 1, 3) == first && first.Rank == 2 && first.OriginalRank == 1,
            "rank stacking updates the first source without replacing its original context");
        var branchBeforeCap = ranked.Copy();
        Check(ranked.AddRank("ranked-buff", 2, 3) == null && first.Rank == 2, "an increment over the cap is rejected, not partly clamped");
        Check(ranked.AddRank("ranked-buff", 1, 3) != null && first.Rank == 3, "rank reaches the configured cap");
        Check(ranked.AddRank("ranked-buff", 1, 3) == null && branchBeforeCap.Active.Single().Rank == 2,
            "cap rejection and sibling state are independent");
        ranked.RemoveFirst("ranked-buff", rankOnly: true);
        var remaining = ranked.Active.Single();
        var rankedModifier = new ProjectedModifier { Value = -2, Descriptor = 25, Stacks = true, Source = remaining.Token };
        var adjustedModifier = new ProjectedModifier { Value = BuffStatMath.RankValue(rankedModifier.Value, -2, remaining.OriginalRank, remaining.Rank).Value,
            Descriptor = 25, Stacks = true, Source = remaining.Token };
        Check(ProjectedStatMath.Project(20, new ProjectedModifier[0], new[] { adjustedModifier }) == 16,
            "virtual rank apply/apply/apply/remove contributes two ranks to the next save");
        Check(BuffStatMath.RankValue(8, 2, 3, 2) == 6, "rank change preserves an existing additive Powerful Change constant");
        Check(BuffStatMath.RankValue(int.MaxValue, 1, 1, 2) == null && BuffStatMath.RankValue(3, 1, 0, 1) == null,
            "invalid and overflowing rank changes remain unavailable");
        ranked.RemoveFirst("ranked-buff");
        var replacement = ranked.Add("ranked-buff", "new-caster", 8);
        Check(ranked.Removed(first.Token) && replacement.Caster == "new-caster" && replacement.Descriptor == 8 && replacement.Rank == 1,
            "replacement creates a fresh source, rank and descriptor instead of keeping the old buff");
        Check(ProjectedStatMath.Project(20, new ProjectedModifier[0], new[] { adjustedModifier,
            new ProjectedModifier { Value = 4, Descriptor = 25, Stacks = true, Source = replacement.Token } }, ranked.Removed) == 24,
            "replacement drops the old pending modifier and uses the new context value");
        Console.WriteLine("PASS: " + assertions + " buff transition/removal/identity assertions");
    }
}
