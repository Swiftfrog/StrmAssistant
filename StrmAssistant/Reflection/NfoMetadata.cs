using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class NfoMetadata : ReflectionBase<NfoMetadata>
    {
        internal static Assembly _nfoMetadataAssembly;
        internal static ConstructorInfo _genericBaseNfoParserConstructor;
        internal static MethodInfo _getPersonFromXmlNode;

        static NfoMetadata()
        {
            new NfoMetadata().Initialize();
        }

        protected override void OnInitialize()
        {
            _nfoMetadataAssembly = GetAssemblyByName("NfoMetadata");

            if (_nfoMetadataAssembly != null)
            {
                var genericBaseNfoParser = _nfoMetadataAssembly.GetType("NfoMetadata.Parsers.BaseNfoParser`1");
                var genericBaseNfoParserVideo = genericBaseNfoParser.MakeGenericType(typeof(Video));
                _genericBaseNfoParserConstructor = genericBaseNfoParserVideo.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public, null,
                    new[]
                    {
                        typeof(ILogger), typeof(IConfigurationManager), typeof(IProviderManager),
                        typeof(IFileSystem)
                    }, null);
                _getPersonFromXmlNode = genericBaseNfoParserVideo.GetMethod("GetPersonFromXmlNode",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            else
            {
                Plugin.Instance.Logger.Warn("NfoMetadata plugin is not installed");
            }
        }
    }
}
