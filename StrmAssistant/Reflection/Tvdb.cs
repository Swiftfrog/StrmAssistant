using HarmonyLib;
using MediaBrowser.Controller.Providers;
using System;
using System.Reflection;
using System.Threading;
using Tvdb;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class Tvdb : ReflectionBase<Tvdb>
    {
        private static readonly string AssemblyName = "Tvdb";
        internal static readonly Assembly _tvdbAssembly;

        internal static readonly Version MinVer = new Version("1.5.8.0");
        internal static Version PluginVer;

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
            _tvdbAssembly = GetAssemblyByName(AssemblyName);

            if (_tvdbAssembly != null)
            {
                RegisterAssemblyResolve(AssemblyName, _tvdbAssembly);

                PluginVer = _tvdbAssembly.GetName().Version;

                if (PluginVer >= MinVer)
                {
                    new Tvdb().Initialize();
                }
                else
                {
                    Plugin.Instance.Logger.Info(
                        $"{AssemblyName} plugin {PluginVer} is not supported. Minimum supported version is {MinVer}.");
                }
            }
            else
            {
                Plugin.Instance.Logger.Info($"{AssemblyName} plugin is not installed");
            }
        }

        internal static bool IsSupported => _tvdbAssembly != null && PluginVer >= MinVer;

        protected override void OnInitialize()
        {
            _convertToTvdbLanguages = AccessTools.Method(typeof(EntryPoint), "ConvertToTvdbLanguages",
                new[] { typeof(ItemLookupInfo) });
            _getTranslation = AccessTools.Method(typeof(Translations), "GetTranslation");
            _addMovieInfo = AccessTools.Method(typeof(TvdbMovieProvider), "AddMovieInfo");
            _ensureMovieInfoTvdb = AccessTools.Method(typeof(TvdbMovieProvider), "EnsureMovieInfo");
            _addSeriesInfo = AccessTools.Method(typeof(TvdbSeriesProvider), "AddSeriesInfo");
            _ensureSeriesInfoTvdb = AccessTools.Method(typeof(TvdbSeriesProvider), "EnsureSeriesInfo");
            _getTvdbSeason = AccessTools.Method(typeof(TvdbSeasonProvider), "GetTvdbSeason",
                new[] { typeof(SeasonInfo), typeof(IDirectoryService), typeof(CancellationToken) });
            _findEpisode = AccessTools.Method(typeof(TvdbEpisodeProvider), "FindEpisode",
                new[] { typeof(EpisodesData), typeof(EpisodeInfo), typeof(int?) });
            _getEpisodeData = AccessTools.Method(typeof(TvdbEpisodeProvider), "GetEpisodeData");
        }
    }
}
