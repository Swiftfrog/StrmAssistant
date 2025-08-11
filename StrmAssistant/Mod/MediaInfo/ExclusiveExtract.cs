using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.MediaInfoExtractOptions;
using static StrmAssistant.Options.Utility;
using static StrmAssistant.Reflection.EmbyApi;
using static StrmAssistant.Reflection.EmbyProviders;
using static StrmAssistant.Reflection.EmbyServerImplementations;
using static StrmAssistant.Reflection.MediaBrowser;

namespace StrmAssistant.Mod.MediaInfo
{
    public class ExclusiveExtract : PatchBase<ExclusiveExtract>
    {
        internal class RefreshContext
        {
            public long InternalId { get; set; }
            public MetadataRefreshOptions MetadataRefreshOptions { get; set; }
            public bool IsNewItem { get; set; }
            public bool IsScanning { get; set; }
            public bool IsPlayback { get; set; }
            public bool IsFileChanged { get; set; }
            public bool IsExternalSubtitleChanged { get; set; }
            public bool IsPersistInScope { get; set; }
            public bool MediaInfoUpdated { get; set; }
            public bool HasMetadataFetchers { get; set; }
            public bool PreRefreshHasMediaInfo { get; set; }
        }

        private static readonly AsyncLocal<bool> WasCalledByGetEnabledMetadataProviders = new AsyncLocal<bool>();
        private static readonly AsyncLocal<long> ExclusiveItem = new AsyncLocal<long>();
        private static readonly AsyncLocal<long> ProtectIntroItem = new AsyncLocal<long>();
        private static readonly AsyncLocal<RefreshContext> CurrentRefreshContext = new AsyncLocal<RefreshContext>();

        internal static long ExclusiveItemValue => ExclusiveItem.Value;

        public ExclusiveExtract()
        {
            if (Plugin.Instance.MediaInfoExtractStore.GetOptions().ExclusiveExtract)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _canRefreshImage, prefix: nameof(CanRefreshImagePrefix));
            PatchUnpatch(PatchTracker, apply, _canRefreshMetadata, prefix: nameof(CanRefreshMetadataPrefix),
                postfix: nameof(CanRefreshMetadataPostfix));
            PatchUnpatch(PatchTracker, apply, _getEnabledMetadataProviders,
                prefix: nameof(GetEnabledMetadataProvidersPrefix));
            PatchUnpatch(PatchTracker, apply, _clearImages, prefix: nameof(ClearImagesPrefix));
            PatchUnpatch(PatchTracker, apply, _isSaverEnabledForItem, prefix: nameof(IsSaverEnabledForItemPrefix));
            PatchUnpatch(PatchTracker, apply, _afterMetadataRefresh, prefix: nameof(AfterMetadataRefreshPrefix));
            PatchUnpatch(PatchTracker, apply, _addVirtualFolder, prefix: nameof(RefreshLibraryPrefix));
            PatchUnpatch(PatchTracker, apply, _removeVirtualFolder, prefix: nameof(RefreshLibraryPrefix));
            PatchUnpatch(PatchTracker, apply, _addMediaPath, prefix: nameof(RefreshLibraryPrefix));
            PatchUnpatch(PatchTracker, apply, _removeMediaPath, prefix: nameof(RefreshLibraryPrefix));
            PatchUnpatch(PatchTracker, apply, _saveChapters, prefix: nameof(SaveChaptersPrefix));
            PatchUnpatch(PatchTracker, apply, _deleteChapters, prefix: nameof(DeleteChaptersPrefix));
            PatchUnpatch(PatchTracker, apply, _getRefreshOptions, postfix: nameof(GetRefreshOptionsPostfix));
        }

        public static void AllowExtractInstance(BaseItem item)
        {
            if (!IsExclusiveFeatureSelected(ExclusiveControl.NoIntroProtect) &&
                item.DateLastRefreshed != DateTimeOffset.MinValue && item is Episode &&
                Plugin.MediaInfoApi.HasIntro(item))
            {
                ProtectIntroItem.Value = item.InternalId;
            }

            ExclusiveItem.Value = item.InternalId;
        }

        [HarmonyPrefix]
        private static bool CanRefreshImagePrefix(IImageProvider provider, BaseItem item, LibraryOptions libraryOptions,
            ImageRefreshOptions refreshOptions, bool ignoreMetadataLock, bool ignoreLibraryOptions, ref bool __result)
        {
            if (ExclusiveItem.Value != 0L && ExclusiveItem.Value == item.InternalId)
            {
                return true;
            }

            if (item.Parent is null && item.ExtraType is null || !(item is Video || item is Audio))
            {
                return true;
            }

            if (refreshOptions is MetadataRefreshOptions options)
            {
                if (CurrentRefreshContext.Value is null)
                {
                    CurrentRefreshContext.Value = new RefreshContext
                    {
                        InternalId = item.InternalId,
                        MetadataRefreshOptions = options,
                        IsNewItem = item.DateLastRefreshed == DateTimeOffset.MinValue,
                        IsScanning = options.MetadataRefreshMode <= MetadataRefreshMode.Default &&
                                     options.ImageRefreshMode <= MetadataRefreshMode.Default,
                        HasMetadataFetchers = libraryOptions.TypeOptions.Any(t =>
                            t.Type == item.GetType().Name && t.MetadataFetchers.Any()),
                        PreRefreshHasMediaInfo = Plugin.MediaInfoApi.HasMediaInfo(item)
                    };

                    if (!CurrentRefreshContext.Value.IsNewItem)
                    {
                        if (options.MetadataRefreshMode == MetadataRefreshMode.FullRefresh &&
                            options.ImageRefreshMode == MetadataRefreshMode.Default &&
                            !options.ReplaceAllMetadata && !options.ReplaceAllImages)
                        {
                            CurrentRefreshContext.Value.IsPlayback = true;
                        }

                        if (Plugin.LibraryApi.HasFileChanged(item, options.DirectoryService))
                        {
                            CurrentRefreshContext.Value.IsFileChanged = true;
                        }

                        if (!IsExclusiveFeatureSelected(item.InternalId, ExclusiveControl.IgnoreExtSubChange) &&
                            item is Video &&
                            Plugin.SubtitleApi.HasExternalSubtitleChanged(item, options.DirectoryService, false))
                        {
                            CurrentRefreshContext.Value.IsExternalSubtitleChanged = true;
                            options.EnableRemoteContentProbe = true;
                        }

                        if (!IsExclusiveFeatureSelected(ExclusiveControl.IgnoreFileChange) &&
                            IsExclusiveFeatureSelected(ExclusiveControl.ExtractOnFileChange) &&
                            CurrentRefreshContext.Value.IsFileChanged &&
                            CurrentRefreshContext.Value.PreRefreshHasMediaInfo ||
                            IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow))
                        {
                            options.EnableRemoteContentProbe = true;
                            EnableImageCapture.AllowImageCaptureInstance(item);
                        }
                    }
                }

                if (CurrentRefreshContext.Value.IsNewItem)
                {
                    return true;
                }

                if (provider is IDynamicImageProviderWithLibraryOptions && item.HasImage(ImageType.Primary) &&
                    (IsExclusiveFeatureSelected(item.InternalId, ExclusiveControl.CatchAllBlock) ||
                     !IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) && !options.ReplaceAllImages))
                {
                    __result = false;
                    return false;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static void GetEnabledMetadataProvidersPrefix(BaseItem item, LibraryOptions libraryOptions)
        {
            WasCalledByGetEnabledMetadataProviders.Value = true;
        }

        [HarmonyPrefix]
        private static bool CanRefreshMetadataPrefix(IMetadataProvider provider, BaseItem item,
            LibraryOptions libraryOptions, bool includeDisabled, bool forceEnableInternetMetadata,
            bool ignoreMetadataLock, ref bool __result, out bool __state)
        {
            __state = false;

            if (ExclusiveItem.Value != 0L && ExclusiveItem.Value == item.InternalId) return true;

            if (item.Parent is null && item.ExtraType is null) return true;

            if (WasCalledByGetEnabledMetadataProviders.Value) return true;

            if (CurrentRefreshContext.Value != null && CurrentRefreshContext.Value.InternalId == item.InternalId &&
                provider is IPreRefreshProvider && provider.Name == "ffprobe")
            {
                __state = true;

                if (CurrentRefreshContext.Value.IsNewItem)
                {
                    __result = false;
                    return false;
                }

                var refreshOptions = CurrentRefreshContext.Value.MetadataRefreshOptions;

                if (!IsExclusiveFeatureSelected(ExclusiveControl.IgnoreFileChange) &&
                    CurrentRefreshContext.Value.IsFileChanged ||
                    IsExclusiveFeatureSelected(ExclusiveControl.ExtractAlternative))
                {
                    return true;
                }

                if (CurrentRefreshContext.Value.IsScanning ||
                    !IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) && refreshOptions.SearchResult != null)
                {
                    __result = false;
                    return false;
                }

                if (!IsExclusiveFeatureSelected(item.InternalId, ExclusiveControl.CatchAllBlock) && !item.IsShortcut &&
                    refreshOptions.ReplaceAllImages)
                {
                    return true;
                }

                if (!IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) &&
                    CurrentRefreshContext.Value.PreRefreshHasMediaInfo)
                {
                    __result = false;
                    return false;
                }

                if (IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) ||
                    !IsExclusiveFeatureSelected(item.InternalId, ExclusiveControl.CatchAllBlock) && !item.IsShortcut)
                {
                    return true;
                }

                if (IsExclusiveFeatureSelected(item.InternalId, ExclusiveControl.CatchAllBlock) &&
                    !CurrentRefreshContext.Value.IsPlayback)
                {
                    return false;
                }

                return true;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void CanRefreshMetadataPostfix(IMetadataProvider provider, BaseItem item,
            LibraryOptions libraryOptions, bool includeDisabled, bool forceEnableInternetMetadata,
            bool ignoreMetadataLock, ref bool __result, bool __state)
        {
            if (!__state) return;

            var isPersistInScope = !IsExclusiveFeatureSelected(ExclusiveControl.NoPersistIntegration) && item is Video;
            CurrentRefreshContext.Value.IsPersistInScope = isPersistInScope;

            if (__result)
            {
                if (!IsExclusiveFeatureSelected(ExclusiveControl.NoIntroProtect) &&
                    (IsExclusiveFeatureSelected(ExclusiveControl.IgnoreFileChange) ||
                     !CurrentRefreshContext.Value.IsFileChanged) && item is Episode &&
                    Plugin.MediaInfoApi.HasIntro(item))
                {
                    ProtectIntroItem.Value = item.InternalId;
                }

                if (isPersistInScope)
                {
                    PersistMediaInfoHelper.BypassChapterInstance(item);
                    CurrentRefreshContext.Value.MediaInfoUpdated = true;
                }
            }
            else if (CurrentRefreshContext.Value.IsExternalSubtitleChanged)
            {
                var refreshOptions = CurrentRefreshContext.Value.MetadataRefreshOptions;
                _ = Plugin.SubtitleApi.UpdateExternalSubtitles(item, refreshOptions, false, isPersistInScope)
                    .ConfigureAwait(false);
            }
        }

        [HarmonyPrefix]
        private static void ClearImagesPrefix(BaseItem item, ref ImageType[] imageTypesToClear, int numBackdropToKeep)
        {
            if (item.HasImage(ImageType.Primary) && imageTypesToClear.Contains(ImageType.Primary))
            {
                imageTypesToClear = imageTypesToClear.Where(i => i != ImageType.Primary).ToArray();
            }
        }

        [HarmonyPrefix]
        private static void IsSaverEnabledForItemPrefix(IMetadataSaver saver, BaseItem item,
            LibraryOptions libraryOptions, ref ItemUpdateType updateType, bool includeDisabled, ref bool __result)
        {
            if ((updateType & ItemUpdateType.MetadataDownload) == 0) return;

            if (ExclusiveItem.Value != 0L && ExclusiveItem.Value == item.InternalId)
            {
                updateType &= ~ItemUpdateType.MetadataDownload;
            }

            if (CurrentRefreshContext.Value != null && CurrentRefreshContext.Value.InternalId == item.InternalId)
            {
                if (!CurrentRefreshContext.Value.IsNewItem && CurrentRefreshContext.Value.IsScanning)
                {
                    updateType &= ~ItemUpdateType.MetadataDownload;
                }
                else if (!IsExclusiveFeatureSelected(ExclusiveControl.NoNfoSaverOptimization) &&
                         !CurrentRefreshContext.Value.HasMetadataFetchers)
                {
                    updateType &= ~ItemUpdateType.MetadataDownload;
                }
            }
        }

        [HarmonyPrefix]
        private static void AfterMetadataRefreshPrefix(BaseItem __instance)
        {
            if (CurrentRefreshContext.Value != null && CurrentRefreshContext.Value.InternalId == __instance.InternalId)
            {
                var refreshOptions = CurrentRefreshContext.Value.MetadataRefreshOptions;
                var directoryService = refreshOptions.DirectoryService;

                if (CurrentRefreshContext.Value.IsFileChanged)
                {
                    Plugin.LibraryApi.UpdateDateModifiedLastSaved(__instance, directoryService);
                }

                if (CurrentRefreshContext.Value.IsPersistInScope)
                {
                    var ignoreFileChange = IsExclusiveFeatureSelected(ExclusiveControl.IgnoreFileChange);

                    if (CurrentRefreshContext.Value.MediaInfoUpdated)
                    {
                        if (__instance.IsShortcut && !refreshOptions.EnableRemoteContentProbe)
                        {
                            if (!CurrentRefreshContext.Value.IsFileChanged)
                            {
                                _ = Plugin.MediaInfoApi.DeserializeMediaInfo(__instance, directoryService,
                                    "Exclusive Restore", true).ConfigureAwait(false);
                            }
                            else if (!ignoreFileChange)
                            {
                                Plugin.MediaInfoApi.DeleteMediaInfoJson(__instance, directoryService,
                                    "Exclusive Delete on Change");
                            }
                        }
                        else
                        {
                            _ = Plugin.MediaInfoApi.SerializeMediaInfo(__instance.InternalId, directoryService, true,
                                "Exclusive Overwrite").ConfigureAwait(false);
                        }
                    }
                    else if (!CurrentRefreshContext.Value.IsNewItem && CurrentRefreshContext.Value.IsScanning)
                    {
                        if (!CurrentRefreshContext.Value.PreRefreshHasMediaInfo)
                        {
                            _ = Plugin.MediaInfoApi
                                .DeserializeMediaInfo(__instance, directoryService, "Exclusive Restore",
                                    ignoreFileChange).ConfigureAwait(false);
                        }
                        else if (!CurrentRefreshContext.Value.IsFileChanged)
                        {
                            _ = Plugin.MediaInfoApi.SerializeMediaInfo(__instance.InternalId, directoryService, false,
                                "Exclusive Non-existent").ConfigureAwait(false);
                        }
                    }
                }
            }

            CurrentRefreshContext.Value = null;
        }

        [HarmonyPrefix]
        private static void RefreshLibraryPrefix(IReturnVoid request)
        {
            Traverse.Create(request).Property("RefreshLibrary").SetValue(false);
        }

        [HarmonyPrefix]
        private static bool SaveChaptersPrefix(long itemId, bool clearExtractionFailureResult,
            List<ChapterInfo> chapters)
        {
            if (ProtectIntroItem.Value != 0L && ProtectIntroItem.Value == itemId) return false;

            return true;
        }

        [HarmonyPrefix]
        private static bool DeleteChaptersPrefix(long itemId, MarkerType[] markerTypes)
        {
            if (ProtectIntroItem.Value != 0L && ProtectIntroItem.Value == itemId) return false;

            return true;
        }

        [HarmonyPostfix]
        private static void GetRefreshOptionsPostfix(IReturnVoid request, MetadataRefreshOptions __result)
        {
            var id = Traverse.Create(request).Property("Id").GetValue<string>();
            var item = BaseItem.LibraryManager.GetItemById(id);

            Plugin.MediaInfoApi.QueueRefreshAlternateVersions(item, __result, true);
        }
    }
}
