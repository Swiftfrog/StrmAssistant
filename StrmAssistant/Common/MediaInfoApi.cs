using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Options.Utility;

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

        private static readonly PatchTracker PatchTracker =
            new PatchTracker(typeof(MediaInfoApi),
                Plugin.Instance.IsModSupported ? PatchApproach.Harmony : PatchApproach.Reflection);

        private readonly MethodInfo _getStaticMediaSources;

        internal class MediaSourceWithChapters
        {
            public MediaSourceInfo MediaSourceInfo { get; set; }
            public List<ChapterInfo> Chapters { get; set; } = new List<ChapterInfo>();
            public bool? ZeroFingerprintConfidence { get; set; }
            public string EmbeddedImage { get; set; }
        }

        public MediaInfoApi(ILibraryManager libraryManager, IFileSystem fileSystem, IProviderManager providerManager,
            IMediaSourceManager mediaSourceManager, IItemRepository itemRepository, IJsonSerializer jsonSerializer,
            ILibraryMonitor libraryMonitor)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _providerManager = providerManager;
            _mediaSourceManager = mediaSourceManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;

            // if (AppVer >= Ver49025)
            // {
            //     try
            //     {
            //         _getStaticMediaSources = mediaSourceManager.GetType()
            //             .GetMethod("GetStaticMediaSources",
            //                 new[]
            //                 {
            //                     typeof(BaseItem), typeof(bool), typeof(bool), typeof(bool), typeof(LibraryOptions),
            //                     typeof(DeviceProfile), typeof(User)
            //                 });
            //     }
            //     catch (Exception e)
            //     {
            //         if (Plugin.Instance.DebugMode)
            //         {
            //             _logger.Debug(e.Message);
            //             _logger.Debug(e.StackTrace);
            //         }
            //     }

            //     if (_getStaticMediaSources is null)
            //     {
            //         _logger.Warn($"{PatchTracker.PatchType.Name} Init Failed");
            //         PatchTracker.FallbackPatchApproach = PatchApproach.None;
            //     }
            //     else if (Plugin.Instance.IsModSupported)
            //     {
            //         PatchManager.ReversePatch(PatchTracker, _getStaticMediaSources,
            //             nameof(GetStaticMediaSourcesStub));
            //     }
            // }
            
            if (AppVer >= Ver49025)
            {
                try
                {
                    var mediaSourceManagerType = mediaSourceManager.GetType();
            
                    // 尝试查找 7 参数版本 (不含可选的 User): BaseItem, bool, bool, bool, BaseItem[], LibraryOptions, DeviceProfile
                    _getStaticMediaSources = mediaSourceManagerType
                        .GetMethod("GetStaticMediaSources",
                            new[]
                            {
                                typeof(BaseItem),
                                typeof(bool), // enableAlternateMediaSources
                                typeof(bool), // enablePathSubstitution
                                typeof(bool), // fillChapters
                                typeof(BaseItem[]), // collectionFolders
                                typeof(LibraryOptions),
                                typeof(DeviceProfile)
                                // 不包含 User
                            });
            
                    // 如果 7 参数未找到，尝试 10 参数版本: BaseItem, bool, bool, bool, bool, BaseItem[], LibraryOptions, DeviceProfile, User, CancellationToken
                    if (_getStaticMediaSources is null)
                    {
                        _getStaticMediaSources = mediaSourceManagerType
                            .GetMethod("GetStaticMediaSources",
                                new[]
                                {
                                    typeof(BaseItem),
                                    typeof(bool), // enableAlternateMediaSources
                                    typeof(bool), // enablePathSubstitution
                                    typeof(bool), // fillMediaStreams
                                    typeof(bool), // fillChapters
                                    typeof(BaseItem[]), // collectionFolders
                                    typeof(LibraryOptions),
                                    typeof(DeviceProfile),
                                    typeof(User),
                                    typeof(CancellationToken)
                                });
                    }
            
                    // 如果 10 参数未找到，尝试 5 参数版本: BaseItem, bool, bool, DeviceProfile, User (可选)
                    if (_getStaticMediaSources is null)
                    {
                        _getStaticMediaSources = mediaSourceManagerType
                            .GetMethod("GetStaticMediaSources",
                                new[]
                                {
                                    typeof(BaseItem),
                                    typeof(bool), // enablePathSubstitution
                                    typeof(bool), // fillChapters
                                    typeof(DeviceProfile)
                                    // 不包含可选的 User
                                });
                    }
            
                    // 可以继续添加其他可能的签名...
            
                }
                catch (Exception e)
                {
                    if (Plugin.Instance.DebugMode)
                    {
                        _logger.Debug(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                }
            
                if (_getStaticMediaSources is null)
                {
                    // 修复：明确记录失败，并设置 PatchApproach 为 None
                    _logger.Warn($"{PatchTracker.PatchType.Name} Init Failed - GetStaticMediaSources method not found with expected signatures for Emby 4.9.x. Falling back to standard API calls without patching. Some features may behave differently.");
                    PatchTracker.FallbackPatchApproach = PatchApproach.None; // 确保不使用 Harmony 或 Reflection
                }
                else
                {
                    // 修复：只有在找到方法时才尝试应用 ReversePatch
                    if (Plugin.Instance.IsModSupported)
                    {
                        try
                        {
                            // 确保 Stub 签名与 _getStaticMediaSources 匹配
                            // 这是一个复杂点：你需要为每种可能找到的签名准备对应的 Stub 和调用方法
                            // 为了简化，我们只处理已知的几种情况，或者统一处理为 None
                            // 如果签名不固定，最好还是放弃 Harmony，只用 Reflection 或标准 API
                            // 假设我们只处理 7 参数或 10 参数的情况，并且有对应的 Stub
            
                            var paramCount = _getStaticMediaSources.GetParameters().Length;
                            if (paramCount == 7)
                            {
                                // 假设 GetStaticMediaSourcesStub 已经匹配 7 参数
                                PatchManager.ReversePatch(PatchTracker, _getStaticMediaSources, nameof(GetStaticMediaSourcesStub));
                            }
                            else if (paramCount == 10)
                            {
                                // 需要另一个 Stub，例如 GetStaticMediaSourcesStub10
                                // 这会增加代码复杂性
                                // 为简化，我们暂时不应用 Harmony，直接使用标准 API
                                _logger.Warn($"{PatchTracker.PatchType.Name} Found 10-parameter GetStaticMediaSources, but Harmony Stub not adapted. Falling back to standard API for this path too.");
                                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                                // 重置 _getStaticMediaSources 为 null，以便在 GetStaticMediaSourcesByRef 中也能触发降级
                                _getStaticMediaSources = null;
                            }
                            else if (paramCount == 5)
                            {
                                // 需要另一个 Stub，例如 GetStaticMediaSourcesStub5
                                _logger.Warn($"{PatchTracker.PatchType.Name} Found 5-parameter GetStaticMediaSources, but Harmony Stub not adapted. Falling back to standard API for this path too.");
                                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                                 _getStaticMediaSources = null;
                            }
                            else
                            {
                                _logger.Warn($"{PatchTracker.PatchType.Name} Found GetStaticMediaSources with {paramCount} parameters, which is unexpected. Falling back to standard API.");
                                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                                 _getStaticMediaSources = null;
                            }
            
                        }
                        catch (Exception e)
                        {
                            _logger.Warn($"{PatchTracker.PatchType.Name} Reverse Patch Failed: {e.Message}. Falling back to standard API.");
                            PatchTracker.FallbackPatchApproach = PatchApproach.None;
                             _getStaticMediaSources = null; // 确保不使用 Harmony 或 Reflection
                        }
                    }
                    else
                    {
                        // 如果不支持 Mod，也设置为 None
                         _logger.Info($"{PatchTracker.PatchType.Name} Mod not supported. Using standard API.");
                         PatchTracker.FallbackPatchApproach = PatchApproach.None;
                          _getStaticMediaSources = null; // 确保不使用 Harmony 或 Reflection
                    }
                }
            }
            else
            {
                // 如果 AppVer < Ver49025，也使用标准 API
                _logger.Info($"{PatchTracker.PatchType.Name} AppVer < Ver49025. Using standard API.");
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                 _getStaticMediaSources = null; // 确保不使用 Harmony 或 Reflection
            }

        // [HarmonyReversePatch]
        // private static List<MediaSourceInfo> GetStaticMediaSourcesStub(IMediaSourceManager instance, BaseItem item,
        //     bool enableAlternateMediaSources, bool enablePathSubstitution, bool fillChapters,
        //     LibraryOptions libraryOptions, DeviceProfile deviceProfile, User user = null) =>
        //     throw new NotImplementedException();

        // 修复：Stub 签名匹配 7 个必需参数的版本，不包含可选的 User
        [HarmonyReversePatch]
        private static List<MediaSourceInfo> GetStaticMediaSourcesStub(IMediaSourceManager instance, BaseItem item,
            bool enableAlternateMediaSources, bool enablePathSubstitution, bool fillChapters, // 修复：顺序和参数
            BaseItem[] collectionFolders, LibraryOptions libraryOptions, DeviceProfile deviceProfile)
            // 修复：不包含可选的 User 参数
            => throw new NotImplementedException();
        

        // private List<MediaSourceInfo> GetStaticMediaSourcesByApi(BaseItem item, bool enableAlternateMediaSources,
        //     LibraryOptions libraryOptions)
        // {
        //     return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false,
        //         libraryOptions, null, null);
        // }
        // 修改 GetStaticMediaSourcesByApi 方法，使其也处理异常
        private List<MediaSourceInfo> GetStaticMediaSourcesByApi(BaseItem item, bool enableAlternateMediaSources,
            LibraryOptions libraryOptions)
        {
            // 修复：只调用 7 个必需参数的版本，不传递可选的 User 参数
            // BaseItem, bool, bool, bool, BaseItem[], LibraryOptions, DeviceProfile
            try
            {
                return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false, // enablePathSubstitution
                    false, // fillChapters
                    Array.Empty<BaseItem>(), // collectionFolders
                    libraryOptions,
                    null  // deviceProfile
                    // 不传递 User 参数，使用其默认值 null
                );
            }
            catch (Exception e)
            {
                _logger.Warn($"GetStaticMediaSourcesByApi failed: {e.Message}. Returning empty list.");
                return new List<MediaSourceInfo>();
            }
        }
        
        // private List<MediaSourceInfo> GetStaticMediaSourcesByRef(BaseItem item, bool enableAlternateMediaSources,
        //     LibraryOptions libraryOptions)
        // {
        //     switch (PatchTracker.FallbackPatchApproach)
        //     {
        //         case PatchApproach.Harmony:
        //             return GetStaticMediaSourcesStub(_mediaSourceManager, item, enableAlternateMediaSources, false,
        //                 false, libraryOptions, null, null);
        //         case PatchApproach.Reflection:
        //             return (List<MediaSourceInfo>)_getStaticMediaSources.Invoke(_mediaSourceManager,
        //                 new object[] { item, enableAlternateMediaSources, false, false, libraryOptions, null, null });
        //         default:
        //             throw new NotImplementedException();
        //     }
        // }
        
        // 修改 GetStaticMediaSourcesByRef 方法，使其在 PatchApproach 为 None 时也返回标准 API 调用结果
        private List<MediaSourceInfo> GetStaticMediaSourcesByRef(BaseItem item, bool enableAlternateMediaSources,
            LibraryOptions libraryOptions)
        {
            // 如果 PatchTracker.FallbackPatchApproach 是 None，或者 _getStaticMediaSources 是 null，
            // 说明无法使用 Harmony 或 Reflection，应该降级到标准 API
            if (PatchTracker.FallbackPatchApproach == PatchApproach.None || _getStaticMediaSources == null)
            {
                // 直接调用标准 API，逻辑与 GetStaticMediaSources 方法中的 else 分支一致
                try
                {
                    return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false, false, Array.Empty<BaseItem>(), libraryOptions, null);
                }
                catch (Exception e)
                {
                    _logger.Warn($"GetStaticMediaSourcesByRef (standard call) failed: {e.Message}. Returning empty list.");
                    return new List<MediaSourceInfo>();
                }
            }
        
            // 否则，尝试使用 Harmony 或 Reflection
            switch (PatchTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    // 修复：调用匹配 Stub 签名的方法 (7 个必需参数，可选 User 用默认值)
                    // 这里需要根据 _getStaticMediaSources 的实际签名来调用对应的 Stub
                    // 由于我们可能有多种签名，这变得复杂
                    // 最简单的方式是假设 Stub 与 _getStaticMediaSources 匹配，并且我们只处理一种主要签名
                    // 如果有多种，需要根据 _getStaticMediaSources 的参数数量来决定调用哪个 Stub
                    // 为了简化，假设 Stub 是 7 参数的
                    var paramCount = _getStaticMediaSources.GetParameters().Length;
                    if (paramCount == 7)
                    {
                         return GetStaticMediaSourcesStub(_mediaSourceManager, item, enableAlternateMediaSources, false, // enablePathSubstitution
                            false, // fillChapters
                            Array.Empty<BaseItem>(), // collectionFolders
                            libraryOptions,
                            null  // deviceProfile
                            // 不传递可选的 User 参数
                        );
                    }
                    else
                    {
                         _logger.Warn($"GetStaticMediaSourcesByRef: Harmony Stub signature mismatch for {paramCount} parameters. This should not happen if PatchManager worked.");
                         // 降级
                         return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false, false, Array.Empty<BaseItem>(), libraryOptions, null);
                    }
        
                case PatchApproach.Reflection:
                    // 修复：使用正确的 7 个必需参数调用 (不包含可选的 User)
                    // 同样，需要根据 _getStaticMediaSources 的实际参数数量来传递参数
                    paramCount = _getStaticMediaSources.GetParameters().Length;
                    if (paramCount == 7)
                    {
                        return (List<MediaSourceInfo>)_getStaticMediaSources.Invoke(_mediaSourceManager,
                            new object[]
                            {
                                item,
                                enableAlternateMediaSources,
                                false, // enablePathSubstitution
                                false, // fillChapters
                                Array.Empty<BaseItem>(), // collectionFolders
                                libraryOptions,
                                null   // deviceProfile
                                // 不包含可选的 User 参数
                            });
                    }
                    else if (paramCount == 10)
                    {
                         return (List<MediaSourceInfo>)_getStaticMediaSources.Invoke(_mediaSourceManager,
                            new object[]
                            {
                                item,
                                enableAlternateMediaSources,
                                false, // enablePathSubstitution
                                false, // fillMediaStreams
                                false, // fillChapters
                                Array.Empty<BaseItem>(), // collectionFolders
                                libraryOptions,
                                null,   // deviceProfile
                                null,   // user
                                CancellationToken.None // cancellationToken
                            });
                    }
                    else if (paramCount == 5)
                    {
                         return (List<MediaSourceInfo>)_getStaticMediaSources.Invoke(_mediaSourceManager,
                            new object[]
                            {
                                item,
                                false, // enablePathSubstitution
                                false, // fillChapters
                                null,   // deviceProfile
                                // 不包含可选的 User
                            });
                    }
                    else
                    {
                         _logger.Warn($"GetStaticMediaSourcesByRef: Reflection signature mismatch for {paramCount} parameters.");
                         // 降级
                         return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false, false, Array.Empty<BaseItem>(), libraryOptions, null);
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        
        // 修改 GetStaticMediaSources 方法，使其在补丁失败时使用标准 API
        public List<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enableAlternateMediaSources)
        {
            var options = _libraryManager.GetLibraryOptions(item);
        
            if (AppVer >= Ver49025 && PatchTracker.FallbackPatchApproach != PatchApproach.None && _getStaticMediaSources != null)
            {
                // 尝试使用 Harmony/Reflection (如果 _getStaticMediaSources 不为 null 且 PatchApproach 不为 None)
                // 注意：如果在构造函数中 PatchApproach 被设为 None，但 _getStaticMediaSources 未被重置为 null，
                // 这里会尝试调用 GetStaticMediaSourcesByRef，但 GetStaticMediaSourcesByRef 内部会根据 PatchApproach 选择行为
                // 更安全的做法是在 PatchApproach 为 None 时也重置 _getStaticMediaSources 为 null
                // 这样下面的 else 分支就会执行
                return GetStaticMediaSourcesByRef(item, enableAlternateMediaSources, options);
            }
            else
            {
                // 降级到标准 API 调用
                // 尝试调用一个在 4.9.x 中最可能存在的重载
                // 优先尝试 7 参数版本 (与我们尝试查找的签名匹配)
                try
                {
                    // 注意：这里调用的是 _mediaSourceManager.GetStaticMediaSources，而不是 this.GetStaticMediaSourcesByApi
                    // this.GetStaticMediaSourcesByApi 也是用来调用 _mediaSourceManager 的，但可能包含额外的错误处理逻辑
                    // 直接调用 _mediaSourceManager 更清晰
                    return _mediaSourceManager.GetStaticMediaSources(item, enableAlternateMediaSources, false, false, Array.Empty<BaseItem>(), options, null);
                }
                catch (Exception e7)
                {
                    _logger.Debug($"Standard GetStaticMediaSources 7-param call failed: {e7.Message}. Trying BaseItem + User (if exists).");
        
                    // 如果 7 参数失败，尝试更简单的调用 (如果存在)
                    // 例如，尝试只接受 BaseItem 和 User 的版本
                    // 注意：这会忽略 enableAlternateMediaSources 参数的效果
                    try
                    {
                        // 假设存在一个接受 BaseItem 和可选 User 的重载
                        // 这个调用无法传递 enableAlternateMediaSources，所以功能可能有差异
                        // return _mediaSourceManager.GetStaticMediaSources(item, null);
                        // 或者尝试其他可能的简单签名
                        // 由于不确定具体签名，记录警告并返回空列表可能是最安全的降级方式
                        // 或者尝试一个可能接受 BaseItem 和 LibraryOptions 的版本 (如果存在)
                        // return _mediaSourceManager.GetStaticMediaSources(item, options); // 这个签名很可能不存在
        
                        // 最终降级：返回一个空列表，表示没有找到静态媒体源
                        // 这可能会导致依赖此方法的功能出现问题，但插件不会崩溃
                        _logger.Warn($"All attempts to call _mediaSourceManager.GetStaticMediaSources failed. This may affect MediaInfo extraction or other features relying on this call. Returning empty list.");
                        return new List<MediaSourceInfo>();
                    }
                    catch (Exception eSimple)
                    {
                         _logger.Warn($"Fallback _mediaSourceManager.GetStaticMediaSources call also failed: {eSimple.Message}. Returning empty list.");
                         return new List<MediaSourceInfo>();
                    }
                }
            }
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
                relativePath = Path.GetRelativePath(Path.GetPathRoot(item.ContainingFolderPath)!,
                    item.ContainingFolderPath);
            }

            var mediaInfoJsonPath = item is Folder
                ? !string.IsNullOrEmpty(jsonRootFolder)
                    ? Path.Combine(jsonRootFolder, relativePath)
                    : item.ContainingFolderPath
                : !string.IsNullOrEmpty(jsonRootFolder)
                    ? Path.Combine(jsonRootFolder, relativePath, item.FileNameWithoutExtension + MediaInfoFileExtension)
                    : Path.Combine(item.ContainingFolderPath!, item.FileNameWithoutExtension + MediaInfoFileExtension);

            return mediaInfoJsonPath;
        }

        private async Task<bool> SerializeMediaInfo(BaseItem item, IDirectoryService directoryService, bool overwrite,
            string source)
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
                            new MediaSourceWithChapters
                                { MediaSourceInfo = mediaSource, Chapters = chapters })
                        .ToList();

                    foreach (var jsonItem in mediaSourcesWithChapters)
                    {
                        jsonItem.MediaSourceInfo.Id = null;
                        jsonItem.MediaSourceInfo.ItemId = null;
                        jsonItem.MediaSourceInfo.Path = null;

                        foreach (var subtitle in jsonItem.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle &&
                                     m.Protocol == MediaProtocol.File))
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
                                var imageBytes = await _fileSystem
                                    .ReadAllBytesAsync(primaryImageInfo.Path, CancellationToken.None)
                                    .ConfigureAwait(false);
                                var base64String = Convert.ToBase64String(imageBytes);
                                jsonItem.EmbeddedImage = base64String;
                            }
                        }
                    }

                    var parentDirectory = Path.GetDirectoryName(mediaInfoJsonPath);
                    if (!string.IsNullOrEmpty(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    _jsonSerializer.SerializeToFile(mediaSourcesWithChapters, mediaInfoJsonPath);

                    _logger.Info("MediaInfoPersist - Serialization Success (" + source + "): " + mediaInfoJsonPath);

                    return true;
                }
                catch (Exception e)
                {
                    _logger.Error("MediaInfoPersist - Serialization Failed (" + source + "): " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        public async Task<bool> SerializeMediaInfo(long itemId, IDirectoryService directoryService, bool overwrite,
            string source)
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

        public async Task<bool> DeserializeMediaInfo(BaseItem item, IDirectoryService directoryService, string source,
            bool ignoreFileChange)
        {
            var workItem = _libraryManager.GetItemById(item.InternalId);

            if (HasMediaInfo(workItem)) return true;

            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true)
            {
                try
                {
                    var mediaSourceWithChapters =
                        (await _jsonSerializer
                            .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                            .ConfigureAwait(false)).ToArray()[0];

                    var mediaSource = mediaSourceWithChapters?.MediaSourceInfo;
                    if (mediaSource != null && mediaSource.Size > 0L && mediaSource.RunTimeTicks.HasValue &&
                        (ignoreFileChange || !Plugin.LibraryApi.HasFileChanged(item, directoryService)))
                    {
                        foreach (var subtitle in mediaSourceWithChapters.MediaSourceInfo.MediaStreams.Where(m =>
                                     m.IsExternal && m.Type == MediaStreamType.Subtitle &&
                                     m.Protocol == MediaProtocol.File))
                        {
                            subtitle.Path = Path.Combine(workItem.ContainingFolderPath,
                                _fileSystem.GetFileInfo(subtitle.Path).Name);
                        }

                        _itemRepository.SaveMediaStreams(item.InternalId,
                            mediaSourceWithChapters.MediaSourceInfo.MediaStreams, CancellationToken.None);

                        if (workItem is Audio && !string.IsNullOrEmpty(mediaSourceWithChapters.EmbeddedImage))
                        {
                            var imageBytes = Convert.FromBase64String(mediaSourceWithChapters.EmbeddedImage);
                            var tempPath = Path.Combine(Plugin.Instance.ApplicationPaths.TempDirectory,
                                Guid.NewGuid() + ".jpg");
                            await _fileSystem.WriteAllBytesAsync(tempPath, imageBytes, CancellationToken.None)
                                .ConfigureAwait(false);

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

                            await DeserializeChapterInfo(video, mediaSourceWithChapters.Chapters, directoryService,
                                source).ConfigureAwait(false);

                            if (video is Episode && mediaSourceWithChapters.ZeroFingerprintConfidence is true)
                            {
                                _itemRepository.LogIntroDetectionFailureFailure(video.InternalId,
                                    item.DateModified.ToUnixTimeSeconds());
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

                        var currentDir =
                            _fileSystem.GetFullPath(_fileSystem.GetDirectoryName(mediaInfoJsonPath) ?? string.Empty);

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

            if (!string.IsNullOrEmpty(jsonRoot) && _fileSystem.DirectoryExists(folderPath) &&
                folderPath.StartsWith(jsonRoot))
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
                    var mediaSourceWithChapters =
                        (await _jsonSerializer
                            .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                            .ConfigureAwait(false)).ToArray()[0];

                    if (mediaSourceWithChapters.ZeroFingerprintConfidence is true)
                    {
                        _itemRepository.LogIntroDetectionFailureFailure(item.InternalId,
                            item.DateModified.ToUnixTimeSeconds());

                        _logger.Info($"ChapterInfoPersist - Log Zero Fingerprint Confidence ({source}): {mediaInfoJsonPath}");

                        return true;
                    }

                    var introStart = mediaSourceWithChapters.Chapters
                        .FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);

                    var introEnd = mediaSourceWithChapters.Chapters
                        .FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);

                    if (introStart != null && introEnd != null && introEnd.StartPositionTicks > introStart.StartPositionTicks)
                    {
                        var chapters = _itemRepository.GetChapters(item);
                        chapters.RemoveAll(c =>
                            c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
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

        public async Task<bool> DeserializeChapterInfo(Video item, List<ChapterInfo> chapters,
            IDirectoryService directoryService, string source)
        {
            var thumbnailResult = false;

            if (Plugin.Instance.IsModSupported || !item.IsShortcut)
            {
                var localThumbnailSets =
                    Video.GetLocalThumbnailSetInfos(item.Path, item.Id, false, directoryService);

                if (localThumbnailSets.Length > 0)
                {
                    var options = _libraryManager.GetLibraryOptions(item);
                    var dummyLibraryOptions = new LibraryOptions
                    {
                        ThumbnailImagesIntervalSeconds = 10,
                        CacheImages = options.CacheImages
                    };

                    thumbnailResult = await Plugin.VideoThumbnailApi.RefreshThumbnailImages(item,
                        dummyLibraryOptions, directoryService, chapters, false, false,
                        CancellationToken.None);

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
                    })
                    .Select(i => i.InternalId);

            foreach (var altId in itemsToRefresh)
            {
                _providerManager.QueueRefresh(altId, options, RefreshPriority.Normal);
            }
        }

        public BaseItem GetItemByMediaSourceId(BaseItem item, string mediaSourceId)
        {
            if (string.IsNullOrEmpty(mediaSourceId)) return null;

            BaseItem targetItem = null;

            if (item.GetDefaultMediaSourceId() == mediaSourceId)
            {
                targetItem = item;
            }
            else
            {
                var mediaSource = item.GetMediaSources(true, false, null).FirstOrDefault(s => s.Id == mediaSourceId);

                if (mediaSource != null)
                {
                    targetItem = _libraryManager.GetItemById(mediaSource.ItemId);
                }
            }

            return targetItem;
        }
    }
}
