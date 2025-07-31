using MediaBrowser.Controller.Providers;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class Tvdb : ReflectionBase<Tvdb>
    {
        internal static Assembly _tvdbAssembly;
        internal static MethodInfo _convertToTvdbLanguages;
        internal static MethodInfo _getTranslation;
        internal static MethodInfo _addMovieInfo;
        internal static MethodInfo _addSeriesInfo;
        internal static MethodInfo _getTvdbSeason;
        internal static MethodInfo _findEpisode;
        internal static MethodInfo _getEpisodeData;
        internal static MethodInfo _ensureMovieInfoTvdb;
        internal static MethodInfo _ensureSeriesInfoTvdb;

        static Tvdb()
        {
            new Tvdb().Initialize();
        }

        protected override void OnInitialize()
        {
            _tvdbAssembly = GetAssemblyByName("Tvdb");

            if (_tvdbAssembly != null)
            {
                var entryPoint = _tvdbAssembly.GetType("Tvdb.EntryPoint");
                _convertToTvdbLanguages = entryPoint.GetMethod("ConvertToTvdbLanguages",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(ItemLookupInfo) }, null);

                var translations = _tvdbAssembly.GetType("Tvdb.Translations");
                _getTranslation =
                    translations.GetMethod("GetTranslation", BindingFlags.Instance | BindingFlags.NonPublic);
                var tvdbMovieProvider = _tvdbAssembly.GetType("Tvdb.TvdbMovieProvider");
                _addMovieInfo = tvdbMovieProvider.GetMethod("AddMovieInfo",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _ensureMovieInfoTvdb = tvdbMovieProvider.GetMethod("EnsureMovieInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var tvdbSeriesProvider = _tvdbAssembly.GetType("Tvdb.TvdbSeriesProvider");
                _addSeriesInfo = tvdbSeriesProvider.GetMethod("AddSeriesInfo",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _ensureSeriesInfoTvdb = tvdbSeriesProvider.GetMethod("EnsureSeriesInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var tvdbSeasonProvider = _tvdbAssembly.GetType("Tvdb.TvdbSeasonProvider");
                _getTvdbSeason =
                    tvdbSeasonProvider.GetMethod("GetTvdbSeason", BindingFlags.Instance | BindingFlags.Public);

                var tvdbEpisodeProvider = _tvdbAssembly.GetType("Tvdb.TvdbEpisodeProvider");
                _findEpisode = tvdbEpisodeProvider.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "FindEpisode" && m.GetParameters().Length == 3);
                _getEpisodeData =
                    tvdbEpisodeProvider.GetMethod("GetEpisodeData", BindingFlags.Instance | BindingFlags.Public);
            }
            else
            {
                Plugin.Instance.Logger.Warn("Tvdb plugin is not installed");
            }
        }
    }
}
