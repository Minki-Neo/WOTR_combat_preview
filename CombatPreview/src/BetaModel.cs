using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    // A deliberately partial basic weapon model. No game objects, events or RNG.
    public sealed class BetaInput
    {
        public readonly string Actor, Target, Weapon, Turn;
        public string Signature { get; private set; }
        public readonly int BAB, AttackBonus, AC, Rolls, Sides, Bonus, CriticalEdge, Multiplier, PendingAttack, PendingDamage;
        public readonly bool FullAttack, DamageKnown, DefensePartPresent;
        public readonly string Source;
        public double? ConcealmentMiss { get; private set; }
        public int MirrorImages { get; private set; }
        internal bool AlwaysChance { get; private set; }
        internal D20RollModel AttackRollModel { get; private set; }
        public bool ACObserved { get { return Source == "observed-completed-rules" || Source.StartsWith("observed-existing-estimate"); } }
        public bool PairObserved { get { return ACObserved && AttackObserved; } }
        public bool AttackObserved { get { return PendingAttack == 0 && (ACObserved || Source.StartsWith("observed-weapon-rules/") || Source.StartsWith("observed-target-attack/")); } }
        public bool DamageObserved { get { return PendingDamage == 0 && Source.StartsWith("observed-"); } }
        public readonly IReadOnlyList<DamageComponentSnapshot> Components;
        public readonly bool NaturalWeapon;
        public readonly IReadOnlyList<int> AttackPenalties;
        public readonly string AttackCountSource, Grip;
        readonly DefenseProfile defense;
        public DefenseProfile Defense { get { return SupportedCombatRules.Copy(defense); } }
        public readonly IReadOnlyList<string> ModifierNotes, DefenseNotes;
        public readonly IReadOnlyList<string> AttackCountReasons;
        public BetaInput(string actor, string target, string weapon, string turn, string signature,
            int bab, int ab, int ac, int rolls, int sides, int bonus, int edge, int multiplier,
            bool fullAttack, bool damageKnown, bool defensePartPresent,
            IEnumerable<DamageComponentSnapshot> components = null, string source = "cached-stats/basic-primary-weapon", bool naturalWeapon = false,
            IEnumerable<int> attackPenalties = null, string attackCountSource = "basic-bab", IEnumerable<string> attackCountReasons = null, string grip = "unknown",
            DefenseProfile defense = null, IEnumerable<string> modifierNotes = null, IEnumerable<string> defenseNotes = null, int pendingAttack = 0, int pendingDamage = 0,
            double? concealmentMiss = null)
        {
            if (new[] { actor, target, weapon, turn, signature }.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Missing beta identity");
            if (bab < 0 || bab > 100 || edge < 2 || edge > 20 || multiplier < 1 || multiplier > 10
                || rolls < 0 || rolls > 100 || sides < 1 || sides > 100) throw new ArgumentOutOfRangeException("beta input");
            Actor = actor; Target = target; Weapon = weapon; Turn = turn; Signature = signature;
            PendingAttack = pendingAttack; PendingDamage = pendingDamage;
            BAB = bab; AttackBonus = ab; AC = ac; Rolls = rolls; Sides = sides; Bonus = bonus;
            CriticalEdge = edge; Multiplier = multiplier; FullAttack = fullAttack; DamageKnown = damageKnown;
            DefensePartPresent = defensePartPresent;
            Source = source;
            ConcealmentMiss = concealmentMiss;
            NaturalWeapon = naturalWeapon;
            var penalties = (attackPenalties ?? WeaponModelRules.BasicPattern(bab, fullAttack, naturalWeapon, 0, 0)).ToArray();
            if (penalties.Length == 0 || penalties.Length > 24 || penalties.Any(p => p < 0 || p > 100)) throw new ArgumentException("Invalid attack pattern");
            AttackPenalties = Array.AsReadOnly(penalties);
            AttackCountSource = attackCountSource; Grip = grip;
            this.defense = SupportedCombatRules.Copy(defense);
            ModifierNotes = Array.AsReadOnly((modifierNotes ?? new string[0]).ToArray());
            DefenseNotes = Array.AsReadOnly((defenseNotes ?? new string[0]).ToArray());
            AttackCountReasons = Array.AsReadOnly((attackCountReasons ?? new string[0]).ToArray());
            var copy = (components ?? new[] { new DamageComponentSnapshot(new DamageTerm(rolls, sides, bonus, true)
                { Kind = DamageKind.Physical }, false, null) }).ToArray();
            if (copy.Length == 0 || copy.Length > 32 || copy.Any(c => c == null)) throw new ArgumentException("Invalid damage components");
            Components = Array.AsReadOnly(copy);
            DamageKnown = damageKnown && copy.All(c => c.Kind != DamageKind.Unknown && Enum.IsDefined(typeof(DamageKind), c.Kind));
        }

        internal BetaInput WithConcealment(double? miss, string signature, int mirrorImages = 0, bool alwaysChance = false)
        {
            var copy = (BetaInput)MemberwiseClone();
            copy.ConcealmentMiss = miss;
            copy.MirrorImages = mirrorImages;
            copy.AlwaysChance = alwaysChance;
            copy.Signature = signature;
            return copy;
        }
        internal BetaInput WithRollModel(D20RollModel model, string stamp)
        { var copy = (BetaInput)MemberwiseClone(); copy.AttackRollModel = model; copy.Signature += "|d20:" + stamp; return copy; }
    }
    public sealed class BetaRow
    {
        public bool RollProbabilityAvailable { get; internal set; } = true;
        public int Index { get; internal set; }
        public int Penalty { get; internal set; }
        public int AB { get; internal set; }
        public int AC { get; internal set; }
        public double Hit { get; internal set; }
        public double HitAcOnly { get; internal set; }
        public double MirrorImageConsumed { get; internal set; }
        public double Crit { get; internal set; }
        public DamageSummary? Normal { get; internal set; }
        public DamageSummary? Critical { get; internal set; }
        public double? ExpectedRaw { get; internal set; }
        public MitigatedResult AfterDefense { get; internal set; }
        public double? MeanOnHit { get { return ExpectedRaw.HasValue && Hit > 0 ? ExpectedRaw.Value / Hit : (double?)null; } }
    }
    public sealed class BetaSnapshot
    {
        public string Id { get; private set; }
        public BetaInput Input { get; private set; }
        public IReadOnlyList<BetaRow> Rows { get; private set; }
        public IReadOnlyList<string> Notes { get; private set; }
        internal string SequenceScope;
        internal FrozenDiagnosticJson DisplayedHealth;
        internal BetaSnapshot(string id, BetaInput input, IList<BetaRow> rows)
        {
            Id = id; Input = input; Rows = Array.AsReadOnly(rows.ToArray());
            Notes = Array.AsReadOnly(new[] {
                "제조 무기는 판독된 추가/반복 공격, 자연 무기는 기존 슬롯별 1회 모델입니다. 실제 전체 순서는 미확정입니다.",
                "확인된 질풍격/가속 효과를 합산합니다. 제조 보조손 등 알 수 없는 추가 타격은 미확인입니다.",
                input.Source.StartsWith("observed-")
                    ? "최근 게임 추정의 AB/AC/피해 구성 사본입니다. 첫 공격을 기본 반복 모델에 적용하며 완전한 조건 일치는 미확정입니다."
                    : "지원된 활성 효과·무기 강화·명중 능력치 치환을 반영합니다. 상세에 판독 내역을 표시합니다.",
                input.Source.StartsWith("observed-")
                    ? "추정 당시 일부 보정이 포함될 수 있지만 공격 시 조건부 효과·난이도 보정은 별도 검증되지 않았습니다."
                    : "지원 목록 밖의 조건부 효과·난이도 보정은 미확인입니다.",
                input.ACObserved
                    ? "AC는 관측된 추정 이벤트의 값입니다. 공격별 특수 보정은 별도입니다."
                    : "AC는 저장된 일반 AC입니다. 협공·무방비·공격별 특수 보정은 별도입니다.",
                "방어 후 수치는 현재 상태의 지원된 DR·저항·면역 모델입니다. 소모형 보호막·특수 방어는 미확인 시 계산하지 않습니다."
            });
        }
        public DiagnosticContext Context(int index = -1)
        { return new DiagnosticContext(Input.Actor, Input.Target, Input.Weapon, Input.Turn, "unknown", index, Input.Signature, false); }
    }
    public static class BetaModel
    {
        public static BetaSnapshot Build(BetaInput input, string id)
            => BuildCore(input, id, null);

        internal static BetaSnapshot[] BuildSequence(IReadOnlyList<BetaSnapshot> displayed)
        {
            if (displayed.Count < 2 || displayed.Select(snapshot => snapshot.Input.Target).Distinct().Count() != 1)
                return displayed.ToArray();
            var context = new SequenceContext(displayed.Select(snapshot => snapshot.Input).ToArray());
            if (context.Images == null && context.Pool == null && !displayed.Any(snapshot => snapshot.Input.AttackRollModel?.ConsumesEffect == true)) return displayed.ToArray();
            string order = string.Join(",", displayed.Select(snapshot => snapshot.Id));
            return displayed.Select(snapshot => BuildCore(snapshot.Input, snapshot.Id + "-sequence:" + order, context)).ToArray();
        }
        sealed class SequenceContext
        {
            internal readonly Dictionary<string, int> Rolls = new Dictionary<string, int>();
            internal bool UnknownImages;
            internal readonly MirrorImageSequence Images;
            internal readonly MitigationMath.PhysicalPoolSequence Pool;
            internal SequenceContext(BetaInput[] inputs)
            {
                int images = inputs.Max(input => input.MirrorImages);
                if (images > 0) Images = new MirrorImageSequence(images);
                var pools = inputs.Select(input => input.Defense?.PhysicalPool).Where(pool => pool.HasValue)
                    .Select(pool => pool.Value).Distinct().ToArray();
                var first = inputs.FirstOrDefault(input => input.Defense?.PhysicalPool.HasValue == true);
                var physical = first?.Components.Where(term => term.Kind == DamageKind.Physical).Take(2).ToArray();
                if (pools.Length == 1 && physical?.Length == 1)
                    Pool = new MitigationMath.PhysicalPoolSequence(physical[0].ToTerm(), first.Multiplier,
                        inputs.Sum(input => input.AttackPenalties.Count), pools[0], images, first.Defense.PhysicalDecline);
            }
        }
        static BetaSnapshot BuildCore(BetaInput input, string id, SequenceContext sequence)
        {
            if (input == null || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Missing beta model input");
            var rows = new List<BetaRow>();
            var images = input.MirrorImages > 0 ? sequence?.Images ?? new MirrorImageSequence(input.MirrorImages) : null;
            int count = input.AttackPenalties.Count;
            var defense = input.Defense;
            var terms = input.DamageKnown ? input.Components.Select(c => c.ToTerm()).ToArray() : null;
            var physical = terms?.Where(term => term.Kind == DamageKind.Physical).Take(2).ToArray();
            MeanWeaponShieldApproximation shieldApproximation = null;
            bool clusteredHasPool = input.FullAttack && defense?.ClusteredPhysicalDR == true
                && (defense.PhysicalPool.HasValue || defense.HasConsumablePool
                    || defense.PhysicalSequenceSources.Any(source => source.Remaining >= 0));
            bool clustered = input.FullAttack && defense?.Complete == true && defense.ClusteredPhysicalDR
                && !defense.Immunities.Contains(DamageKind.Physical) && physical?.Length == 1
                && defense.Reduction.TryGetValue(DamageKind.Physical, out int clusteredReduction) && clusteredReduction > 0;
            MitigationMath.PhysicalPoolSequence clusteredDR = null;
            if (clustered && !clusteredHasPool)
            {
                int reduction = defense.Reduction[DamageKind.Physical];
                clusteredDR = new MitigationMath.PhysicalPoolSequence(physical[0], input.Multiplier, count,
                    (reduction, reduction), input.MirrorImages, defense.PhysicalDecline);
                defense = SupportedCombatRules.Copy(defense);
                defense.Reduction[DamageKind.Physical] = 0;
            }
            bool usesPool = defense?.Complete == true && defense.PhysicalPool.HasValue
                && !defense.Immunities.Contains(DamageKind.Physical) && physical?.Length == 1;
            var physicalPool = sequence?.Pool ?? (usesPool
                ? new MitigationMath.PhysicalPoolSequence(physical[0], input.Multiplier, count, defense.PhysicalPool.Value, input.MirrorImages, defense.PhysicalDecline)
                : null);
            if (sequence?.Pool != null)
                physicalPool.SetDamage(usesPool ? physical[0] : null, input.Multiplier, defense?.PhysicalDecline ?? 1);
            if (!input.DamageKnown) physicalPool?.Invalidate();
            bool unknownImages = sequence?.UnknownImages == true;
            for (int i = 0; i < count; i++)
            {
                var row = new BetaRow { Index = i, Penalty = input.AttackPenalties[i], AB = input.AttackBonus - input.AttackPenalties[i], AC = input.AC };
                int hits = 0, threats = 0, confirms = 0, nearMisses = 0;
                for (int d = 1; d <= 20; d++)
                {
                    bool hit = SpellPreviewMath.AttackHits(d, row.AB, row.AC, input.AlwaysChance);
                    if (hit) hits++;
                    else if ((long)row.AC - row.AB - d <= 5) nearMisses++;
                    if (hit && d >= input.CriticalEdge) threats++;
                    // The base WotR confirmation compares the total, without the
                    // initial attack's natural-1/20 shortcut. Critical AC may differ.
                    if (d + row.AB >= row.AC) confirms++;
                }
                double sightPass = 1 - input.ConcealmentMiss.GetValueOrDefault();
                row.HitAcOnly = hits / 20.0;
                double threatChance = threats / 20.0, confirmChance = confirms / 20.0, nearMissChance = nearMisses / 20.0;
                var rollModel = input.AttackRollModel;
                if (rollModel != null)
                {
                    int ordinal = i;
                    if (sequence != null)
                    { sequence.Rolls.TryGetValue(input.Actor, out ordinal); sequence.Rolls[input.Actor] = ordinal + 1; }
                    row.RollProbabilityAvailable = rollModel.Complete && (!rollModel.ConsumesEffect || ordinal == 0);
                    if (row.RollProbabilityAvailable)
                    {
                        var distribution = D20.Outcomes(rollModel.Modifiers,
                            die => !SpellPreviewMath.AttackHits(die, row.AB, row.AC, input.AlwaysChance), rollModel.AfterRoll, rollModel.ReplaceOneWithMax);
                        bool Hits(D20.Outcome outcome) => SpellPreviewMath.AttackHits(outcome.Die, row.AB + outcome.Bonus, row.AC, input.AlwaysChance);
                        row.HitAcOnly = distribution.Where(Hits).Sum(outcome => outcome.P);
                        threatChance = distribution.Where(outcome => Hits(outcome) && outcome.Die >= input.CriticalEdge).Sum(outcome => outcome.P);
                        confirmChance = rollModel.Probability(die => die == 1, (die, extra) => (long)die + row.AB + extra >= row.AC);
                        nearMissChance = distribution.Where(outcome => !Hits(outcome) && (long)row.AC - row.AB - outcome.Bonus - outcome.Die <= 5).Sum(outcome => outcome.P);
                    }
                }
                row.RollProbabilityAvailable &= !unknownImages;
                if (!row.RollProbabilityAvailable)
                {
                    // An unknown hit cannot advance shared defenses using a fair
                    // die fallback. Preserve the damage formula, invalidate totals.
                    physicalPool?.Invalidate();
                    clusteredDR?.Invalidate();
                    if (images != null) { unknownImages = true; if (sequence != null) sequence.UnknownImages = true; }
                    row.AfterDefense = new MitigatedResult { Reason = "재굴림의 동적/소모 효과 또는 앞선 거울상 소모 미확인" };
                    if (input.DamageKnown) { row.Normal = DamageMath.Summarize(terms, 1); row.Critical = DamageMath.Summarize(terms, input.Multiplier); }
                    rows.Add(row); continue;
                }
                double hitBeforeImages = row.HitAcOnly * sightPass;
                double nearMissBeforeImages = nearMissChance * sightPass;
                double critBeforeImages = defense?.CriticalImmune == true ? 0 : Math.Min(hitBeforeImages, threatChance * confirmChance * sightPass);
                double realTarget = images?.Apply(hitBeforeImages, nearMissBeforeImages) ?? 1;
                row.MirrorImageConsumed = images?.LastConsumed ?? 0;
                row.Hit = hitBeforeImages * realTarget;
                row.Crit = critBeforeImages * realTarget;
                if (input.DamageKnown)
                {
                    row.Normal = DamageMath.Summarize(terms, 1);
                    row.Critical = DamageMath.Summarize(terms, input.Multiplier);
                    row.ExpectedRaw = DamageMath.Expected(row.Hit, row.Crit, row.Normal.Value.Mean, row.Critical.Value.Mean);
                    row.AfterDefense = physical.Length > 1
                        ? new MitigatedResult { Reason = "여러 물리 피해 구성의 개별 관통/순서 미확인" }
                        : MitigationMath.Calculate(terms, input.Multiplier, row.Hit, row.Crit, defense);
                    if (physicalPool != null)
                        row.AfterDefense = physicalPool.Apply(row.AfterDefense, row.Hit, row.Crit,
                            hitBeforeImages, critBeforeImages, nearMissBeforeImages, input.MirrorImages == 0);
                    if (clusteredDR != null)
                    {
                        row.AfterDefense = clusteredDR.Apply(row.AfterDefense, row.Hit, row.Crit,
                            hitBeforeImages, critBeforeImages, nearMissBeforeImages, input.MirrorImages == 0);
                        if (row.AfterDefense.Available) row.AfterDefense.Reason = "readonly-clustered-physical-DR";
                    }
                    else if (clusteredHasPool)
                        row.AfterDefense = new MitigatedResult { Reason = "응집 공격과 소모형 보호막의 공동 감소 순서 미확인" };
                    if (rollModel?.ConsumesEffect == true)
                    {
                        row.ExpectedRaw = null;
                        row.AfterDefense = new MitigatedResult { Reason = "초기 명중 뒤 소모되는 재굴림 효과의 치명타 확인 미확인" };
                        physicalPool?.Invalidate();
                        clusteredDR?.Invalidate();
                    }
                    else if (row.AfterDefense?.Available != true)
                    {
                        if (shieldApproximation == null) shieldApproximation = new MeanWeaponShieldApproximation(input.Defense, input.FullAttack);
                        var approximate = shieldApproximation.Apply(terms, input.Multiplier, row.Hit, row.Crit);
                        if (approximate != null)
                        {
                            approximate.ApproximationAssumptions += " · 원래 미확인: " + row.AfterDefense?.Reason;
                            row.AfterDefense = approximate;
                        }
                    }
                }
                rows.Add(row);
            }
            return new BetaSnapshot(id, input, rows) { SequenceScope = sequence == null ? null
                : "displayed attack order; shared mirror images and supported physical pool; actual scheduling order not certified" };
        }
    }

    internal sealed class MirrorImageSequence
    {
        readonly double[] remaining;
        internal double LastConsumed { get; private set; }

        internal MirrorImageSequence(int count)
        {
            remaining = new double[count + 1];
            remaining[count] = 1;
        }

        // Ascending order updates lower states after their original mass has been processed.
        internal double Apply(double hit, double nearMiss)
        {
            double realTarget = remaining[0];
            double consumed = 0;
            for (int images = 1; images < remaining.Length; images++)
            {
                double targetShare = 1.0 / (images + 1);
                realTarget += remaining[images] * targetShare;
                double removed = remaining[images] * (hit * (1 - targetShare) + nearMiss);
                remaining[images] -= removed;
                remaining[images - 1] += removed;
                consumed += removed;
            }
            LastConsumed = consumed;
            return realTarget;
        }

        internal static double NearMiss(int ab, int ac, bool alwaysChance = false)
        {
            int count = 0;
            for (int d = 1; d < 20; d++)
            {
                long gap = (long)ac - ab - d;
                if (gap <= 5 && !SpellPreviewMath.AttackHits(d, ab, ac, alwaysChance)) count++;
            }
            return count / 20.0;
        }
    }
    public enum BetaComparisonStatus { ComparedPartialModel, InputsChanged, PreviewExpired, AttackModeChanged, UnmodeledAttack, PairChanged, NoComparableFields }
    public sealed class BetaComparison
    {
        public BetaSnapshot Preview { get; internal set; }
        internal FrozenDiagnosticJson HealthAtBegin;
        internal bool HealthCapturedAtBegin;
        internal FrozenDiagnosticJson InputAtBegin;
        public AttackRowLink AttackLink { get; internal set; }
        public BetaRow Row { get; internal set; }
        internal BetaRow PreviousRow { get; set; }
        internal bool Rolled { get; set; }
        public BetaComparisonStatus Status { get; internal set; }
        public IReadOnlyList<string> InputChanges { get; internal set; }
        public long DisplayAgeAtBeginMs { get; internal set; }
        public long AttackDurationMs { get; internal set; }
        public int ActualAB { get; internal set; }
        public int ActualAC { get; internal set; }
        public int? ActualRolls { get; internal set; }
        public int? ActualSides { get; internal set; }
        public int? ActualBonus { get; internal set; }
        public IReadOnlyList<string> Differences { get; internal set; }
        public IReadOnlyList<string> ComparedFields { get; internal set; }
        public IReadOnlyList<DamageComponentSnapshot> ActualComponents { get; internal set; }
        public bool HasDifference { get { return Status == BetaComparisonStatus.ComparedPartialModel && Differences.Count > 0; } }
    }
    public sealed class BetaTracker
    {
        sealed class Pending { internal FrozenDiagnosticJson InputAtBegin; internal AttackRowLink Link; internal FrozenDiagnosticJson DisplayedHealth; internal BetaSnapshot Preview; internal BetaRow Row, PreviousRow; internal BetaComparisonStatus Status; internal long Time, DisplayAge; internal string[] Changes; }
        sealed class Displayed { internal FrozenDiagnosticJson Health; internal BetaSnapshot Preview; internal long Time; }
        readonly Dictionary<string, Displayed> shown = new Dictionary<string, Displayed>();
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        static string Key(BetaInput input) { return input.Actor + "|" + input.Target + "|" + input.Weapon; }
        public void Shown(BetaSnapshot snapshot, long now)
        {
            if (shown.Count >= 32 && !shown.ContainsKey(Key(snapshot.Input))) shown.Remove(shown.OrderBy(p => p.Value.Time).First().Key);
            shown[Key(snapshot.Input)] = new Displayed { Preview = snapshot, Health = snapshot.DisplayedHealth, Time = now };
        }
        public void Invalidate() { shown.Clear(); pending.Clear(); }
        public bool HasPair(string actor, string target)
        { return shown.Values.Any(v => v.Preview.Input.Actor == actor && v.Preview.Input.Target == target); }
        public void Begin(string eventId, BetaInput current, int index, bool fullAttack, long now, string actualWeapon = null, AttackRowLink attackLink = null)
        {
            foreach (var key in pending.Where(p => now < p.Value.Time || now - p.Value.Time > 10000).Select(p => p.Key).ToArray()) pending.Remove(key);
            if (current == null || !HasPair(current.Actor, current.Target) || string.IsNullOrEmpty(eventId)) return;
            Displayed displayed;
            if (!shown.TryGetValue(Key(current), out displayed)) displayed = shown.Values.First(v => v.Preview.Input.Actor == current.Actor && v.Preview.Input.Target == current.Target);
            var preview = displayed.Preview;
            if (pending.Count >= 32) pending.Remove(pending.OrderBy(p => p.Value.Time).First().Key);
            var changes = new List<string>();
            var previous = preview.Input;
            if (previous.Source != current.Source) changes.Add("Source");
            if (previous.AttackBonus != current.AttackBonus) changes.Add("AB");
            if (previous.AC != current.AC) changes.Add("AC");
            if (previous.BAB != current.BAB) changes.Add("BAB");
            if (previous.Rolls != current.Rolls || previous.Sides != current.Sides || previous.Bonus != current.Bonus) changes.Add("WeaponDamage");
            if (previous.CriticalEdge != current.CriticalEdge || previous.Multiplier != current.Multiplier) changes.Add("Critical");
            if (previous.PendingAttack != current.PendingAttack || previous.PendingDamage != current.PendingDamage) changes.Add("PendingModifiers");
            if (previous.Grip != current.Grip) changes.Add("Grip");
            if (!previous.AttackPenalties.SequenceEqual(current.AttackPenalties)) changes.Add("AttackPattern");
            if (!previous.ModifierNotes.SequenceEqual(current.ModifierNotes)) changes.Add("ModifierEvidence");
            if (!previous.DefenseNotes.SequenceEqual(current.DefenseNotes)) changes.Add("DefenseEvidence");
            if (previous.ConcealmentMiss != current.ConcealmentMiss) changes.Add("Concealment");
            if (previous.MirrorImages != current.MirrorImages) changes.Add("MirrorImages");
            if (previous.Defense?.PhysicalPool != current.Defense?.PhysicalPool) changes.Add("PhysicalPool");
            if (previous.Signature != current.Signature) changes.Add("ContextSignature");
            var p = new Pending { Preview = preview, DisplayedHealth = displayed.Health, Time = now, DisplayAge = now - displayed.Time,
                Changes = changes.ToArray(), Status = BetaComparisonStatus.ComparedPartialModel,
                Link = attackLink, InputAtBegin = FrozenDiagnosticJson.Capture(BetaInputEvidence.Capture(current)) };
            int rowIndex = attackLink != null ? attackLink.Exact ? attackLink.WeaponOrdinal : -1 : current.NaturalWeapon && index >= 0 ? 0 : index;
            bool linkedPenaltyMatches = attackLink == null || rowIndex >= 0 && rowIndex < preview.Rows.Count
                && preview.Rows[rowIndex].Penalty == attackLink.Penalty;
            if (now < displayed.Time || now - displayed.Time > 2000) p.Status = BetaComparisonStatus.PreviewExpired;
            else if (preview.Input.Weapon != current.Weapon || preview.Input.Weapon != (actualWeapon ?? current.Weapon) || preview.Input.Turn != current.Turn) p.Status = BetaComparisonStatus.PairChanged;
            else if (preview.Input.FullAttack != fullAttack) p.Status = BetaComparisonStatus.AttackModeChanged;
            else if (attackLink != null && (!attackLink.Exact || !linkedPenaltyMatches)) p.Status = BetaComparisonStatus.UnmodeledAttack;
            else if (preview.Input.Signature != current.Signature)
            {
                p.Status = BetaComparisonStatus.InputsChanged;
                if (rowIndex >= 0 && rowIndex < preview.Rows.Count) p.PreviousRow = preview.Rows[rowIndex];
            }
            else if (rowIndex < 0 || rowIndex >= preview.Rows.Count) p.Status = BetaComparisonStatus.UnmodeledAttack;
            else p.Row = preview.Rows[rowIndex];
            pending[eventId] = p;
        }
        public void Cancel(string eventId) { if (eventId != null) pending.Remove(eventId); }
        public BetaComparison Finish(string eventId, string actor, string target, string weapon, bool rolled,
            int ab, int ac, int? rolls, int? sides, int? bonus, long now, int componentCount = -1,
            IReadOnlyList<DamageComponentSnapshot> actualComponents = null)
        {
            Pending p;
            if (eventId == null || !pending.TryGetValue(eventId, out p)) return null;
            pending.Remove(eventId);
            var result = new BetaComparison { Preview = p.Preview, AttackLink = p.Link, InputAtBegin = p.InputAtBegin, HealthAtBegin = p.DisplayedHealth, HealthCapturedAtBegin = true, Row = p.Row, Status = p.Status,
                InputChanges = Array.AsReadOnly(p.Changes), DisplayAgeAtBeginMs = p.DisplayAge, AttackDurationMs = now - p.Time,
                ActualAB = ab, ActualAC = ac, ActualRolls = rolls, ActualSides = sides, ActualBonus = bonus,
                ActualComponents = actualComponents == null ? null : Array.AsReadOnly(actualComponents.ToArray()),
                Differences = Array.AsReadOnly(new string[0]), ComparedFields = Array.AsReadOnly(new string[0]) };
            result.Rolled = rolled;
            if (now < p.Time || now - p.Time > 10000) result.Status = BetaComparisonStatus.PreviewExpired;
            if (result.Status == BetaComparisonStatus.InputsChanged && p.Preview.Input.Actor == actor
                && p.Preview.Input.Target == target && p.Preview.Input.Weapon == weapon) result.PreviousRow = p.PreviousRow;
            if (result.Status != BetaComparisonStatus.ComparedPartialModel) return result;
            if (p.Preview.Input.Actor != actor || p.Preview.Input.Target != target || p.Preview.Input.Weapon != weapon)
            { result.Status = BetaComparisonStatus.PairChanged; return result; }
            var differences = new List<string>();
            var compared = new List<string>();
            if (rolled) { compared.Add("AB"); compared.Add("AC"); }
            if (rolled && p.Row.AB != ab) differences.Add("AB");
            if (rolled && p.Row.AC != ac) differences.Add("AC");
            if (p.Preview.Input.DamageKnown)
            {
                if (rolls.HasValue && sides.HasValue)
                { compared.Add("WeaponDice"); if (rolls != p.Preview.Input.Rolls || sides != p.Preview.Input.Sides) differences.Add("WeaponDice"); }
                if (bonus.HasValue)
                { compared.Add("WeaponBonus"); if (bonus != p.Preview.Input.Bonus) differences.Add("WeaponBonus"); }
                if (componentCount >= 0)
                { compared.Add("DamageComponents"); if (componentCount != p.Preview.Input.Components.Count) differences.Add("DamageComponents"); }
                if (actualComponents != null)
                    for (int i = 0; i < Math.Min(actualComponents.Count, p.Preview.Input.Components.Count); i++)
                    {
                        var actual = actualComponents[i]; var predicted = p.Preview.Input.Components[i];
                        string name = "Damage[" + (i + 1) + "]";
                        compared.Add(name);
                        if (actual.Kind != predicted.Kind || actual.Rolls != predicted.Rolls || actual.Sides != predicted.Sides || actual.Bonus != predicted.Bonus)
                            differences.Add(name);
                    }
            }
            result.Differences = Array.AsReadOnly(differences.ToArray());
            result.ComparedFields = Array.AsReadOnly(compared.ToArray());
            if (compared.Count == 0) result.Status = BetaComparisonStatus.NoComparableFields;
            return result;
        }
    }
}
