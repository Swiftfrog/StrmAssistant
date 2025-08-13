using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Serialization;
using MovieDb;
using System;
using System.Reflection;
using System.Threading;
using static MovieDb.MovieDbProvider;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class MovieDb : ReflectionBase<MovieDb>
    {
        private static readonly string AssemblyName = "MovieDb";
        internal static readonly Assembly _movieDbAssembly;

        internal static readonly Version MinVer = new Version("1.8.0.0");
        internal static Version PluginVer;

        internal static FieldInfo _apiKey;
        internal static FieldInfo _cacheTime;
        internal static MethodInfo _getMovieDbResponse;
        internal static MethodInfo _getImageResponse;
        internal static MethodInfo _movieGetSearchResults;
        internal static MethodInfo _seriesGetSearchResults;
        internal static MethodInfo _boxsetGetSearchResults;
        internal static MethodInfo _personGetSearchResults;
        internal static MethodInfo _getMovieDbMetadataLanguages;
        internal static MethodInfo _mapLanguageToProviderLanguage;
        internal static MethodInfo _getImageLanguagesParam;
        internal static MethodInfo _genericProcessMainInfoMovie;
        internal static MethodInfo _genericIsCompleteMovie;
        internal static MethodInfo _movieGetMetadata;
        internal static MethodInfo _getTitleMovieData;
        internal static MethodInfo _seriesProviderIsComplete;
        internal static MethodInfo _seriesProviderImportData;
        internal static MethodInfo _ensureSeriesInfo;
        internal static MethodInfo _seriesGetMetadata;
        internal static MethodInfo _seasonProviderIsComplete;
        internal static MethodInfo _seasonProviderImportData;
        internal static MethodInfo _seasonGetMetadata;
        internal static MethodInfo _episodeProviderIsComplete;
        internal static MethodInfo _episodeProviderImportData;
        internal static MethodInfo _episodeGetMetadata;
        internal static MethodInfo _seasonGetImages;
        internal static MethodInfo _episodeGetImages;
        internal static MethodInfo _personProviderImportData;
        internal static MethodInfo _ensurePersonInfoAsync;
        internal static MethodInfo _getEpisodeInfoAsync;
        internal static MethodInfo _getMovieInfo;
        internal static MethodInfo _getBackdrops;

        static MovieDb()
        {
            _movieDbAssembly = GetAssemblyByName(AssemblyName);

            if (_movieDbAssembly != null)
            {
                RegisterAssemblyResolve(AssemblyName, _movieDbAssembly);

                PluginVer = _movieDbAssembly.GetName().Version;

                if (PluginVer >= MinVer)
                {
                    new MovieDb().Initialize();
                }
                else
                {
                    Plugin.Instance.Logger.Info(
                        $"{AssemblyName} plugin {PluginVer} is not supported. Minimum supported version is {MinVer}.");
                }
            }
            else
            {
                Plugin.Instance.Logger.Info($"{AssemblyName} plugin is not installed");
            }
        }

        internal static bool IsSupported => _movieDbAssembly != null && PluginVer >= MinVer;

        protected override void OnInitialize()
        {
            _apiKey = AccessTools.Field(typeof(MovieDbProviderBase), "ApiKey");
            _cacheTime = AccessTools.Field(typeof(MovieDbProviderBase), "CacheTime");
            _getMovieDbResponse = AccessTools.Method(typeof(MovieDbProviderBase), "GetMovieDbResponse");
            _getImageResponse = AccessTools.Method(typeof(MovieDbProviderBase), "GetImageResponse");
            _getMovieDbMetadataLanguages =
                AccessTools.Method(typeof(MovieDbProviderBase), "GetMovieDbMetadataLanguages");
            _mapLanguageToProviderLanguage =
                AccessTools.Method(typeof(MovieDbProviderBase), "MapLanguageToProviderLanguage");
            _getImageLanguagesParam = AccessTools.Method(typeof(MovieDbProviderBase), "GetImageLanguagesParam",
                new[] { typeof(string[]) });
            var getEpisodeInfo = AccessTools.Method(typeof(MovieDbProviderBase), "GetEpisodeInfo");
            _getEpisodeInfoAsync = AccessTools.AsyncMoveNext(getEpisodeInfo);
            _getBackdrops = AccessTools.Method(typeof(MovieDbProviderBase), "GetBackdrops");

            _movieGetSearchResults = AccessTools.Method(typeof(MovieDbProvider), "GetSearchResults");
            _movieGetMetadata = AccessTools.Method(typeof(MovieDbProvider), "GetMetadata");
            _getTitleMovieData = AccessTools.Method(typeof(CompleteMovieData), "GetTitle");

            _genericIsCompleteMovie = AccessTools.Method(typeof(GenericMovieDbInfo<Movie>), "IsComplete");
            _genericProcessMainInfoMovie = AccessTools.Method(typeof(GenericMovieDbInfo<Movie>), "ProcessMainInfo");

            _seriesGetSearchResults = AccessTools.Method(typeof(MovieDbSeriesProvider), "GetSearchResults");
            _seriesProviderIsComplete = AccessTools.Method(typeof(MovieDbSeriesProvider), "IsComplete");
            _seriesProviderImportData = AccessTools.Method(typeof(MovieDbSeriesProvider), "ImportData");
            _ensureSeriesInfo = AccessTools.Method(typeof(MovieDbSeriesProvider), "EnsureSeriesInfo");
            _seriesGetMetadata = AccessTools.Method(typeof(MovieDbSeriesProvider), "GetMetadata");

            _seasonProviderIsComplete = AccessTools.Method(typeof(MovieDbSeasonProvider), "IsComplete");
            _seasonProviderImportData = AccessTools.Method(typeof(MovieDbSeasonProvider), "ImportData");
            _seasonGetMetadata = AccessTools.Method(typeof(MovieDbSeasonProvider), "GetMetadata",
                new[] { typeof(RemoteMetadataFetchOptions<SeasonInfo>), typeof(CancellationToken) });

            _episodeProviderIsComplete = AccessTools.Method(typeof(MovieDbEpisodeProvider), "IsComplete");
            _episodeProviderImportData = AccessTools.Method(typeof(MovieDbEpisodeProvider), "ImportData");
            _episodeGetMetadata = AccessTools.Method(typeof(MovieDbEpisodeProvider), "GetMetadata",
                new[] { typeof(RemoteMetadataFetchOptions<EpisodeInfo>), typeof(CancellationToken) });

            _boxsetGetSearchResults = AccessTools.Method(typeof(MovieDbBoxSetProvider), "GetSearchResults");

            _personGetSearchResults = AccessTools.Method(typeof(MovieDbPersonProvider), "GetSearchResults");
            _personProviderImportData = AccessTools.Method(typeof(MovieDbPersonProvider), "ImportData");
            var ensurePersonInfo = AccessTools.Method(typeof(MovieDbPersonProvider), "EnsurePersonInfo");
            _ensurePersonInfoAsync = AccessTools.AsyncMoveNext(ensurePersonInfo);

            _getMovieInfo = AccessTools.Method(typeof(MovieDbImageProvider), "GetMovieInfo",
                new[] { typeof(BaseItem), typeof(string), typeof(IJsonSerializer), typeof(CancellationToken) });
            _seasonGetImages = AccessTools.Method(typeof(MovieDbSeasonImageProvider), "GetImages",
                new[] { typeof(RemoteImageFetchOptions), typeof(CancellationToken) });
            _episodeGetImages = AccessTools.Method(typeof(MovieDbEpisodeImageProvider), "GetImages",
                new[] { typeof(RemoteImageFetchOptions), typeof(CancellationToken) });
        }
    }
}
