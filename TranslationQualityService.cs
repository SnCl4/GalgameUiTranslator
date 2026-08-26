using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GalgameUiTranslator
{
    public sealed class TranslationQualityFinding
    {
        public PreflightSeverity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public static class TranslationQualityService
    {
        private static readonly Regex ProtectedTokenPattern = new Regex(
            @"%(?:\d+\$)?[-+#0 ']*\d*(?:\.\d+)?[A-Za-z%]|\{[^{}\r\n]+\}|\\[A-Za-z]+(?:\[[^\]\r\n]*\])?|<[/!]?[A-Za-z][^>\r\n]*>|\[[A-Za-z][^\]\r\n]*\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NumberPattern = new Regex(
            @"\d+(?:[.,]\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<TranslationQualityFinding> Analyze(
            TextRegion region,
            IEnumerable<GlossaryEntry> glossary)
        {
            var findings = new List<TranslationQualityFinding>();
            if (region == null || string.IsNullOrWhiteSpace(region.Translation)) return findings;

            var source = Normalize(region.SourceText);
            var translation = Normalize(region.Translation);
            if (source.Length > 0 && string.Equals(source, translation, StringComparison.Ordinal))
            {
                Add(findings, PreflightSeverity.Warning, "TRANSLATION_UNCHANGED",
                    "译文与原文完全相同，请确认是否漏译。");
            }

            if (ContainsJapaneseKana(translation))
            {
                Add(findings, PreflightSeverity.Warning, "JAPANESE_REMAINS",
                    "译文仍包含平假名、片假名或日文迭代符号，请检查是否有漏译。 ");
            }

            var sourceTokens = ExtractMultiset(ProtectedTokenPattern, source);
            var translatedTokens = ExtractMultiset(ProtectedTokenPattern, translation);
            if (!MultisetsEqual(sourceTokens, translatedTokens))
            {
                Add(findings, PreflightSeverity.Error, "PROTECTED_TOKEN_MISMATCH",
                    "占位符、转义序列或脚本标签与原文不一致。原文：" +
                    FormatTokens(sourceTokens) + "；译文：" + FormatTokens(translatedTokens) + "。 ");
            }

            var sourceNumbers = ExtractMultiset(NumberPattern, source);
            var translatedNumbers = ExtractMultiset(NumberPattern, translation);
            if (!MultisetsEqual(sourceNumbers, translatedNumbers))
            {
                Add(findings, PreflightSeverity.Error, "NUMBER_MISMATCH",
                    "译文中的数字与原文不一致。原文：" + FormatTokens(sourceNumbers) +
                    "；译文：" + FormatTokens(translatedNumbers) + "。 ");
            }

            foreach (var entry in glossary ?? Enumerable.Empty<GlossaryEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Source) ||
                    string.IsNullOrWhiteSpace(entry.Translation)) continue;
                var sourceTerm = Normalize(entry.Source);
                if (source.IndexOf(sourceTerm, StringComparison.Ordinal) < 0) continue;
                var expected = Normalize(entry.Translation);
                if (translation.IndexOf(expected, StringComparison.Ordinal) >= 0) continue;
                Add(findings, PreflightSeverity.Warning, "GLOSSARY_MISMATCH",
                    $"术语“{entry.Source}”应优先译为“{entry.Translation}”，当前译文未采用该译法。 ");
            }

            var sourceLength = CountVisibleCharacters(source);
            var translationLength = CountVisibleCharacters(translation);
            if (sourceLength >= 4 &&
                (translationLength > Math.Max(sourceLength * 3, sourceLength + 12) ||
                 translationLength * 4 < sourceLength))
            {
                Add(findings, PreflightSeverity.Info, "LENGTH_RATIO_SUSPICIOUS",
                    $"原文与译文长度差异较大（{sourceLength} → {translationLength} 个字符），建议人工复核。 ");
            }

            return findings;
        }

        public static bool ContainsJapaneseKana(string value)
        {
            foreach (var character in value ?? string.Empty)
            {
                if ((character >= '\u3040' && character <= '\u30ff') ||
                    (character >= '\uff66' && character <= '\uff9f') ||
                    character == '\u3005' || character == '\u3006')
                    return true;
            }
            return false;
        }

        private static Dictionary<string, int> ExtractMultiset(Regex pattern, string value)
        {
            return pattern.Matches(value ?? string.Empty)
                .Cast<Match>()
                .Where(match => match.Success && match.Value.Length > 0)
                .GroupBy(match => match.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        }

        private static bool MultisetsEqual(
            IReadOnlyDictionary<string, int> left,
            IReadOnlyDictionary<string, int> right)
        {
            return left.Count == right.Count &&
                   left.All(pair => right.TryGetValue(pair.Key, out var count) && count == pair.Value);
        }

        private static string FormatTokens(IReadOnlyDictionary<string, int> tokens)
        {
            if (tokens.Count == 0) return "无";
            return string.Join("、", tokens.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value > 1 ? pair.Key + "×" + pair.Value : pair.Key));
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim()
                .Normalize(NormalizationForm.FormKC);
        }

        private static int CountVisibleCharacters(string value)
        {
            return (value ?? string.Empty).Count(character => !char.IsWhiteSpace(character));
        }

        private static void Add(
            ICollection<TranslationQualityFinding> findings,
            PreflightSeverity severity,
            string code,
            string message)
        {
            findings.Add(new TranslationQualityFinding
            {
                Severity = severity,
                Code = code,
                Message = message
            });
        }
    }
}
