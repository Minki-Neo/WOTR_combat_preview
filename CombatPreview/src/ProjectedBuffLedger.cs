using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    // Detached identity/order data. The game removes the first matching buff,
    // rather than all stacks or all copies from different casters.
    internal sealed class ProjectedBuffEntry
    {
        internal string Token, Blueprint, Caster;
        internal int Rank = 1, OriginalRank = 1;
        internal ulong? Descriptor;
        internal bool Removed, Live;
        internal bool Active = true;
        internal ProjectedBuffEntry Copy() => (ProjectedBuffEntry)MemberwiseClone();
    }
    internal sealed class ProjectedBuffLedger
    {
        readonly List<ProjectedBuffEntry> entries = new List<ProjectedBuffEntry>();
        readonly HashSet<string> removedTokens = new HashSet<string>();
        int next;
        internal IEnumerable<ProjectedBuffEntry> Active => entries.Where(entry => !entry.Removed);
        internal IEnumerable<ProjectedBuffEntry> Changes => entries.Where(entry => entry.Removed || !entry.Live || entry.Rank != entry.OriginalRank);
        internal bool HasRemovals => removedTokens.Count > 0;
        internal bool HasRankChanges => Active.Any(entry => entry.Rank != entry.OriginalRank);
        internal ProjectedBuffEntry Find(string token) => entries.FirstOrDefault(entry => entry.Token == token);
        internal bool Removed(string token) => token != null && removedTokens.Contains(token);
        internal void Seed(string token, string blueprint, string caster, int rank, ulong? descriptor = null, bool active = true)
            => entries.Add(new ProjectedBuffEntry { Token = token, Blueprint = blueprint, Caster = caster, Rank = rank, OriginalRank = rank, Live = true, Descriptor = descriptor, Active = active });
        internal ProjectedBuffEntry Add(string blueprint, string caster, ulong? descriptor = null)
        {
            var entry = new ProjectedBuffEntry { Token = "projected:" + next++, Blueprint = blueprint, Caster = caster, Descriptor = descriptor };
            entries.Add(entry); return entry;
        }
        internal ProjectedBuffEntry RemoveFirst(string blueprint, string caster = null, bool rankOnly = false)
        {
            var entry = Active.FirstOrDefault(candidate => candidate.Blueprint == blueprint && (caster == null || candidate.Caster == caster));
            if (entry == null) return null;
            if (rankOnly && entry.Rank > 1) entry.Rank--;
            else RemoveToken(entry.Token);
            return entry;
        }
        internal ProjectedBuffEntry RemoveToken(string token)
        {
            var entry = Active.FirstOrDefault(candidate => candidate.Token == token);
            if (entry == null) return null;
            entry.Removed = true; removedTokens.Add(entry.Token); return entry;
        }
        internal ProjectedBuffEntry[] DescriptorSnapshot(ulong descriptor, out bool unknown)
        {
            var snapshot = Active.ToArray();
            unknown = descriptor != 0 && snapshot.Any(entry => !entry.Descriptor.HasValue);
            return snapshot.Where(entry => entry.Descriptor.HasValue && (entry.Descriptor.Value & descriptor) != 0).ToArray();
        }
        internal ProjectedBuffEntry AddRank(string blueprint, int amount, int maximum)
        {
            var entry = Active.FirstOrDefault(candidate => candidate.Blueprint == blueprint);
            if (entry == null || amount <= 0 || (long)entry.Rank + amount > maximum) return null;
            entry.Rank += amount; return entry;
        }
        internal ProjectedBuffLedger Copy()
        {
            var copy = new ProjectedBuffLedger { next = next };
            copy.entries.AddRange(entries.Select(entry => entry.Copy()));
            foreach (var token in removedTokens) copy.removedTokens.Add(token);
            return copy;
        }
    }
}
