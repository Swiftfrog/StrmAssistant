using HarmonyLib;
using InfuseSync.EntryPoints;
using MediaBrowser.Controller.Library;
using System;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.InfuseSync;

namespace StrmAssistant.Mod
{
    public class InfuseSyncThreadSafety : PatchBase<InfuseSyncThreadSafety>
    {
        public InfuseSyncThreadSafety()
        {
            Initialize();

            Patch();
        }

        protected override void OnInitialize()
        {
            if (IsSupported)
            {
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
        private static void ItemUpdatedStub(LibrarySyncManager instance, object sender, ItemChangeEventArgs e) =>
            throw new NotImplementedException();

        [HarmonyPrefix]
        private static bool ItemUpdatedPrefix(LibrarySyncManager __instance, object sender, ItemChangeEventArgs e,
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
