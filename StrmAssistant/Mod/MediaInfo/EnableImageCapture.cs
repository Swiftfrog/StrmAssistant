using Emby.Server.MediaEncoding.ImageExtraction;
using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyProviders;
using static StrmAssistant.Reflection.EmbyServerImplementations;
using static StrmAssistant.Reflection.EmbyServerMediaEncoding;
using static StrmAssistant.Reflection.MediaBrowser;

namespace StrmAssistant.Mod.MediaInfo
{
    public class EnableImageCapture : PatchBase<EnableImageCapture>
    {
        private static readonly AsyncLocal<BaseItem> ImageCaptureItem = new AsyncLocal<BaseItem>();
        private static readonly AsyncLocal<MediaContainers?> VideoThumbnailMediaContainer =
            new AsyncLocal<MediaContainers?>();
        private static readonly object AddHdrAdjustFilterLock = new object();

        public static int SemaphoreFfmpegMaxCount { get; private set; }

        public EnableImageCapture()
        {
            Initialize();

            if (Plugin.Instance.MediaInfoExtractStore.GetOptions().EnableImageCapture)
            {
                Patch();

                if (Plugin.Instance.DebugMode)
                {
                    var resourcePool = Traverse.Create(typeof(ImageExtractorBase))
                        .Field("resourcePool")
                        .GetValue<SemaphoreSlim>();
                    Plugin.Instance.Logger.Debug("Current Ffmpeg ResourcePool: " + resourcePool?.CurrentCount);
                }
            }
        }

        protected override void OnInitialize()
        {
            SemaphoreFfmpegMaxCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            ReversePatch(PatchTracker, _addHdrAdjustFilter, nameof(AddHdrAdjustFilterStub));
        }

        protected override void Prepare(bool apply)
        {
            if (apply)
            {
                PatchUnpatch(Instance.PatchTracker, true, _staticConstructor, prefix: nameof(ResourcePoolPrefix));
            }

            PatchUnpatch(PatchTracker, apply, _supportsVideoImageCapture, prefix: nameof(SupportsImageCapturePrefix));
            PatchUnpatch(PatchTracker, apply, _getThumbnailPositionTicks,
                prefix: nameof(GetThumbnailPositionTicksPrefix));
            PatchUnpatch(PatchTracker, apply, _supportsAudioEmbeddedImages, prefix: nameof(SupportsImageCapturePrefix));
            PatchUnpatch(PatchTracker, apply, _getImage, prefix: nameof(GetImagePrefix));
            PatchUnpatch(PatchTracker, apply, _supportsThumbnailsGetter,
                prefix: nameof(SupportsThumbnailsGetterPrefix), postfix: nameof(SupportsThumbnailsGetterPostfix));
            PatchUnpatch(PatchTracker, apply, _runExtraction, prefix: nameof(RunExtractionPrefix));
            PatchUnpatch(PatchTracker, apply, _logThumbnailImageExtractionFailure,
                prefix: nameof(LogThumbnailImageExtractionFailurePrefix));
            PatchUnpatch(PatchTracker, apply, _extractVideoImagesOnInterval,
                prefix: nameof(ExtractVideoImagesOnIntervalPrefix));
            PatchUnpatch(PatchTracker, apply, _enableQuickImageSeriesExtractor,
                postfix: nameof(EnableQuickImageSeriesExtractorPostfix));
            PatchUnpatch(PatchTracker, apply, _addHdrAdjustFilter, prefix: nameof(AddHdrAdjustFilterPrefix));
        }

        [HarmonyPrefix]
        private static bool ResourcePoolPrefix(ref SemaphoreSlim ___resourcePool)
        {
            ___resourcePool = new SemaphoreSlim(SemaphoreFfmpegMaxCount);
            return false;
        }

        public static void AllowImageCaptureInstance(BaseItem item)
        {
            ImageCaptureItem.Value = item;
        }

        [HarmonyPrefix]
        private static void SupportsImageCapturePrefix(BaseItem item)
        {
            if (item.IsShortcut && ImageCaptureItem.Value != null &&
                ImageCaptureItem.Value.InternalId == item.InternalId)
            {
                ExtractMediaInfoHelper.ShortcutItem.Value = item.InternalId;
            }
        }

        [HarmonyPrefix]
        private static void GetImagePrefix(BaseMetadataResult itemResult)
        {
            var mediaStreams = itemResult?.MediaStreams;
            if (mediaStreams != null && mediaStreams.Any(m => m.Type == MediaStreamType.Video) &&
                mediaStreams.Any(m => m.Type == MediaStreamType.EmbeddedImage))
            {
                itemResult.MediaStreams = mediaStreams.Where(m => m.Type != MediaStreamType.EmbeddedImage).ToArray();
            }
        }

        private static long GetThumbnailPositionTicks(long runtimeTicks)
        {
            var percent = Plugin.Instance.MediaInfoExtractStore.GetOptions().ImageCapturePosition / 100.0;

            var min = Math.Min(Convert.ToInt64(runtimeTicks * 0.5), TimeSpan.FromSeconds(20.0).Ticks);

            return Math.Max(Convert.ToInt64(runtimeTicks * percent), min);
        }

        [HarmonyPrefix]
        private static bool GetThumbnailPositionTicksPrefix(long runtimeTicks, ref long __result)
        {
            __result = GetThumbnailPositionTicks(runtimeTicks);

            return false;
        }

        [HarmonyPrefix]
        private static void RunExtractionPrefix(ImageExtractorBase __instance, ref string inputPath,
            MediaContainers? container, MediaStream videoStream, MediaProtocol? protocol, int? streamIndex,
            Video3DFormat? threedFormat, ref TimeSpan? startOffset, TimeSpan? interval, string targetDirectory,
            string targetFilename, int? maxWidth, bool enableThumbnailFilter)
        {
            if (ImageCaptureItem.Value != null && __instance.GetType() == _quickSingleImageExtractor)
            {
                var config = Plugin.Instance.ConfigurationManager.Configuration;
                var baseTimeoutMs = config.ImageExtractionTimeoutMs > 0 ? config.ImageExtractionTimeoutMs : 60000;
                var concurrency = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
                var timeoutMs = Math.Min(baseTimeoutMs + (concurrency - 1) * 5000, 150000);
                __instance.TotalTimeoutMs = timeoutMs;

                if (startOffset.HasValue && startOffset.Value == TimeSpan.FromSeconds(10.0))
                {
                    var item = ImageCaptureItem.Value;
                    if (item.MediaContainer != MediaContainers.Dvd && item.RunTimeTicks > 0L)
                    {
                        startOffset = TimeSpan.FromTicks(GetThumbnailPositionTicks(item.RunTimeTicks.Value));
                    }
                }

                ImageCaptureItem.Value = null;
            }
        }

        [HarmonyPrefix]
        private static void ExtractVideoImagesOnIntervalPrefix(MediaContainers? container)
        {
            VideoThumbnailMediaContainer.Value = container;
        }

        [HarmonyPostfix]
        private static void EnableQuickImageSeriesExtractorPostfix(ref bool __result)
        {
            if (__result && VideoThumbnailMediaContainer.Value.HasValue)
            {
                var mediaContainer = VideoThumbnailMediaContainer.Value.Value;
                VideoThumbnailMediaContainer.Value = null;

                if (mediaContainer == MediaContainers.MpegTs || mediaContainer == MediaContainers.Ts ||
                    mediaContainer == MediaContainers.M2Ts)
                {
                    __result = false;
                }
            }
        }

        [HarmonyPrefix]
        private static bool LogThumbnailImageExtractionFailurePrefix(long itemId, long dateModifiedUnixTimeSeconds)
        {
            return false;
        }

        [HarmonyPrefix]
        private static void SupportsThumbnailsGetterPrefix(BaseItem __instance, out ExtraType? __state)
        {
            __state = __instance.ExtraType;

            if (__instance.IsShortcut) ExtractMediaInfoHelper.ShortcutItem.Value = __instance.InternalId;

            if (__instance.ExtraType.HasValue) __instance.ExtraType = null;
        }

        [HarmonyPostfix]
        private static void SupportsThumbnailsGetterPostfix(BaseItem __instance, ref bool __result, ExtraType? __state)
        {
            if (__result && __state.HasValue)
            {
                __instance.ExtraType = __state;

                if (__state == ExtraType.Trailer || __state == ExtraType.ThemeVideo)
                {
                    __result = false;
                }
            }
        }

        [HarmonyReversePatch]
        private static void AddHdrAdjustFilterStub(ImageExtractorBase instance, List<string> filters,
            MediaStream mediaStream) =>
            throw new NotImplementedException();

        [HarmonyPrefix]
        private static bool AddHdrAdjustFilterPrefix(ImageExtractorBase __instance, List<string> filters,
            MediaStream mediaStream)
        {
            lock (AddHdrAdjustFilterLock)
            {
                AddHdrAdjustFilterStub(__instance, filters, mediaStream);
            }

            return false;
        }
    }
}
