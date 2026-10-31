using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CombatPreview
{
    // The condition revision must cover buffs/equipment/actions/defenses and rule
    // configuration. A turn ID alone is not a proof of equal conditions.
    public sealed class DiagnosticContext
    {
        public string Attacker { get; private set; }
        public string Target { get; private set; }
        public string Weapon { get; private set; }
        public string Turn { get; private set; }
        public string Scene { get; private set; }
        public int AttackIndex { get; private set; }
        public string ConditionRevision { get; private set; }
        public bool Complete { get; private set; }
        internal string Slot { get { return string.Join("", new[] { Attacker, Target, Weapon, Turn, Scene, AttackIndex.ToString(CultureInfo.InvariantCulture) }
            .Select(v => v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v).ToArray()); } }
        public DiagnosticContext(string attacker, string target, string weapon, string turn, string scene,
            int attackIndex, string conditionRevision, bool complete)
        {
            Attacker = attacker ?? ""; Target = target ?? ""; Weapon = weapon ?? ""; Turn = turn ?? ""; Scene = scene ?? "";
            AttackIndex = attackIndex; ConditionRevision = conditionRevision ?? "";
            Complete = complete && attackIndex >= 0 && new[] { Attacker, Target, Weapon, Turn, Scene, ConditionRevision }.All(s => s.Length > 0);
        }
        public bool SameConditions(DiagnosticContext other)
        { return other != null && Complete && other.Complete && Slot == other.Slot && ConditionRevision == other.ConditionRevision; }
    }
    public sealed class DiagnosticPrediction
    {
        public DiagnosticContext Context { get; private set; }
        public int AttackBonus { get; private set; }
        public int TargetAC { get; private set; }
        public int Penalty { get; private set; }
        public double Hit { get; private set; }
        public double Crit { get; private set; }
        public int Min { get; private set; }
        public int Max { get; private set; }
        public string DamageSignature { get; private set; }
        public string DefenseSource { get; private set; }
        public string ReductionsSignature { get; private set; }
        public SnapshotOrigin Origin { get; private set; }
        public DiagnosticPrediction(DiagnosticContext context, int ab, int ac, int penalty, double hit, double crit,
            int min, int max, string damageSignature, string defenseSource, string reductionsSignature, SnapshotOrigin origin)
        {
            if (context == null) throw new ArgumentNullException("context");
            DamageMath.Expected(hit, crit, 0, 0);
            if (min < 0 || max < min) throw new ArgumentOutOfRangeException("range");
            Context = context; AttackBonus = ab; TargetAC = ac; Penalty = penalty; Hit = hit; Crit = crit;
            Min = min; Max = max; DamageSignature = damageSignature; DefenseSource = defenseSource;
            ReductionsSignature = reductionsSignature; Origin = origin;
        }
    }
    public sealed class ActualAttackObservation
    {
        public DiagnosticContext Context { get; private set; }
        public int AttackBonus { get; private set; }
        public int TargetAC { get; private set; }
        public bool Rolled { get; private set; }
        public bool Hit { get; private set; }
        public double? Damage { get; private set; }
        public string DamageSignature { get; private set; }
        public string ReductionsSignature { get; private set; }
        public int? Penalty { get; private set; }
        public bool? FullAttack { get; private set; }
        public bool? CriticalConfirmed { get; private set; }
        public bool IsFake { get; private set; }
        public bool HasWeaponAttack { get; private set; }
        public ActualAttackObservation(DiagnosticContext context, int ab, int ac, bool rolled, bool hit,
            double? damage = null, string damageSignature = null, string reductionsSignature = null,
            int? penalty = null, bool? fullAttack = null, bool? criticalConfirmed = null,
            bool isFake = false, bool hasWeaponAttack = false)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (damage.HasValue && (double.IsNaN(damage.Value) || double.IsInfinity(damage.Value) || damage < 0))
                throw new ArgumentOutOfRangeException("damage");
            Context = context; AttackBonus = ab; TargetAC = ac; Rolled = rolled; Hit = hit;
            Damage = damage; DamageSignature = damageSignature; ReductionsSignature = reductionsSignature;
            Penalty = penalty; FullAttack = fullAttack; CriticalConfirmed = criticalConfirmed;
            IsFake = isFake; HasWeaponAttack = hasWeaponAttack;
        }
    }
    public enum ComparisonStatus { NoPrediction, ConditionsChanged, SnapshotExpired, ManualScenario, UnverifiableConditions, Compared, UnassociatedRoll }
    public sealed class DiagnosticComparison
    {
        public ComparisonStatus Status { get; internal set; }
        public bool? AttackBonusMatches { get; internal set; }
        public bool? ArmorClassMatches { get; internal set; }
        public bool? DamageComponentsMatch { get; internal set; }
        public bool? ReductionsMatch { get; internal set; }
        // No random sample is ever compared with an expected value.
    }
    public sealed class DiagnosticRecorder
    {
        sealed class Pending { public string Id; public DiagnosticPrediction Value; public long Time; }
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly Queue<string> seenOrder = new Queue<string>();
        readonly Action<string> sink;
        readonly Func<long> clock;
        readonly object gate = new object();
        readonly string sessionId;
        string runtimeMetadata = "{}";
        bool enabled, busy;
        long sequence, recordSequence, window = long.MinValue;
        int written;
        public bool Faulted { get; private set; }
        public int Dropped { get; private set; }
        public int IgnoredSimulated { get; private set; }
        public bool Enabled
        {
            get { lock (gate) return enabled; }
            set { lock (gate) { enabled = value && !Faulted; pending.Clear(); seen.Clear(); seenOrder.Clear(); } }
        }
        public DiagnosticRecorder(Action<string> sink, Func<long> clock, string sessionId = null)
        {
            this.sink = sink ?? throw new ArgumentNullException("sink"); this.clock = clock ?? throw new ArgumentNullException("clock");
            // OS-generated ID; does not touch Unity or the game's RNG.
            this.sessionId = sessionId ?? Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(this.sessionId) || this.sessionId.Length > 128) throw new ArgumentException("Invalid session ID");
        }
        public void SetRuntimeMetadata(IDictionary<string, string> values)
        {
            lock (gate)
            {
                runtimeMetadata = "{" + string.Join(",", (values ?? new Dictionary<string, string>()).Take(16)
                    .Select(p => Quote(p.Key) + ":" + Quote(p.Value)).ToArray()) + "}";
            }
        }
        public void IgnoreSimulated()
        { lock (gate) { if (enabled) IgnoredSimulated++; } }
        public void Invalidate()
        { lock (gate) { pending.Clear(); } }
        public string RecordPrediction(DiagnosticPrediction prediction)
        {
            lock (gate)
            {
                if (!enabled || busy || prediction == null) return null;
                long now = clock(); Prune(now);
                if (pending.Count >= 128 && !pending.ContainsKey(prediction.Context.Slot))
                    pending.Remove(pending.OrderBy(p => p.Value.Time).First().Key);
                string id = (++sequence).ToString(CultureInfo.InvariantCulture);
                bool emitted = Emit(now, "prediction", id, prediction.Context,
                    "\"ab\":" + prediction.AttackBonus + ",\"ac\":" + prediction.TargetAC + ",\"penalty\":" + prediction.Penalty
                    + ",\"hit\":" + Number(prediction.Hit) + ",\"crit\":" + Number(prediction.Crit)
                    + ",\"normalHitMin\":" + prediction.Min + ",\"normalHitMax\":" + prediction.Max
                    + ",\"damageComponents\":" + Quote(prediction.DamageSignature) + ",\"defenseSource\":" + Quote(prediction.DefenseSource)
                    + ",\"reductions\":" + Quote(prediction.ReductionsSignature) + ",\"origin\":" + Quote(prediction.Origin.ToString()));
                // Never link an observation to a prediction missing from the log.
                pending.Remove(prediction.Context.Slot);
                if (!emitted) return null;
                pending[prediction.Context.Slot] = new Pending { Id = id, Value = prediction, Time = now };
                return id;
            }
        }
        public DiagnosticComparison Observe(string eventId, ActualAttackObservation actual)
        {
            lock (gate)
            {
                if (enabled && !busy && actual != null && actual.IsFake) { IgnoredSimulated++; return null; }
                if (!enabled || busy || actual == null || string.IsNullOrEmpty(eventId) || !seen.Add(eventId)) return null;
                seenOrder.Enqueue(eventId);
                if (seenOrder.Count > 256) seen.Remove(seenOrder.Dequeue());
                var result = new DiagnosticComparison { Status = ComparisonStatus.NoPrediction };
                long now = clock(); Pending p;
                string id = "";
                if (!actual.HasWeaponAttack) result.Status = ComparisonStatus.UnassociatedRoll;
                else if (pending.TryGetValue(actual.Context.Slot, out p))
                {
                    pending.Remove(actual.Context.Slot); id = p.Id;
                    if (now < p.Time || now - p.Time > 1000) result.Status = ComparisonStatus.SnapshotExpired;
                    else if (p.Value.Origin == SnapshotOrigin.Manual)
                        result.Status = ComparisonStatus.ManualScenario;
                    else if ((p.Value.Origin != SnapshotOrigin.Target && p.Value.Origin != SnapshotOrigin.Fixture)
                        || !p.Value.Context.Complete || !actual.Context.Complete) result.Status = ComparisonStatus.UnverifiableConditions;
                    else if (!p.Value.Context.SameConditions(actual.Context)) result.Status = ComparisonStatus.ConditionsChanged;
                    else
                    {
                        result.Status = ComparisonStatus.Compared;
                        if (actual.Rolled) { result.AttackBonusMatches = actual.AttackBonus == p.Value.AttackBonus;
                            result.ArmorClassMatches = actual.TargetAC == p.Value.TargetAC; }
                        result.DamageComponentsMatch = Match(p.Value.DamageSignature, actual.DamageSignature);
                        result.ReductionsMatch = Match(p.Value.ReductionsSignature, actual.ReductionsSignature);
                    }
                }
                Prune(now);
                Emit(now, actual.HasWeaponAttack ? "observation" : "unassociated-observation", id, actual.Context, "\"eventId\":" + Quote(eventId)
                    + ",\"isFake\":false,\"hasWeaponAttack\":" + Bool(actual.HasWeaponAttack)
                    + ",\"status\":" + Quote(result.Status.ToString()) + ",\"ab\":" + actual.AttackBonus + ",\"ac\":" + actual.TargetAC
                    + ",\"rolled\":" + Bool(actual.Rolled) + ",\"hit\":" + Bool(actual.Hit)
                    + ",\"penalty\":" + (actual.Penalty.HasValue ? actual.Penalty.Value.ToString(CultureInfo.InvariantCulture) : "null")
                    + ",\"fullAttack\":" + Bool(actual.FullAttack) + ",\"criticalConfirmed\":" + Bool(actual.CriticalConfirmed)
                    + ",\"damage\":" + (actual.Damage.HasValue ? Number(actual.Damage.Value) : "null")
                    + ",\"damageComponents\":" + Quote(actual.DamageSignature) + ",\"reductions\":" + Quote(actual.ReductionsSignature)
                    + ",\"abMatches\":" + Bool(result.AttackBonusMatches) + ",\"acMatches\":" + Bool(result.ArmorClassMatches)
                    + ",\"componentsMatch\":" + Bool(result.DamageComponentsMatch) + ",\"reductionsMatch\":" + Bool(result.ReductionsMatch));
                return result;
            }
        }
        public void RecordDamage(string eventId, string rollEventId, DiagnosticContext context,
            int reportedDamage, int rawDamage, int beforeDifficulty, string defenseEvidence,
            IEnumerable<string> defenseMissing = null, IEnumerable<string> defenseObserved = null, bool hasWeaponAttack = false)
        {
            lock (gate)
            {
                if (!enabled || busy || context == null || string.IsNullOrEmpty(eventId) || !seen.Add(eventId)) return;
                seenOrder.Enqueue(eventId);
                if (seenOrder.Count > 256) seen.Remove(seenOrder.Dequeue());
                Emit(clock(), "damage-observation", "", context, "\"eventId\":" + Quote(eventId)
                    + ",\"rollEventId\":" + Quote(rollEventId) + ",\"reportedDamage\":" + reportedDamage
                    + ",\"rawDamage\":" + rawDamage + ",\"beforeDifficulty\":" + beforeDifficulty
                    + ",\"hasWeaponAttack\":" + Bool(hasWeaponAttack)
                    + ",\"defenseEvidence\":" + Quote(defenseEvidence)
                    + ",\"defenseMissing\":" + StringArray(defenseMissing)
                    + ",\"defenseObserved\":" + StringArray(defenseObserved)
                    + ",\"comparison\":\"NotCompared: no verified damage snapshot\"");
            }
        }
        void Prune(long now)
        { foreach (var key in pending.Where(p => now < p.Value.Time || now - p.Value.Time > 1000).Select(p => p.Key).ToArray()) pending.Remove(key); }
        bool Emit(long now, string kind, string id, DiagnosticContext c, string values)
        {
            if (now < window || window == long.MinValue || now - window >= 1000) { window = now; written = 0; }
            if (written >= 20) { Dropped++; return false; }
            busy = true;
            try
            {
                sink("{\"schema\":2,\"sessionId\":" + Quote(sessionId) + ",\"recordSequence\":" + (++recordSequence)
                    + ",\"runtime\":" + runtimeMetadata + ",\"kind\":" + Quote(kind) + ",\"predictionId\":" + Quote(id)
                    + ",\"elapsedMs\":" + now + ",\"attacker\":" + Quote(c.Attacker) + ",\"target\":" + Quote(c.Target)
                    + ",\"weapon\":" + Quote(c.Weapon) + ",\"turn\":" + Quote(c.Turn) + ",\"scene\":" + Quote(c.Scene)
                    + ",\"attackIndex\":" + c.AttackIndex + ",\"conditionRevision\":" + Quote(c.ConditionRevision)
                    + ",\"conditionsComplete\":" + Bool(c.Complete) + "," + values + "}");
                written++;
                return true;
            }
            catch { Faulted = true; enabled = false; pending.Clear(); return false; }
            finally { busy = false; }
        }
        static bool? Match(string a, string b) { return string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) ? (bool?)null : a == b; }
        static string Bool(bool? b) { return b.HasValue ? (b.Value ? "true" : "false") : "null"; }
        static string Number(double d) { return d.ToString("R", CultureInfo.InvariantCulture); }
        static string StringArray(IEnumerable<string> values)
        {
            var copy = (values ?? new string[0]).Take(33).ToArray();
            var limited = copy.Take(32).Select(Quote).ToList();
            if (copy.Length > 32) limited.Add(Quote("[additional entries truncated]"));
            return "[" + string.Join(",", limited.ToArray()) + "]";
        }
        internal static string Quote(string text)
        {
            if (text == null) return "null";
            // Bound each user-derived field. Escape every control character and
            // surrogate code unit, so arbitrary names still produce valid JSONL.
            var s = new StringBuilder("\"");
            int length = Math.Min(512, text.Length);
            if (length < text.Length && length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length])) length--;
            for (int i = 0; i < length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\\') s.Append('\\').Append(c);
                else if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(text[i + 1]))
                { s.Append("\\u").Append(((int)c).ToString("x4")).Append("\\u").Append(((int)text[++i]).ToString("x4")); }
                else if (char.IsSurrogate(c)) s.Append("\\ufffd");
                else if (c < 32) s.Append("\\u").Append(((int)c).ToString("x4"));
                else s.Append(c);
            }
            if (text.Length > length) s.Append("[truncated]");
            return s.Append('"').ToString();
        }
    }
    public sealed class LocalDiagnosticLog
    {
        readonly string path;
        readonly int limit;
        readonly object gate = new object();
        public LocalDiagnosticLog(string path, int byteLimit = 1048576)
        { this.path = path ?? throw new ArgumentNullException("path"); if (byteLimit < 16384) throw new ArgumentOutOfRangeException("byteLimit"); limit = byteLimit; }
        public void Write(string line)
        {
            lock (gate)
            {
                int size = Encoding.UTF8.GetByteCount(line + "\n");
                if (size > limit) throw new InvalidOperationException("Diagnostic record exceeds file budget");
                if (File.Exists(path) && new FileInfo(path).Length + size > limit)
                { if (File.Exists(path + ".prev")) File.Delete(path + ".prev"); File.Move(path, path + ".prev"); }
                File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
            }
        }
    }
}
