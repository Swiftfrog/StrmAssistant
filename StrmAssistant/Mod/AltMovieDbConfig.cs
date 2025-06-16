using HarmonyLib;
using MediaBrowser.Model.Providers;
using StrmAssistant.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Mod.PatchManager;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace StrmAssistant.Mod
{
    public class AltMovieDbConfig : PatchBase<AltMovieDbConfig>
    {
        private static Assembly _movieDbAssembly;
        private static MethodInfo _getMovieDbResponse;
        private static MethodInfo _getImageResponse;
        private static MethodInfo _saveImageFromRemoteUrl;
        private static MethodInfo _downloadImage;
        private static MethodInfo _createHttpClientHandler;

        private static MethodInfo _movieGetSearchResults;
        private static MethodInfo _seriesGetSearchResults;
        private static MethodInfo _boxsetGetSearchResults;
        private static MethodInfo _personGetSearchResults;

        private static readonly string DefaultMovieDbApiUrl = "https://api.themoviedb.org";
        private static readonly string DefaultAltMovieDbApiUrl = "https://api.tmdb.org";
        private static readonly string DefaultMovieDbImageUrl = "https://image.tmdb.org";
        private static string SystemDefaultMovieDbApiKey;

        internal static string CurrentMovieDbApiUrl { get; private set; } = DefaultMovieDbApiUrl;
        internal static string CurrentMovieDbImageUrl { get; private set; } = DefaultMovieDbImageUrl;
        internal static string CurrentMovieDbApiKey { get; private set; }

        internal AltMovieDbConfig()
        {
            Initialize();

            var options = Plugin.Instance.MetadataEnhanceStore.GetOptions();
            if (options.AltMovieDbConfig)
            {
                if (!string.IsNullOrEmpty(options.AltMovieDbApiUrl) || !string.IsNullOrEmpty(options.AltMovieDbApiKey))
                {
                    PatchApiUrl();
                }

                if (!string.IsNullOrEmpty(options.AltMovieDbImageUrl))
                {
                    PatchImageUrl();
                }

                UpdateMovieDbConfig(options);
            }
        }

        protected override void OnInitialize()
        {
            _movieDbAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "MovieDb");

            if (_movieDbAssembly != null)
            {
                var movieDbProviderBase = _movieDbAssembly.GetType("MovieDb.MovieDbProviderBase");
                _getMovieDbResponse = movieDbProviderBase.GetMethod("GetMovieDbResponse",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var apiKey = movieDbProviderBase.GetField("ApiKey", BindingFlags.Static | BindingFlags.NonPublic);
                CurrentMovieDbApiKey = SystemDefaultMovieDbApiKey = apiKey.GetValue(null) as string;
                _getImageResponse = movieDbProviderBase.GetMethod("GetImageResponse");

                var movieDbProvider = _movieDbAssembly.GetType("MovieDb.MovieDbProvider");
                _movieGetSearchResults = movieDbProvider.GetMethod("GetSearchResults");
                var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                _seriesGetSearchResults = movieDbSeriesProvider.GetMethod("GetSearchResults");
                var movieDbBoxSetProvider = _movieDbAssembly.GetType("MovieDb.MovieDbBoxSetProvider");
                _boxsetGetSearchResults = movieDbBoxSetProvider.GetMethod("GetSearchResults");
                var movieDbPersonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbPersonProvider");
                _personGetSearchResults = movieDbPersonProvider.GetMethod("GetSearchResults");

                var embyProviders = Assembly.Load("Emby.Providers");
                var providerManager = embyProviders.GetType("Emby.Providers.Manager.ProviderManager");
                _saveImageFromRemoteUrl = providerManager.GetMethod("SaveImageFromRemoteUrl",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var embyApi = Assembly.Load("Emby.Api");
                var remoteImageService = embyApi.GetType("Emby.Api.Images.RemoteImageService");
                _downloadImage = remoteImageService.GetMethod("DownloadImage",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var applicationHost =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.ApplicationHost");
                _createHttpClientHandler = applicationHost.GetMethod("CreateHttpClientHandler",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            else
            {
                Plugin.Instance.Logger.Info("AltMovieDbConfig - MovieDb plugin is not installed");
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                PatchTracker.IsSupported = false;
            }
        }

        protected override void Prepare(bool apply)
        {
            // No action needed
        }

        private void PrepareApiUrl(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _getMovieDbResponse, prefix: nameof(GetMovieDbResponsePrefix));
            PatchUnpatch(PatchTracker, apply, _createHttpClientHandler,
                postfix: nameof(CreateHttpClientHandlerPostfix));
        }

        public void PatchApiUrl() => PrepareApiUrl(true);

        public void UnpatchApiUrl() => PrepareApiUrl(false);

        private void PrepareImageUrl(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _getImageResponse, prefix: nameof(ImageUrlPrefix));
            PatchUnpatch(PatchTracker, apply, _saveImageFromRemoteUrl, prefix: nameof(ImageUrlPrefix));
            PatchUnpatch(PatchTracker, apply, _downloadImage, prefix: nameof(ImageUrlPrefix));
            PatchUnpatch(PatchTracker, apply, _movieGetSearchResults, postfix: nameof(GetSearchResultsPostfix));
            PatchUnpatch(PatchTracker, apply, _seriesGetSearchResults, postfix: nameof(GetSearchResultsPostfix));
            PatchUnpatch(PatchTracker, apply, _boxsetGetSearchResults, postfix: nameof(GetSearchResultsPostfix));
            PatchUnpatch(PatchTracker, apply, _personGetSearchResults, postfix: nameof(GetSearchResultsPostfix));
        }

        public void PatchImageUrl() => PrepareImageUrl(true);

        public void UnpatchImageUrl() => PrepareImageUrl(false);

        public static void UpdateMovieDbConfig(MetadataEnhanceOptions options)
        {
            if (options.AltMovieDbConfig)
            {
                CurrentMovieDbApiUrl = IsValidHttpUrl(options.AltMovieDbApiUrl)
                    ? options.AltMovieDbApiUrl
                    : DefaultAltMovieDbApiUrl;

                CurrentMovieDbImageUrl = IsValidHttpUrl(options.AltMovieDbImageUrl)
                    ? options.AltMovieDbImageUrl
                    : DefaultMovieDbImageUrl;

                CurrentMovieDbApiKey = IsValidMovieDbApiKey(options.AltMovieDbApiKey)
                    ? options.AltMovieDbApiKey
                    : SystemDefaultMovieDbApiKey;
            }
            else
            {
                CurrentMovieDbApiUrl = DefaultMovieDbApiUrl;
                CurrentMovieDbImageUrl = DefaultMovieDbImageUrl;
                CurrentMovieDbApiKey = SystemDefaultMovieDbApiKey;
            }
        }

        [HarmonyPostfix]
        private static void CreateHttpClientHandlerPostfix(ref HttpMessageHandler __result)
        {
            switch (__result)
            {
                case HttpClientHandler httpClientHandler:
                    httpClientHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    break;
                case SocketsHttpHandler socketsHttpHandler:
                    socketsHttpHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    break;
            }
        }

        [HarmonyPrefix]
        private static void GetMovieDbResponsePrefix(HttpRequestOptions options)
        {
            var requestUrl = options.Url;

            requestUrl = requestUrl.Replace(DefaultMovieDbApiUrl,
                    requestUrl.StartsWith(DefaultMovieDbApiUrl + "/3/configuration", StringComparison.Ordinal)
                        ? DefaultAltMovieDbApiUrl
                        : CurrentMovieDbApiUrl)
                .Replace(SystemDefaultMovieDbApiKey, CurrentMovieDbApiKey);

            if (!string.Equals(requestUrl, options.Url, StringComparison.Ordinal))
            {
                options.Url = requestUrl;
            }
        }

        [HarmonyPrefix]
        private static void ImageUrlPrefix(ref string url)
        {
            if (url.StartsWith(DefaultMovieDbImageUrl))
            {
                url = url.Replace(DefaultMovieDbImageUrl, CurrentMovieDbImageUrl);
            }
        }

        [HarmonyPostfix]
        private static Task<IEnumerable<RemoteSearchResult>> GetSearchResultsPostfix(
            Task<IEnumerable<RemoteSearchResult>> __result)
        {
            IEnumerable<RemoteSearchResult> result = null;

            try
            {
                result = __result.Result;
            }
            catch
            {
                // ignored
            }

            if (result is null) return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

            var searchResult = result.ToList();

            foreach (var remoteSearchResult in searchResult)
            {
                var imageUrl = remoteSearchResult.ImageUrl;
                if (imageUrl.StartsWith(DefaultMovieDbImageUrl))
                {
                    remoteSearchResult.ImageUrl = imageUrl.Replace(DefaultMovieDbImageUrl, CurrentMovieDbImageUrl);
                }
            }

            return Task.FromResult(searchResult.AsEnumerable());
        }
    }
}
