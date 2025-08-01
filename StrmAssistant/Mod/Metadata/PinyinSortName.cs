using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using static StrmAssistant.Common.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyApi;
using static StrmAssistant.Reflection.MediaBrowser;

namespace StrmAssistant.Mod.Metadata
{
    public class PinyinSortName : PatchBase<PinyinSortName>
    {
        public PinyinSortName()
        {
            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().PinyinSortName)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _createSortName, postfix: nameof(CreateSortNamePostfix));
            PatchUnpatch(PatchTracker, apply, _getPrefixes, postfix: nameof(GetPrefixesPostfix));
            PatchUnpatch(PatchTracker, apply, _getArtistPrefixes, postfix: nameof(GetPrefixesPostfix));
        }

        [HarmonyPostfix]
        private static void CreateSortNamePostfix(BaseItem __instance, ref ReadOnlySpan<char> __result)
        {
            if (__instance.SupportsUserData && __instance.EnableAlphaNumericSorting && !(__instance is IHasSeries) &&
                (__instance is Video || __instance is Audio || __instance is IItemByName ||
                 __instance is Folder) && !__instance.IsFieldLocked(MetadataFields.SortName))
            {
                var result = new string(__result);

                if (!IsChineseNoJapanese(result)) return;

                var nameToProcess = __instance is BoxSet ? RemoveDefaultCollectionName(result) : result;

                __result = ConvertToPinyinInitials(nameToProcess, __instance is MusicArtist).AsSpan();
            }
        }

        [HarmonyPostfix]
        private static void GetPrefixesPostfix(object request, ref object __result)
        {
            if (__result is NameValuePair[] pairs)
            {
                var validChars = new HashSet<char>("#ABCDEFGHIJKLMNOPQRSTUVWXYZ");

                var filteredPairs = pairs.Where(p => p.Name?.Length == 1 && validChars.Contains(p.Name[0])).ToArray();

                if (filteredPairs.Length != pairs.Length && filteredPairs.Any(p => p.Name[0] != '#'))
                {
                    __result = filteredPairs;
                }
            }
        }
    }
}
