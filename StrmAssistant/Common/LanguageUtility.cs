using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ToolGood.Words;

namespace StrmAssistant.Common
{
    public static class LanguageUtility
    {
        private static readonly Regex EnglishRegex = new Regex(@"^[\x00-\x7F]+$", RegexOptions.Compiled);
        private static readonly Regex ChineseRegex = new Regex(@"[\u4E00-\u9FFF]", RegexOptions.Compiled);
        private static readonly Regex JapaneseRegex = new Regex(@"[\u3040-\u30FF]", RegexOptions.Compiled);
        private static readonly Regex KoreanRegex = new Regex(@"[\uAC00-\uD7A3]", RegexOptions.Compiled);
        private static readonly Regex DefaultEnglishEpisodeNameRegex = new Regex(@"^Episode\s*\d+$", RegexOptions.Compiled);
        private static readonly Regex DefaultChineseEpisodeNameRegex = new Regex(@"^第\s*\d+\s*集$", RegexOptions.Compiled);
        private static readonly Regex DefaultJapaneseEpisodeNameRegex =
            new Regex(@"^(第\s*\d+\s*話|エピソード\s*\d+)$", RegexOptions.Compiled);
        private static readonly Regex DefaultChineseCollectionNameRegex = new Regex(@"（系列）$", RegexOptions.Compiled);
        private static readonly Regex CleanPersonNameRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex CleanEpisodeNameRegex =
            new Regex(
                @"(?:[Ss]\d+[Ee]\d+(?:[Ee]\d+)*|[Ss]\d+[xX]\d+(?:[xX]\d+)*|[Ss]\d+[-_]\d+(?:[-_]\d+)*|第\s*[0-9一二三四五六七八九十百]+\s*集)",
                RegexOptions.Compiled);

        public static readonly string[] MovieDbFallbackLanguages = { "zh-CN", "zh-SG", "zh-HK", "zh-TW", "ja-JP" };
        public static readonly string[] TvdbFallbackLanguages = { "zho", "zhtw", "yue", "jpn" };

        public static bool IsEnglish(string input) => !string.IsNullOrEmpty(input) && EnglishRegex.IsMatch(input);

        public static bool IsChinese(string input) => !string.IsNullOrEmpty(input) && ChineseRegex.IsMatch(input);

        public static bool IsJapanese(string input) =>
            !string.IsNullOrEmpty(input) && JapaneseRegex.IsMatch(input.Replace("\u30FB", string.Empty));

        public static bool IsChineseNoJapanese(string input) =>
            !string.IsNullOrEmpty(input) && ChineseRegex.IsMatch(input) &&
            !JapaneseRegex.IsMatch(input.Replace("\u30FB", string.Empty));

        public static bool IsChineseJapanese(string input) =>
            !string.IsNullOrEmpty(input) && (ChineseRegex.IsMatch(input) ||
                                             JapaneseRegex.IsMatch(input.Replace("\u30FB", string.Empty)));

        public static bool IsKorean(string input) => !string.IsNullOrEmpty(input) && KoreanRegex.IsMatch(input);
        
        public static bool IsDefaultEnglishEpisodeName(string input) =>
            !string.IsNullOrEmpty(input) && DefaultEnglishEpisodeNameRegex.IsMatch(input);

        public static bool IsDefaultChineseEpisodeName(string input) =>
            !string.IsNullOrEmpty(input) && DefaultChineseEpisodeNameRegex.IsMatch(input);

        public static bool IsDefaultJapaneseEpisodeName(string input) =>
            !string.IsNullOrEmpty(input) && DefaultJapaneseEpisodeNameRegex.IsMatch(input);

        public static string ConvertTraditionalToSimplified(string input)
        {
            return WordsHelper.ToSimplifiedChinese(input);
        }

        public static string GetLanguageByTitle(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            return IsJapanese(input) ? "ja" : IsKorean(input) ? "ko" : IsChinese(input) ? "zh" : "en";
        }

        public static string ConvertToPinyinInitials(string input, bool isName)
        {
            return GetFirstPinyin(input, isName);
        }

        private static string GetFirstPinyin(string text, bool isName = false)
        {
            var pinyinList = isName
                ? (IEnumerable<string>)WordsHelper.GetPinyinListForName(text)
                : WordsHelper.GetPinyinList(text);

            var sb = new StringBuilder(text.Length);

            foreach (var pinyin in pinyinList)
            {
                if (pinyin[0] <= '\u0080')
                    sb.Append(pinyin[0]);
                else
                    sb.Append(pinyin);
            }

            return sb.ToString();
        }

        public static string RemoveDefaultCollectionName(string input)
        {
            return string.IsNullOrEmpty(input) ? input : DefaultChineseCollectionNameRegex.Replace(input, "").Trim();
        }

        public static string CleanPersonName(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            if (IsChineseJapanese(input) || IsKorean(input))
            {
                return CleanPersonNameRegex.Replace(input, "");
            }

            return input.Trim();
        }

        public static string CleanEpisodeName(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            return CleanEpisodeNameRegex.Replace(input, "");
        }

        public static List<string> GetMovieDbFallbackLanguages()
        {
            var currentFallbackLanguages = Plugin.Instance.MetadataEnhanceStore.GetOptions().FallbackLanguages;

            if (string.IsNullOrWhiteSpace(currentFallbackLanguages)) return new List<string>();

            var languages = currentFallbackLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries);

            return MovieDbFallbackLanguages
                .Where(l => languages.Contains(l, StringComparer.OrdinalIgnoreCase))
                .Select(l => l.ToLowerInvariant())
                .ToList();
        }

        public static bool HasMovieDbJapaneseFallback()
        {
            var currentFallbackLanguages = Plugin.Instance.MetadataEnhanceStore.GetOptions().FallbackLanguages;

            return !string.IsNullOrWhiteSpace(currentFallbackLanguages) &&
                   currentFallbackLanguages.IndexOf("ja-jp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool BlockMovieDbNonFallbackLanguage(string input)
        {
            return !string.IsNullOrEmpty(input) &&
                   Plugin.Instance.MetadataEnhanceStore.GetOptions().BlockNonFallbackLanguage &&
                   (!HasMovieDbJapaneseFallback() || !IsJapanese(input));
        }

        public static List<string> GetTvdbFallbackLanguages()
        {
            var currentFallbackLanguages = Plugin.Instance.MetadataEnhanceStore.GetOptions().TvdbFallbackLanguages;

            if (string.IsNullOrWhiteSpace(currentFallbackLanguages)) return new List<string>();

            var languages = currentFallbackLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries);

            return TvdbFallbackLanguages
                .Where(l => languages.Contains(l, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        public static bool HasTvdbJapaneseFallback()
        {
            var currentFallbackLanguages = Plugin.Instance.MetadataEnhanceStore.GetOptions().TvdbFallbackLanguages;

            return !string.IsNullOrWhiteSpace(currentFallbackLanguages) &&
                   currentFallbackLanguages.IndexOf("jpn", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool BlockTvdbNonFallbackLanguage(string input)
        {
            return !string.IsNullOrEmpty(input) &&
                   Plugin.Instance.MetadataEnhanceStore.GetOptions().BlockNonFallbackLanguage &&
                   (!HasTvdbJapaneseFallback() || !IsJapanese(input));
        }
    }
}
