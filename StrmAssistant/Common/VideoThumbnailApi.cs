using HarmonyLib;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Options.Utility;
using static StrmAssistant.Reflection.EmbyProviders;

namespace StrmAssistant.Common
{
    public class VideoThumbnailApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        private static readonly PatchTracker PatchTracker = new PatchTracker(typeof(VideoThumbnailApi),
            Plugin.Instance.IsModSupported ? PatchApproach.Harmony : PatchApproach.Reflection);

        private readonly object _thumbnailGenerator;

        internal VideoThumbnailApi(ILibraryManager libraryManager, IFileSystem fileSystem,
            IImageExtractionManager imageExtractionManager, IItemRepository itemRepository,
            IMediaMountManager mediaMountManager, IServerApplicationPaths applicationPaths,
            ILibraryMonitor libraryMonitor, IFfmpegManager ffmpegManager)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;

            try
            {
                _thumbnailGenerator = _thumbnailGeneratorConstructor?.Invoke(new object[]
                {
                    fileSystem, _logger, imageExtractionManager, itemRepository, mediaMountManager,
                    applicationPaths, libraryMonitor, ffmpegManager
                });
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            if (_thumbnailGenerator is null || _refreshThumbnailImages is null)
            {
                _logger.Warn($"{PatchTracker.PatchType.Name} Init Failed");
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
            }
            else if (Plugin.Instance.IsModSupported)
            {
                PatchManager.ReversePatch(PatchTracker, _refreshThumbnailImages,
                    AppVer >= Ver49036 ? nameof(RefreshThumbnailImagesStub49) : nameof(RefreshThumbnailImagesStub48));
            }
        }

#pragma warning disable CS1998
        [HarmonyReversePatch]
        private static async Task<bool> RefreshThumbnailImagesStub49(object instance, Video item,
            MediaSourceInfo mediaSource, MediaStream videoStream, LibraryOptions libraryOptions,
            IDirectoryService directoryService, List<ChapterInfo> chapters, bool extractImages, bool saveChapters,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        [HarmonyReversePatch]
        private static async Task<bool> RefreshThumbnailImagesStub48(object instance, Video item, MediaStream videoStream,
            LibraryOptions libraryOptions, IDirectoryService directoryService, List<ChapterInfo> chapters,
            bool extractImages, bool saveChapters, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
#pragma warning restore CS1998

        public Task<bool> RefreshThumbnailImages(Video item, LibraryOptions libraryOptions,
            IDirectoryService directoryService, List<ChapterInfo> chapters, bool extractImages, bool saveChapters,
            CancellationToken cancellationToken)
        {
            var mediaSource = AppVer >= Ver49036
                ? item.GetMediaSources(false, false, libraryOptions).FirstOrDefault()
                : null;

            switch (PatchTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    return AppVer >= Ver49036
                        ? RefreshThumbnailImagesStub49(_thumbnailGenerator, item, mediaSource, null, libraryOptions,
                            directoryService, chapters, extractImages, saveChapters, cancellationToken)
                        : RefreshThumbnailImagesStub48(_thumbnailGenerator, item, null, libraryOptions,
                            directoryService, chapters, extractImages, saveChapters, cancellationToken);
                case PatchApproach.Reflection:
                {
                    var parameters = AppVer >= Ver49036
                        ? new object[]
                        {
                            item, mediaSource, null, libraryOptions, directoryService, chapters, extractImages,
                            saveChapters, cancellationToken
                        }
                        : new object[]
                        {
                            item, null, libraryOptions, directoryService, chapters, extractImages, saveChapters,
                            cancellationToken
                        };

                    return (Task<bool>)_refreshThumbnailImages.Invoke(_thumbnailGenerator, parameters);
                }
                default:
                    throw new NotImplementedException();
            }
        }

        public List<Video> FetchExtractTaskItems()
        {
            var libraryIds = Plugin.Instance.MediaInfoExtractStore.GetOptions()
                .LibraryScope.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            var librariesWithVideoThumbnail = _libraryManager.GetVirtualFolders()
                .Where(f => f.LibraryOptions.EnableChapterImageExtraction)
                .ToList();
            var librariesSelected = librariesWithVideoThumbnail.Where(f => libraryIds.Contains(f.Id)).ToList();

            _logger.Info("VideoThumbnailExtract - LibraryScope: " + (!librariesWithVideoThumbnail.Any()
                ? "NONE"
                : string.Join(", ",
                    (libraryIds.Contains("-1")
                        ? new[] { Resources.Favorites }.Concat(librariesSelected.Select(l => l.Name))
                        : librariesSelected.Select(l => l.Name)).DefaultIfEmpty("ALL"))));

            var includeExtra = Plugin.Instance.MediaInfoExtractStore.GetOptions().IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);

            var librariesWithVideoThumbnailPaths = librariesWithVideoThumbnail.SelectMany(l => l.Locations)
                .Select(ls =>
                    ls.EndsWith(Path.DirectorySeparatorChar.ToString()) ? ls : ls + Path.DirectorySeparatorChar)
                .ToArray();
            var librariesSelectedPaths = librariesSelected.SelectMany(l => l.Locations)
                .Select(ls =>
                    ls.EndsWith(Path.DirectorySeparatorChar.ToString()) ? ls : ls + Path.DirectorySeparatorChar)
                .ToArray();

            var favoritesWithExtra = Array.Empty<BaseItem>();

            if (libraryIds.Contains("-1") && librariesWithVideoThumbnailPaths.Any())
            {
                var favorites = LibraryApi.AllUsers.Select(e => e.Key)
                    .SelectMany(user => _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        User = user,
                        IsFavorite = true,
                        PathStartsWithAny = librariesWithVideoThumbnailPaths
                    })).GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

                var expanded = Plugin.LibraryApi.ExpandFavorites(favorites, false, false, false);

                favoritesWithExtra = expanded
                    .Concat(includeExtra
                        ? expanded.SelectMany(f => f.GetExtras(IncludeExtraTypes))
                        : Enumerable.Empty<BaseItem>())
                    .Where(i => Plugin.LibraryApi.HasMediaInfo(i) && !i.HasImage(ImageType.Chapter))
                    .ToArray();
            }

            var items = Array.Empty<BaseItem>();
            var extras = Array.Empty<BaseItem>();

            if (!libraryIds.Any() && librariesWithVideoThumbnailPaths.Any() ||
                libraryIds.Any(id => id != "-1") && librariesSelectedPaths.Any())
            {
                var videoThumbnailQuery = new InternalItemsQuery
                {
                    MediaTypes = new[] { MediaType.Video },
                    HasAudioStream = true,
                    HasChapterImages = false,
                    PathStartsWithAny = !libraryIds.Any() ? librariesWithVideoThumbnailPaths : librariesSelectedPaths
                };

                items = _libraryManager.GetItemList(videoThumbnailQuery);

                if (includeExtra)
                {
                    videoThumbnailQuery.ExtraTypes = IncludeExtraTypes;
                    extras = _libraryManager.GetItemList(videoThumbnailQuery);
                }
            }

            var enableVideoThumbnail = Plugin.Instance.MediaInfoExtractStore.GetOptions().EnableImageCapture;
            var combined = favoritesWithExtra.Concat(items).Concat(extras).GroupBy(i => i.InternalId)
                .Select(g => g.First()).Where(i => enableVideoThumbnail || !i.IsShortcut).OfType<Video>().ToList();

            var filteredItems = FilterUnprocessed(combined);

            return filteredItems;
        }

        private static List<Video> FilterUnprocessed(List<Video> items)
        {
            var results = new List<Video>();

            foreach (var item in items)
            {
                if (!item.MediaContainer.HasValue &&
                    string.Equals(item.Container, "rm", StringComparison.OrdinalIgnoreCase))
                {
                    item.Container = "rmvb";
                }

                if (item.MediaContainer.HasValue &&
                    !VideoThumbnailExcludeMediaContainers.Contains(item.MediaContainer.Value))
                {
                    results.Add(item);
                }
            }

            return results;
        }
    }
}
