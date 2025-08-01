using HarmonyLib;
using MediaBrowser.Model.Providers;
using StrmAssistant.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyApi;
using static StrmAssistant.Reflection.EmbyProviders;
using static StrmAssistant.Reflection.EmbyServerImplementations;
using static StrmAssistant.Reflection.MovieDb;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace StrmAssistant.Mod.Metadata
{
    public class AltMovieDbConfig : PatchBase<AltMovieDbConfig>
    {
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
            if (_movieDbAssembly != null)
            {
                CurrentMovieDbApiKey = SystemDefaultMovieDbApiKey = _apiKey.GetValue(null) as string;
            }
            else
            {
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
                if (!string.IsNullOrEmpty(imageUrl) && imageUrl.StartsWith(DefaultMovieDbImageUrl))
                {
                    remoteSearchResult.ImageUrl = imageUrl.Replace(DefaultMovieDbImageUrl, CurrentMovieDbImageUrl);
                }
            }

            return Task.FromResult(searchResult.AsEnumerable());
        }
    }
}
