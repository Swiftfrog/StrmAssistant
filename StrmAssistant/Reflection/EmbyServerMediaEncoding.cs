using Emby.Server.MediaEncoding.Encoder;
using Emby.Server.MediaEncoding.ImageExtraction;
using Emby.Server.MediaEncoding.Probing;
using HarmonyLib;
using System;
using System.Reflection;

namespace StrmAssistant.Reflection
{
    internal class EmbyServerMediaEncoding : ReflectionBase<EmbyServerMediaEncoding>
    {
        internal static Type _quickSingleImageExtractor;
        internal static ConstructorInfo _staticConstructor;
        internal static MethodInfo _runExtraction;
        internal static MethodInfo _extractVideoImagesOnInterval;
        internal static MethodInfo _enableQuickImageSeriesExtractor;
        internal static MethodInfo _addHdrAdjustFilter;
        internal static MethodInfo _runFfProcess;
        internal static MethodInfo _getInputArgument;
        internal static MethodInfo _getAnalyzeDurationArgument;
        internal static MethodInfo _getProbeSizeArgument;
        internal static MethodInfo _getMediaInfo;

        static EmbyServerMediaEncoding()
        {
            new EmbyServerMediaEncoding().Initialize();
        }

        protected override void OnInitialize()
        {
            _staticConstructor = AccessTools.Constructor(typeof(ImageExtractorBase), Type.EmptyTypes, true);
            _runExtraction = AccessTools.Method(typeof(ImageExtractorBase), "RunExtraction");
            _quickSingleImageExtractor =
                AccessTools.TypeByName("Emby.Server.MediaEncoding.ImageExtraction.QuickSingleImageExtractor");
            _addHdrAdjustFilter = AccessTools.Method(typeof(ImageExtractorBase), "AddHdrAdjustFilter");

            _extractVideoImagesOnInterval =
                AccessTools.Method(typeof(ImageExtractionManager), "ExtractVideoImagesOnInterval");
            _enableQuickImageSeriesExtractor =
                AccessTools.Method(typeof(ImageExtractionManager), "EnableQuickImageSeriesExtractor");

            _runFfProcess = AccessTools.Method(typeof(MediaProbeManager), "RunFfProcess");

            _getInputArgument = AccessTools.Method(typeof(EncodingHelpers), "GetInputArgument");
            _getAnalyzeDurationArgument = AccessTools.Method(typeof(EncodingHelpers), "GetAnalyzeDurationArgument");
            _getProbeSizeArgument = AccessTools.Method(typeof(EncodingHelpers), "GetProbeSizeArgument");

            _getMediaInfo = AccessTools.Method(typeof(ProbeResultNormalizer), "GetMediaInfo");
        }
    }
}
