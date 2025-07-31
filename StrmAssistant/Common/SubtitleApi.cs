using Emby.Naming.Common;
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

        private static readonly PatchTracker PatchTracker =
            new PatchTracker(typeof(SubtitleApi),
                Plugin.Instance.IsModSupported ? PatchApproach.Harmony : PatchApproach.Reflection);

        private readonly object _subtitleResolver;
        private readonly object _ffProbeSubtitleInfo;

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
                _subtitleResolver = _subtitleResolverConstructor?.Invoke(new object[]
                {
                    localizationManager, fileSystem, libraryManager
                });
                _ffProbeSubtitleInfo = _ffProbeSubtitleInfoConstructor?.Invoke(new object[] { mediaProbeManager });
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
        private static List<MediaStream> GetExternalSubtitleStreamsStub(object instance, BaseItem item, int startIndex,
            IDirectoryService directoryService, NamingOptions namingOptions, bool clearCache) =>
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
        private static async Task<bool> UpdateExternalSubtitleStreamStub(object instance, BaseItem item,
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

        public bool HasExternalSubtitleChanged(BaseItem item, IDirectoryService directoryService, bool clearCache)
        {
            var currentExternalSubtitleFiles = _libraryManager.GetExternalSubtitleFiles(item.InternalId);
            var currentSet = new HashSet<string>(currentExternalSubtitleFiles, StringComparer.Ordinal);

            try
            {
                var newExternalSubtitleFiles = GetExternalSubtitleStreams(item, 0, directoryService, clearCache)
                    .Select(i => i.Path)
                    .ToArray();
                var newSet = new HashSet<string>(newExternalSubtitleFiles, StringComparer.Ordinal);

                return !currentSet.SetEquals(newSet);
            }
            catch
            {
                // ignored
            }

            return false;
        }

        public async Task UpdateExternalSubtitles(BaseItem item, MetadataRefreshOptions refreshOptions, bool clearCache,
            bool persistMediaInfo)
        {
            var directoryService = refreshOptions.DirectoryService;
            var currentStreams = item.GetMediaStreams()
                .FindAll(i =>
                    !(i.IsExternal && i.Type == MediaStreamType.Subtitle && i.Protocol == MediaProtocol.File));
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
