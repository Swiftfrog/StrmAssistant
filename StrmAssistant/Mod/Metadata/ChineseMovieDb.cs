using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using StrmAssistant.Common;
using StrmAssistant.ScheduledTask;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Common.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.MovieDb;

namespace StrmAssistant.Mod.Metadata
{
    public class ChineseMovieDb : PatchBase<ChineseMovieDb>
    {
        private static readonly object _lock = new object();

        public ChineseMovieDb()
        {
            Initialize();

            PatchCacheTime();

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().ChineseMovieDb)
            {
                Patch();
            }
        }

        protected override void OnInitialize()
        {
            if (_movieDbAssembly != null)
            {
                ReversePatch(PatchTracker, _getTitleMovieData, nameof(MovieGetTitleStub));
                ReversePatch(PatchTracker, _mapLanguageToProviderLanguage, nameof(MapLanguageToProviderLanguageStub));
                ReversePatch(PatchTracker, _getTitleSeriesInfo, nameof(SeriesGetTitleStub));
            }
            else
            {
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                PatchTracker.IsSupported = false;
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _getMovieDbMetadataLanguages, postfix: nameof(MetadataLanguagesPostfix));
            PatchUnpatch(PatchTracker, apply, _getImageLanguagesParam, prefix: nameof(GetImageLanguagesParamPrefix));

            PatchUnpatch(PatchTracker, apply, _movieGetMetadata, prefix: nameof(MovieGetMetadataPrefix));
            if (!apply)
            {
                PatchUnpatch(Instance.PatchTracker, false, _genericProcessMainInfoMovie,
                    prefix: nameof(ProcessMainInfoMoviePrefix));
                PatchUnpatch(Instance.PatchTracker, false, _genericIsCompleteMovie,
                    prefix: nameof(IsCompletePrefix), postfix: nameof(IsCompletePostfix));
            }

            PatchUnpatch(PatchTracker, apply, _seriesProviderIsComplete, prefix: nameof(IsCompletePrefix),
                postfix: nameof(IsCompletePostfix));
            PatchUnpatch(PatchTracker, apply, _seriesProviderImportData, prefix: nameof(SeriesImportDataPrefix));
            PatchUnpatch(PatchTracker, apply, _ensureSeriesInfo, postfix: nameof(EnsureSeriesInfoPostfix));
            PatchUnpatch(PatchTracker, apply, _seasonProviderIsComplete, prefix: nameof(IsCompletePrefix),
                postfix: nameof(IsCompletePostfix));
            PatchUnpatch(PatchTracker, apply, _seasonProviderImportData, prefix: nameof(SeasonImportDataPrefix));
            PatchUnpatch(PatchTracker, apply, _episodeProviderIsComplete, prefix: nameof(IsCompletePrefix),
                postfix: nameof(IsCompletePostfix));
            PatchUnpatch(PatchTracker, apply, _episodeProviderImportData, prefix: nameof(EpisodeImportDataPrefix));
        }

        private void PatchCacheTime()
        {
            PatchUnpatch(PatchTracker, true, _getEpisodeInfoAsync, transpiler: nameof(GetEpisodeInfoAsyncTranspiler));
        }

        private static TimeSpan GetEpisodeCacheTime()
        {
            if (RefreshEpisodeTask.IsRunning || QueueManager.IsEpisodeRefreshProcessTaskRunning)
            {
                return TimeSpan.Zero;
            }

            return MetadataApi.DefaultCacheTime;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> GetEpisodeInfoAsyncTranspiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codeMatcher = new CodeMatcher(instructions, generator);

            codeMatcher.MatchStartForward(CodeMatch.LoadsField(_cacheTime))
                .ThrowIfInvalid("Could not find call to MovieDbProviderBase.CacheTime")
                .RemoveInstruction()
                .InsertAndAdvance(CodeInstruction.Call(typeof(ChineseMovieDb), nameof(GetEpisodeCacheTime)));

            return codeMatcher.Instructions();
        }

        private static bool IsUpdateNeeded(string currentValue, string newValue = null)
        {
            if (string.IsNullOrEmpty(currentValue)) return true;

            var isEpisodeName = newValue != null;
            var isJapaneseFallback = HasMovieDbJapaneseFallback();
            
            if (!isEpisodeName)
            {
                return !isJapaneseFallback ? !IsChinese(currentValue) : !IsChineseJapanese(currentValue);
            }

            if (!isJapaneseFallback)
            {
                return IsDefaultChineseEpisodeName(currentValue) && IsChinese(newValue) &&
                       !IsDefaultChineseEpisodeName(newValue);
            }

            if (IsDefaultChineseEpisodeName(currentValue))
            {
                if (IsChinese(newValue) && !IsDefaultChineseEpisodeName(newValue)) return true;

                if (IsJapanese(newValue) && !IsDefaultJapaneseEpisodeName(newValue)) return true;
            }

            return false;
        }
        
        [HarmonyReversePatch]
        private static string MovieGetTitleStub(object instance) => throw new NotImplementedException();

        [HarmonyPrefix]
        private static void MovieGetMetadataPrefix(MovieInfo info)
        {
            lock (_lock)
            {
                PatchUnpatch(Instance.PatchTracker, false, _genericProcessMainInfoMovie,
                    prefix: nameof(ProcessMainInfoMoviePrefix), suppress: true);
                PatchUnpatch(Instance.PatchTracker, false, _genericIsCompleteMovie, prefix: nameof(IsCompletePrefix),
                    postfix: nameof(IsCompletePostfix), suppress: true);

                PatchUnpatch(Instance.PatchTracker, true, _genericProcessMainInfoMovie,
                    prefix: nameof(ProcessMainInfoMoviePrefix), suppress: true);
                PatchUnpatch(Instance.PatchTracker, true, _genericIsCompleteMovie, prefix: nameof(IsCompletePrefix),
                    postfix: nameof(IsCompletePostfix), suppress: true);
            }
        }

        [HarmonyPrefix]
        private static void ProcessMainInfoMoviePrefix(object resultItem, object settings,
            string preferredCountryCode, object movieData, bool isFirstLanguage)
        {
            if (!(resultItem is MetadataResult<Movie> metadataResult)) return;

            var item = metadataResult.Item;

            if (IsUpdateNeeded(item.Name))
            {
                item.Name = MovieGetTitleStub(movieData);
            }

            var overview = Traverse.Create(movieData).Property("overview").GetValue<string>();

            if (IsUpdateNeeded(item.Overview) && !string.IsNullOrEmpty(overview))
            {
                item.Overview = WebUtility.HtmlDecode(overview).Replace("\n\n", "\n");
            }
        }

        [HarmonyPrefix]
        private static bool IsCompletePrefix(BaseItem item, ref bool __result, out bool __state)
        {
            __state = false;

            var name = item.Name;
            var overview = item.Overview;
            var isJapaneseFallback = HasMovieDbJapaneseFallback();

            if (item is Movie || item is Series || item is Season)
            {
                __state = true;

                __result = !isJapaneseFallback
                    ? IsChinese(name) && IsChinese(overview)
                    : IsChineseJapanese(name) && IsChineseJapanese(overview);

                return false;
            }

            if (item is Episode)
            {
                __state = true;

                if (!isJapaneseFallback)
                {
                    if (IsDefaultChineseEpisodeName(name))
                    {
                        __result = false;
                    }
                    else if (IsChinese(overview))
                    {
                        __result = true;
                    }
                    else
                    {
                        __result = false;
                    }
                }
                else
                {
                    if (IsDefaultChineseEpisodeName(name))
                    {
                        __result = false;
                    }
                    else if (IsDefaultJapaneseEpisodeName(name))
                    {
                        __result = false;
                    }
                    else if (IsChineseJapanese(overview))
                    {
                        __result = true;
                    }
                    else
                    {
                        __result = false;
                    }
                }

                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void IsCompletePostfix(BaseItem item, ref bool __result, bool __state)
        {
            if (__state)
            {
                if (IsChinese(item.Name))
                {
                    item.Name = ConvertTraditionalToSimplified(item.Name);
                }

                if (IsChinese(item.Overview))
                {
                    item.Overview = ConvertTraditionalToSimplified(item.Overview);
                }
                else if (BlockMovieDbNonFallbackLanguage(item.Overview))
                {
                    item.Overview = null;
                }

                if (!string.IsNullOrEmpty(item.Tagline))
                {
                    item.Tagline = null;
                }
            }
        }

        [HarmonyReversePatch]
        private static string SeriesGetTitleStub(object instance) => throw new NotImplementedException();

        [HarmonyPrefix]
        private static void SeriesImportDataPrefix(MetadataResult<Series> seriesResult, object seriesInfo,
            string preferredCountryCode, object settings, bool isFirstLanguage)
        {
            var item = seriesResult.Item;

            if (IsUpdateNeeded(item.Name))
            {
                item.Name = SeriesGetTitleStub(seriesInfo);
            }

            var overview = Traverse.Create(seriesInfo).Property("overview").GetValue<string>();

            if (IsUpdateNeeded(item.Overview) && !string.IsNullOrEmpty(overview))
            {
                item.Overview = WebUtility.HtmlDecode(overview).Replace("\n\n", "\n");
            }

            if (isFirstLanguage)
            {
                var genresList = Traverse.Create(seriesInfo).Property("genres").GetValue<IEnumerable<object>>();

                if (genresList != null)
                {
                    foreach (var genre in genresList)
                    {
                        var genreNameProperty = Traverse.Create(genre).Property("name");
                        var genreNameValue = genreNameProperty.GetValue<string>();

                        if (!string.IsNullOrEmpty(genreNameValue))
                        {
                            if (string.Equals(genreNameValue, "Sci-Fi & Fantasy", StringComparison.OrdinalIgnoreCase))
                                genreNameProperty.SetValue("科幻奇幻");

                            if (string.Equals(genreNameValue, "War & Politics", StringComparison.OrdinalIgnoreCase))
                                genreNameProperty.SetValue("战争政治");
                        }
                    }
                }
            }
        }

        [HarmonyPostfix]
        private static void EnsureSeriesInfoPostfix(string tmdbId, string language, CancellationToken cancellationToken,
            Task __result)
        {
            if (string.IsNullOrEmpty(language)) return;

            var lookupLanguageCountryCode = !string.IsNullOrEmpty(language) && language.Contains('-')
                ? language.Split('-')[1]
                : null;

            object seriesInfo = null;

            try
            {
                seriesInfo = Traverse.Create(__result).Property("Result").GetValue();
            }
            catch
            {
                // ignored
            }

            if (seriesInfo != null)
            {
                var nameProperty = Traverse.Create(seriesInfo).Property("name");
                var nameValue = nameProperty.GetValue<string>();

                if (!HasMovieDbJapaneseFallback() ? !IsChineseNoJapanese(nameValue) : !IsChineseJapanese(nameValue))
                {
                    var alternativeTitles = Traverse.Create(seriesInfo)
                        .Property("alternative_titles")
                        .Property("results")
                        .GetValue<IEnumerable<object>>();

                    if (alternativeTitles != null)
                    {
                        foreach (var altTitle in alternativeTitles)
                        {
                            var traverseAltTitle = Traverse.Create(altTitle);
                            var iso3166Value = traverseAltTitle.Property("iso_3166_1").GetValue<string>();
                            var titleValue = traverseAltTitle.Property("title").GetValue<string>();

                            if (!string.IsNullOrEmpty(iso3166Value) && !string.IsNullOrEmpty(titleValue) &&
                                string.Equals(iso3166Value, lookupLanguageCountryCode,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                nameProperty.SetValue(titleValue);
                                break;
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPrefix]
        private static void SeasonImportDataPrefix(Season item, object seasonInfo, string name, int seasonNumber,
            bool isFirstLanguage)
        {
            if (IsUpdateNeeded(item.Name))
            {
                item.Name = Traverse.Create(seasonInfo).Property("name").GetValue<string>();
            }

            if (IsUpdateNeeded(item.Overview))
            {
                item.Overview = Traverse.Create(seasonInfo).Property("overview").GetValue<string>();
            }
        }

        [HarmonyPrefix]
        private static void EpisodeImportDataPrefix(MetadataResult<Episode> result, EpisodeInfo info, object response,
            object settings, bool isFirstLanguage)
        {
            var item = result.Item;

            var nameValue = Traverse.Create(response).Property("name").GetValue<string>();

            if (IsUpdateNeeded(item.Name, nameValue))
            {
                item.Name = nameValue;
            }

            if (IsUpdateNeeded(item.Overview))
            {
                item.Overview = Traverse.Create(response).Property("overview").GetValue<string>();
            }
        }

        [HarmonyReversePatch]
        private static string MapLanguageToProviderLanguageStub(object instance, string language, string country,
            bool exactMatchOnly, string[] providerLanguages) => throw new NotImplementedException();

        [HarmonyPostfix]
        private static void MetadataLanguagesPostfix(object __instance, ItemLookupInfo searchInfo,
            string[] providerLanguages, ref string[] __result)
        {
            var list = __result.ToList();
            var index = list.FindIndex(l => string.Equals(l, "en", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(l, "en-us", StringComparison.OrdinalIgnoreCase));

            var currentFallbackLanguages = GetMovieDbFallbackLanguages();

            foreach (var fallbackLanguage in currentFallbackLanguages)
            {
                if (!list.Contains(fallbackLanguage, StringComparer.OrdinalIgnoreCase))
                {
                    var mappedLanguage = MapLanguageToProviderLanguageStub(__instance, fallbackLanguage, null, false,
                        providerLanguages);

                    if (!string.IsNullOrEmpty(mappedLanguage))
                    {
                        if (index >= 0)
                        {
                            list.Insert(index, mappedLanguage);
                            index++;
                        }
                        else
                        {
                            list.Add(mappedLanguage);
                        }
                    }
                }
            }

            __result = list.ToArray();
        }

        [HarmonyPrefix]
        private static void GetImageLanguagesParamPrefix(ref string[] configuredLanguages)
        {
            var list = configuredLanguages.ToList();

            if (list.Count > 0)
            {
                list.Add("zh");
                configuredLanguages = list.ToArray();
            }
        }
    }
}
