using HarmonyLib;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyNaming;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod
{
    public class BeautifyMissingMetadata : PatchBase<BeautifyMissingMetadata>
    {
        private static readonly string SeasonNumberAndEpisodeNumberExpression =
            "(?<![a-z]|[0-9])(?<seasonnumber>[0-9]+)(?:[ ._x-]*e|x|[ ._-]*ep[._ -]*|[ ._-]*episode[._ -]+)";

        public BeautifyMissingMetadata()
        {
            if (Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.BeautifyMissingMetadata)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _getBaseItemDtos, postfix: nameof(GetBaseItemDtosPostfix));
            PatchUnpatch(PatchTracker, apply, _getBaseItemDto, postfix: nameof(GetBaseItemDtoPostfix));
            PatchUnpatch(PatchTracker, apply, _getMainExpression, postfix: nameof(GetMainExpressionPostfix));
        }

        [HarmonyPostfix]
        private static void GetBaseItemDtosPostfix(BaseItem[] items, ref BaseItemDto[] __result)
        {
            if (items.Length == 0) return;

            var checkItem = items.FirstOrDefault();

            if (checkItem is null) return;

            if (checkItem.ExtraType == ExtraType.AdditionalPart)
            {
                var videoCount = __result.Count(i => i.Type == nameof(Video));

                foreach (var (currentItem, index) in __result.Where(i => i.Type == nameof(Video))
                             .Select((currentItem, index) => (currentItem, index)))
                {
                    if (videoCount == 1)
                    {
                        currentItem.Name = "下部分";
                        return;
                    }

                    currentItem.Name = $"第{index + 2}部分";
                }

                return;
            }

            if (checkItem is Episode)
            {
                var episodes = !string.IsNullOrEmpty(checkItem.FileNameWithoutExtension)
                    ? items
                    : Plugin.LibraryApi.GetItemsByIds(items.Select(i => i.InternalId).ToArray());

                foreach (var (currentItem, index) in episodes.Select((currentItem, index) => (currentItem, index)))
                {
                    if (currentItem.IndexNumber.HasValue && string.Equals(currentItem.Name,
                            currentItem.FileNameWithoutExtension, StringComparison.Ordinal))
                    {
                        var matchItem = __result[index];
                        matchItem.Name = $"第 {currentItem.IndexNumber} 集";
                    }
                }
            }
        }

        [HarmonyPostfix]
        private static void GetBaseItemDtoPostfix(BaseItem item, DtoOptions options, User user,
            ref BaseItemDto __result)
        {
            if (item is Episode && item.IndexNumber.HasValue &&
                string.Equals(item.Name, item.FileNameWithoutExtension, StringComparison.Ordinal))
            {
                __result.Name = $"第 {item.IndexNumber} 集";
            }
        }

        [HarmonyPostfix]
        private static void GetMainExpressionPostfix(ref string __result, bool allowEpisodeNumberOnly,
            bool allowMultiEpisodeNumberOnlyExpression, bool allowX)
        {
            if (allowEpisodeNumberOnly && !allowMultiEpisodeNumberOnlyExpression && allowX)
            {
                __result = Regex.Replace(__result, Regex.Escape(SeasonNumberAndEpisodeNumberExpression) + @"\|?", "");
            }
        }
    }
}
