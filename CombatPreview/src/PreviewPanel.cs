using System.Linq;
using UnityEngine;

namespace CombatPreview
{
    partial class HoverChance
    {
        Vector2 panelScroll;
        GUIStyle wrapped;
        void Label(string value)
        {
            if (wrapped == null) wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label(value, wrapped);
        }
        void OnGUI()
        {
            var settings = Main.Settings;
            if (!BetaRuntime.Enabled || settings == null) return;
            if (BetaRuntime.AlertVisible)
                GUI.Box(new Rect(30, 35, Mathf.Min(720, Screen.width - 60), 55), BetaRuntime.Alert);
            if (m_Target == null || (panelSnapshot == null && string.IsNullOrEmpty(panelReason))) return;
            float scale = Mathf.Clamp(settings.Scale, .7f, 1.6f);
            var old = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
                float width = Mathf.Min(Mathf.Clamp(settings.Width, 420, 850), Screen.width / scale);
                float height = Mathf.Min(settings.Detailed ? 610 : 430, Screen.height / scale);
                var rect = new Rect(Mathf.Clamp(settings.X, 0, Mathf.Max(0, Screen.width / scale - width)),
                    Mathf.Clamp(settings.Y, 0, Mathf.Max(0, Screen.height / scale - height)), width, height);
                rect = GUI.Window(739261, rect, DrawPanel, "Combat Preview | 읽기 전용 BETA · 부분 추정");
                settings.X = rect.x; settings.Y = rect.y;
            }
            finally { GUI.matrix = old; }
        }
        void DrawPanel(int id)
        {
            Label(attacker.CharacterName + " → " + m_Target.CharacterName);
            Label(targetHealth ?? "대상 체력: 확인 불가");
            if (panelSnapshot == null)
            {
                Label("계산 불가: " + panelReason); GUI.DragWindow(new Rect(0, 0, 10000, 24)); return;
            }
            var snapshot = panelSnapshot;
            Label(m_Full ? "기본 주무기 반복 모델 (실제 풀어택 전체 아님)" : "기본 주무기 단타 모델");
            Label("PARTIAL / RAW: DR·저항·면역·보호막 적용 후 피해는 계산 불가");
            if (snapshot.Input.DefensePartPresent) Label("대상에 방어 관련 파트가 있습니다. 적용 후 피해 미지원; 실제 이벤트를 기록합니다.");
            if (snapshot.Rows.All(r => r.ExpectedRaw.HasValue))
                Label(string.Format("기본 모델의 방어 전 기대 피해 합계: {0:0.00}", snapshot.Rows.Sum(r => r.ExpectedRaw.Value)));
            else Label("피해 유형 미지원: 기대 피해 계산 불가");
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            foreach (var row in snapshot.Rows)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                Label(string.Format("#{0}  AB {1:+0;-0;0} vs 일반 AC {2} | 기본 명중 {3:P0}", row.Index + 1, row.AB, row.AC, row.Hit));
                if (row.ExpectedRaw.HasValue)
                    Label(string.Format("기본 RAW E {0:0.00} | 명중 피해 {1} / {2:0.00} / {3}",
                        row.ExpectedRaw.Value, row.Normal.Value.Min, row.Normal.Value.Mean, row.Normal.Value.Max));
                if (Main.Settings.Detailed)
                {
                    Label(string.Format("{0}d{1}{2:+0;-0;0} | 치명 위협 {3}–20/x{4} | 기본 확정 치명 {5:P1}",
                        snapshot.Input.Rolls, snapshot.Input.Sides, snapshot.Input.Bonus, snapshot.Input.CriticalEdge, snapshot.Input.Multiplier, row.Crit));
                    Label("AB = BAB + 기본 명중 능력치 + 저장된 추가 AB − 기본 반복 페널티");
                }
                if (Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed && snapshot.Input.DamageKnown)
                {
                    var term = new DamageTerm(snapshot.Input.Rolls, snapshot.Input.Sides, snapshot.Input.Bonus, true) { Kind = DamageKind.Physical };
                    var manual = MitigationMath.Calculate(new[] { term }, snapshot.Input.Multiplier, row.Hit, row.Crit, DefenseReader.Scenario(Main.Settings));
                    if (manual.Available) Label(string.Format("수동 물리 시나리오 E {0:0.00} (실제 대상 방어 아님)", manual.Expected));
                    else Label("수동 시나리오 계산 불가: " + manual.Reason);
                }
                GUILayout.EndVertical();
            }
            foreach (var note in snapshot.Notes.Take(Main.Settings.Detailed ? snapshot.Notes.Count : 3)) Label(note);
            GUILayout.EndScrollView();
            Label("표시 모델과 실제 AB/AC/무기 구성의 차이는 참고 로그입니다. 한 번의 피해 ≠ 기대값 오류.");
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
            if (Event.current.type == EventType.Repaint) BetaRuntime.Shown(snapshot);
        }
    }
}
