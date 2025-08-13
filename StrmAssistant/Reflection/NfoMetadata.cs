using HarmonyLib;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using NfoMetadata.Parsers;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class NfoMetadata : ReflectionBase<NfoMetadata>
    {
        private static readonly string AssemblyName = "NfoMetadata";
        private static readonly Assembly _nfoMetadataAssembly;

        internal static ConstructorInfo _genericBaseNfoParserConstructor;
        internal static MethodInfo _getPersonFromXmlNode;

        static NfoMetadata()
        {
            _nfoMetadataAssembly = GetAssemblyByName(AssemblyName);

            if (_nfoMetadataAssembly != null)
            {
                RegisterAssemblyResolve(AssemblyName, _nfoMetadataAssembly);
                new NfoMetadata().Initialize();
            }
            else
            {
                Plugin.Instance.Logger.Info($"{AssemblyName} plugin is not installed");
            }
        }

        internal static bool IsSupported => _nfoMetadataAssembly != null;

        protected override void OnInitialize()
        {
            _genericBaseNfoParserConstructor = AccessTools.Constructor(typeof(BaseNfoParser<Video>),
                new[]
                {
                    typeof(ILogger), typeof(IConfigurationManager), typeof(IProviderManager), typeof(IFileSystem)
                });
            _getPersonFromXmlNode = AccessTools.Method(typeof(BaseNfoParser<Video>), "GetPersonFromXmlNode");
        }
    }
}
