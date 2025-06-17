using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using StrmAssistant.Common;
using StrmAssistant.Mod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Provider
{
    public class MovieDbSeriesProvider : ISeriesMetadataProvider
    {
        public string Name => "TheMovieDb";

        public async Task<RemoteSearchResult[]> GetAllEpisodes(SeriesInfo seriesInfo,
            CancellationToken cancellationToken)
        {
            if (!Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.EnhanceMissingEpisodes)
            {
                return Array.Empty<RemoteSearchResult>();
            }

            var tmdbId = seriesInfo.GetProviderId(MetadataProviders.Tmdb);
            if (string.IsNullOrEmpty(tmdbId)) return Array.Empty<RemoteSearchResult>();
            var language = seriesInfo.MetadataLanguage;
            var episodeGroupId = seriesInfo.GetProviderId(MovieDbEpisodeGroupExternalId.StaticName);
            episodeGroupId = episodeGroupId?.Trim();

            EpisodeGroupResponse episodeGroupInfo = null;
            string localEpisodeGroupPath = null;

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup &&
                EnhanceMissingEpisodes.CurrentSeriesContainingFolderPath.Value != null)
            {
                localEpisodeGroupPath = Path.Combine(EnhanceMissingEpisodes.CurrentSeriesContainingFolderPath.Value,
                    MovieDbEpisodeGroup.LocalEpisodeGroupFileName);
                EnhanceMissingEpisodes.CurrentSeriesContainingFolderPath.Value = null;

                episodeGroupInfo = await Plugin.MetadataApi.FetchLocalEpisodeGroup(localEpisodeGroupPath)
                    .ConfigureAwait(false);
            }

            try
            {
                if (episodeGroupInfo is null && !string.IsNullOrEmpty(episodeGroupId))
                {
                    episodeGroupInfo = await Plugin.MetadataApi
                        .FetchOnlineEpisodeGroup(tmdbId, episodeGroupId, language, localEpisodeGroupPath,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                SeriesResponseInfo seriesInfoResponse;

                if (episodeGroupInfo != null)
                {
                    if (episodeGroupInfo.groups is null || !episodeGroupInfo.groups.Any())
                        return Array.Empty<RemoteSearchResult>();

                    var firstGroup = episodeGroupInfo.groups.FirstOrDefault();

                    if (firstGroup?.episodes is null || !firstGroup.episodes.Any())
                        return Array.Empty<RemoteSearchResult>();

                    var firstEpisode = firstGroup.episodes.First();

                    if (string.IsNullOrEmpty(firstEpisode.name))
                    {
                        seriesInfoResponse = await FetchSeriesInfoAsync(tmdbId, language, cancellationToken)
                            .ConfigureAwait(false);

                        if (seriesInfoResponse?.seasons is null)
                        {
                            return Array.Empty<RemoteSearchResult>();
                        }

                        foreach (var group in episodeGroupInfo.groups.Where(g => g?.episodes != null))
                        {
                            foreach (var episode in group.episodes)
                            {
                                var matchedSeason = seriesInfoResponse.seasons.FirstOrDefault(season =>
                                    season.season_number == episode.season_number);

                                var mappedEpisode =
                                    matchedSeason?.episodes?.FirstOrDefault(ep =>
                                        ep?.episode_number == episode.episode_number);

                                if (mappedEpisode != null)
                                {
                                    episode.air_date = mappedEpisode.air_date;
                                    episode.name = mappedEpisode.name;
                                    episode.overview = mappedEpisode.overview;
                                    episode.id = mappedEpisode.id;
                                }
                            }
                        }
                    }

                    return episodeGroupInfo.groups.Where(g => g?.episodes != null)
                        .SelectMany(group => group.episodes.Where(ep => ep != null),
                            (group, episode) => new RemoteSearchResult
                            {
                                SearchProviderName = Name,
                                IndexNumber = episode.order + 1,
                                ParentIndexNumber = group.order,
                                Name = episode.name,
                                Overview = episode.overview,
                                PremiereDate = episode.air_date,
                                ProductionYear = episode.air_date != default ? episode.air_date.Year : (int?)null,
                                ProviderIds = new ProviderIdDictionary
                                {
                                    {
                                        MetadataProviders.Tmdb.ToString(),
                                        episode.id.ToString(CultureInfo.InvariantCulture)
                                    }
                                }
                            })
                        .ToArray();
                }

                seriesInfoResponse =
                    await FetchSeriesInfoAsync(tmdbId, language, cancellationToken).ConfigureAwait(false);

                if (seriesInfoResponse?.seasons != null)
                {
                    return seriesInfoResponse.seasons.Where(season => season?.episodes != null)
                        .SelectMany(season => season.episodes.Select(episode => new RemoteSearchResult
                        {
                            SearchProviderName = Name,
                            IndexNumber = episode.episode_number,
                            ParentIndexNumber = episode.season_number,
                            Name = episode.name,
                            Overview = episode.overview,
                            PremiereDate = episode.air_date,
                            ProductionYear = episode.air_date.Year,
                            ProviderIds = new ProviderIdDictionary
                            {
                                {
                                    MetadataProviders.Tmdb.ToString(),
                                    episode.id.ToString(CultureInfo.InvariantCulture)
                                }
                            }
                        }))
                        .ToArray();
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
            }

            return Array.Empty<RemoteSearchResult>();
        }

        private static async Task<SeriesResponseInfo> FetchSeriesInfoAsync(string tmdbId, string language,
            CancellationToken cancellationToken)
        {
            var cacheFilename = "series-all-episodes";
            if (!string.IsNullOrEmpty(language)) cacheFilename = cacheFilename + "-" + language;
            var cacheKey = "tmdb_all_episodes_" + tmdbId + "_" + language;
            var cachePath = Path.Combine(Plugin.Instance.ApplicationPaths.CachePath, "tmdb-tv", tmdbId,
                cacheFilename + ".json");

            var seriesInfoResponse = Plugin.MetadataApi.TryGetFromCache<SeriesResponseInfo>(cacheKey, cachePath);

            if (seriesInfoResponse != null)
            {
                return seriesInfoResponse;
            }

            var seriesUrl = MetadataApi.BuildMovieDbApiUrl($"tv/{tmdbId}", language);
            seriesInfoResponse = await Plugin.MetadataApi
                .GetMovieDbResponse<SeriesResponseInfo>(seriesUrl, cancellationToken)
                .ConfigureAwait(false);

            if (seriesInfoResponse?.seasons is null) return null;

            var seasonResults = new List<SeasonResponseInfo>();

            foreach (var season in seriesInfoResponse.seasons)
            {
                var seasonInfoResponse =
                    await FetchSeasonInfoAsync(tmdbId, season.season_number, language, cancellationToken)
                        .ConfigureAwait(false);
                seasonResults.Add(seasonInfoResponse);
            }

            seriesInfoResponse.seasons = seasonResults;

            if (seriesInfoResponse.seasons.All(season => season.episodes is null || !season.episodes.Any()))
            {
                return null;
            }

            Plugin.MetadataApi.AddOrUpdateCache(seriesInfoResponse, cacheKey, cachePath);

            return seriesInfoResponse;
        }

        private static async Task<SeasonResponseInfo> FetchSeasonInfoAsync(string tmdbId, int seasonNumber, string language,
            CancellationToken cancellationToken)
        {
            var seasonUrl = MetadataApi.BuildMovieDbApiUrl($"tv/{tmdbId}/season/{seasonNumber}", language);

            var seasonInfoResponse = await Plugin.MetadataApi
                .GetMovieDbResponse<SeasonResponseInfo>(seasonUrl, cancellationToken)
                .ConfigureAwait(false);

            return seasonInfoResponse;
        }
    }
}
