using Emby.Naming.Common;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class EmbyNaming : ReflectionBase<EmbyNaming>
    {
        internal static MethodInfo _getMainExpression;
        internal static MethodInfo _isEligibleForMultiVersion;

        static EmbyNaming()
        {
            new EmbyNaming().Initialize();
        }

        protected override void OnInitialize()
        {
            var namingAssembly = GetAssemblyByName("Emby.Naming");

            _getMainExpression =
                typeof(NamingOptions).GetMethod("GetMainExpression", BindingFlags.NonPublic | BindingFlags.Static);
            var videoListResolverType = namingAssembly.GetType("Emby.Naming.Video.VideoListResolver");
            _isEligibleForMultiVersion = videoListResolverType.GetMethod("IsEligibleForMultiVersion",
                BindingFlags.Static | BindingFlags.NonPublic);
        }
    }
}
