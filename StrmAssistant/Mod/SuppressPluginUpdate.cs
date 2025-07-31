using HarmonyLib;
using MediaBrowser.Model.Updates;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod
{
    public class SuppressPluginUpdate : PatchBase<SuppressPluginUpdate>
    {
        public SuppressPluginUpdate()
        {
            var suppressPluginUpdates = Plugin.Instance.ExperienceEnhanceStore.GetOptions().SuppressPluginUpdates;

            if (!string.IsNullOrWhiteSpace(suppressPluginUpdates))
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _getAvailablePluginUpdates,
                postfix: nameof(GetAvailablePluginUpdatesPostfix));
        }

        [HarmonyPostfix]
        private static Task<PackageVersionInfo[]> GetAvailablePluginUpdatesPostfix(Task<PackageVersionInfo[]> __result)
        {
            PackageVersionInfo[] result = null;

            try
            {
                result = __result?.Result;
            }
            catch
            {
                // ignored
            }

            if (result is null) return Task.FromResult(Array.Empty<PackageVersionInfo>());

            var suppressPluginUpdates = new HashSet<string>(
                Plugin.Instance.ExperienceEnhanceStore.GetOptions().SuppressPluginUpdates
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);

            result = result.Where(p =>
                    !suppressPluginUpdates.Contains(p.name) &&
                    !suppressPluginUpdates.Contains(Path.GetFileNameWithoutExtension(p.targetFilename)))
                .ToArray();

            return Task.FromResult(result);
        }
    }
}
