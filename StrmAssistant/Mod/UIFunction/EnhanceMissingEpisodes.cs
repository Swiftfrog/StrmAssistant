using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant.Provider;
using System.Linq;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyProviders;

namespace StrmAssistant.Mod.UIFunction
{
    public class EnhanceMissingEpisodes : PatchBase<EnhanceMissingEpisodes>
    {
        public static readonly AsyncLocal<Series> CurrentSeries = new AsyncLocal<Series>();

        public EnhanceMissingEpisodes()
        {
            if (Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.EnhanceMissingEpisodes)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _getEnabledMetadataProviders,
                postfix: nameof(GetEnabledMetadataProvidersPostfix));
        }

        [HarmonyPostfix]
        private static void GetEnabledMetadataProvidersPostfix(BaseItem item, LibraryOptions libraryOptions,
            ref IMetadataProvider[] __result)
        {
            if (item is Series series && item.ProviderIds.ContainsKey(MetadataProviders.Tmdb.ToString()))
            {
                var movieDbSeriesProvider =
                    __result.FirstOrDefault(p => p.GetType().FullName == "MovieDb.MovieDbSeriesProvider");
                var newResult = __result.Where(p => p.GetType().FullName != typeof(MovieDbSeriesProvider).FullName)
                    .ToList();
                var provider = Plugin.MetadataApi.GetMovieDbSeriesProvider();

                if (movieDbSeriesProvider != null)
                {
                    var index = newResult.IndexOf(movieDbSeriesProvider);
                    newResult.Insert(index, provider);
                }
                else if (!newResult.Any(p => p is ISeriesMetadataProvider))
                {
                    newResult.Add(provider);
                }

                if (Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup)
                {
                    CurrentSeries.Value = series;
                }

                __result = newResult.ToArray();
            }
        }
    }
}
