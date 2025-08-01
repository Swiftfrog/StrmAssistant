using HarmonyLib;
using MediaBrowser.Controller.Library;
using System;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public class InfuseSyncThreadSafety : PatchBase<InfuseSyncThreadSafety>
    {
        private static MethodInfo _itemUpdated;

        public InfuseSyncThreadSafety()
        {
            Initialize();

            Patch();
        }

        protected override void OnInitialize()
        {
            var infuseSyncAssembly = GetAssemblyByName("InfuseSync");

            if (infuseSyncAssembly != null)
            {
                var librarySyncManager = infuseSyncAssembly.GetType("InfuseSync.EntryPoints.LibrarySyncManager");
                _itemUpdated = librarySyncManager.GetMethod("ItemUpdated",
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(object), typeof(ItemChangeEventArgs) }, null);
                ReversePatch(PatchTracker, _itemUpdated, nameof(ItemUpdatedStub));
            }
            else
            {
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                PatchTracker.IsSupported = false;
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _itemUpdated, prefix: nameof(ItemUpdatedPrefix));
        }

        [HarmonyReversePatch]
        private static void ItemUpdatedStub(object instance, object sender, ItemChangeEventArgs e) =>
            throw new NotImplementedException();

        [HarmonyPrefix]
        private static bool ItemUpdatedPrefix(object __instance, object sender, ItemChangeEventArgs e,
            object ____libraryChangedSyncLock)
        {
            lock (____libraryChangedSyncLock)
            {
                ItemUpdatedStub(__instance, sender, e);
            }

            return false;
        }
    }
}
