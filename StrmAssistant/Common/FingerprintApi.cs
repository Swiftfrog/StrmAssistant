using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using StrmAssistant.Options;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant.Common
{
    public class FingerprintApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IItemRepository _itemRepository;
        private readonly IProviderManager _providerManager;

        public static List<string> LibraryPathsInScope;

        // 移除所有 Harmony/Reflection 相关字段
        // private static readonly PatchTracker PatchTracker = ...
        // private readonly object _audioFingerprintManager;

        public FingerprintApi(
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IItemRepository itemRepository,
            IProviderManager providerManager) // ← 新增依赖
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _itemRepository = itemRepository;
            _providerManager = providerManager; // ← 注入

            UpdateLibraryPathsInScope();
        }

        // ✅ 核心方法：不再创建指纹，而是触发 Emby 自带的分析
        public Task CreateTitleFingerprint(Episode item, CancellationToken cancellationToken)
        {
            // 直接让 Emby 自己分析片头（使用标准刷新机制）
            var options = new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableRemoteContentProbe = false,
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ReplaceAllMetadata = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                EnableSubtitleDownloading = false,
                EnableInternetProviders = false,
                // 关键：启用片头检测
                EnableMarkerDetection = true
            };

            _providerManager.QueueRefresh(item.Id, options, RefreshPriority.High);
            return Task.CompletedTask;
        }

        // 为兼容性保留重载
        public Task CreateTitleFingerprint(Episode item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return CreateTitleFingerprint(item, cancellationToken);
        }

        // 移除所有 Stub 方法和反射调用
        // [HarmonyReversePatch] ... => 全部删除

        // 移除 GetAllFingerprintFilesForSeason / UpdateSequencesForSeason 等内部方法
        // 改为：Emby 自动处理，插件只负责触发和读取结果

        public bool IsLibraryInScope(BaseItem item)
        {
            return !string.IsNullOrEmpty(item.Path) && LibraryPathsInScope.Any(l => item.Path.StartsWith(l));
        }

        public void UpdateLibraryPathsInScope(string currentScope)
        {
            var validLibraryIds = GetValidLibraryIds(currentScope);

            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => f.LibraryOptions.EnableMarkerDetection &&
                            (f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null) &&
                            (!validLibraryIds.Any() || validLibraryIds.All(id => id == "-1") ||
                             validLibraryIds.Contains(f.Id)))
                .ToList();

            LibraryPathsInScope = libraries.SelectMany(l => l.Locations)
                .Select(ls => ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                    ? ls
                    : ls + Path.DirectorySeparatorChar)
                .ToList();
        }

        public void UpdateLibraryPathsInScope()
        {
            UpdateLibraryPathsInScope(Plugin.Instance.IntroSkipStore.GetOptions().MarkerEnabledLibraryScope);
        }

        public HashSet<long> GetBlacklistSeasons()
        {
            var blacklistShowIds = Plugin.Instance.IntroSkipStore.GetOptions()
                .FingerprintBlacklistShows.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => long.TryParse(part.Trim(), out var id) ? id : (long?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToArray();

            if (!blacklistShowIds.Any()) return new HashSet<long>();

            var items = _libraryManager.GetItemList(new InternalItemsQuery { ItemIds = blacklistShowIds });

            var seasons = items.OfType<Season>().Select(s => s.InternalId).ToList();
            seasons.AddRange(items.OfType<Series>()
                .SelectMany(series => _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { nameof(Season) },
                        ParentWithPresentationUniqueKeyFromItemId = series.InternalId
                    })
                    .OfType<Season>()
                    .Select(s => s.InternalId)));

            return new HashSet<long>(seasons);
        }

        public long[] GetFavoriteSeasons(HashSet<long> blacklistSeasons)
        {
            var favorites = LibraryApi.AllUsers.Select(e => e.Key)
                .SelectMany(u => _libraryManager.GetItemList(new InternalItemsQuery
                {
                    User = u,
                    IsFavorite = true,
                    IncludeItemTypes = new[] { nameof(Series), nameof(Episode) },
                    PathStartsWithAny = LibraryPathsInScope.ToArray()
                }))
                .GroupBy(i => i.InternalId)
                .Select(g => g.First())
                .ToList();

            var expanded = Plugin.LibraryApi.ExpandFavorites(favorites, false, null, false).OfType<Episode>();

            var result = expanded.GroupBy(e => e.ParentId).Select(g => g.Key).ToArray();
            result = result.Where(s => !blacklistSeasons.Contains(s)).ToArray();

            return result;
        }

        public List<Episode> FetchFingerprintQueueItems(List<BaseItem> items)
        {
            var libraryIds = Plugin.Instance.IntroSkipStore.GetOptions().MarkerEnabledLibraryScope?
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToArray();

            var includeFavorites = libraryIds?.Contains("-1") == true;

            var resultItems = new List<Episode>();
            var incomingItems = items.OfType<Episode>().ToList();

            if (IsCatchupTaskSelected(GeneralOptions.CatchupTask.Fingerprint) && LibraryPathsInScope.Any())
            {
                if (includeFavorites)
                {
                    resultItems = Plugin.LibraryApi.ExpandFavorites(items, true, null, false).OfType<Episode>()
                        .ToList();
                }

                if (libraryIds is null || !libraryIds.Any() || libraryIds.Any(id => id != "-1"))
                {
                    var filteredItems = incomingItems
                        .Where(i => LibraryPathsInScope.Any(p => i.ContainingFolderPath.StartsWith(p)))
                        .ToList();
                    resultItems = resultItems.Concat(filteredItems).ToList();
                }
            }

            var unlockIntroSkip = Plugin.Instance.IntroSkipStore.GetOptions().UnlockIntroSkip;
            resultItems = resultItems.Where(i => unlockIntroSkip || !i.IsShortcut).GroupBy(i => i.InternalId)
                .Select(g => g.First()).ToList();

            var unprocessedItems = FilterUnprocessed(resultItems);

            return unprocessedItems;
        }

        private List<Episode> FilterUnprocessed(List<Episode> items)
        {
            var enableImageCapture = Plugin.Instance.MediaInfoExtractStore.GetOptions().EnableImageCapture;
            var blacklistSeasons = GetBlacklistSeasons();

            var results = new List<Episode>();

            foreach (var item in items)
            {
                if (blacklistSeasons.Contains(item.ParentId) || LibraryApi.IsExtractExclude(item))
                {
                    continue;
                }

                if (Plugin.LibraryApi.IsExtractNeeded(item, enableImageCapture))
                {
                    results.Add(item);
                }
                else if (!IsExtractExclude(item) && IsExtractNeeded(item))
                {
                    results.Add(item);
                }
            }

            _logger.Info("IntroFingerprintExtract - Number of items: " + results.Count);

            return results;
        }

        private bool IsExtractExclude(BaseItem item)
        {
            var length = _libraryManager.GetLibraryOptions(item).IntroDetectionFingerprintLength;

            if (length < 2 || length > 20) return true;

            return !item.RunTimeTicks.HasValue || item.RunTimeTicks < TimeSpan.FromMinutes(length).Ticks;
        }

        public bool IsExtractNeeded(BaseItem item)
        {
            return !Plugin.MediaInfoApi.HasIntro(item) &&
                   string.IsNullOrEmpty(_itemRepository.GetIntroDetectionFailureResult(item.InternalId));
        }

        public List<Episode> FetchIntroPreExtractTaskItems(HashSet<long> blacklistSeasons)
        {
            var markerEnabledLibraryScope = Plugin.Instance.IntroSkipStore.GetOptions().MarkerEnabledLibraryScope;

            var itemsFingerprintQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                Recursive = true,
                GroupByPresentationUniqueKey = false,
                HasPath = true,
                HasAudioStream = false,
            };

            if (!string.IsNullOrEmpty(markerEnabledLibraryScope) && markerEnabledLibraryScope.Contains("-1"))
            {
                itemsFingerprintQuery.ParentIds = GetFavoriteSeasons(blacklistSeasons).DefaultIfEmpty(-1).ToArray();
            }
            else
            {
                if (LibraryPathsInScope.Any())
                {
                    itemsFingerprintQuery.PathStartsWithAny = LibraryPathsInScope.ToArray();
                }

                if (blacklistSeasons.Any())
                {
                    itemsFingerprintQuery.ExcludeParentIds = blacklistSeasons.ToArray();
                }
            }

            var unlockIntroSkip = Plugin.Instance.IntroSkipStore.GetOptions().UnlockIntroSkip;
            var items = _libraryManager.GetItemList(itemsFingerprintQuery)
                .Where(i => (unlockIntroSkip || !i.IsShortcut) && !LibraryApi.IsExtractExclude(i))
                .OfType<Episode>()
                .ToList();

            return items;
        }

        public List<Episode> FetchIntroFingerprintTaskItems(HashSet<long> blacklistSeasons)
        {
            var libraryIds = Plugin.Instance.IntroSkipStore.GetOptions()
                .MarkerEnabledLibraryScope.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            var librariesWithMarkerDetection = _libraryManager.GetVirtualFolders()
                .Where(f => (f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null) &&
                            f.LibraryOptions.EnableMarkerDetection)
                .ToList();
            var librariesSelected = librariesWithMarkerDetection.Where(f => libraryIds.Contains(f.Id)).ToList();

            _logger.Info("IntroFingerprintExtract - LibraryScope: " + (!librariesWithMarkerDetection.Any()
                ? "NONE"
                : string.Join(", ",
                    (libraryIds.Contains("-1")
                        ? new[] { Resources.Favorites }.Concat(librariesSelected.Select(l => l.Name))
                        : librariesSelected.Select(l => l.Name)).DefaultIfEmpty("ALL"))));

            var introDetectionFingerprintMinutes =
                Plugin.Instance.IntroSkipStore.GetOptions().IntroDetectionFingerprintMinutes;
            _logger.Info("Intro Detection Fingerprint Length (Minutes): " + introDetectionFingerprintMinutes);

            var itemsFingerprintQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                Recursive = true,
                GroupByPresentationUniqueKey = false,
                WithoutChapterMarkers = new[] { MarkerType.IntroStart },
                MinRunTimeTicks = TimeSpan.FromMinutes(introDetectionFingerprintMinutes).Ticks,
                HasIntroDetectionFailure = false,
                HasAudioStream = true
            };

            if (libraryIds.Contains("-1") && libraryIds.All(i => i == "-1"))
            {
                itemsFingerprintQuery.ParentIds = GetFavoriteSeasons(blacklistSeasons).DefaultIfEmpty(-1).ToArray();
            }
            else
            {
                if (LibraryPathsInScope.Any())
                {
                    itemsFingerprintQuery.PathStartsWithAny = LibraryPathsInScope.ToArray();
                }

                if (blacklistSeasons.Any())
                {
                    itemsFingerprintQuery.ExcludeParentIds = blacklistSeasons.ToArray();
                }
            }

            var unlockIntroSkip = Plugin.Instance.IntroSkipStore.GetOptions().UnlockIntroSkip;
            var items = _libraryManager.GetItemList(itemsFingerprintQuery)
                .Where(i => (unlockIntroSkip || !i.IsShortcut) && !IsExtractExclude(i))
                .OfType<Episode>()
                .ToList();

            return items;
        }

        public void UpdateLibraryIntroDetectionFingerprintLength(int currentLength)
        {
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null)
                .ToList();

            foreach (var library in libraries)
            {
                var options = library.LibraryOptions;

                if (options.IntroDetectionFingerprintLength != currentLength &&
                    long.TryParse(library.ItemId, out var itemId))
                {
                    options.IntroDetectionFingerprintLength = currentLength;
                    CollectionFolder.SaveLibraryOptions(itemId, options);
                }
            }
        }

        public void UpdateLibraryIntroDetectionFingerprintLength()
        {
            UpdateLibraryIntroDetectionFingerprintLength(Plugin.Instance.IntroSkipStore.GetOptions()
                .IntroDetectionFingerprintMinutes);
        }

        // 移除 UpdateIntroMarkerForSeason 方法（Emby 自动处理）
        // 或者保留但改为触发刷新
        public async Task UpdateIntroMarkerForSeason(Season season, CancellationToken cancellationToken,
            IProgress<double>? progress = null)
        {
            // 触发整个季的片头分析
            var options = new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableMarkerDetection = true,
                MetadataRefreshMode = MetadataRefreshMode.Default
            };

            _providerManager.QueueRefresh(season.Id, options, RefreshPriority.High);
            await Task.Delay(100, cancellationToken); // 避免阻塞
        }

        public void ClearFingerprintCache(BaseItem item)
        {
            // 清除 Emby 的片头失败记录
            _itemRepository.LogIntroDetectionFailureFailure(item.InternalId, 0);

            // 可选：清除章节标记
            var chapters = _itemRepository.GetChapters(item);
            var filtered = chapters.Where(c => c.MarkerType != MarkerType.IntroStart && c.MarkerType != MarkerType.IntroEnd).ToList();
            _itemRepository.SaveChapters(item.InternalId, filtered);
        }
    }
}
