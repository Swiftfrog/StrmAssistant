using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using StrmAssistant.Mod;
using StrmAssistant.Mod.MediaInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Common.CommonUtility;

namespace StrmAssistant.Common
{
    public class MediaInfoApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IItemRepository _itemRepository;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly IProviderManager _providerManager;

        private const string MediaInfoFileExtension = "-mediainfo.json";

        internal class MediaSourceWithChapters
        {
            public MediaSourceInfo MediaSourceInfo { get; set; }
            public List<ChapterInfo> Chapters { get; set; } = new List<ChapterInfo>();
            public bool? ZeroFingerprintConfidence { get; set; }
            public string EmbeddedImage { get; set; }
        }

        public MediaInfoApi(
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IProviderManager providerManager,
            IMediaSourceManager mediaSourceManager,
            IItemRepository itemRepository,
            IJsonSerializer jsonSerializer,
            ILibraryMonitor libraryMonitor)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _providerManager = providerManager;
            _mediaSourceManager = mediaSourceManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;

            // 移除所有 Harmony/Reflection 初始化逻辑

            // 保留 .json 忽略逻辑（这个仍然有用）
            try
            {
                var alwaysIgnoreExtensions = libraryMonitor.GetType()
                    .GetField("_alwaysIgnoreExtensions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var currentArray = (string[])alwaysIgnoreExtensions.GetValue(libraryMonitor);
                var newArray = currentArray.Concat(new[] { ".json" }).ToArray();
                alwaysIgnoreExtensions.SetValue(libraryMonitor, newArray);
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }
                _logger.Warn("Failed to patch library monitor to ignore .json files");
            }
        }

        // ✅ 核心方法：直接调用 Emby 4.9+ 公开 API
        public List<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enableAlternateMediaSources)
        {
            var options = _libraryManager.GetLibraryOptions(item);
            return _mediaSourceManager.GetStaticMediaSources(
                item: item,
                enableAlternateMediaSources: enableAlternateMediaSources,
                enablePathSubstitution: false,
                fillMediaStreams: true,      // 必须为 true 才能获取媒体流
                fillChapters: false,         // 本插件不需要章节（章节单独处理）
                collectionFolders: null,
                libraryOptions: options,
                deviceProfile: null,
                user: null,
                cancellationToken: CancellationToken.None
            );
        }

        public MetadataRefreshOptions GetMediaInfoRefreshOptions()
        {
            return new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false
            };
        }

        public IEnumerable<MediaStream> GetMediaStreamsSafe(BaseItem item)
        {
            return ConditionalLock.Run(item.GetMediaStreams);
        }

        public bool HasMediaInfo(BaseItem item)
        {
            if (!item.RunTimeTicks.HasValue) return false;
            if (item.Size == 0L) return false;
            return GetMediaStreamsSafe(item)
                .Any(i => (i.Type == MediaStreamType.Video || i.Type == MediaStreamType.Audio) && !i.IsExternal);
        }

        public IEnumerable<ChapterInfo> GetChaptersSafe(BaseItem item)
        {
            return ConditionalLock.Run(() => _itemRepository.GetChapters(item));
        }

        public bool HasIntro(BaseItem item)
        {
            return GetChaptersSafe(item).Any(c => c.MarkerType == MarkerType.IntroStart);
        }

        public static string GetMediaInfoJsonPath(BaseItem item)
        {
            var jsonRootFolder = Plugin.Instance.MediaInfoExtractStore.GetOptions().MediaInfoJsonRootFolder;
            var relativePath = item.ContainingFolderPath;

            if (!string.IsNullOrEmpty(jsonRootFolder) && Path.IsPathRooted(item.ContainingFolderPath))
            {
                relativePath = Path.GetRelativePath(Path.GetPathRoot(item.ContainingFolderPath)!, item.ContainingFolderPath);
            }

            return item is Folder
                ? !string.IsNullOrEmpty(jsonRootFolder)
                    ? Path.Combine(jsonRootFolder, relativePath)
                    : item.ContainingFolderPath
                : !string.IsNullOrEmpty(jsonRootFolder)
                    ? Path.Combine(jsonRootFolder, relativePath, item.FileNameWithoutExtension + MediaInfoFileExtension)
                    : Path.Combine(item.ContainingFolderPath!, item.FileNameWithoutExtension + MediaInfoFileExtension);
        }

        private async Task<bool> SerializeMediaInfo(BaseItem item, IDirectoryService directoryService, bool overwrite, string source)
        {
            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (overwrite || file?.Exists != true)
            {
                try
                {
                    var options = _libraryManager.GetLibraryOptions(item);
                    var mediaSources = item.GetMediaSources(false, false, options);
                    var chapters = _itemRepository.GetChapters(item);
                    var mediaSourcesWithChapters = mediaSources.Select(mediaSource =>
                            new MediaSourceWithChapters { MediaSourceInfo = mediaSource, Chapters = chapters })
                        .ToList();

                    foreach (var jsonItem in mediaSourcesWithChapters)
                    {
                        jsonItem.MediaSourceInfo.Id = null;
                        jsonItem.MediaSourceInfo.ItemId = null;
                        jsonItem.MediaSourceInfo.Path = null;

                        foreach (var subtitle in jsonItem.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle && m.Protocol == MediaProtocol.File))
                        {
                            subtitle.Path = _fileSystem.GetFileInfo(subtitle.Path).Name;
                        }

                        foreach (var chapter in jsonItem.Chapters)
                        {
                            chapter.ImageTag = null;
                        }

                        if (item is Episode)
                        {
                            jsonItem.ZeroFingerprintConfidence =
                                !string.IsNullOrEmpty(_itemRepository.GetIntroDetectionFailureResult(item.InternalId));
                        }

                        if (item is Audio)
                        {
                            var primaryImageInfo = item.GetImageInfo(ImageType.Primary, 0);
                            if (primaryImageInfo != null && _fileSystem.FileExists(primaryImageInfo.Path))
                            {
                                var imageBytes = await _fileSystem.ReadAllBytesAsync(primaryImageInfo.Path, CancellationToken.None).ConfigureAwait(false);
                                jsonItem.EmbeddedImage = Convert.ToBase64String(imageBytes);
                            }
                        }
                    }

                    var parentDirectory = Path.GetDirectoryName(mediaInfoJsonPath);
                    if (!string.IsNullOrEmpty(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    _jsonSerializer.SerializeToFile(mediaSourcesWithChapters, mediaInfoJsonPath);
                    _logger.Info($"MediaInfoPersist - Serialization Success ({source}): {mediaInfoJsonPath}");
                    return true;
                }
                catch (Exception e)
                {
                    _logger.Error($"MediaInfoPersist - Serialization Failed ({source}): {mediaInfoJsonPath}");
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
            return false;
        }

        public async Task<bool> SerializeMediaInfo(long itemId, IDirectoryService directoryService, bool overwrite, string source)
        {
            var workItem = _libraryManager.GetItemById(itemId);
            if (!Plugin.LibraryApi.IsLibraryInScope(workItem)) return false;
            if (!HasMediaInfo(workItem))
            {
                _logger.Info($"MediaInfoPersist - Serialization Skipped - No MediaInfo ({source})");
                return false;
            }
            if (workItem.Size == 0L || !workItem.RunTimeTicks.HasValue)
            {
                _logger.Info($"MediaInfoPersist - Serialization Skipped - Abnormal MediaInfo ({source})");
                return false;
            }

            var ds = directoryService ?? new DirectoryService(_logger, _fileSystem);
            return await SerializeMediaInfo(workItem, ds, overwrite, source).ConfigureAwait(false);
        }

        public async Task<bool> DeserializeMediaInfo(BaseItem item, IDirectoryService directoryService, string source, bool ignoreFileChange)
        {
            var workItem = _libraryManager.GetItemById(item.InternalId);
            if (HasMediaInfo(workItem)) return true;

            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true)
            {
                try
                {
                    var mediaSourceWithChapters = (await _jsonSerializer
                        .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                        .ConfigureAwait(false)).ToArray()[0];

                    var mediaSource = mediaSourceWithChapters?.MediaSourceInfo;
                    if (mediaSource != null && mediaSource.Size > 0L && mediaSource.RunTimeTicks.HasValue &&
                        (ignoreFileChange || !Plugin.LibraryApi.HasFileChanged(item, directoryService)))
                    {
                        foreach (var subtitle in mediaSourceWithChapters.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle && m.Protocol == MediaProtocol.File))
                        {
                            subtitle.Path = Path.Combine(workItem.ContainingFolderPath, _fileSystem.GetFileInfo(subtitle.Path).Name);
                        }

                        _itemRepository.SaveMediaStreams(item.InternalId, mediaSourceWithChapters.MediaSourceInfo.MediaStreams, CancellationToken.None);

                        if (workItem is Audio && !string.IsNullOrEmpty(mediaSourceWithChapters.EmbeddedImage))
                        {
                            var imageBytes = Convert.FromBase64String(mediaSourceWithChapters.EmbeddedImage);
                            var tempPath = Path.Combine(Plugin.Instance.ApplicationPaths.TempDirectory, Guid.NewGuid() + ".jpg");
                            await _fileSystem.WriteAllBytesAsync(tempPath, imageBytes, CancellationToken.None).ConfigureAwait(false);

                            var libraryOptions = _libraryManager.GetLibraryOptions(workItem);
                            await _providerManager.SaveImage(workItem, libraryOptions, tempPath, ImageType.Primary,
                                    null, Array.Empty<long>(), directoryService, false, CancellationToken.None)
                                .ConfigureAwait(false);
                        }

                        workItem.Size = mediaSourceWithChapters.MediaSourceInfo.Size.GetValueOrDefault();
                        workItem.RunTimeTicks = mediaSourceWithChapters.MediaSourceInfo.RunTimeTicks;
                        workItem.Container = mediaSourceWithChapters.MediaSourceInfo.Container;
                        workItem.TotalBitrate = mediaSourceWithChapters.MediaSourceInfo.Bitrate.GetValueOrDefault();

                        var videoStream = mediaSourceWithChapters.MediaSourceInfo.MediaStreams
                            .Where(s => s.Type == MediaStreamType.Video && s.Width.HasValue && s.Height.HasValue)
                            .OrderByDescending(s => (long)s.Width.Value * s.Height.Value)
                            .FirstOrDefault();

                        if (videoStream != null)
                        {
                            workItem.Width = videoStream.Width.GetValueOrDefault();
                            workItem.Height = videoStream.Height.GetValueOrDefault();
                        }

                        _libraryManager.UpdateItems(new List<BaseItem> { workItem }, null,
                            ItemUpdateType.MetadataImport, false, false, null, CancellationToken.None);

                        if (workItem is Video video)
                        {
                            PersistMediaInfoHelper.BypassChapterInstance(video);
                            await DeserializeChapterInfo(video, mediaSourceWithChapters.Chapters, directoryService, source).ConfigureAwait(false);

                            if (video is Episode && mediaSourceWithChapters.ZeroFingerprintConfidence is true)
                            {
                                _itemRepository.LogIntroDetectionFailureFailure(video.InternalId, item.DateModified.ToUnixTimeSeconds());
                            }
                        }

                        _logger.Info($"MediaInfoPersist - Deserialization Success ({source}): {mediaInfoJsonPath}");
                        return true;
                    }

                    _logger.Info($"MediaInfoPersist - Deserialization Skipped - Abnormal MediaInfo ({source}): {mediaInfoJsonPath}");
                }
                catch (Exception e)
                {
                    _logger.Error($"MediaInfoPersist - Deserialization Failed ({source}): {mediaInfoJsonPath}");
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
            return false;
        }

        public void DeleteMediaInfoJson(BaseItem item, IDirectoryService directoryService, string source)
        {
            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists is true)
            {
                try
                {
                    _logger.Info($"MediaInfoPersist - Attempting to delete file ({source}): {mediaInfoJsonPath}");
                    _fileSystem.DeleteFile(mediaInfoJsonPath);

                    var jsonRoot = Plugin.Instance.MediaInfoExtractStore.GetOptions().MediaInfoJsonRootFolder;
                    if (!string.IsNullOrEmpty(jsonRoot))
                    {
                        jsonRoot = _fileSystem.GetFullPath(jsonRoot).TrimEnd(Path.DirectorySeparatorChar);
                        var currentDir = _fileSystem.GetFullPath(_fileSystem.GetDirectoryName(mediaInfoJsonPath) ?? string.Empty);

                        while (!string.IsNullOrEmpty(currentDir) &&
                               !string.Equals(currentDir, jsonRoot, StringComparison.OrdinalIgnoreCase) &&
                               IsDirectoryEmpty(currentDir))
                        {
                            _logger.Info($"MediaInfoPersist - Attempting to delete empty folder ({source}): {currentDir}");
                            _fileSystem.DeleteDirectory(currentDir, false);
                            currentDir = Path.GetDirectoryName(currentDir);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
        }

        public void DeleteMediaInfoJson(string folderPath, string source)
        {
            var jsonRoot = Plugin.Instance.MediaInfoExtractStore.GetOptions().MediaInfoJsonRootFolder;
            if (!string.IsNullOrEmpty(jsonRoot) && _fileSystem.DirectoryExists(folderPath) && folderPath.StartsWith(jsonRoot))
            {
                try
                {
                    _logger.Info($"MediaInfoPersist - Attempting to delete folder ({source}): {folderPath}");
                    _fileSystem.DeleteDirectory(folderPath, true);

                    jsonRoot = _fileSystem.GetFullPath(jsonRoot).TrimEnd(Path.DirectorySeparatorChar);
                    var currentDir = folderPath;

                    while (!string.IsNullOrEmpty(currentDir) &&
                           !string.Equals(currentDir, jsonRoot, StringComparison.OrdinalIgnoreCase) &&
                           IsDirectoryEmpty(currentDir))
                    {
                        _logger.Info($"MediaInfoPersist - Attempting to delete empty folder ({source}): {currentDir}");
                        _fileSystem.DeleteDirectory(currentDir, false);
                        currentDir = Path.GetDirectoryName(currentDir);
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
        }

        public async Task<bool> DeserializeIntroMarker(Episode item, IDirectoryService directoryService, string source)
        {
            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true)
            {
                try
                {
                    var mediaSourceWithChapters = (await _jsonSerializer
                        .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                        .ConfigureAwait(false)).ToArray()[0];

                    if (mediaSourceWithChapters.ZeroFingerprintConfidence is true)
                    {
                        _itemRepository.LogIntroDetectionFailureFailure(item.InternalId, item.DateModified.ToUnixTimeSeconds());
                        _logger.Info($"ChapterInfoPersist - Log Zero Fingerprint Confidence ({source}): {mediaInfoJsonPath}");
                        return true;
                    }

                    var introStart = mediaSourceWithChapters.Chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
                    var introEnd = mediaSourceWithChapters.Chapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);

                    if (introStart != null && introEnd != null && introEnd.StartPositionTicks > introStart.StartPositionTicks)
                    {
                        var chapters = _itemRepository.GetChapters(item);
                        chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
                        chapters.Add(introStart);
                        chapters.Add(introEnd);
                        chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));

                        PersistMediaInfoHelper.BypassChapterInstance(item);
                        _itemRepository.SaveChapters(item.InternalId, chapters);
                        _logger.Info($"ChapterInfoPersist - Deserialization Success ({source}): {mediaInfoJsonPath}");
                        return true;
                    }
                }
                catch (Exception e)
                {
                    _logger.Error($"ChapterInfoPersist - Deserialization Failed ({source}): {mediaInfoJsonPath}");
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
            return false;
        }

        public async Task<bool> DeserializeChapterInfo(Video item, List<ChapterInfo> chapters, IDirectoryService directoryService, string source)
        {
            var thumbnailResult = false;

            if (Plugin.Instance.IsModSupported || !item.IsShortcut)
            {
                var localThumbnailSets = Video.GetLocalThumbnailSetInfos(item.Path, item.Id, false, directoryService);
                if (localThumbnailSets.Length > 0)
                {
                    var options = _libraryManager.GetLibraryOptions(item);
                    var dummyLibraryOptions = new LibraryOptions
                    {
                        ThumbnailImagesIntervalSeconds = 10,
                        CacheImages = options.CacheImages
                    };

                    thumbnailResult = await Plugin.VideoThumbnailApi.RefreshThumbnailImages(item,
                        dummyLibraryOptions, directoryService, chapters, false, false, CancellationToken.None);

                    if (thumbnailResult)
                    {
                        _logger.Info($"ChapterInfoPersist - Video Thumbnail Restore Success ({source}): {localThumbnailSets[0].Path}");
                    }
                }
            }

            _itemRepository.SaveChapters(item.InternalId, true, chapters);
            return thumbnailResult;
        }

        public void QueueRefreshAlternateVersions(BaseItem item, MetadataRefreshOptions options, bool force)
        {
            //if (item is not Video video) return;
            if (!(item is Video video)) return;
            var altIds = video.GetAlternateVersionIds();
            if (!altIds.Any()) return;

            var itemsToRefresh = force
                ? altIds
                : _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ItemIds = altIds.ToArray(),
                    HasPath = true,
                    HasAudioStream = false,
                    MediaTypes = new[] { MediaType.Video }
                }).Select(i => i.InternalId);

            foreach (var altId in itemsToRefresh)
            {
                _providerManager.QueueRefresh(altId, options, RefreshPriority.Normal);
            }
        }

        public BaseItem GetItemByMediaSourceId(BaseItem item, string mediaSourceId)
        {
            if (string.IsNullOrEmpty(mediaSourceId)) return null;

            if (item.GetDefaultMediaSourceId() == mediaSourceId)
            {
                return item;
            }

            var mediaSource = item.GetMediaSources(true, false, null).FirstOrDefault(s => s.Id == mediaSourceId);
            return mediaSource != null ? _libraryManager.GetItemById(mediaSource.ItemId) : null;
        }
    }
}
