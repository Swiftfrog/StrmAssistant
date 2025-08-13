using Emby.Server.MediaEncoding.ImageExtraction;
using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyProviders;
using static StrmAssistant.Reflection.EmbyServerImplementations;
using static StrmAssistant.Reflection.EmbyServerMediaEncoding;
using static StrmAssistant.Reflection.MediaBrowser;

namespace StrmAssistant.Mod.MediaInfo
{
    public class EnableImageCapture : PatchBase<EnableImageCapture>
    {
        private static readonly AsyncLocal<BaseItem> ShortcutItem = new AsyncLocal<BaseItem>();
        private static readonly AsyncLocal<BaseItem> ImageCaptureItem = new AsyncLocal<BaseItem>();
        private static readonly AsyncLocal<MediaContainers?> VideoThumbnailMediaContainer =
            new AsyncLocal<MediaContainers?>();
        private static int _isShortcutPatchUsageCount;
        private static readonly object AddHdrAdjustFilterLock = new object();

        private static SemaphoreSlim SemaphoreFFmpeg;
        public static int SemaphoreFFmpegMaxCount { get; private set; }

        public EnableImageCapture()
        {
            SemaphoreFFmpegMaxCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;

            Initialize();

            if (Plugin.Instance.MediaInfoExtractStore.GetOptions().EnableImageCapture)
            {
                SemaphoreFFmpeg = new SemaphoreSlim(SemaphoreFFmpegMaxCount);
                PatchResourcePool();
                var resourcePool = (SemaphoreSlim)_resourcePoolField?.GetValue(null);

                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug("Current FFmpeg ResourcePool: " + resourcePool?.CurrentCount);
                }

                Patch();
            }
        }

        protected override void OnInitialize()
        {
            ReversePatch(PatchTracker, _addHdrAdjustFilter, nameof(AddHdrAdjustFilterStub));
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatchIsShortcut(apply);

            PatchUnpatch(PatchTracker, apply, _supportsVideoImageCapture, prefix: nameof(SupportsImageCapturePrefix),
                postfix: nameof(SupportsImageCapturePostfix));
            PatchUnpatch(PatchTracker, apply, _getThumbnailPositionTicks,
                prefix: nameof(GetThumbnailPositionTicksPrefix));
            PatchUnpatch(PatchTracker, apply, _supportsAudioEmbeddedImages, prefix: nameof(SupportsImageCapturePrefix),
                postfix: nameof(SupportsImageCapturePostfix));
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

        private static void PatchResourcePool()
        {
            var result = PatchUnpatch(Instance.PatchTracker, true, _staticConstructor,
                prefix: nameof(ResourcePoolPrefix));
            //var result = PatchUnpatch(Instance.PatchTracker, true, _staticConstructor,
            //    transpiler: nameof(ResourcePoolTranspiler));

            if (!result && Instance.PatchTracker.FallbackPatchApproach == PatchApproach.Reflection)
                PatchResourcePoolByReflection();
        }

        private static void PatchResourcePoolByReflection()
        {
            //works only with modded Emby.Server.MediaEncoding.dll

            try
            {
                _resourcePoolField.SetValue(null, SemaphoreFFmpeg);

                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug("Patch FFmpeg ResourcePool Success by Reflection");
                }
            }
            catch (Exception re)
            {
                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug("Patch FFmpeg ResourcePool Failed by Reflection");
                    Plugin.Instance.Logger.Debug(re.Message);
                }

                Instance.PatchTracker.FallbackPatchApproach = PatchApproach.None;
            }
        }

        private void UnpatchResourcePool()
        {
            PatchUnpatch(PatchTracker, false, _staticConstructor, prefix: nameof(ResourcePoolPrefix));
            //PatchUnpatch(PatchTracker, false, _staticConstructor, transpiler: nameof(ResourcePoolTranspiler));

            var resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
            Plugin.Instance.Logger.Info("Current FFmpeg Resource Pool: " + resourcePool?.CurrentCount ?? string.Empty);
        }

        public static void UpdateResourcePool(int maxConcurrentCount)
        {
            if (SemaphoreFFmpegMaxCount != maxConcurrentCount)
            {
                SemaphoreFFmpegMaxCount = maxConcurrentCount;
                SemaphoreSlim newSemaphoreFFmpeg;
                SemaphoreSlim oldSemaphoreFFmpeg;

                switch (Instance.PatchTracker.FallbackPatchApproach)
                {
                    case PatchApproach.Harmony:
                        NotifyPendingRestart();

                        /* un-patch and re-patch don't work for readonly static field
                        UnpatchResourcePool();

                        _currentMaxConcurrentCount = maxConcurrentCount;
                        newSemaphoreFFmpeg = new SemaphoreSlim(maxConcurrentCount);
                        oldSemaphoreFFmpeg = SemaphoreFFmpeg;
                        SemaphoreFFmpeg = newSemaphoreFFmpeg;

                        PatchResourcePool();

                        oldSemaphoreFFmpeg.Dispose();
                        */
                        break;

                    case PatchApproach.Reflection:

                        newSemaphoreFFmpeg = new SemaphoreSlim(maxConcurrentCount);
                        oldSemaphoreFFmpeg = SemaphoreFFmpeg;
                        SemaphoreFFmpeg = newSemaphoreFFmpeg;

                        PatchResourcePoolByReflection();

                        oldSemaphoreFFmpeg.Dispose();

                        break;
                }
            }

            var resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
            Plugin.Instance.Logger.Info("Current FFmpeg ResourcePool: " + resourcePool?.CurrentCount ?? string.Empty);
        }

        public static void PatchUnpatchIsShortcut(bool apply)
        {
            PatchUnpatch(Instance.PatchTracker, apply, _isShortcutGetter, ref _isShortcutPatchUsageCount,
                prefix: nameof(IsShortcutPrefix));
        }

        public static void AllowImageCaptureInstance(BaseItem item)
        {
            ImageCaptureItem.Value = item;
        }

        public static void PatchIsShortcutInstance(BaseItem item)
        {
            switch (Instance.PatchTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    ShortcutItem.Value = item;
                    break;

                case PatchApproach.Reflection:
                    try
                    {
                        _isShortcutProperty.SetValue(item, true); //special logic depending on modded MediaBrowser.Controller.dll
                        //Plugin.Instance.Logger.Debug("Patch IsShortcut Success by Reflection" + " - " + item.Name + " - " + item.Path);
                    }
                    catch (Exception re)
                    {
                        if (Plugin.Instance.DebugMode)
                        {
                            Plugin.Instance.Logger.Debug("Patch IsShortcut Failed by Reflection");
                            Plugin.Instance.Logger.Debug(re.Message);
                        }

                        Instance.PatchTracker.FallbackPatchApproach = PatchApproach.None;
                    }
                    break;
            }
        }

        public static void UnpatchIsShortcutInstance(BaseItem item)
        {
            switch (Instance.PatchTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    ShortcutItem.Value = null;
                    break;

                case PatchApproach.Reflection:
                    try
                    {
                        _isShortcutProperty.SetValue(item, false); //special logic depending on modded MediaBrowser.Controller.dll
                        //Plugin.Instance.Logger.Debug("Unpatch IsShortcut Success by Reflection" + " - " + item.Name + " - " + item.Path);
                    }
                    catch (Exception re)
                    {
                        if (Plugin.Instance.DebugMode)
                        {
                            Plugin.Instance.Logger.Debug("Unpatch IsShortcut Failed by Reflection");
                            Plugin.Instance.Logger.Debug(re.Message);
                        }
                    }
                    break;
            }
        }

        [HarmonyPrefix]
        private static bool ResourcePoolPrefix()
        {
            _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
            return false;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ResourcePoolTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4_1)
                {
                    codes[i] = new CodeInstruction(OpCodes.Ldc_I4_S,
                        (sbyte)Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount);
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Ldc_I4_1)
                    {
                        codes[i + 1] = new CodeInstruction(OpCodes.Ldc_I4_S,
                            (sbyte)Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount);
                    }
                    break;
                }
            }
            return codes.AsEnumerable();
        }

        [HarmonyPrefix]
        private static bool IsShortcutPrefix(BaseItem __instance, ref bool __result)
        {
            if (ShortcutItem.Value != null && __instance.InternalId == ShortcutItem.Value.InternalId)
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static void SupportsImageCapturePrefix(BaseItem item, out bool __state)
        {
            __state = false;

            if (item.IsShortcut && ImageCaptureItem.Value != null &&
                ImageCaptureItem.Value.InternalId == item.InternalId)
            {
                PatchIsShortcutInstance(item);
                __state = true;
            }
        }

        [HarmonyPostfix]
        private static void SupportsImageCapturePostfix(BaseItem item, bool __result, bool __state)
        {
            if (__state)
            {
                UnpatchIsShortcutInstance(item);
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
        private static bool SupportsThumbnailsGetterPrefix(BaseItem __instance, ref bool __result,
            out (bool, ExtraType?) __state)
        {
            __state = new ValueTuple<bool, ExtraType?>(false, __instance.ExtraType);

            if (__instance.IsShortcut)
            {
                PatchIsShortcutInstance(__instance);
                __state.Item1 = true;
            }

            if (__instance.ExtraType.HasValue)
            {
                __instance.ExtraType = null;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void SupportsThumbnailsGetterPostfix(BaseItem __instance, ref bool __result,
            (bool, ExtraType?) __state)
        {
            if (__state.Item1)
            {
                UnpatchIsShortcutInstance(__instance);
            }

            if (__result && __state.Item2.HasValue)
            {
                __instance.ExtraType = __state.Item2;

                if (__state.Item2 == ExtraType.Trailer || __state.Item2 == ExtraType.ThemeVideo)
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
