using HarmonyLib;
using MediaBrowser.Controller.Entities;
using StrmAssistant.Common;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod.UIFunction
{
    public class EnforceLibraryOrder : PatchBase<EnforceLibraryOrder>
    {
        public EnforceLibraryOrder()
        {
            if (Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.EnforceLibraryOrder)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _getUserViews, nameof(GetUserViewsPrefix));
        }

        [HarmonyPrefix]
        private static bool GetUserViewsPrefix(User user)
        {
            user.Configuration.OrderedViews = LibraryApi.AdminOrderedViews;

            return true;
        }
    }
}
