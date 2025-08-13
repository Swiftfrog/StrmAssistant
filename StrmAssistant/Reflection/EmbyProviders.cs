using Emby.Providers.Manager;
using Emby.Providers.Markers;
using Emby.Providers.MediaInfo;
using HarmonyLib;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System.Linq;
using System.Reflection;
using System.Threading;

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
        internal static MethodInfo _getExternalSubtitleStreams;
        internal static MethodInfo _updateExternalSubtitleStream;
        internal static ConstructorInfo _audioFingerprintManagerConstructor;

        static EmbyProviders()
        {
            new EmbyProviders().Initialize();
        }

        protected override void OnInitialize()
        {
            _saveImageFromRemoteUrl = AccessTools.Method(typeof(ProviderManager), "SaveImageFromRemoteUrl");
            _canRefreshMetadata = AccessTools.GetDeclaredMethods(typeof(ProviderManager))
                .FirstOrDefault(m => m.Name == "CanRefresh" && m.IsStatic);
            _getEnabledMetadataProviders = AccessTools.Method(typeof(ProviderManager), "GetEnabledMetadataProviders");
            _canRefreshImage = AccessTools.GetDeclaredMethods(typeof(ProviderManager))
                .FirstOrDefault(m => m.Name == "CanRefresh" && !m.IsStatic);
            _isSaverEnabledForItem = AccessTools.Method(typeof(ProviderManager), "IsSaverEnabledForItem");
            _getAvailableRemoteImages = AccessTools.Method(typeof(ProviderManager), "GetAvailableRemoteImages",
                new[]
                {
                    typeof(BaseItem), typeof(LibraryOptions), typeof(RemoteImageQuery), typeof(IDirectoryService),
                    typeof(CancellationToken)
                });
            _clearImages = AccessTools.Method(typeof(ItemImageProvider), "ClearImages");
            _supportsVideoImageCapture = AccessTools.Method(typeof(VideoImageProvider), "Supports");
            _getThumbnailPositionTicks = AccessTools.Method(typeof(VideoImageProvider), "GetThumbnailPositionTicks");
            _getImage = AccessTools.GetDeclaredMethods(typeof(VideoImageProvider))
                .Where(m => m.Name == "GetImage")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
            _supportsAudioEmbeddedImages = AccessTools.Method(typeof(AudioImageProvider), "Supports");
            _isProbingAllowed = AccessTools.Method(typeof(FFProbeProvider), "IsProbingAllowed");
            _refreshThumbnailImages = AccessTools.Method(typeof(ThumbnailGenerator), "RefreshThumbnailImages");
            _onFailedToFindIntro = AccessTools.Method(typeof(AudioFingerprintManager), "OnFailedToFindIntro");
            _isIntroDetectionSupported =
                AccessTools.Method(typeof(AudioFingerprintManager), "IsIntroDetectionSupported");
            _audioFingerprintManagerConstructor = AccessTools.Constructor(typeof(AudioFingerprintManager),
                new[]
                {
                    typeof(IFileSystem), typeof(ILogger), typeof(IApplicationPaths), typeof(IFfmpegManager),
                    typeof(IMediaEncoder), typeof(IMediaMountManager), typeof(IJsonSerializer),
                    typeof(IServerApplicationHost)
                });
            _createTitleFingerprint = AccessTools.Method(typeof(AudioFingerprintManager), "CreateTitleFingerprint",
                new[]
                {
                    typeof(Episode), typeof(LibraryOptions), typeof(IDirectoryService), typeof(CancellationToken)
                });
            _getTitleFingerprintFileName =
                AccessTools.Method(typeof(AudioFingerprintManager), "GetTitleFingerprintFileName");
            _getAllFingerprintFilesForSeason =
                AccessTools.Method(typeof(AudioFingerprintManager), "GetAllFingerprintFilesForSeason");
            _updateSequencesForSeason = AccessTools.Method(typeof(AudioFingerprintManager), "UpdateSequencesForSeason");
            _createQueryForEpisodeIntroDetection =
                AccessTools.Method(typeof(MarkerScheduledTask), "CreateQueryForEpisodeIntroDetection");
            _detectSequences = AccessTools.GetDeclaredMethods(typeof(SequenceDetection))
                .FirstOrDefault(m => m.Name == "DetectSequences" && m.GetParameters().Length == 8);
            _getExternalSubtitleStreams = AccessTools.Method(typeof(SubtitleResolver), "GetExternalSubtitleStreams");
            _updateExternalSubtitleStream =
                AccessTools.Method(typeof(FFProbeSubtitleInfo), "UpdateExternalSubtitleStream");
        }
    }
}
