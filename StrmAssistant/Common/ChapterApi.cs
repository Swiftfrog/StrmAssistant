using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using StrmAssistant.IntroSkip;
using System;
using System.Collections.Generic;
using System.Linq;
using static StrmAssistant.Options.IntroSkipOptions;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant.Common
{
    public class ChapterApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;

        private const string MarkerSuffix = "#SA";

        public ChapterApi(ILibraryManager libraryManager, IItemRepository itemRepository)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
        }

        public long? GetIntroStart(BaseItem item)
        {
            var introStart = _itemRepository.GetChapters(item.InternalId, new[] { MarkerType.IntroStart })
                .FirstOrDefault();

            return introStart?.StartPositionTicks;
        }

        public long? GetIntroEnd(BaseItem item)
        {
            var introEnd = _itemRepository.GetChapters(item.InternalId, new[] { MarkerType.IntroEnd }).FirstOrDefault();

            return introEnd?.StartPositionTicks;
        }

        public long? GetCreditsStart(BaseItem item)
        {
            var creditsStart = _itemRepository.GetChapters(item.InternalId, new[] { MarkerType.CreditsStart })
                .FirstOrDefault();

            return creditsStart?.StartPositionTicks;
        }

        public void UpdateIntro(Episode item, SessionInfo session, long introStartPositionTicks,
            long introEndPositionTicks)
        {
            if (introStartPositionTicks > introEndPositionTicks) return;

            var resultEpisodes = FetchEpisodes(item, MarkerType.IntroEnd);

            foreach (var episode in resultEpisodes)
            {
                var chapters = _itemRepository.GetChapters(episode);

                chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);

                var introStart = new ChapterInfo
                {
                    Name = MarkerType.IntroStart + MarkerSuffix,
                    MarkerType = MarkerType.IntroStart,
                    StartPositionTicks = introStartPositionTicks
                };
                chapters.Add(introStart);
                var introEnd = new ChapterInfo
                {
                    Name = MarkerType.IntroEnd + MarkerSuffix,
                    MarkerType = MarkerType.IntroEnd,
                    StartPositionTicks = introEndPositionTicks
                };
                chapters.Add(introEnd);

                chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));

                _itemRepository.SaveChapters(episode.InternalId, chapters);
            }

            _logger.Info("Intro marker updated by " + session.UserName + " for " +
                         item.FindSeriesName() + " - " + item.FindSeasonName() + " - " + item.Season.Path);
            var introStartTime = new TimeSpan(introStartPositionTicks).ToString(@"hh\:mm\:ss\.fff");
            _logger.Info("Intro start time: " + introStartTime);
            var introEndTime = new TimeSpan(introEndPositionTicks).ToString(@"hh\:mm\:ss\.fff");
            _logger.Info("Intro end time: " + introEndTime);
            _ = Plugin.NotificationApi.IntroUpdateSendNotification(item, session, introStartTime, introEndTime);
        }

        public void UpdateCredits(Episode item, SessionInfo session, long creditsDurationTicks)
        {
            var resultEpisodes = FetchEpisodes(item, MarkerType.CreditsStart);

            foreach (var episode in resultEpisodes)
            {
                if (episode.RunTimeTicks.HasValue)
                {
                    if (episode.RunTimeTicks.Value - creditsDurationTicks > 0)
                    {
                        var chapters = _itemRepository.GetChapters(episode);
                        chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart);

                        var creditsStart = new ChapterInfo
                        {
                            Name = MarkerType.CreditsStart + MarkerSuffix,
                            MarkerType = MarkerType.CreditsStart,
                            StartPositionTicks = episode.RunTimeTicks.Value - creditsDurationTicks
                        };
                        chapters.Add(creditsStart);

                        chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));

                        _itemRepository.SaveChapters(episode.InternalId, chapters);
                    }
                }
            }

            _logger.Info("Credits marker updated by " + session.UserName + " for " +
                         item.FindSeriesName() + " - " + item.FindSeasonName() + " - " + item.Season.Path);
            var creditsDuration = new TimeSpan(creditsDurationTicks).ToString(@"hh\:mm\:ss\.fff");
            _logger.Info("Credits duration: " + new TimeSpan(creditsDurationTicks).ToString(@"hh\:mm\:ss\.fff"));
            _ = Plugin.NotificationApi.CreditsUpdateSendNotification(item, session, creditsDuration);
        }

        private static bool IsMarkerAddedByIntroSkip(ChapterInfo chapter)
        {
            return chapter.Name.EndsWith(MarkerSuffix);
        }

        private static bool AllowOverwrite(ChapterInfo chapter)
        {
            return IsMarkerAddedByIntroSkip(chapter) ||
                   IsIntroSkipPreferenceSelected(IntroSkipPreference.ResetAndOverwrite);
        }

        private static bool AllowOverwrite(ChapterInfo chapter, bool ignore)
        {
            return IsMarkerAddedByIntroSkip(chapter) || ignore;
        }

        private static BaseItem[] GetEpisodesInSeason(Season season, int? minIndexNumber = null)
        {
            return season.GetEpisodes(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    HasPath = true,
                    MediaTypes = new[] { MediaType.Video },
                    MinIndexNumber = minIndexNumber ?? 1,
                    OrderBy = new (string, SortOrder)[] { (ItemSortBy.IndexNumber, SortOrder.Ascending) }
                })
                .Items;
        }

        public List<BaseItem> FetchEpisodes(Episode item, MarkerType markerType)
        {
            var episodesInSeason = GetEpisodesInSeason(item.Season);

            var priorEpisodesWithoutMarkers = episodesInSeason.Where(e => e.IndexNumber < item.IndexNumber)
                .Where(e =>
                {
                    if (!Plugin.MediaInfoApi.HasMediaInfo(e))
                    {
                        QueueManager.MediaInfoExtractItemQueue.Enqueue(e);
                        return false;
                    }

                    var chapters = _itemRepository.GetChapters(e);
                    switch (markerType)
                    {
                        case MarkerType.IntroEnd:
                        {
                            var hasIntroStart = chapters.Any(c => c.MarkerType == MarkerType.IntroStart);
                            var hasIntroEnd = chapters.Any(c => c.MarkerType == MarkerType.IntroEnd);
                            return !hasIntroStart || !hasIntroEnd;
                        }
                        case MarkerType.CreditsStart:
                            var hasCredits = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);
                            return !hasCredits;
                    }

                    return false;
                });

            var currentEpisodes = episodesInSeason.Where(e => e.IndexNumber == item.IndexNumber);

            var followingEpisodes = episodesInSeason.Where(e => e.IndexNumber > item.IndexNumber)
                .Where(e =>
                {
                    if (!Plugin.MediaInfoApi.HasMediaInfo(e))
                    {
                        QueueManager.MediaInfoExtractItemQueue.Enqueue(e);
                        return false;
                    }

                    var chapters = _itemRepository.GetChapters(e);
                    switch (markerType)
                    {
                        case MarkerType.IntroEnd:
                        {
                            var hasIntroStart = chapters.Any(c =>
                                c.MarkerType == MarkerType.IntroStart && !AllowOverwrite(c));
                            var hasIntroEnd = chapters.Any(c =>
                                c.MarkerType == MarkerType.IntroEnd && !AllowOverwrite(c));
                            return !hasIntroStart || !hasIntroEnd;
                        }
                        case MarkerType.CreditsStart:
                            var hasCredits = chapters.Any(c =>
                                c.MarkerType == MarkerType.CreditsStart && !AllowOverwrite(c));
                            return !hasCredits;
                    }

                    return false;
                });

            var result = priorEpisodesWithoutMarkers.Concat(currentEpisodes).Concat(followingEpisodes).ToList();
            return result;
        }

        public void RemoveSeasonIntroCreditsMarkers(Episode item, SessionInfo session)
        {
            var episodesInSeason = GetEpisodesInSeason(item.Season, item.IndexNumber);

            foreach (var episode in episodesInSeason)
            {
                RemoveIntroCreditsMarkers(episode);
            }

            _logger.Info("Intro and Credits markers are cleared by " + session.UserName + " since " + item.Name +
                         " in " + item.FindSeriesName() + " - " + item.FindSeasonName() + " - " + item.Season.Path);
        }

        public void RemoveIntroCreditsMarkers(BaseItem item)
        {
            var chapters = _itemRepository.GetChapters(item);
            chapters.RemoveAll(c =>
                c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd ||
                c.MarkerType == MarkerType.CreditsStart);
            _itemRepository.SaveChapters(item.InternalId, chapters);
        }

        public List<BaseItem> FetchClearTaskItems(List<BaseItem> clearItems)
        {
            var itemsIntroSkipQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video }
            };

            var clearAll = !clearItems.Any();
            List<BaseItem> results;

            if (clearAll)
            {
                var currentScope = Plugin.Instance.IntroSkipStore.GetOptions().LibraryScope;
                var validLibraryIds = GetValidLibraryIds(currentScope);
                var libraries = _libraryManager.GetVirtualFolders()
                    .Where(f => (f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null) &&
                                (!validLibraryIds.Any() || validLibraryIds.Contains(f.Id)))
                    .ToList();

                _logger.Info("IntroSkipClear - LibraryScope: " +
                             (validLibraryIds.Any() ? string.Join(", ", libraries.Select(l => l.Name)) : "ALL"));

                itemsIntroSkipQuery.PathStartsWithAny = PlaySessionMonitor.LibraryPathsInScope.ToArray();

                results = _libraryManager.GetItemList(itemsIntroSkipQuery).ToList();
            }
            else
            {
                results = new List<BaseItem>();

                foreach (var item in clearItems)
                {
                    if (item is Series series)
                    {
                        itemsIntroSkipQuery.SeriesPresentationUniqueKey = series.PresentationUniqueKey;
                        itemsIntroSkipQuery.ParentWithPresentationUniqueKey = null;

                        results.AddRange(_libraryManager.GetItemList(itemsIntroSkipQuery));

                        _logger.Info(
                            $"IntroSkipClear - {series.Name} ({series.InternalId}) - {series.ContainingFolderPath}");
                    }
                    else if (item is Season season)
                    {
                        itemsIntroSkipQuery.ParentWithPresentationUniqueKey = season.PresentationUniqueKey;
                        itemsIntroSkipQuery.SeriesPresentationUniqueKey = null;

                        results.AddRange(_libraryManager.GetItemList(itemsIntroSkipQuery));

                        _logger.Info(
                            $"IntroSkipClear - {season.SeriesName} - {season.Name} ({season.InternalId}) - {season.ContainingFolderPath}");
                    }
                }

                results = results.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();
            }

            var items = new List<BaseItem>();

            foreach (var item in results)
            {
                var chapters = _itemRepository.GetChapters(item);

                if (chapters != null && chapters.Any())
                {
                    var hasMarkers = chapters.Any(c =>
                        (c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd ||
                         c.MarkerType == MarkerType.CreditsStart) && AllowOverwrite(c, !clearAll));
                    if (hasMarkers)
                    {
                        items.Add(item);
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(_itemRepository.GetIntroDetectionFailureResult(item.InternalId)))
                {
                    items.Add(item);
                }
            }

            if (clearAll)
            {
                _logger.Info("IntroSkipClear - Number of items: " + items.Count);
            }

            return items;
        }

        public bool SeasonHasIntroCredits(Episode item)
        {
            if (!item.IndexNumber.HasValue || !(item.ParentIndexNumber > 0))
            {
                return false;
            }

            var episodesInSeason = GetEpisodesInSeason(item.Season);

            var result = episodesInSeason.Any(e =>
            {
                var chapters = _itemRepository.GetChapters(e);
                var hasIntroMarkers =
                    chapters.Any(c => c.MarkerType == MarkerType.IntroStart && IsMarkerAddedByIntroSkip(c)) &&
                    chapters.Any(c => c.MarkerType == MarkerType.IntroEnd && IsMarkerAddedByIntroSkip(c));
                var hasCreditsStart =
                    chapters.Any(c => c.MarkerType == MarkerType.CreditsStart && IsMarkerAddedByIntroSkip(c));

                return hasIntroMarkers || hasCreditsStart;
            });

            return result;
        }

        public List<Episode> SeasonHasIntroCredits(List<Episode> episodes)
        {
            var episodesInScope = episodes.Where(e => Plugin.PlaySessionMonitor.IsLibraryInScope(e)).ToList();

            var parentIds = episodesInScope.Select(e => e.ParentId).Distinct().ToArray();

            var groupedByParent = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    HasPath = true,
                    MediaTypes = new[] { MediaType.Video },
                    ParentIds = parentIds,
                    HasIndexNumber = true
                })
                .OfType<Episode>()
                .Where(ep => !episodesInScope.Select(e => e.InternalId).Contains(ep.InternalId))
                .GroupBy(ep => ep.ParentId);

            var resultEpisodes = new List<Episode>();

            foreach (var parent in groupedByParent)
            {
                var hasMarkers = parent.Any(e =>
                {
                    var chapters = _itemRepository.GetChapters(e);

                    if (chapters != null && chapters.Any())
                    {
                        var hasIntroMarkers =
                            chapters.Any(c => c.MarkerType == MarkerType.IntroStart && IsMarkerAddedByIntroSkip(c)) &&
                            chapters.Any(c => c.MarkerType == MarkerType.IntroEnd && IsMarkerAddedByIntroSkip(c));
                        var hasCreditsMarker = chapters.Any(c =>
                            c.MarkerType == MarkerType.CreditsStart && IsMarkerAddedByIntroSkip(c));
                        return hasIntroMarkers || hasCreditsMarker;
                    }

                    return false;
                });

                if (hasMarkers)
                {
                    var episodesCanMarkers = episodesInScope.Where(e => e.ParentId == parent.Key).ToList();
                    resultEpisodes.AddRange(episodesCanMarkers);
                }
            }

            return resultEpisodes;
        }

        public void PopulateIntroCredits(List<Episode> incomingEpisodes)
        {
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ItemIds = incomingEpisodes.Select(e => e.InternalId).ToArray()
            });

            var parentIds = episodes.Select(e => e.ParentId).Distinct().ToArray();

            var groupedByParent = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    HasPath = true,
                    MediaTypes = new[] { MediaType.Video },
                    ParentIds = parentIds,
                    HasIndexNumber = true
                })
                .Where(ep => !episodes.Select(e => e.InternalId).Contains(ep.InternalId))
                .OfType<Episode>()
                .GroupBy(ep => ep.ParentId);

            foreach (var parent in groupedByParent)
            {
                Episode lastIntroEpisode = null;
                Episode lastCreditsEpisode = null;

                foreach (var episode in parent.Reverse())
                {
                    var chapters = _itemRepository.GetChapters(episode);

                    var hasIntroMarkers =
                        chapters.Any(c => c.MarkerType == MarkerType.IntroStart && IsMarkerAddedByIntroSkip(c)) &&
                        chapters.Any(c => c.MarkerType == MarkerType.IntroEnd && IsMarkerAddedByIntroSkip(c));

                    if (hasIntroMarkers && lastIntroEpisode == null)
                    {
                        lastIntroEpisode = episode;
                    }

                    var hasCreditsMarker =
                        chapters.Any(c => c.MarkerType == MarkerType.CreditsStart && IsMarkerAddedByIntroSkip(c));

                    if (hasCreditsMarker && lastCreditsEpisode == null && episode.RunTimeTicks.HasValue)
                    {
                        lastCreditsEpisode = episode;
                    }

                    if (lastIntroEpisode != null && lastCreditsEpisode != null)
                    {
                        break;
                    }
                }

                if (lastIntroEpisode != null)
                {
                    var introChapters = _itemRepository.GetChapters(lastIntroEpisode);
                    var introStart = introChapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
                    var introEnd = introChapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);

                    if (introStart != null && introEnd != null)
                    {
                        foreach (var episode in episodes.Where(e => e.ParentId == parent.Key && e.IndexNumber.HasValue))
                        {
                            var chapters = _itemRepository.GetChapters(episode);
                            chapters.Add(introStart);
                            chapters.Add(introEnd);
                            chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));
                            _itemRepository.SaveChapters(episode.InternalId, chapters);
                            _logger.Info("Intro marker updated for " + episode.Path);
                        }
                    }
                }

                if (lastCreditsEpisode?.RunTimeTicks != null)
                {
                    var lastEpisodeChapters = _itemRepository.GetChapters(lastCreditsEpisode);
                    var lastEpisodeCreditsStart = lastEpisodeChapters.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart);

                    if (lastEpisodeCreditsStart != null)
                    {
                        var creditsDurationTicks = lastCreditsEpisode.RunTimeTicks.Value - lastEpisodeCreditsStart.StartPositionTicks;
                        if (creditsDurationTicks > 0)
                        {
                            foreach (var episode in episodes.Where(e =>
                                         e.ParentId == parent.Key && e.IndexNumber.HasValue))
                            {
                                if (episode.RunTimeTicks.HasValue)
                                {
                                    var creditsStartTicks = episode.RunTimeTicks.Value - creditsDurationTicks;
                                    if (creditsStartTicks > 0)
                                    {
                                        var chapters = _itemRepository.GetChapters(episode);
                                        var creditsStart = new ChapterInfo
                                        {
                                            Name = MarkerType.CreditsStart + MarkerSuffix,
                                            MarkerType = MarkerType.CreditsStart,
                                            StartPositionTicks = creditsStartTicks
                                        };
                                        chapters.Add(creditsStart);
                                        chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));
                                        _itemRepository.SaveChapters(episode.InternalId, chapters);
                                        _logger.Info("Credits marker updated for " + episode.Path);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
