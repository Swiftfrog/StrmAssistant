using HarmonyLib;
using InfuseSync.EntryPoints;
using MediaBrowser.Controller.Library;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class InfuseSync : ReflectionBase<InfuseSync>
    {
        private static readonly string AssemblyName = "InfuseSync";
        private static readonly Assembly _infuseSyncAssembly;

        internal static MethodInfo _itemUpdated;

        static InfuseSync()
        {
            _infuseSyncAssembly = GetAssemblyByName(AssemblyName);

            if (_infuseSyncAssembly != null)
            {
                RegisterAssemblyResolve(AssemblyName, _infuseSyncAssembly);
                new InfuseSync().Initialize();
            }
            else
            {
                Plugin.Instance.Logger.Info($"{AssemblyName} plugin is not installed");
            }
        }

        internal static bool IsSupported => _infuseSyncAssembly != null;

        protected override void OnInitialize()
        {
            _itemUpdated = AccessTools.Method(typeof(LibrarySyncManager), "ItemUpdated",
                new[] { typeof(object), typeof(ItemChangeEventArgs) });
        }
    }
}
