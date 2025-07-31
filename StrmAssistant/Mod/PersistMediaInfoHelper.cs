using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using StrmAssistant.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.MediaInfoExtractOptions;
using static StrmAssistant.Reflection.EmbyProviders;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod
{
    public class PersistMediaInfoHelper : PatchBase<PersistMediaInfoHelper>
    {
        private static readonly AsyncLocal<long> BypassChapterItem = new AsyncLocal<long>();

        public PersistMediaInfoHelper()
        {
            if (Plugin.Instance.MediaInfoExtractStore.GetOptions().PersistMediaInfoMode !=
                PersistMediaInfoOption.None.ToString())
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            if (Plugin.Instance.MediaInfoExtractStore.GetOptions().IsModSupported)
            {
                PatchUnpatch(PatchTracker, apply, _saveChapters, postfix: nameof(SaveChaptersPostfix));
                //PatchUnpatch(PatchTracker, apply, _deleteChapters, postfix: nameof(DeleteChaptersPostfix));
                PatchUnpatch(PatchTracker, apply, _onFailedToFindIntro, postfix: nameof(OnFailedToFindIntroPostfix));
                PatchUnpatch(PatchTracker, apply, _deleteItem, prefix: nameof(DeleteItemPrefix),
                    finalizer: nameof(DeleteItemFinalizer));
            }
        }

        public static void BypassChapterInstance(BaseItem item)
        {
            BypassChapterItem.Value = item.InternalId;
        }

        [HarmonyPostfix]
        private static void SaveChaptersPostfix(long itemId, bool clearExtractionFailureResult,
            List<ChapterInfo> chapters)
        {
            if (chapters.Count == 0) return;

            if (BypassChapterItem.Value != 0L && BypassChapterItem.Value == itemId) return;

            _ = Plugin.MediaInfoApi.SerializeMediaInfo(itemId, null, true, "Save Chapters").ConfigureAwait(false);
        }

        [HarmonyPostfix]
        private static void DeleteChaptersPostfix(long itemId, MarkerType[] markerTypes)
        {
            if (BypassChapterItem.Value != 0L && BypassChapterItem.Value == itemId) return;

            _ = Plugin.MediaInfoApi.SerializeMediaInfo(itemId, null, true, "Delete Chapters").ConfigureAwait(false);
        }

        [HarmonyPostfix]
        private static void OnFailedToFindIntroPostfix(Episode episode, bool __runOriginal)
        {
            if (__runOriginal)
            {
                _ = Plugin.MediaInfoApi.SerializeMediaInfo(episode.InternalId, null, true,
                    "Zero Fingerprint Confidence").ConfigureAwait(false);
            }
        }

        [HarmonyPrefix]
        private static void DeleteItemPrefix(ILibraryManager __instance, BaseItem item, DeleteOptions options,
            BaseItem parent, bool notifyParentItem, out string __state)
        {
            __state = null;

            if (options.DeleteFileLocation)
            {
                var mediaInfoOptions = Plugin.Instance.MediaInfoExtractStore.GetOptions();
                if (mediaInfoOptions.PersistMediaInfoMode != PersistMediaInfoOption.Restore.ToString() &&
                    !string.IsNullOrEmpty(mediaInfoOptions.MediaInfoJsonRootFolder))
                {
                    var collectionFolder = options.CollectionFolders ?? __instance.GetCollectionFolders(item);
                    var isFolder = item.GetDeletePaths(true, collectionFolder).Any(i => i.IsDirectory);

                    if (isFolder)
                    {
                        __state = MediaInfoApi.GetMediaInfoJsonPath(item);
                    }
                }
            }
        }

        [HarmonyFinalizer]
        private static void DeleteItemFinalizer(Exception __exception, string __state)
        {
            if (__state != null && __exception is null)
            {
                Plugin.MediaInfoApi.DeleteMediaInfoJson(__state, "Delete Item Finalizer");
            }
        }
    }
}
