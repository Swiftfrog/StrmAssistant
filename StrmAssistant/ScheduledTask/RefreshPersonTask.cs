using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Options;
using StrmAssistant.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Common.LanguageUtility;

namespace StrmAssistant.ScheduledTask
{
    public class RefreshPersonTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        private static readonly HashSet<string> ProviderIdCheckKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tmdb", "imdb", "tvdb" };

        public RefreshPersonTask(ILibraryManager libraryManager)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("RefreshPerson - Scheduled Task Execute");
            await Task.Yield();
            progress.Report(0);

            var serverPreferredMetadataLanguage = Plugin.MetadataApi.GetServerPreferredMetadataLanguage();
            _logger.Info("Server Preferred Metadata Language: " + serverPreferredMetadataLanguage);
            var isServerPreferZh = !string.IsNullOrEmpty(serverPreferredMetadataLanguage) && string.Equals(
                serverPreferredMetadataLanguage.Split('-')[0], "zh", StringComparison.OrdinalIgnoreCase);

            if (!isServerPreferZh)
            {
                progress.Report(100.0);
                _ = Plugin.NotificationApi.SendMessageToAdmins(
                    $"[{Resources.PluginOptions_EditorTitle_Strm_Assistant}] {Resources.ServerPreferredMetadataLanguageIsNotZh}",
                    10000);
                _logger.Warn("Server Preferred Metadata Language is not set to Chinese.");
                _logger.Warn("RefreshPerson - Scheduled Task Aborted");
                return;
            }

            var tier2MaxConcurrentCount =
                Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.Tier2MaxConcurrentCount;
            _logger.Info("Tier2 Max Concurrent Count: " + tier2MaxConcurrentCount);

            var personQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Person) },
                IsLocked = false,
                HasAnyProviderId = new[]
                {
                    MetadataProviders.Tmdb.ToString(),
                    MetadataProviders.Imdb.ToString(),
                    MetadataProviders.Tvdb.ToString()
                }
            };
            var personItems = _libraryManager.GetItemList(personQuery).Cast<Person>().ToList();
            _logger.Info("RefreshPerson - Number of Persons Before: " + personItems.Count);

            var dupPersonItems = personItems.Where(item => item.ProviderIds != null)
                .SelectMany(item => item.ProviderIds
                    .Where(kvp => ProviderIdCheckKeys.Contains(kvp.Key))
                    .Select(kvp => new { kvp.Key, kvp.Value, item }))
                .GroupBy(kvp => new { kvp.Key, kvp.Value })
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(g => g.item))
                .GroupBy(i => i.InternalId)
                .Select(g => g.First())
                .ToList();

            if (dupPersonItems.Count > 0)
            {
                foreach (var dupItem in dupPersonItems)
                {
                    _logger.Info($"RefreshPerson - Duplicate Person: {dupItem.Name}");
                    var relatedItems = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        PersonIds = new[] { dupItem.InternalId },
                        Recursive = true,
                        IncludeItemTypes = new[]
                            { nameof(Movie), nameof(Series), nameof(Episode), nameof(Video), nameof(Trailer) }
                    });
                    foreach (var relatedItem in relatedItems)
                    {
                        _logger.Info(
                            $"RefreshPerson - Deleting duplicate person {dupItem.Name} related to {relatedItem.Path}");
                    }
                }
                _libraryManager.DeleteItems(dupPersonItems.Select(i => i.InternalId).ToArray());
            }
            _logger.Info("RefreshPerson - Number of Duplicate Persons Deleted: " + dupPersonItems.Count);

            var skipCount = personItems.Count(i => !i.HasProviderId(MetadataProviders.Tmdb));
            _logger.Info("RefreshPerson - Number of Persons without Tmdb Id Skipped: " + skipCount);

            var remainingCount = personItems.Count - dupPersonItems.Count - skipCount;
            _logger.Info("RefreshPerson - Number of Persons After: " + remainingCount);

            personItems.Clear();
            personItems.TrimExcess();

            personQuery.HasAnyProviderId = new[] { MetadataProviders.Tmdb.ToString() };

            double total = remainingCount;
            var current = 0;
            const int batchSize = 100;
            var tasks = new List<Task>();

            IsRunning = true;

            var refreshPersonMode = Plugin.Instance.MetadataEnhanceStore.GetOptions().RefreshPersonMode;
            var refreshPersonOptions = Enum.GetValues<RefreshPersonOption>()
                .Where(o => refreshPersonMode?.Contains(o.ToString(), StringComparison.OrdinalIgnoreCase) is true)
                .ToHashSet();
            _logger.Info("Refresh Person Mode: " + (refreshPersonOptions.Any()
                ? string.Join(", ", refreshPersonOptions)
                : RefreshPersonOption.Default.ToString()));
            NoAdult = refreshPersonOptions.Contains(RefreshPersonOption.NoAdult);

            for (var startIndex = 0; startIndex < remainingCount; startIndex += batchSize)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("RefreshPerson - Scheduled Task Cancelled");
                    return;
                }

                personQuery.Limit = batchSize;
                personQuery.StartIndex = startIndex;
                personItems = _libraryManager.GetItemList(personQuery).Cast<Person>().ToList();

                if (personItems.Count == 0) break;

                var personsToUpdate = new ConcurrentBag<BaseItem>();

                foreach (var item in personItems)
                {
                    var taskItem = item;

                    var metadataRefreshSkip =
                        (taskItem.IsFieldLocked(MetadataFields.Name) &&
                         taskItem.IsFieldLocked(MetadataFields.Overview)) ||
                        (!refreshPersonOptions.Contains(RefreshPersonOption.FullRefresh) &&
                         IsChineseNoJapanese(taskItem.Name) && IsChineseNoJapanese(taskItem.Overview) &&
                         taskItem.DateLastSaved >= DateTimeOffset.UtcNow.AddDays(-30));
                    var imageRefreshSkip = !refreshPersonOptions.Contains(RefreshPersonOption.FullImageRefresh) &&
                                           taskItem.HasImage(ImageType.Primary);

                    if (metadataRefreshSkip && imageRefreshSkip)
                    {
                        var currentCount = Interlocked.Increment(ref current);
                        progress.Report(currentCount / total * 100);
                        _logger.Info("RefreshPerson - Task " + currentCount + "/" + total + " Skipped - " +
                                     taskItem.Name);
                        continue;
                    }

                    try
                    {
                        await QueueManager.Tier2Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }
                    
                    if (cancellationToken.IsCancellationRequested)
                    {
                        QueueManager.Tier2Semaphore.Release();
                        _logger.Info("RefreshPerson - Scheduled Task Cancelled");
                        return;
                    }

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(
                                    Random.Shared.Next(0,
                                        Math.Max(0,
                                            tier2MaxConcurrentCount - QueueManager.Tier2Semaphore.CurrentCount) *
                                        MetadataApi.RequestIntervalMs), cancellationToken)
                                .ConfigureAwait(false);

                            if (cancellationToken.IsCancellationRequested)
                            {
                                _logger.Info("RefreshPerson - Scheduled Task Cancelled");
                                return;
                            }

                            var refreshOptions = Plugin.MetadataApi.GetMetadataFullRefreshOptions();

                            if (!metadataRefreshSkip)
                            {
                                var result = await Plugin.MetadataApi
                                    .GetPersonMetadataFromMovieDb(taskItem, serverPreferredMetadataLanguage,
                                        refreshOptions.DirectoryService, cancellationToken).ConfigureAwait(false);

                                if (result?.Item is { } person)
                                {
                                    var needsUpdate = false;

                                    var newName = person.Name;
                                    if (!taskItem.IsFieldLocked(MetadataFields.Name) &&
                                        !string.IsNullOrEmpty(newName) && !string.IsNullOrEmpty(newName))
                                    {
                                        taskItem.Name = MetadataApi.ProcessPersonInfo(newName, true);
                                        needsUpdate = true;
                                    }

                                    var newOverview = person.Overview;
                                    if (!taskItem.IsFieldLocked(MetadataFields.Overview) &&
                                        !string.IsNullOrEmpty(newOverview))
                                    {
                                        taskItem.Overview = MetadataApi.ProcessPersonInfo(newOverview, false);
                                        needsUpdate = true;
                                    }

                                    if (!taskItem.PremiereDate.HasValue && person.PremiereDate.HasValue)
                                    {
                                        taskItem.PremiereDate = person.PremiereDate;
                                        taskItem.ProductionYear = person.PremiereDate.Value.Year;
                                        needsUpdate = true;
                                    }

                                    if (!taskItem.EndDate.HasValue && person.EndDate.HasValue)
                                    {
                                        taskItem.EndDate = person.EndDate;
                                        needsUpdate = true;
                                    }

                                    if (!taskItem.ProductionLocations.Any() && person.ProductionLocations.Any())
                                    {
                                        taskItem.ProductionLocations = person.ProductionLocations;
                                        needsUpdate = true;
                                    }

                                    if (needsUpdate) personsToUpdate.Add(taskItem);
                                }
                            }

                            if (!imageRefreshSkip)
                            {
                                await taskItem.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.Info("RefreshPerson - Item Cancelled: " + taskItem.Name);
                        }
                        catch (Exception e)
                        {
                            _logger.Error("RefreshPerson - Item Failed: " + taskItem.Name);
                            _logger.Error(e.Message);
                            _logger.Debug(e.StackTrace);
                        }
                        finally
                        {
                            QueueManager.Tier2Semaphore.Release();

                            var currentCount = Interlocked.Increment(ref current);
                            progress.Report(currentCount / total * 100);
                            _logger.Info("RefreshPerson - Task " + currentCount + "/" + total + " - " + taskItem.Name);
                        }
                    }, cancellationToken);

                    tasks.Add(task);
                    Task.Delay(10).Wait();
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);

                _libraryManager.UpdateItems(personsToUpdate.ToList(), null, ItemUpdateType.MetadataDownload, true,
                    false, null, cancellationToken);

                tasks.Clear();
                personItems.Clear();
            }

            IsRunning = false;

            progress.Report(100.0);
            _logger.Info("RefreshPerson - Scheduled Task Complete");
        }

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Plugin.Instance.DefaultUICulture);

        public string Key => "RefreshPersonTask";

        public string Description => Resources.ResourceManager.GetString(
            "RefreshPersonTask_Description_Refreshes_and_repairs_Chinese_actors", Plugin.Instance.DefaultUICulture);

        public string Name => "Refresh Chinese Actor";
        //public string Name => Resources.ResourceManager.GetString("RefreshPersonTask_Name_Refresh_Chinese_Actor",
        //    Plugin.Instance.DefaultUICulture);

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public static bool IsRunning { get; private set; }

        public static bool NoAdult { get; private set; }
    }
}
