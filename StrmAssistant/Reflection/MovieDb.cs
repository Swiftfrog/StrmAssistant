using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Serialization;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class MovieDb : ReflectionBase<MovieDb>
    {
        internal static Assembly _movieDbAssembly;
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
        internal static MethodInfo _getTitleSeriesInfo;
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
            new MovieDb().Initialize();
        }

        protected override void OnInitialize()
        {
            _movieDbAssembly = GetAssemblyByName("MovieDb");

            if (_movieDbAssembly != null)
            {
                var movieDbProviderBase = _movieDbAssembly.GetType("MovieDb.MovieDbProviderBase");
                _apiKey = movieDbProviderBase.GetField("ApiKey", BindingFlags.Static | BindingFlags.NonPublic);
                _cacheTime = movieDbProviderBase.GetField("CacheTime", BindingFlags.Public | BindingFlags.Static);
                _getMovieDbResponse = movieDbProviderBase.GetMethod("GetMovieDbResponse",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _getImageResponse = movieDbProviderBase.GetMethod("GetImageResponse");
                _getMovieDbMetadataLanguages = movieDbProviderBase.GetMethod("GetMovieDbMetadataLanguages",
                    BindingFlags.Public | BindingFlags.Instance);
                _mapLanguageToProviderLanguage = movieDbProviderBase.GetMethod("MapLanguageToProviderLanguage",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _getImageLanguagesParam = movieDbProviderBase.GetMethod("GetImageLanguagesParam",
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string[]) }, null);
                var getEpisodeInfo =
                    movieDbProviderBase.GetMethod("GetEpisodeInfo", BindingFlags.NonPublic | BindingFlags.Instance);
                _getEpisodeInfoAsync = AccessTools.AsyncMoveNext(getEpisodeInfo);
                _getBackdrops = movieDbProviderBase.GetMethod("GetBackdrops",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                var movieDbProvider = _movieDbAssembly.GetType("MovieDb.MovieDbProvider");
                _movieGetSearchResults = movieDbProvider.GetMethod("GetSearchResults");
                _movieGetMetadata = movieDbProvider.GetMethod("GetMetadata");
                var completeMovieData = movieDbProvider.GetNestedType("CompleteMovieData", BindingFlags.NonPublic);
                _getTitleMovieData = completeMovieData.GetMethod("GetTitle");

                var genericMovieDbInfo = _movieDbAssembly.GetType("MovieDb.GenericMovieDbInfo`1");
                var genericMovieDbInfoMovie = genericMovieDbInfo.MakeGenericType(typeof(Movie));
                _genericIsCompleteMovie = genericMovieDbInfoMovie.GetMethod("IsComplete",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _genericProcessMainInfoMovie = genericMovieDbInfoMovie.GetMethod("ProcessMainInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                _seriesGetSearchResults = movieDbSeriesProvider.GetMethod("GetSearchResults");
                _seriesProviderIsComplete =
                    movieDbSeriesProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                _seriesProviderImportData =
                    movieDbSeriesProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                _ensureSeriesInfo = movieDbSeriesProvider.GetMethod("EnsureSeriesInfo",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _seriesGetMetadata =
                    movieDbSeriesProvider.GetMethod("GetMetadata", BindingFlags.Public | BindingFlags.Instance);
                var seriesRootObject = movieDbSeriesProvider.GetNestedType("SeriesRootObject", BindingFlags.Public);
                _getTitleSeriesInfo = seriesRootObject.GetMethod("GetTitle");

                var movieDbSeasonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeasonProvider");
                _seasonProviderIsComplete =
                    movieDbSeasonProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                _seasonProviderImportData =
                    movieDbSeasonProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                _seasonGetMetadata = movieDbSeasonProvider.GetMethod("GetMetadata",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(RemoteMetadataFetchOptions<SeasonInfo>), typeof(CancellationToken) }, null);
                var ensureSeasonInfo = movieDbSeasonProvider.GetMethod("EnsureSeasonInfo",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                var movieDbEpisodeProvider = _movieDbAssembly.GetType("MovieDb.MovieDbEpisodeProvider");
                _episodeProviderIsComplete =
                    movieDbEpisodeProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                _episodeProviderImportData =
                    movieDbEpisodeProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                _episodeGetMetadata = movieDbEpisodeProvider.GetMethod("GetMetadata",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(RemoteMetadataFetchOptions<EpisodeInfo>), typeof(CancellationToken) }, null);

                var movieDbBoxSetProvider = _movieDbAssembly.GetType("MovieDb.MovieDbBoxSetProvider");
                _boxsetGetSearchResults = movieDbBoxSetProvider.GetMethod("GetSearchResults");

                var movieDbPersonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbPersonProvider");
                _personGetSearchResults = movieDbPersonProvider.GetMethod("GetSearchResults");
                _personProviderImportData = movieDbPersonProvider.GetMethod("ImportData",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var ensurePersonInfo = movieDbPersonProvider.GetMethod("EnsurePersonInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _ensurePersonInfoAsync = AccessTools.AsyncMoveNext(ensurePersonInfo);

                var movieDbImageProvider = _movieDbAssembly.GetType("MovieDb.MovieDbImageProvider");
                _getMovieInfo = movieDbImageProvider.GetMethod("GetMovieInfo",
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(BaseItem), typeof(string), typeof(IJsonSerializer), typeof(CancellationToken) },
                    null);

                var movieDbSeasonImageProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeasonImageProvider");
                _seasonGetImages = movieDbSeasonImageProvider.GetMethod("GetImages",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(RemoteImageFetchOptions), typeof(CancellationToken) }, null);

                var movieDbEpisodeImageProvider = _movieDbAssembly.GetType("MovieDb.MovieDbEpisodeImageProvider");
                _episodeGetImages = movieDbEpisodeImageProvider.GetMethod("GetImages",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(RemoteImageFetchOptions), typeof(CancellationToken) }, null);
            }
            else
            {
                Plugin.Instance.Logger.Warn("MovieDb plugin is not installed");
            }
        }
    }
}
