using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    // A deliberately partial basic weapon model. No game objects, events or RNG.
    public sealed class BetaInput
    {
        public readonly string Actor, Target, Weapon, Turn, Signature;
        public readonly int BAB, AttackBonus, AC, Rolls, Sides, Bonus, CriticalEdge, Multiplier;
        public readonly bool FullAttack, DamageKnown, DefensePartPresent;
        public readonly string Source;
        public readonly IReadOnlyList<DamageComponentSnapshot> Components;
        public readonly bool NaturalWeapon;
        public BetaInput(string actor, string target, string weapon, string turn, string signature,
            int bab, int ab, int ac, int rolls, int sides, int bonus, int edge, int multiplier,
            bool fullAttack, bool damageKnown, bool defensePartPresent,
            IEnumerable<DamageComponentSnapshot> components = null, string source = "cached-stats/basic-primary-weapon", bool naturalWeapon = false)
        {
            if (new[] { actor, target, weapon, turn, signature }.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Missing beta identity");
            if (bab < 0 || bab > 100 || edge < 2 || edge > 20 || multiplier < 1 || multiplier > 10
                || rolls < 0 || rolls > 100 || sides < 1 || sides > 100) throw new ArgumentOutOfRangeException("beta input");
            Actor = actor; Target = target; Weapon = weapon; Turn = turn; Signature = signature;
            BAB = bab; AttackBonus = ab; AC = ac; Rolls = rolls; Sides = sides; Bonus = bonus;
            CriticalEdge = edge; Multiplier = multiplier; FullAttack = fullAttack; DamageKnown = damageKnown;
            DefensePartPresent = defensePartPresent;
            Source = source;
            NaturalWeapon = naturalWeapon;
            var copy = (components ?? new[] { new DamageComponentSnapshot(new DamageTerm(rolls, sides, bonus, true)
                { Kind = DamageKind.Physical }, false, null) }).ToArray();
            if (copy.Length == 0 || copy.Length > 32 || copy.Any(c => c == null)) throw new ArgumentException("Invalid damage components");
            Components = Array.AsReadOnly(copy);
            DamageKnown = damageKnown && copy.All(c => c.Kind != DamageKind.Unknown && Enum.IsDefined(typeof(DamageKind), c.Kind));
        }
    }
    public sealed class BetaRow
    {
        public int Index { get; internal set; }
        public int Penalty { get; internal set; }
        public int AB { get; internal set; }
        public int AC { get; internal set; }
        public double Hit { get; internal set; }
        public double Crit { get; internal set; }
        public DamageSummary? Normal { get; internal set; }
        public DamageSummary? Critical { get; internal set; }
        public double? ExpectedRaw { get; internal set; }
        public double? MeanOnHit { get { return ExpectedRaw.HasValue && Hit > 0 ? ExpectedRaw.Value / Hit : (double?)null; } }
    }
    public sealed class BetaSnapshot
    {
        public string Id { get; private set; }
        public BetaInput Input { get; private set; }
        public IReadOnlyList<BetaRow> Rows { get; private set; }
        public IReadOnlyList<string> Notes { get; private set; }
        internal BetaSnapshot(string id, BetaInput input, IList<BetaRow> rows)
        {
            Id = id; Input = input; Rows = Array.AsReadOnly(rows.ToArray());
            Notes = Array.AsReadOnly(new[] {
                "제조 무기는 기본 BAB 반복, 자연 무기는 기존 무기 슬롯별 1회 모델입니다. 실제 전체 순서·횟수는 미확정입니다.",
                "기승 시 기수와 탈것을 나누어 표시합니다. 가속·연타·보조손 등 추가 타격은 미확인입니다.",
                input.Source.StartsWith("observed-")
                    ? "최근 게임 추정의 AB/AC/피해 구성 사본입니다. 첫 공격을 기본 반복 모델에 적용하며 완전한 조건 일치는 미확정입니다."
                    : "무기 강화·특기·명중/피해 능력치 치환·조건부 발동 효과는 미반영입니다.",
                input.Source.StartsWith("observed-")
                    ? "추정 당시 일부 보정이 포함될 수 있지만 공격 시 조건부 효과·난이도 보정은 별도 검증되지 않았습니다."
                    : "난이도 보정·강타 등 공격 시 적용되는 보정은 미반영입니다.",
                input.Source.StartsWith("observed-")
                    ? "AC는 관측된 추정 이벤트의 값입니다. 은폐·재굴림·치명타 면역 등 확률 수정은 미반영입니다."
                    : "AC는 저장된 일반 AC입니다. 협공·은폐·무방비·재굴림·치명타 면역 등은 미반영입니다.",
                "피해는 방어 전 모델입니다. DR·저항·면역·보호막 적용 후 피해는 계산하지 않습니다."
            });
        }
        public DiagnosticContext Context(int index = -1)
        { return new DiagnosticContext(Input.Actor, Input.Target, Input.Weapon, Input.Turn, "unknown", index, Input.Signature, false); }
    }
    public static class BetaModel
    {
        public static BetaSnapshot Build(BetaInput input, string id)
        {
            if (input == null || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Missing beta model input");
            var rows = new List<BetaRow>();
            int count = input.FullAttack && !input.NaturalWeapon ? Math.Min(4, 1 + Math.Max(0, input.BAB - 1) / 5) : 1;
            for (int i = 0; i < count; i++)
            {
                var row = new BetaRow { Index = i, Penalty = i * 5, AB = input.AttackBonus - i * 5, AC = input.AC };
                int hits = 0, threats = 0, confirms = 0;
                for (int d = 1; d <= 20; d++)
                {
                    bool hit = d == 20 || (d > 1 && d + row.AB >= row.AC);
                    if (hit) hits++;
                    if (hit && d >= input.CriticalEdge) threats++;
                    // The base WotR confirmation compares the total, without the
                    // initial attack's natural-1/20 shortcut. Critical AC may differ.
                    if (d + row.AB >= row.AC) confirms++;
                }
                row.Hit = hits / 20.0; row.Crit = threats / 20.0 * confirms / 20.0;
                if (input.DamageKnown)
                {
                    var terms = input.Components.Select(c => c.ToTerm()).ToArray();
                    row.Normal = DamageMath.Summarize(terms, 1);
                    row.Critical = DamageMath.Summarize(terms, input.Multiplier);
                    row.ExpectedRaw = DamageMath.Expected(row.Hit, row.Crit, row.Normal.Value.Mean, row.Critical.Value.Mean);
                }
                rows.Add(row);
            }
            return new BetaSnapshot(id, input, rows);
        }
    }
    public enum BetaComparisonStatus { ComparedPartialModel, InputsChanged, PreviewExpired, AttackModeChanged, UnmodeledAttack, PairChanged, NoComparableFields }
    public sealed class BetaComparison
    {
        public BetaSnapshot Preview { get; internal set; }
        public BetaRow Row { get; internal set; }
        public BetaComparisonStatus Status { get; internal set; }
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
        sealed class Pending { internal BetaSnapshot Preview; internal BetaRow Row; internal BetaComparisonStatus Status; internal long Time; }
        sealed class Displayed { internal BetaSnapshot Preview; internal long Time; }
        readonly Dictionary<string, Displayed> shown = new Dictionary<string, Displayed>();
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        static string Key(BetaInput input) { return input.Actor + "|" + input.Target + "|" + input.Weapon; }
        public void Shown(BetaSnapshot snapshot, long now)
        {
            if (shown.Count >= 32 && !shown.ContainsKey(Key(snapshot.Input))) shown.Remove(shown.OrderBy(p => p.Value.Time).First().Key);
            shown[Key(snapshot.Input)] = new Displayed { Preview = snapshot, Time = now };
        }
        public void Invalidate() { shown.Clear(); pending.Clear(); }
        public bool HasPair(string actor, string target)
        { return shown.Values.Any(v => v.Preview.Input.Actor == actor && v.Preview.Input.Target == target); }
        public void Begin(string eventId, BetaInput current, int index, bool fullAttack, long now, string actualWeapon = null)
        {
            foreach (var key in pending.Where(p => now < p.Value.Time || now - p.Value.Time > 10000).Select(p => p.Key).ToArray()) pending.Remove(key);
            if (current == null || !HasPair(current.Actor, current.Target) || string.IsNullOrEmpty(eventId)) return;
            Displayed displayed;
            if (!shown.TryGetValue(Key(current), out displayed)) displayed = shown.Values.First(v => v.Preview.Input.Actor == current.Actor && v.Preview.Input.Target == current.Target);
            var preview = displayed.Preview;
            if (pending.Count >= 32) pending.Remove(pending.OrderBy(p => p.Value.Time).First().Key);
            var p = new Pending { Preview = preview, Time = now, Status = BetaComparisonStatus.ComparedPartialModel };
            int rowIndex = current.NaturalWeapon && index >= 0 ? 0 : index;
            if (now < displayed.Time || now - displayed.Time > 2000) p.Status = BetaComparisonStatus.PreviewExpired;
            else if (preview.Input.Weapon != current.Weapon || preview.Input.Weapon != (actualWeapon ?? current.Weapon) || preview.Input.Turn != current.Turn) p.Status = BetaComparisonStatus.PairChanged;
            else if (preview.Input.FullAttack != fullAttack) p.Status = BetaComparisonStatus.AttackModeChanged;
            else if (preview.Input.Signature != current.Signature) p.Status = BetaComparisonStatus.InputsChanged;
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
            var result = new BetaComparison { Preview = p.Preview, Row = p.Row, Status = p.Status,
                ActualAB = ab, ActualAC = ac, ActualRolls = rolls, ActualSides = sides, ActualBonus = bonus,
                ActualComponents = actualComponents == null ? null : Array.AsReadOnly(actualComponents.ToArray()),
                Differences = Array.AsReadOnly(new string[0]), ComparedFields = Array.AsReadOnly(new string[0]) };
            if (now < p.Time || now - p.Time > 10000) result.Status = BetaComparisonStatus.PreviewExpired;
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
