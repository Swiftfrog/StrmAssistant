using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System.Linq;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class EmbyProviders : ReflectionBase<EmbyProviders>
    {
        internal static MethodInfo _saveImageFromRemoteUrl;
        internal static MethodInfo _canRefreshMetadata;
        internal static MethodInfo _getEnabledMetadataProviders;
        internal static MethodInfo _supportsVideoImageCapture;
        internal static MethodInfo _getThumbnailPositionTicks;
        internal static MethodInfo _supportsAudioEmbeddedImages;
        internal static MethodInfo _getImage;
        internal static MethodInfo _canRefreshImage;
        internal static MethodInfo _clearImages;
        internal static MethodInfo _isSaverEnabledForItem;
        internal static MethodInfo _isProbingAllowed;
        internal static MethodInfo _refreshThumbnailImages;
        internal static MethodInfo _getAvailableRemoteImages;
        internal static MethodInfo _onFailedToFindIntro;
        internal static MethodInfo _isIntroDetectionSupported;
        internal static MethodInfo _createQueryForEpisodeIntroDetection;
        internal static MethodInfo _detectSequences;
        internal static MethodInfo _createTitleFingerprint;
        internal static MethodInfo _getTitleFingerprintFileName;
        internal static MethodInfo _getAllFingerprintFilesForSeason;
        internal static MethodInfo _updateSequencesForSeason;
        internal static FieldInfo _timeoutMs;
        internal static MethodInfo _getExternalSubtitleStreams;
        internal static MethodInfo _updateExternalSubtitleStream;
        internal static ConstructorInfo _thumbnailGeneratorConstructor;
        internal static ConstructorInfo _audioFingerprintManagerConstructor;
        internal static ConstructorInfo _subtitleResolverConstructor;
        internal static ConstructorInfo _ffProbeSubtitleInfoConstructor;

        static EmbyProviders()
        {
            new EmbyProviders().Initialize();
        }

        protected override void OnInitialize()
        {
            var embyProviders = GetAssemblyByName("Emby.Providers");

            var providerManager = embyProviders.GetType("Emby.Providers.Manager.ProviderManager");
            _saveImageFromRemoteUrl = providerManager.GetMethod("SaveImageFromRemoteUrl",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _canRefreshMetadata = providerManager.GetMethod("CanRefresh", BindingFlags.Static | BindingFlags.NonPublic);
            _getEnabledMetadataProviders = providerManager.GetMethod("GetEnabledMetadataProviders",
                BindingFlags.Instance | BindingFlags.Public);
            _canRefreshImage = providerManager.GetMethod("CanRefresh", BindingFlags.Instance | BindingFlags.NonPublic);
            _isSaverEnabledForItem =
                providerManager.GetMethod("IsSaverEnabledForItem", BindingFlags.Instance | BindingFlags.NonPublic);
            _getAvailableRemoteImages = providerManager.GetMethod("GetAvailableRemoteImages",
                BindingFlags.Instance | BindingFlags.Public, null,
                new[]
                {
                    typeof(BaseItem), typeof(LibraryOptions), typeof(RemoteImageQuery), typeof(IDirectoryService),
                    typeof(CancellationToken)
                }, null);

            var itemImageProvider = embyProviders.GetType("Emby.Providers.Manager.ItemImageProvider");
            _clearImages = itemImageProvider.GetMethod("ClearImages", BindingFlags.Instance | BindingFlags.NonPublic);

            var videoImageProvider = embyProviders.GetType("Emby.Providers.MediaInfo.VideoImageProvider");
            _supportsVideoImageCapture =
                videoImageProvider.GetMethod("Supports", BindingFlags.Instance | BindingFlags.Public);
            _getThumbnailPositionTicks = videoImageProvider.GetMethod("GetThumbnailPositionTicks",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _getImage = videoImageProvider.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "GetImage")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();

            var audioImageProvider = embyProviders.GetType("Emby.Providers.MediaInfo.AudioImageProvider");
            _supportsAudioEmbeddedImages =
                audioImageProvider.GetMethod("Supports", BindingFlags.Instance | BindingFlags.Public);

            var fFProbeProvider = embyProviders.GetType("Emby.Providers.MediaInfo.FFProbeProvider");
            _isProbingAllowed =
                fFProbeProvider.GetMethod("IsProbingAllowed", BindingFlags.Static | BindingFlags.NonPublic);

            var thumbnailGenerator = embyProviders.GetType("Emby.Providers.MediaInfo.ThumbnailGenerator");
            _refreshThumbnailImages = thumbnailGenerator.GetMethod("RefreshThumbnailImages",
                BindingFlags.Public | BindingFlags.Instance);
            _thumbnailGeneratorConstructor = thumbnailGenerator.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, null,
                new[]
                {
                    typeof(IFileSystem), typeof(ILogger), typeof(IImageExtractionManager), typeof(IItemRepository),
                    typeof(IMediaMountManager), typeof(IServerApplicationPaths), typeof(ILibraryMonitor),
                    typeof(IFfmpegManager)
                }, null);

            var audioFingerprintManager = embyProviders.GetType("Emby.Providers.Markers.AudioFingerprintManager");
            _onFailedToFindIntro = audioFingerprintManager.GetMethod("OnFailedToFindIntro",
                BindingFlags.NonPublic | BindingFlags.Static);
            _isIntroDetectionSupported = audioFingerprintManager.GetMethod("IsIntroDetectionSupported",
                BindingFlags.Public | BindingFlags.Instance);
            _audioFingerprintManagerConstructor = audioFingerprintManager.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, null,
                new[]
                {
                    typeof(IFileSystem), typeof(ILogger), typeof(IApplicationPaths), typeof(IFfmpegManager),
                    typeof(IMediaEncoder), typeof(IMediaMountManager), typeof(IJsonSerializer),
                    typeof(IServerApplicationHost)
                }, null);
            _createTitleFingerprint = audioFingerprintManager.GetMethod("CreateTitleFingerprint",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(Episode), typeof(LibraryOptions), typeof(IDirectoryService), typeof(CancellationToken) },
                null);
            _getTitleFingerprintFileName = audioFingerprintManager.GetMethod("GetTitleFingerprintFileName",
                BindingFlags.NonPublic | BindingFlags.Static);
            _getAllFingerprintFilesForSeason = audioFingerprintManager.GetMethod("GetAllFingerprintFilesForSeason",
                BindingFlags.Public | BindingFlags.Instance);
            _updateSequencesForSeason = audioFingerprintManager.GetMethod("UpdateSequencesForSeason",
                BindingFlags.Public | BindingFlags.Instance);
            _timeoutMs = audioFingerprintManager.GetField("TimeoutMs", BindingFlags.NonPublic | BindingFlags.Instance);

            var markerScheduledTask = embyProviders.GetType("Emby.Providers.Markers.MarkerScheduledTask");
            _createQueryForEpisodeIntroDetection = markerScheduledTask.GetMethod("CreateQueryForEpisodeIntroDetection",
                BindingFlags.Public | BindingFlags.Static);
            var sequenceDetection = embyProviders.GetType("Emby.Providers.Markers.SequenceDetection");
            _detectSequences = sequenceDetection.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "DetectSequences" && m.GetParameters().Length == 8);

            var subtitleResolverType = embyProviders.GetType("Emby.Providers.MediaInfo.SubtitleResolver");
            _subtitleResolverConstructor = subtitleResolverType.GetConstructor(new[]
            {
                typeof(ILocalizationManager), typeof(IFileSystem), typeof(ILibraryManager)
            });
            _getExternalSubtitleStreams = subtitleResolverType.GetMethod("GetExternalSubtitleStreams");

            var ffProbeSubtitleInfoType = embyProviders.GetType("Emby.Providers.MediaInfo.FFProbeSubtitleInfo");
            _ffProbeSubtitleInfoConstructor = ffProbeSubtitleInfoType.GetConstructor(new[]
            {
                typeof(IMediaProbeManager)
            });
            _updateExternalSubtitleStream = ffProbeSubtitleInfoType.GetMethod("UpdateExternalSubtitleStream");
        }
    }
}
