using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant.Common;
using System.Linq;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyProviders;

namespace StrmAssistant.Mod
{
    public class UnlockIntroSkip : PatchBase<UnlockIntroSkip>
    {
        private static readonly AsyncLocal<bool> LogZeroConfidence = new AsyncLocal<bool>();

        public UnlockIntroSkip()
        {
            if (Plugin.Instance.IntroSkipStore.GetOptions().UnlockIntroSkip)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            EnableImageCapture.PatchUnpatchIsShortcut(apply);

            PatchUnpatch(PatchTracker, apply, _isIntroDetectionSupported,
                prefix: nameof(IsIntroDetectionSupportedPrefix), postfix: nameof(IsIntroDetectionSupportedPostfix));
            PatchUnpatch(PatchTracker, apply, _createQueryForEpisodeIntroDetection,
                postfix: nameof(CreateQueryForEpisodeIntroDetectionPostfix));
            PatchUnpatch(PatchTracker, apply, _detectSequences, postfix: nameof(DetectSequencesPostfix));
            PatchUnpatch(PatchTracker, apply, _onFailedToFindIntro, prefix: nameof(OnFailedToFindIntroPrefix));
        }

        [HarmonyPrefix]
        private static bool IsIntroDetectionSupportedPrefix(Episode item, LibraryOptions libraryOptions,
            ref bool __result, out bool __state)
        {
            __state = false;

            if (item.IsShortcut)
            {
                EnableImageCapture.PatchIsShortcutInstance(item);
                __state = true;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void IsIntroDetectionSupportedPostfix(Episode item, LibraryOptions libraryOptions,
            ref bool __result, bool __state)
        {
            if (__state)
            {
                EnableImageCapture.UnpatchIsShortcutInstance(item);
            }
        }

        [HarmonyPostfix]
        private static void CreateQueryForEpisodeIntroDetectionPostfix(LibraryOptions libraryOptions,
            ref InternalItemsQuery __result)
        {
            var markerEnabledLibraryScope = Plugin.Instance.IntroSkipStore.GetOptions().MarkerEnabledLibraryScope;
            var blacklistSeasons = Plugin.FingerprintApi.GetBlacklistSeasons();

            if (!string.IsNullOrEmpty(markerEnabledLibraryScope) && markerEnabledLibraryScope.Contains("-1"))
            {
                __result.ParentIds = Plugin.FingerprintApi.GetFavoriteSeasons(blacklistSeasons)
                    .DefaultIfEmpty(-1)
                    .ToArray();
            }
            else
            {
                if (FingerprintApi.LibraryPathsInScope.Any())
                {
                    __result.PathStartsWithAny = FingerprintApi.LibraryPathsInScope.ToArray();
                }

                if (blacklistSeasons.Any())
                {
                    __result.ExcludeParentIds = blacklistSeasons.ToArray();
                }
            }
        }

        [HarmonyPostfix]
        private static void DetectSequencesPostfix(object __result)
        {
            if (__result != null && Traverse.Create(__result).Property("Confidence").GetValue() is double confidence &&
                confidence == 0)
            {
                LogZeroConfidence.Value = true;
            }
        }

        [HarmonyPrefix]
        private static bool OnFailedToFindIntroPrefix(Episode episode)
        {
            if (LogZeroConfidence.Value) return true;

            BaseItem.ItemRepository.DeleteChapters(episode.InternalId,
                new[] { MarkerType.IntroStart, MarkerType.IntroEnd });

            return false;
        }
    }
}
