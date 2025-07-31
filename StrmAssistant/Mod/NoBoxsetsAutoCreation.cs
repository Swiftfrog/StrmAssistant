using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Library;
using System.Linq;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.Utility;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod
{
    public class NoBoxsetsAutoCreation: PatchBase<NoBoxsetsAutoCreation>
    {
        public NoBoxsetsAutoCreation()
        {
            Initialize();

            if (Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.NoBoxsetsAutoCreation)
            {
                Patch();
            }
        }

        protected override void OnInitialize()
        {
            if (AppVer < Ver4840)
            {
                Plugin.Instance.Logger.Warn("NoBoxsetsAutoCreation - Minimum required server version is 4.8.4.0");
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                PatchTracker.IsSupported = false;
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _ensureLibraryFolder, nameof(EnsureLibraryFolderPrefix));
            PatchUnpatch(PatchTracker, apply, _getUserViews, nameof(GetUserViewsPrefix));
        }

        [HarmonyPrefix]
        private static bool EnsureLibraryFolderPrefix()
        {
            return false;
        }

        [HarmonyPrefix]
        private static bool GetUserViewsPrefix(UserViewQuery query, User user, ref Folder[] folders)
        {
            folders = folders.Where(i => !(i is CollectionFolder library) ||
                                         library.CollectionType != CollectionType.BoxSets.ToString())
                .ToArray();

            return true;
        }
    }
}
