using System.Linq;
using UnityEngine;

namespace CombatPreview
{
    partial class HoverChance
    {
        sealed class SpellOutcomeRowView
        {
            internal SpellOutcomeRow Model;
            internal GUIContent Path, Chance, Effect;
        }
        sealed class SpellOutcomeTableView
        {
            internal SpellOutcomeTable Model;
            internal SpellOutcomeRowView[] Rows;
            internal GUIContent ProbabilityHeading, OutcomeHeading, Note, ChecksCaption;
            internal float CachedWidth = -1;
            internal float[] Heights;
            internal float HeaderHeight;
        }
        GUIStyle spellTableText, spellTableChance, spellTableUnknown, spellTableLethal;
        readonly GUIContent spellEffectHeading = new GUIContent("피해 · 상태 효과");

        static SpellOutcomeTableView PresentSpellTable(SpellOutcomeTable table) => new SpellOutcomeTableView {
            Model = table, ProbabilityHeading = new GUIContent(table.ProbabilityHeading), Note = new GUIContent(table.Note ?? ""),
            ChecksCaption = new GUIContent(table.ChecksCaption ?? ""),
            OutcomeHeading = new GUIContent(table.OutcomeHeading),
            Rows = table.Rows.Select(row => new SpellOutcomeRowView { Model = row, Path = new GUIContent(row.Path),
                Chance = new GUIContent(row.ChanceText), Effect = new GUIContent(row.EffectText) }).ToArray()
        };
        void EnsureSpellTableStyles()
        {
            if (spellTableText != null) return;
            spellTableText = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = true, clipping = TextClipping.Clip };
            spellTableChance = new GUIStyle(hitStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
            spellTableUnknown = new GUIStyle(spellTableChance);
            spellTableUnknown.normal.textColor = mutedStyle.normal.textColor;
            spellTableLethal = new GUIStyle(spellTableText) { fontStyle = FontStyle.Bold };
            spellTableLethal.normal.textColor = new Color(1f, .65f, .38f);
        }
        float SpellTableHeight(SpellOutcomeTableView table, float width)
        {
            EnsureSpellTableStyles();
            float chanceWidth = Mathf.Clamp(width * .19f, 72, 96);
            float pathWidth = (width - chanceWidth) * .43f;
            float effectWidth = width - chanceWidth - pathWidth;
            if (!Mathf.Approximately(table.CachedWidth, width))
            {
                table.CachedWidth = width;
                table.HeaderHeight = Mathf.Max(30, Mathf.Max(mutedStyle.CalcHeight(table.OutcomeHeading, pathWidth - 8),
                    Mathf.Max(mutedStyle.CalcHeight(table.ProbabilityHeading, chanceWidth), mutedStyle.CalcHeight(spellEffectHeading, effectWidth - 8))) + 4);
                table.Heights = table.Rows.Select(row => Mathf.Max(36,
                    Mathf.Max(spellTableText.CalcHeight(row.Path, pathWidth - 8),
                        (row.Model.Lethal ? spellTableLethal : spellTableText).CalcHeight(row.Effect, effectWidth - 8)) + 8)).ToArray();
            }
            return table.HeaderHeight + table.Heights.Sum() + (string.IsNullOrEmpty(table.Model.Note) ? 0 : mutedStyle.CalcHeight(table.Note, width) + 4)
                + (string.IsNullOrEmpty(table.Model.ChecksCaption) ? 0 : mutedStyle.CalcHeight(table.ChecksCaption, width) + 4);
        }
        void DrawSpellTable(SpellOutcomeTableView table)
        {
            float width = Mathf.Max(160, panelWidth - 56);
            SpellTableHeight(table, width);
            float chanceWidth = Mathf.Clamp(width * .19f, 72, 96);
            float pathWidth = (width - chanceWidth) * .43f;
            float effectWidth = width - chanceWidth - pathWidth;
            if (!string.IsNullOrEmpty(table.Model.ChecksCaption)) GUILayout.Label(table.ChecksCaption, mutedStyle);
            Rect header = GUILayoutUtility.GetRect(width, table.HeaderHeight, GUILayout.ExpandWidth(false));
            GUI.Label(new Rect(header.x + 4, header.y, pathWidth - 8, header.height), table.OutcomeHeading, mutedStyle);
            GUI.Label(new Rect(header.x + pathWidth, header.y, chanceWidth, header.height), table.ProbabilityHeading, mutedStyle);
            GUI.Label(new Rect(header.x + pathWidth + chanceWidth + 4, header.y, effectWidth - 8, header.height), spellEffectHeading, mutedStyle);
            for (int index = 0; index < table.Rows.Length; index++)
            {
                var row = table.Rows[index];
                Rect rect = GUILayoutUtility.GetRect(width, table.Heights[index], GUILayout.ExpandWidth(false));
                if (index % 2 == 0) GUI.DrawTexture(rect, cardTexture);
                GUI.Label(new Rect(rect.x + 4, rect.y + 2, pathWidth - 8, rect.height - 4), row.Path, spellTableText);
                GUI.Label(new Rect(rect.x + pathWidth, rect.y + 2, chanceWidth, rect.height - 4), row.Chance,
                    row.Model.Chance.HasValue && row.Model.Chance != 0 ? spellTableChance : spellTableUnknown);
                GUI.Label(new Rect(rect.x + pathWidth + chanceWidth + 4, rect.y + 2, effectWidth - 8, rect.height - 4), row.Effect,
                    row.Model.Lethal ? spellTableLethal : spellTableText);
            }
            if (!string.IsNullOrEmpty(table.Model.Note)) GUILayout.Label(table.Note, mutedStyle);
        }
    }
}
