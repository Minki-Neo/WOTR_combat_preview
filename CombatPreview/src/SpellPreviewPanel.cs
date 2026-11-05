using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;

namespace CombatPreview
{
    partial class HoverChance
    {
        AbilityData panelSpellAbility;
        readonly List<KeyValuePair<string, List<string>>> spellRows = new List<KeyValuePair<string, List<string>>>();
        bool spellArea;
        void UpdateSpellPanel(AbilityData ability, UnitEntityData selected)
        {
            var caster = SpellPreviewRuntime.Caster(ability);
            if (caster == null || caster != selected) { Hide(); return; }
            bool changed = panelSpellAbility != ability;
            if (changed) { nextRefresh = 0; panelScroll = Vector2.zero; }
            if (!changed && OverPanel() && spellRows.Count > 0) { panelHeld = true; return; }
            if (panelHeld) nextRefresh = 0;
            panelHeld = false;
            if (!changed && Time.unscaledTime < nextRefresh) return;
            var targets = SpellPreviewRuntime.Targets(ability);
            spellArea = targets.Length > 0;
            if (targets.Length == 0)
            {
                var hover = Game.Instance.State.Units.FirstOrDefault(u => u.View != null && u.View.MouseHighlighted) ?? hovered;
                if (hover != null && hover.IsVisibleForPlayer && !hover.Descriptor.State.IsDead) targets = new[] { hover };
            }
            if (targets.Length == 0) { Hide(); return; }
            BetaRuntime.InvalidateDisplayed();
            panelSpellAbility = ability; panelSnapshot = null; panelReason = null; panelAttacks.Clear();
            m_Target = targets[0]; attacker = caster; panelHeld = false; retention.Clear();
            spellRows.Clear();
            foreach (var target in targets.Take(32))
            {
                List<string> values;
                try { values = SpellPreviewRuntime.Describe(ability, target); }
                catch (NotSupportedException e) { values = new List<string> { e.Message }; }
                catch (Exception e) { Main.Error("Spell preview unavailable", e); values = new List<string> { "주문 정보 판독 불가 · " + e.GetType().Name }; }
                Main.Diagnostics?.SpellShown(new DiagnosticContext(BetaIdentity.Id(caster), BetaIdentity.Id(target), BetaIdentity.Id(ability),
                    BetaIdentity.Id(Game.Instance.TurnBasedCombatController.CurrentTurn), "unknown", 0, "", false), ability.Blueprint.Name, values, spellArea);
                spellRows.Add(new KeyValuePair<string, List<string>>(target.CharacterName + (target.IsPlayersEnemy ? " · 적" : " · 아군/중립"), values));
            }
            nextRefresh = Time.unscaledTime + .15f;
        }
        void DrawSpellPanel()
        {
            GUILayout.Label("주문 판정 · " + panelSpellAbility.Blueprint.Name, titleStyle);
            if (panelHeld) GUILayout.Label("보관 중 · 패널에 들어오기 전 조준 대상의 기록", mutedStyle);
            GUILayout.Label(spellArea ? "게임이 방금 범위 대상으로 판정한 유닛 (최대 32) · 대상별 조건부 확률" : "커서 대상만 표시 · 사거리/범위 포함 여부 미확인", mutedStyle);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            foreach (var entry in spellRows)
            {
                GUILayout.BeginVertical(cardStyle);
                GUILayout.Label(entry.Key, numberStyle);
                foreach (var value in entry.Value) GUILayout.Label(value, textStyle);
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("~ 기본 판정 추정 · 분기 내성은 후보 · 효과 면역/조건부 내성/재굴림/성공 시 잔여 효과는 별도", mutedStyle);
            GUILayout.Label("내성 실패 ≠ 디버프 최종 적용률 · 범위 합산 성공률은 표시하지 않음", mutedStyle);
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
        }
    }
}
