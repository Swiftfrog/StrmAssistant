using System;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class EmbyServerMediaEncoding : ReflectionBase<EmbyServerMediaEncoding>
    {
        internal static Type _quickSingleImageExtractor;
        internal static ConstructorInfo _staticConstructor;
        internal static FieldInfo _resourcePoolField;
        internal static MethodInfo _runExtraction;
        internal static MethodInfo _extractVideoImagesOnInterval;
        internal static MethodInfo _enableQuickImageSeriesExtractor;
        internal static MethodInfo _addHdrAdjustFilter;
        internal static MethodInfo _runFfProcess;
        internal static MethodInfo _getInputArgument;
        internal static MethodInfo _getMediaInfo;

        static EmbyServerMediaEncoding()
        {
            new EmbyServerMediaEncoding().Initialize();
        }

        protected override void OnInitialize()
        {
            var mediaEncodingAssembly = GetAssemblyByName("Emby.Server.MediaEncoding");

            var imageExtractorBaseType =
                mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.ImageExtraction.ImageExtractorBase");
            _staticConstructor = imageExtractorBaseType.GetConstructor(BindingFlags.Static | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            _resourcePoolField =
                imageExtractorBaseType.GetField("resourcePool", BindingFlags.NonPublic | BindingFlags.Static);
            _runExtraction =
                imageExtractorBaseType.GetMethod("RunExtraction", BindingFlags.Instance | BindingFlags.Public);
            _quickSingleImageExtractor =
                mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.ImageExtraction.QuickSingleImageExtractor");
            _addHdrAdjustFilter =
                imageExtractorBaseType.GetMethod("AddHdrAdjustFilter", BindingFlags.Instance | BindingFlags.NonPublic);

            var imageExtractionManager =
                mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.ImageExtraction.ImageExtractionManager");
            _extractVideoImagesOnInterval = imageExtractionManager.GetMethod("ExtractVideoImagesOnInterval");
            _enableQuickImageSeriesExtractor = imageExtractionManager.GetMethod("EnableQuickImageSeriesExtractor",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var mediaProbeManager =
                mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.Probing.MediaProbeManager");
            _runFfProcess = mediaProbeManager.GetMethod("RunFfProcess", BindingFlags.Instance | BindingFlags.NonPublic);

            var encodingHelpers = mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.Encoder.EncodingHelpers");
            _getInputArgument =
                encodingHelpers.GetMethod("GetInputArgument", BindingFlags.Static | BindingFlags.Public);

            var probeResultNormalizer =
                mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.Probing.ProbeResultNormalizer");
            _getMediaInfo =
                probeResultNormalizer.GetMethod("GetMediaInfo", BindingFlags.Instance | BindingFlags.Public);
        }
    }
}
