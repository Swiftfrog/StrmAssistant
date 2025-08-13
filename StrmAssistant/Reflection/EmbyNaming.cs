using Emby.Naming.Common;
using Emby.Naming.Video;
using HarmonyLib;
using System.Reflection;

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
            _getMainExpression = AccessTools.Method(typeof(NamingOptions), "GetMainExpression");
            _isEligibleForMultiVersion = AccessTools.Method(typeof(VideoListResolver), "IsEligibleForMultiVersion");
        }
    }
}
