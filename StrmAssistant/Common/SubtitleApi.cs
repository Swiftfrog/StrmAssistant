using Emby.Naming.Common;
using Emby.Providers.MediaInfo;
using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using StrmAssistant.Mod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Reflection.EmbyProviders;

namespace StrmAssistant.Common
{
    public class SubtitleApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly IFileSystem _fileSystem;
        private readonly SubtitleResolver _subtitleResolver;
        private readonly FFProbeSubtitleInfo _ffProbeSubtitleInfo;

        private static readonly PatchTracker PatchTracker =
            new PatchTracker(typeof(SubtitleApi),
                Plugin.Instance.IsModSupported ? PatchApproach.Harmony : PatchApproach.Reflection);

        private static readonly HashSet<string> ProbeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".sub", ".smi", ".sami", ".mpl" };

        public SubtitleApi(ILibraryManager libraryManager, IFileSystem fileSystem, IMediaProbeManager mediaProbeManager,
            ILocalizationManager localizationManager, IItemRepository itemRepository)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _fileSystem = fileSystem;

            try
            {
                _subtitleResolver = new SubtitleResolver(localizationManager, fileSystem, libraryManager);
                _ffProbeSubtitleInfo = new FFProbeSubtitleInfo(mediaProbeManager);
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            if (_subtitleResolver is null || _getExternalSubtitleStreams is null ||
                _ffProbeSubtitleInfo is null || _updateExternalSubtitleStream is null)
            {
                _logger.Warn($"{PatchTracker.PatchType.Name} Init Failed");
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
            }
            else if (Plugin.Instance.IsModSupported)
            {
                PatchManager.ReversePatch(PatchTracker, _getExternalSubtitleStreams,
                    nameof(GetExternalSubtitleStreamsStub));
                PatchManager.ReversePatch(PatchTracker, _updateExternalSubtitleStream,
                    nameof(UpdateExternalSubtitleStreamStub));
            }
        }

        [HarmonyReversePatch]
        private static List<MediaStream> GetExternalSubtitleStreamsStub(SubtitleResolver instance, BaseItem item,
            int startIndex, IDirectoryService directoryService, NamingOptions namingOptions, bool clearCache) =>
            throw new NotImplementedException();

        private List<MediaStream> GetExternalSubtitleStreams(BaseItem item, int startIndex,
            IDirectoryService directoryService, bool clearCache)
        {
            var namingOptions = _libraryManager.GetNamingOptions();

            switch (PatchTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    return GetExternalSubtitleStreamsStub(_subtitleResolver, item, startIndex, directoryService,
                        namingOptions, clearCache);
                case PatchApproach.Reflection:
                    return (List<MediaStream>)_getExternalSubtitleStreams.Invoke(_subtitleResolver,
                        new object[] { item, startIndex, directoryService, namingOptions, clearCache });
                default:
                    throw new NotImplementedException();
            }
        }
        
#pragma warning disable CS1998
        [HarmonyReversePatch]
        private static async Task<bool> UpdateExternalSubtitleStreamStub(FFProbeSubtitleInfo instance, BaseItem item,
            MediaStream subtitleStream, MetadataRefreshOptions options, LibraryOptions libraryOptions,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
#pragma warning restore CS1998

        private Task<bool> UpdateExternalSubtitleStream(BaseItem item, MediaStream subtitleStream,
            MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);

            switch (PatchTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    return UpdateExternalSubtitleStreamStub(_ffProbeSubtitleInfo, item, subtitleStream, options,
                        libraryOptions, cancellationToken);
                case PatchApproach.Reflection:
                    return (Task<bool>)_updateExternalSubtitleStream.Invoke(_ffProbeSubtitleInfo,
                        new object[] { item, subtitleStream, options, libraryOptions, cancellationToken });
                default:
                    throw new NotImplementedException();
            }
        }

        public MetadataRefreshOptions GetExternalSubtitleRefreshOptions()
        {
            return new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllMetadata = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false
            };
        }

        // public bool HasExternalSubtitleChanged(BaseItem item, IDirectoryService directoryService, bool clearCache)
        // {
        //     var currentExternalSubtitleFiles = _libraryManager.GetExternalSubtitleFiles(item.InternalId);
        //     var currentSet = new HashSet<string>(currentExternalSubtitleFiles, StringComparer.Ordinal);

        //     try
        //     {
        //         var newExternalSubtitleFiles = GetExternalSubtitleStreams(item, 0, directoryService, clearCache)
        //             .Select(i => i.Path)
        //             .ToArray();
        //         var newSet = new HashSet<string>(newExternalSubtitleFiles, StringComparer.Ordinal);

        //         return !currentSet.SetEquals(newSet);
        //     }
        //     catch
        //     {
        //         // ignored
        //     }

        //     return false;
        // }

        public bool HasExternalSubtitleChanged(BaseItem item, IDirectoryService directoryService, bool clearCache)
        {
            // 修复：不再依赖可能已移除或变更的 _libraryManager.GetExternalSubtitleFiles
            // 修复：不再依赖可能已移除或变更的内部方法 GetExternalSubtitleStreams
            // 方案：获取当前已知的外部字幕流路径，然后手动扫描目录获取新的潜在外部字幕文件路径，进行比较。
        
            // 1. 获取当前 item 中已知的外部字幕流路径
            var currentExternalSubtitlePaths = Plugin.MediaInfoApi.GetMediaStreamsSafe(item)
                .Where(stream => stream.IsExternal && stream.Type == MediaStreamType.Subtitle && stream.Protocol == MediaProtocol.File)
                .Select(stream => stream.Path)
                .Where(path => !string.IsNullOrEmpty(path)) // 过滤掉可能的 null 或空路径
                .ToList();
        
            var currentSet = new HashSet<string>(currentExternalSubtitlePaths, StringComparer.OrdinalIgnoreCase);
        
            // 2. 手动扫描媒体文件所在目录，获取所有潜在的外部字幕文件路径
            var newPotentialSubtitlePaths = GetExternalSubtitleFilesManually(item);
        
            // 3. 比较集合
            var newSet = new HashSet<string>(newPotentialSubtitlePaths, StringComparer.OrdinalIgnoreCase);
        
            // 4. 检查是否有差异
            // 集合不相等意味着：文件增、删、改名
            // 或者，可以更精确地检查：
            // var hasFilesAddedOrRemoved = !currentSet.SetEquals(newSet);
            // var hasFilesChanged = clearCache || hasFilesAddedOrRemoved; // clearCache 通常意味着强制重新扫描和处理
        
            // 对于 HasExternalSubtitleChanged 这个方法名，检查集合是否相等通常就足够了
            return !currentSet.SetEquals(newSet);
        }
        
        // 辅助方法：手动查找外部字幕文件
        private List<string> GetExternalSubtitleFilesManually(BaseItem item)
        {
            var subtitleFiles = new List<string>();
            if (string.IsNullOrEmpty(item.Path) || !item.IsFileProtocol)
            {
                return subtitleFiles; // 如果项目没有路径或不是文件协议，则没有外部文件
            }
        
            var mediaFileDirectory = Path.GetDirectoryName(item.Path);
            if (string.IsNullOrEmpty(mediaFileDirectory) || !_fileSystem.DirectoryExists(mediaFileDirectory))
            {
                return subtitleFiles; // 如果目录不存在，则没有外部文件
            }
        
            var mediaFileNameWithoutExtension = Path.GetFileNameWithoutExtension(item.Path);
        
            // 修复：使用 _fileSystem.GetFilePaths 或 _fileSystem.GetFileSystemEntries 来获取 FileSystemMetadata
            // 注意：GetFiles 返回的是 System.IO.FileSystemInfo，不是 MediaBrowser.Model.IO.FileSystemMetadata
            // 我们应该使用 GetFileSystemEntries 或 GetFilePaths
        
            // 使用 GetFilePaths 获取文件路径字符串列表，然后根据路径创建 FileSystemMetadata 并检查
            // 或者使用 GetFileSystemEntries 获取 FileSystemMetadata 列表
            var fileSystemEntries = _fileSystem.GetFileSystemEntries(mediaFileDirectory, true); // 递归搜索子目录？根据需要调整
        
            foreach (var entry in fileSystemEntries)
            {
                if (entry.IsDirectory) continue; // 只处理文件
        
                if (IsSubtitleFile(entry) && IsRelatedToMediaFile(entry, mediaFileNameWithoutExtension))
                {
                    subtitleFiles.Add(entry.FullName); // FileSystemMetadata 有 FullName 属性
                }
            }
        
            // 你也可以根据需要添加其他查找逻辑，比如查找同目录下的 .subtitles 文件夹等
            // 例如：
            // var subtitlesFolder = Path.Combine(mediaFileDirectory, ".subtitles");
            // if (_fileSystem.DirectoryExists(subtitlesFolder))
            // {
            //     var subFolderEntries = _fileSystem.GetFileSystemEntries(subtitlesFolder, false); // 通常不递归
            //     foreach (var entry in subFolderEntries)
            //     {
            //         if (!entry.IsDirectory && IsSubtitleFile(entry) && IsRelatedToMediaFile(entry, mediaFileNameWithoutExtension))
            //         {
            //             subtitleFiles.Add(entry.FullName);
            //         }
            //     }
            // }
        
            return subtitleFiles;
        }
        
        // 修改辅助方法：判断文件是否为字幕文件
        private bool IsSubtitleFile(FileSystemMetadata file) // 修复：参数类型改为 FileSystemMetadata
        {
            var extension = file.Extension; // FileSystemMetadata 也有 Extension 属性
            return !string.IsNullOrEmpty(extension) &&
                   (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".sub", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".smi", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".sami", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".mpl", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".ttml", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".sup", StringComparison.OrdinalIgnoreCase) || // PGS subtitles
                    extension.Equals(".idx", StringComparison.OrdinalIgnoreCase)); // VobSub index file (often paired with .sub)
                    // 添加更多字幕扩展名，根据需要
        }
        
        // 修改辅助方法：判断文件名是否与媒体文件相关（基于名称匹配）
        private bool IsRelatedToMediaFile(FileSystemMetadata file, string mediaFileNameWithoutExtension) // 修复：参数类型改为 FileSystemMetadata
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name); // FileSystemMetadata 有 Name 属性
            // 基本匹配：文件名开头与媒体文件名相同（忽略大小写）
            return fileNameWithoutExtension.StartsWith(mediaFileNameWithoutExtension, StringComparison.OrdinalIgnoreCase);
            // 你可能需要更复杂的逻辑来处理语言代码等，例如 "movie.en.srt", "movie.eng.forced.ass"
            // 这里使用简单的前缀匹配作为起点
        }
        
        public async Task UpdateExternalSubtitles(BaseItem item, MetadataRefreshOptions refreshOptions, bool clearCache,
            bool persistMediaInfo)
        {
            var directoryService = refreshOptions.DirectoryService;
            var currentStreams = Plugin.MediaInfoApi.GetMediaStreamsSafe(item)
                .Where(i => !(i.IsExternal && (i.Type == MediaStreamType.Subtitle || i.Type == MediaStreamType.Audio) &&
                              i.Protocol == MediaProtocol.File))
                .ToList();
            var startIndex = currentStreams.Count == 0 ? 0 : currentStreams.Max(i => i.Index) + 1;

            if (GetExternalSubtitleStreams(item, startIndex, directoryService, clearCache) is
                { } externalSubtitleStreams)
            {
                foreach (var subtitleStream in externalSubtitleStreams)
                {
                    var extension = Path.GetExtension(subtitleStream.Path);
                    if (!string.IsNullOrEmpty(extension) && ProbeExtensions.Contains(extension))
                    {
                        var result =
                            await UpdateExternalSubtitleStream(item, subtitleStream, refreshOptions,
                                CancellationToken.None).ConfigureAwait(false);

                        if (!result)
                            _logger.Warn("No result when probing external subtitle file: {0}", subtitleStream.Path);
                    }

                    _logger.Info("ExternalSubtitle - Subtitle Processed: " + subtitleStream.Path);
                }

                currentStreams.AddRange(externalSubtitleStreams);
                _itemRepository.SaveMediaStreams(item.InternalId, currentStreams, CancellationToken.None);

                if (persistMediaInfo && Plugin.LibraryApi.IsLibraryInScope(item))
                {
                    _ = Plugin.MediaInfoApi.SerializeMediaInfo(item.InternalId, directoryService, true,
                        "External Subtitle Update").ConfigureAwait(false);
                }
            }
        }
    }
}
