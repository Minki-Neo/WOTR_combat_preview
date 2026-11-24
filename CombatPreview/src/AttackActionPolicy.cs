using System;

namespace CombatPreview
{
    public sealed class AttackActionInput
    {
        public bool Known, Surprise, Overused, OutOfRange, TargetMatches, PredictionsPending;
        public bool PredictedAttack, RequestedFull, MovementRequired, RangeKnown, InCurrentRange;
        public int? StandardCurrent, StandardPredicted, MoveCurrent, MovePredicted, PlannedType;
        public string Stamp { get { return string.Join("|", new object[] { Known, Surprise, Overused, OutOfRange, TargetMatches,
            PredictionsPending, PredictedAttack, RequestedFull, MovementRequired, RangeKnown, InCurrentRange,
            StandardCurrent, StandardPredicted, MoveCurrent, MovePredicted, PlannedType }); } }
    }
    public sealed class AttackActionPlan
    {
        public bool Available, Excluded, FullAttack;
        public string Reason;
        public AttackActionInput Input;
    }
    public static class AttackActionPolicy
    {
        // Values are copied from the supplied game's CombatAction.ActivityState:
        // Available=0, WillBeUsed=1, WillBeLost=2, Unable=3, Lost=4, Used=5.
        public static AttackActionPlan Decide(AttackActionInput input)
        {
            var p = new AttackActionPlan { Input = input, Reason = "행동 상태 판독 대기" };
            if (!input.Known || !input.TargetMatches) return p;
            if (Valid(input.StandardCurrent) && input.StandardCurrent != 0)
            { p.Excluded = true; p.Reason = "일반 행동 소모/제한"; return p; }
            if (input.PredictionsPending && !input.PredictedAttack) return p;
            // Overused describes the selected/planned sequence, not every possible
            // attack. An ordinary in-range standard attack can still fit when
            // an unrelated/overlong movement prediction fails.
            if (input.Overused)
            {
                if (input.StandardCurrent == 0 && input.RangeKnown && input.InCurrentRange && !input.MovementRequired
                    && !input.OutOfRange && !input.PredictedAttack && (!input.PlannedType.HasValue || input.PlannedType == 1))
                {
                    p.Available = true; p.Reason = "단일 공격 추정 · 선택 행동 대신 남은 표준 행동 기준"; return p;
                }
                p.Excluded = true;
                p.Reason = input.StandardCurrent >= 3 && input.MoveCurrent >= 3 ? "현재 턴의 공격 행동 소모/제한"
                    : "선택한 행동을 현재 턴에 실행할 수 없음";
                return p;
            }
            if (input.OutOfRange) { p.Excluded = true; p.Reason = "현재 행동으로 대상에 도달/공격 불가"; return p; }
            if (!Valid(input.StandardCurrent)) return p;
            bool standard = input.StandardCurrent == 0;
            bool move = input.MoveCurrent == 0;
            if (!standard && !move) { p.Excluded = true; p.Reason = "공격에 쓸 수 있는 행동 소모/제한"; return p; }
            if (input.Surprise && !standard)
            { p.Excluded = true; p.Reason = "서프라이즈: 표준 행동 소모/제한"; return p; }
            if (input.Surprise && input.MovementRequired)
            { p.Excluded = true; p.Reason = "서프라이즈: 이동 필요 · 이동과 일반 공격 동시 불가"; return p; }
            if (input.PredictedAttack)
            {
                if (input.PlannedType.HasValue && input.PlannedType != 2 && input.PlannedType != 3)
                { p.Reason = "특수 공격 행동 미지원"; return p; }
                p.Available = true;
                p.FullAttack = !input.Surprise && standard && move && !input.MovementRequired
                    && (input.PlannedType == 3 || !input.PlannedType.HasValue && input.RequestedFull);
                p.Reason = p.FullAttack ? "전력 공격 가능" : input.Surprise ? "서프라이즈: 단일 공격 가능" : "단일 공격 가능";
                return p;
            }
            // Only infer an ordinary standard attack when current availability,
            // matching cached approach geometry and no contradictory prediction agree.
            if (input.PlannedType.HasValue && input.PlannedType != 2 && input.PlannedType != 3)
            { p.Reason = "특수 공격 행동 미지원"; return p; }
            if (standard && (input.StandardPredicted == null || input.StandardPredicted == 0)
                && input.RangeKnown && input.InCurrentRange && !input.MovementRequired)
            {
                p.Available = true;
                p.Reason = input.Surprise ? "서프라이즈: 단일 공격 추정 (표준 행동·사거리 기준)" : "단일 공격 추정 (표준 행동·사거리 기준)";
            }
            else p.Reason = "공격 예측 대기 · 남은 행동과 대상 사거리 재확인 필요";
            return p;
        }
        static bool Valid(int? value) { return value.HasValue && value >= 0 && value <= 5; }
    }
}
