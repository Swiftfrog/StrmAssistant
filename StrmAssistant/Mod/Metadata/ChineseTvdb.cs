using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
            if (_tvdbAssembly is null)
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
        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static bool GetTranslationPrefix(ref List<object> translations, ref string[] tvdbLanguages, int field,
            ref bool defaultToFirst)
        {
            if (translations != null && translations.Count > 0)
            {
                if (field == 0)
                {
                    translations.RemoveAll(t =>
                        t != null && bool.TryParse(Traverse.Create(t).Property("isAlias")?.GetValue()?.ToString(),
                            out var isAlias) && isAlias);
                }

                if (HasTvdbJapaneseFallback())
                {
                    var considerJapanese = translations.Where(t => t != null).Any(t =>
                    {
                        var tran = Traverse.Create(t);
                        var language = tran.Property("language")?.GetValue()?.ToString();
                        var isPrimary = tran.Property("IsPrimary")?.GetValue() as bool?;

                        return language == "jpn" && isPrimary is true;
                    });

                    tvdbLanguages = tvdbLanguages.Where(l =>
                        considerJapanese || !string.Equals(l, "jpn", StringComparison.OrdinalIgnoreCase)).ToArray();
                }

                if (field == 0)
                {
                    var cnLanguages = new HashSet<string> { "zho", "zhtw", "yue" };
                    var trans = translations;
                    Array.Sort(tvdbLanguages, (lang1, lang2) =>
                    {
                        if (lang1 is null && lang2 is null) return 0;
                        if (lang1 is null) return 1;
                        if (lang2 is null) return -1;

                        var tran1 = trans.FirstOrDefault(t =>
                            Traverse.Create(t).Property("language")?.GetValue()?.ToString() == lang1);
                        var tran2 = trans.FirstOrDefault(t =>
                            Traverse.Create(t).Property("language")?.GetValue()?.ToString() == lang2);

                        var name1 = Traverse.Create(tran1)?.Property("name")?.GetValue()?.ToString();
                        var name2 = Traverse.Create(tran2)?.Property("name")?.GetValue()?.ToString();

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
                }

                var languageOrder = tvdbLanguages.Select((l, index) => (l, index))
                    .ToDictionary(x => x.l, x => x.index);

                translations.Sort((t1, t2) =>
                {
                    if (t1 is null) return 1;
                    if (t2 is null) return -1;

                    var language1 = Traverse.Create(t1).Property("language")?.GetValue()?.ToString();
                    var language2 = Traverse.Create(t2).Property("language")?.GetValue()?.ToString();

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
        private static void GetTranslationPostfix(List<object> translations, string[] tvdbLanguages, int field,
            bool defaultToFirst, ref object __result)
        {
            if (__result != null && !defaultToFirst)
            {
                var traverseResult = Traverse.Create(__result);
                var nameProperty = traverseResult.Property("name");
                var overviewProperty = traverseResult.Property("overview");

                if (nameProperty != null && overviewProperty != null)
                {
                    var name = nameProperty.GetValue()?.ToString();

                    switch (field)
                    {
                        case 0:
                        {
                            if (IsChinese(name))
                            {
                                nameProperty.SetValue(ConvertTraditionalToSimplified(name));
                            }
                            else if (BlockTvdbNonFallbackLanguage(name))
                            {
                                nameProperty.SetValue(null);
                            }

                            break;
                        }
                        case 1:
                        {
                            var overview = overviewProperty.GetValue()?.ToString();

                            if (IsChinese(overview))
                            {
                                overview = ConvertTraditionalToSimplified(overview);
                                overviewProperty.SetValue(overview);
                            }
                            else if (BlockTvdbNonFallbackLanguage(overview))
                            {
                                overview = null;
                                overviewProperty.SetValue(null);
                            }

                            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(overview))
                            {
                                nameProperty.SetValue(overview);
                            }

                            break;
                        }
                    }
                }
            }
        }

        [HarmonyPostfix]
        private static void GetTvdbSeasonPostfix(SeasonInfo id, IDirectoryService directoryService,
            CancellationToken cancellationToken, Task __result)
        {
            object tvdbSeason = null;

            try
            {
                tvdbSeason = Traverse.Create(__result).Property("Result")?.GetValue();
            }
            catch
            {
                // ignored
            }

            if (tvdbSeason != null)
            {
                var nameProperty = Traverse.Create(tvdbSeason).Property("name");

                if (nameProperty != null)
                {
                    var name = nameProperty.GetValue()?.ToString();

                    if (IsChinese(name))
                    {
                        nameProperty.SetValue(ConvertTraditionalToSimplified(name));
                    }
                    else if (id.IndexNumber.HasValue &&
                             (string.IsNullOrEmpty(name) || BlockTvdbNonFallbackLanguage(name)))
                    {
                        nameProperty.SetValue($"第 {id.IndexNumber} 季");
                    }
                }
            }
        }

        [HarmonyPostfix]
        private static void FindEpisodePostfix(object data, EpisodeInfo searchInfo, int? seasonNumber,
            ref object __result)
        {
            if (__result != null)
            {
                var traverseResult = Traverse.Create(__result);
                var nameProperty = traverseResult.Property("name");
                var overviewProperty = traverseResult.Property("overview");

                if (nameProperty != null && overviewProperty != null)
                {
                    var name = nameProperty.GetValue()?.ToString();
                    var overview = overviewProperty.GetValue()?.ToString();

                    var considerJapanese = HasTvdbJapaneseFallback() && (IsJapanese(name) || IsJapanese(overview));
                    ConsiderJapanese.Value = considerJapanese;

                    if (!considerJapanese)
                    {
                        if (!IsChinese(name)) nameProperty.SetValue(null);
                        if (!IsChinese(overview)) overviewProperty.SetValue(null);
                    }
                    else
                    {
                        if (!IsChineseJapanese(name)) nameProperty.SetValue(null);
                        if (!IsChineseJapanese(overview)) overviewProperty.SetValue(null);
                    }
                }
            }
        }

        [HarmonyPostfix]
        private static void GetEpisodeDataPostfix(EpisodeInfo searchInfo, bool fillExtendedInfo,
            IDirectoryService directoryService, CancellationToken cancellationToken, Task __result)
        {
            object tvdbEpisode = null;

            try
            {
                var taskResult = Traverse.Create(__result).Property("Result")?.GetValue();
                tvdbEpisode = Traverse.Create(taskResult)?.Property("Item1")?.GetValue();
            }
            catch
            {
                // ignored
            }

            if (tvdbEpisode != null)
            {
                var traverseTvdbEpisode = Traverse.Create(tvdbEpisode);
                var nameProperty = traverseTvdbEpisode.Property("name");
                var overviewProperty = traverseTvdbEpisode.Property("overview");

                if (nameProperty != null && overviewProperty != null)
                {
                    var name = nameProperty.GetValue()?.ToString();
                    var overview = overviewProperty.GetValue()?.ToString();

                    if (IsChinese(name))
                    {
                        nameProperty.SetValue(ConvertTraditionalToSimplified(name));
                    }
                    else if (searchInfo.IndexNumber.HasValue &&
                             (string.IsNullOrEmpty(name) || BlockTvdbNonFallbackLanguage(name)))
                    {
                        nameProperty.SetValue($"第 {searchInfo.IndexNumber} 集");
                    }

                    if (IsChinese(overview))
                    {
                        overviewProperty.SetValue(ConvertTraditionalToSimplified(overview));
                    }
                    else if (BlockTvdbNonFallbackLanguage(overview))
                    {
                        overviewProperty.SetValue(null);
                    }
                }
            }
        }
    }
}
