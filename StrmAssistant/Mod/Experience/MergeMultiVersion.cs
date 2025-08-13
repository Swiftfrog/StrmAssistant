using Emby.Api;
using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant.Provider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.ExperienceEnhanceOptions;
using static StrmAssistant.Reflection.EmbyApi;
using static StrmAssistant.Reflection.EmbyNaming;
using static StrmAssistant.Reflection.EmbyProviders;
using static StrmAssistant.Reflection.MediaBrowser;

namespace StrmAssistant.Mod.Experience
{
    public class MergeMultiVersion : PatchBase<MergeMultiVersion>
    {
        public static readonly AsyncLocal<BaseItem[]> CurrentAllCollectionFolders = new AsyncLocal<BaseItem[]>();

        public MergeMultiVersion()
        {
            if (Plugin.Instance.ExperienceEnhanceStore.GetOptions().MergeMultiVersion)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _isEligibleForMultiVersion,
                prefix: nameof(IsEligibleForMultiVersionPrefix));
            PatchUnpatch(PatchTracker, apply, _canRefreshImage, prefix: nameof(CanRefreshImagePrefix));
            PatchUnpatch(PatchTracker, apply, _addLibrariesToPresentationUniqueKey,
                prefix: nameof(AddLibrariesToPresentationUniqueKeyPrefix));
            PatchUnpatch(PatchTracker, apply, _getRefreshOptions, postfix: nameof(GetRefreshOptionsPostfix));
        }

        [HarmonyPrefix]
        private static bool IsEligibleForMultiVersionPrefix(string folderName, string testFilename, ref bool __result)
        {
            __result = string.Equals(folderName, Path.GetFileName(Path.GetDirectoryName(testFilename)),
                StringComparison.OrdinalIgnoreCase);

            return false;
        }

        private static BaseItem[] GetAllCollectionFolders(Series series)
        {
            var providerIds = new List<KeyValuePair<string, string>>();

            var tmdbId = series.GetProviderId(MetadataProviders.Tmdb);
            if (!string.IsNullOrEmpty(tmdbId))
            {
                providerIds.Add(new KeyValuePair<string, string>(MetadataProviders.Tmdb.ToString(), tmdbId));
            }

            var imdbId = series.GetProviderId(MetadataProviders.Imdb);
            if (!string.IsNullOrEmpty(imdbId))
            {
                providerIds.Add(new KeyValuePair<string, string>(MetadataProviders.Imdb.ToString(), imdbId));
            }

            var tvdbId = series.GetProviderId(MetadataProviders.Tvdb);
            if (!string.IsNullOrEmpty(tvdbId))
            {
                providerIds.Add(new KeyValuePair<string, string>(MetadataProviders.Tvdb.ToString(), tvdbId));
            }

            if (providerIds.Count == 0) return Array.Empty<BaseItem>();

            var allSeries = BaseItem.LibraryManager.GetItemList(new InternalItemsQuery
                {
                    EnableTotalRecordCount = false,
                    Recursive = false,
                    ExcludeItemIds = new[] { series.InternalId },
                    IncludeItemTypes = new[] { nameof(Series) },
                    HasAnyProviderId = providerIds.Select(p => p.Key).ToArray(),
                    AnyProviderIdEquals = providerIds
                })
                .Concat(new[] { series })
                .ToList();

            var collectionFolders = allSeries.SelectMany(i => BaseItem.LibraryManager.GetCollectionFolders(i))
                .GroupBy(i => i.InternalId)
                .Select(g => g.First())
                .OrderBy(i => i.InternalId)
                .Cast<BaseItem>()
                .ToArray();

            return collectionFolders;
        }

        [HarmonyPrefix]
        private static void CanRefreshImagePrefix(IImageProvider provider, BaseItem item, LibraryOptions libraryOptions,
            ImageRefreshOptions refreshOptions, bool ignoreMetadataLock, bool ignoreLibraryOptions)
        {
            if (CurrentAllCollectionFolders.Value != null) return;

            if (item.Parent is null && item.ExtraType is null) return;

            if (item is Series series && Plugin.Instance.ExperienceEnhanceStore.GetOptions().MergeSeriesPreference ==
                MergeSeriesScopeOption.GlobalScope)
            {
                CurrentAllCollectionFolders.Value = GetAllCollectionFolders(series);
            }
        }

        [HarmonyPrefix]
        private static bool AddLibrariesToPresentationUniqueKeyPrefix(Series __instance, string key,
            ref BaseItem[] collectionFolders, LibraryOptions libraryOptions, ref string __result)
        {
            if (CurrentAllCollectionFolders.Value != null)
            {
                if (CurrentAllCollectionFolders.Value.Length > 1)
                {
                    collectionFolders = CurrentAllCollectionFolders.Value;
                }

                CurrentAllCollectionFolders.Value = null;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void GetRefreshOptionsPostfix(RefreshItem request, MetadataRefreshOptions __result)
        {
            var item = BaseItem.LibraryManager.GetItemById(request.Id);

            if (item is Series || item is Season)
            {
                var series = item as Series ?? (item as Season).Series;
                var seriesTmdbId = series?.GetProviderId(MetadataProviders.Tmdb);
                var episodeGroupId = series?.GetProviderId(MovieDbEpisodeGroupExternalId.StaticName)?.Trim();

                var itemsToRefresh = BaseItem.LibraryManager.GetItemList(new InternalItemsQuery
                {
                    PresentationUniqueKey = item.PresentationUniqueKey,
                    ExcludeItemIds = new[] { item.InternalId }
                });

                foreach (var alt in itemsToRefresh)
                {
                    if (!string.IsNullOrEmpty(episodeGroupId))
                    {
                        var altSeries = alt as Series ?? (alt as Season)?.Series;

                        if (altSeries != null)
                        {
                            var altSeriesTmdbId = altSeries.GetProviderId(MetadataProviders.Tmdb);
                            var altEpisodeGroupId = altSeries.GetProviderId(MovieDbEpisodeGroupExternalId.StaticName);
                            if (string.IsNullOrEmpty(altEpisodeGroupId) && !string.IsNullOrEmpty(seriesTmdbId) &&
                                !string.IsNullOrEmpty(altSeriesTmdbId) && string.Equals(seriesTmdbId, altSeriesTmdbId,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                alt.SetProviderId(MovieDbEpisodeGroupExternalId.StaticName, episodeGroupId);
                                alt.UpdateToRepository(ItemUpdateType.MetadataEdit);
                            }
                        }
                    }

                    BaseItem.ProviderManager.QueueRefresh(alt.InternalId, __result, RefreshPriority.Normal, true);
                }
            }
        }
    }
}
