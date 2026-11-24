using System;
using System.Collections.Generic;
using System.Linq;
namespace CombatPreview
{
    internal static class PreviewIssuePresentation
    {
        static string Text(Dictionary<string, object> data, string key)
            => data.TryGetValue(key, out var value) ? value as string : null;
        internal static Dictionary<string, object> Build(Dictionary<string, object> metadata, IEnumerable<SpellCheckRecord> checks)
        {
            if (!checks.Any(check => check.Status == "Unavailable")) return null;
            string code = Text(metadata, "parameterFallbackReason");
            if (string.IsNullOrEmpty(code)) code = Text(metadata, "automaticMetamagicStatus");
            if (string.IsNullOrEmpty(code) || code == "Resolved") code = Text(metadata, "rodMetamagicStatus");
            if (string.IsNullOrEmpty(code) || code == "Resolved")
            {
                string item = Text(metadata, "itemParameterStatus");
                if (item != "ReadOnlyCalculated") code = item;
            }
            if (string.IsNullOrEmpty(code) || code == "Resolved")
                code = checks.First(check => check.Status == "Unavailable").Reason ?? "ProbabilityInputUnavailable";
            string sourceType = code.Contains(":") ? code.Substring(code.IndexOf(':') + 1) : null;
            string summary = code.StartsWith("UnhandledParameterHandler:") ? "주문 보정 효과 미지원"
                : code.StartsWith("AutoMetamagic") || code.StartsWith("ItemAutoMetamagic") ? "자동 메타매직 조건 미확인"
                : code.StartsWith("Rod") ? "메타매직 로드 조건 미확인"
                : code.StartsWith("NegativeLevel") ? "음수 레벨 스택 미확인"
                : code.Contains("SpellLevel") ? "주문 레벨 미확인"
                : code.Contains("CasterLevel") ? "시전자 레벨/관통 보정 미확인"
                : code.Contains("Inherited") || code.Contains("Context") ? "효과 출처의 주문 수치 미확인"
                : code == "StateChanged" ? "상태 변경 후 주문 수치 재계산 대기"
                : "판정에 필요한 입력 미확인";
            var needed = checks.Where(check => check.Status == "Unavailable").Select(check => check.Kind).Distinct().ToArray();
            return new Dictionary<string, object> { { "code", code }, { "sourceType", sourceType }, { "summary", summary },
                { "unavailableChecks", needed }, { "zeroProbability", false } };
        }
    }
}
