using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tvdb;
using static StrmAssistant.Common.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.Tvdb;

namespace StrmAssistant.Mod.Metadata
{
    public class ChineseTvdb : PatchBase<ChineseTvdb>
    {
        private static readonly ThreadLocal<bool?> ConsiderJapanese = new ThreadLocal<bool?>();

        public ChineseTvdb()
        {
            Initialize();

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().ChineseTvdb)
            {
                Patch();
            }
        }

        protected override void OnInitialize()
        {
            if (!IsSupported)
            {
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                PatchTracker.IsSupported = false;
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _convertToTvdbLanguages,
                postfix: nameof(ConvertToTvdbLanguagesPostfix));
            PatchUnpatch(PatchTracker, apply, _getTranslation, prefix: nameof(GetTranslationPrefix),
                postfix: nameof(GetTranslationPostfix));
            PatchUnpatch(PatchTracker, apply, _addMovieInfo, postfix: nameof(AddInfoPostfix));
            PatchUnpatch(PatchTracker, apply, _addSeriesInfo, postfix: nameof(AddInfoPostfix));
            PatchUnpatch(PatchTracker, apply, _getTvdbSeason, postfix: nameof(GetTvdbSeasonPostfix));
            PatchUnpatch(PatchTracker, apply, _findEpisode, postfix: nameof(FindEpisodePostfix));
            PatchUnpatch(PatchTracker, apply, _getEpisodeData, postfix: nameof(GetEpisodeDataPostfix));
        }

        [HarmonyPostfix]
        private static void ConvertToTvdbLanguagesPostfix(ItemLookupInfo lookupInfo, ref string[] __result)
        {
            if (lookupInfo.MetadataLanguage?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) is true)
            {
                var list = __result.ToList();
                var index = list.FindIndex(l => string.Equals(l, "eng", StringComparison.OrdinalIgnoreCase));

                var currentFallbackLanguages = GetTvdbFallbackLanguages()
                    .Where(l => (ConsiderJapanese.Value ?? true) ||
                                !string.Equals(l, "jpn", StringComparison.OrdinalIgnoreCase));

                foreach (var fallbackLanguage in currentFallbackLanguages)
                {
                    if (!list.Contains(fallbackLanguage, StringComparer.OrdinalIgnoreCase))
                    {
                        if (index >= 0)
                        {
                            list.Insert(index, fallbackLanguage);
                            index++;
                        }
                        else
                        {
                            list.Add(fallbackLanguage);
                        }
                    }
                }

                __result = list.ToArray();
            }
        }

        [HarmonyPrefix]
        private static bool GetTranslationPrefix(ref List<NameTranslation> translations, ref string[] tvdbLanguages,
            TranslationField field, ref bool defaultToFirst)
        {
            if (translations != null && translations.Count > 0)
            {
                if (field == TranslationField.Name)
                {
                    translations.RemoveAll(t =>
                        t != null && bool.TryParse(t.isAlias?.ToString(), out var isAlias) && isAlias);
                }

                var tvdbLanguageList = tvdbLanguages.ToList();

                if (HasTvdbJapaneseFallback())
                {
                    var considerJapanese = translations.Any(t => t != null && t.language == "jpn" && t.IsPrimary);
                    tvdbLanguages = tvdbLanguageList.Where(l =>
                            considerJapanese || !string.Equals(l, "jpn", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                }

                if (field == TranslationField.Name)
                {
                    var cnLanguages = new HashSet<string> { "zho", "zhtw", "yue" };

                    var transByLang = translations.Where(t => t != null && !string.IsNullOrEmpty(t.language))
                        .GroupBy(t => t.language)
                        .ToDictionary(g => g.Key, g => g.First());

                    tvdbLanguageList.Sort((lang1, lang2) =>
                    {
                        if (lang1 is null && lang2 is null) return 0;
                        if (lang1 is null) return 1;
                        if (lang2 is null) return -1;

                        transByLang.TryGetValue(lang1, out var tran1);
                        transByLang.TryGetValue(lang2, out var tran2);

                        var name1 = tran1?.name;
                        var name2 = tran2?.name;

                        var cn1 = cnLanguages.Contains(lang1);
                        var cn2 = cnLanguages.Contains(lang2);

                        if (cn1 && cn2)
                        {
                            if (IsChinese(name1) && !IsChinese(name2)) return -1;
                            if (!IsChinese(name1) && IsChinese(name2)) return 1;
                            return 0;
                        }

                        if (cn1) return -1;
                        if (cn2) return 1;

                        return 0;
                    });
                    tvdbLanguages = tvdbLanguageList.ToArray();
                }

                var languageOrder = tvdbLanguageList.Select((l, index) => (l, index))
                    .ToDictionary(x => x.l, x => x.index);

                translations.Sort((t1, t2) =>
                {
                    if (t1 is null) return 1;
                    if (t2 is null) return -1;

                    var language1 = t1.language;
                    var language2 = t2.language;

                    var index1 = languageOrder.GetValueOrDefault(language1, int.MaxValue);
                    var index2 = languageOrder.GetValueOrDefault(language2, int.MaxValue);

                    return index1.CompareTo(index2);
                });
            }

            if (translations?.Count == 0) translations = null;

            return true;
        }

        [HarmonyPostfix]
        private static void AddInfoPostfix(MetadataResult<BaseItem> metadataResult)
        {
            var instance = metadataResult.Item;

            if (IsChinese(instance.Name))
            {
                instance.Name = ConvertTraditionalToSimplified(instance.Name);
            }

            if (IsChinese(instance.Overview))
            {
                instance.Overview = ConvertTraditionalToSimplified(instance.Overview);
            }
            else if (BlockTvdbNonFallbackLanguage(instance.Overview))
            {
                instance.Overview = null;
            }
        }

        [HarmonyPostfix]
        private static void GetTranslationPostfix(List<NameTranslation> translations, string[] tvdbLanguages, int field,
            bool defaultToFirst, ref NameTranslation __result)
        {
            if (__result != null && !defaultToFirst)
            {
                var name = __result.name;

                switch (field)
                {
                    case 0:
                    {
                        if (IsChinese(name))
                        {
                            __result.name = ConvertTraditionalToSimplified(name);
                        }
                        else if (BlockTvdbNonFallbackLanguage(name))
                        {
                            __result.name = null;
                        }

                        break;
                    }
                    case 1:
                    {
                        var overview = __result.overview;

                        if (IsChinese(overview))
                        {
                            overview = ConvertTraditionalToSimplified(overview);
                            __result.overview = overview;
                        }
                        else if (BlockTvdbNonFallbackLanguage(overview))
                        {
                            overview = null;
                            __result.overview = null;
                        }

                        if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(overview))
                        {
                            __result.name = overview;
                        }

                        break;
                    }
                }
            }
        }

        [HarmonyPostfix]
        private static void GetTvdbSeasonPostfix(SeasonInfo id, IDirectoryService directoryService,
            Task<TvdbSeason> __result)
        {
            TvdbSeason tvdbSeason = null;

            try
            {
                tvdbSeason = __result?.Result;
            }
            catch
            {
                // ignored
            }

            if (tvdbSeason != null)
            {
                var name = tvdbSeason.name;

                if (IsChinese(name))
                {
                    tvdbSeason.name = ConvertTraditionalToSimplified(name);
                }
                else if (id.IndexNumber.HasValue && (string.IsNullOrEmpty(name) || BlockTvdbNonFallbackLanguage(name)))
                {
                    tvdbSeason.name = $"第 {id.IndexNumber} 季";
                }
            }
        }

        [HarmonyPostfix]
        private static void FindEpisodePostfix(EpisodesData data, EpisodeInfo searchInfo, int? seasonNumber,
            TvdbEpisode __result)
        {
            if (__result != null)
            {
                var name = __result.name;
                var overview = __result.overview;

                var considerJapanese = HasTvdbJapaneseFallback() && (IsJapanese(name) || IsJapanese(overview));
                ConsiderJapanese.Value = considerJapanese;

                if (!considerJapanese)
                {
                    if (!IsChinese(name)) __result.name = null;
                    if (!IsChinese(overview)) __result.overview = null;
                }
                else
                {
                    if (!IsChineseJapanese(name)) __result.name = null;
                    if (!IsChineseJapanese(overview)) __result.overview = null;
                }
            }
        }

        [HarmonyPostfix]
        private static void GetEpisodeDataPostfix(EpisodeInfo searchInfo, bool fillExtendedInfo,
            IDirectoryService directoryService, Task<Tuple<TvdbEpisode, List<EpisodesData>>> __result)
        {
            TvdbEpisode tvdbEpisode = null;

            try
            {
                tvdbEpisode = __result?.Result?.Item1;
            }
            catch
            {
                // ignored
            }

            if (tvdbEpisode != null)
            {
                var name = tvdbEpisode.name;
                var overview = tvdbEpisode.overview;

                if (IsChinese(name))
                {
                    tvdbEpisode.name = ConvertTraditionalToSimplified(name);
                }
                else if (searchInfo.IndexNumber.HasValue &&
                         (string.IsNullOrEmpty(name) || BlockTvdbNonFallbackLanguage(name)))
                {
                    tvdbEpisode.name = $"第 {searchInfo.IndexNumber} 集";
                }

                if (IsChinese(overview))
                {
                    tvdbEpisode.overview = ConvertTraditionalToSimplified(overview);
                }
                else if (BlockTvdbNonFallbackLanguage(overview))
                {
                    tvdbEpisode.overview = null;
                }
            }
        }
    }
}
