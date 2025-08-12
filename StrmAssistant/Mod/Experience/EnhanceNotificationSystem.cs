using Emby.Notifications;
using HarmonyLib;
using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyApi;
using static StrmAssistant.Reflection.EmbyNotifications;
using static StrmAssistant.Reflection.EmbyServerImplementations;
using static StrmAssistant.Reflection.MediaBrowser;

namespace StrmAssistant.Mod.Experience
{
    public class EnhanceNotificationSystem: PatchBase<EnhanceNotificationSystem>
    {
        private static readonly AsyncLocal<Dictionary<long, List<(int? IndexNumber, int? ParentIndexNumber)>>>
            GroupDetails = new AsyncLocal<Dictionary<long, List<(int? IndexNumber, int? ParentIndexNumber)>>>();
        private static readonly AsyncLocal<string> Description = new AsyncLocal<string>();
        private static readonly AsyncLocal<User> DeleteByUser = new AsyncLocal<User>();

        public EnhanceNotificationSystem()
        {
            Initialize();

            if (Plugin.Instance.ExperienceEnhanceStore.GetOptions().EnhanceNotificationSystem)
            {
                Patch();
            }
        }

        protected override void OnInitialize()
        {
            ReversePatch(PatchTracker, _getUserForRequest, nameof(GetUserForRequestStub));
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _convertToGroups, postfix: nameof(ConvertToGroupsPostfix));
            PatchUnpatch(PatchTracker, apply, _sendNotification, prefix: nameof(SendNotificationPrefix));
            PatchUnpatch(PatchTracker, apply, _queueNotification, prefix: nameof(QueueNotificationPrefix));
            PatchUnpatch(PatchTracker, apply, _deleteItemsRequest, prefix: nameof(DeleteItemsRequestPrefix),
                postfix: nameof(DeleteItemsRequestPostfix));
            PatchUnpatch(PatchTracker, apply, _deleteItem, prefix: nameof(DeleteItemPrefix),
                finalizer: nameof(DeleteItemFinalizer));
        }

        [HarmonyPostfix]
        private static void ConvertToGroupsPostfix(ItemChangeEventArgs[] list,
            ref Dictionary<long, List<ItemChangeEventArgs>> __result)
        {
            var filteredItems = list.Where(i => i.Item.SeriesId != 0L).ToArray();

            if (filteredItems.Length == 0) return;

            GroupDetails.Value = filteredItems.GroupBy(i => i.Item.SeriesId)
                .ToDictionary(g => g.Key, g => g.Select(i => (i.Item.IndexNumber, i.Item.ParentIndexNumber)).ToList());
        }

        [HarmonyPrefix]
        private static void SendNotificationPrefix(INotifier notifier, NotificationInfo[] notifications,
            NotificationRequest request, bool enableUserDataInDto)
        {
            if (notifications.FirstOrDefault()?.GroupItems is true
                && request.Item is Series series && GroupDetails.Value != null
                && GroupDetails.Value.TryGetValue(series.InternalId, out var groupDetails))
            {
                var groupedBySeason = groupDetails.Where(e => e.ParentIndexNumber.HasValue)
                    .GroupBy(e => e.ParentIndexNumber)
                    .OrderBy(g => g.Key)
                    .ToList();

                var descriptions = new List<string>();

                foreach (var seasonGroup in groupedBySeason)
                {
                    var seasonIndex = seasonGroup.Key;
                    var episodesBySeason = seasonGroup
                        .Where(e => e.IndexNumber.HasValue)
                        .OrderBy(e => e.IndexNumber.Value)
                        .Select(e => e.IndexNumber.Value)
                        .Distinct()
                        .ToList();

                    if (!episodesBySeason.Any()) continue;

                    var episodeRanges = new List<string>();
                    var rangeStart = episodesBySeason[0];
                    var lastEpisodeInRange = rangeStart;

                    for (var i = 1; i < episodesBySeason.Count; i++)
                    {
                        var current = episodesBySeason[i];
                        if (current != lastEpisodeInRange + 1)
                        {
                            episodeRanges.Add(rangeStart == lastEpisodeInRange
                                ? $"E{rangeStart:D2}"
                                : $"E{rangeStart:D2}-E{lastEpisodeInRange:D2}");
                            rangeStart = current;
                        }

                        lastEpisodeInRange = current;
                    }

                    episodeRanges.Add(rangeStart == lastEpisodeInRange
                        ? $"E{rangeStart:D2}"
                        : $"E{rangeStart:D2}-E{lastEpisodeInRange:D2}");

                    descriptions.Add($"S{seasonIndex:D2} {string.Join(", ", episodeRanges)}");
                }

                var summary = string.Join(" / ", descriptions);

                var tmdbId = series.GetProviderId(MetadataProviders.Tmdb);

                if (!string.IsNullOrEmpty(tmdbId))
                {
                    summary += $"{Environment.NewLine}{Environment.NewLine}TmdbId: {tmdbId}";
                }

                Description.Value = summary;
            }
        }

        [HarmonyPrefix]
        private static void QueueNotificationPrefix(INotifier sender, InternalNotificationRequest request, int priority)
        {
            if (!string.IsNullOrEmpty(Description.Value))
            {
                request.Description = Description.Value;
                Description.Value = null;
            }
        }

        [HarmonyReversePatch]
        private static User GetUserForRequestStub(BaseApiService instance, string requestedUserId,
            bool autoRevertToLoggedInUser = true) => throw new NotImplementedException();

        [HarmonyPrefix]
        private static void DeleteItemsRequestPrefix(BaseApiService __instance, IReturnVoid request)
        {
            DeleteByUser.Value = GetUserForRequestStub(__instance, null);
        }

        [HarmonyPostfix]
        private static void DeleteItemsRequestPostfix(BaseApiService __instance, IReturnVoid request)
        {
            DeleteByUser.Value = null;
        }

        [HarmonyPrefix]
        private static void DeleteItemPrefix(ILibraryManager __instance, BaseItem item, DeleteOptions options,
            BaseItem parent, bool notifyParentItem, out Dictionary<string, bool?> __state)
        {
            __state = null;

            if (options.DeleteFileLocation)
            {
                var collectionFolder = options.CollectionFolders ?? __instance.GetCollectionFolders(item);
                var scope = item.GetDeletePaths(true, collectionFolder).Select(i => i.FullName).ToArray();

                __state = Plugin.LibraryApi.PrepareDeepDelete(item, scope);
            }
        }

        [HarmonyFinalizer]
        private static void DeleteItemFinalizer(Exception __exception, BaseItem item, Dictionary<string, bool?> __state)
        {
            if (__state != null && __state.Count > 0 && __exception is null && DeleteByUser.Value != null)
            {
                var user = DeleteByUser.Value;

                Task.Run(() => Plugin.NotificationApi.DeepDeleteSendNotification(item, user,
                        new HashSet<string>(__state.Keys), new List<string> { item.Path }))
                    .ConfigureAwait(false);
            }
        }
    }
}
