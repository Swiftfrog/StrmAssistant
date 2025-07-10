using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using StrmAssistant.Web.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Common.LanguageUtility;

namespace StrmAssistant.Web.Service
{
    public class LibraryService : BaseApiService
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IItemRepository _itemRepository;

        public LibraryService(ILibraryManager libraryManager, IItemRepository itemRepository, IFileSystem fileSystem)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _fileSystem = fileSystem;
        }

        public void Any(DeleteVersion request)
        {
            var item = _libraryManager.GetItemById(request.Id);

            if (!(item is Video video) || !(video is Movie || video is Episode) || !video.IsFileProtocol ||
                video.GetAlternateVersionIds().Count == 0)
            {
                return;
            }
            
            var user = GetUserForRequest(null);
            var collectionFolders = _libraryManager.GetCollectionFolders(item);

            if (user is null)
            {
                if (!item.CanDelete())
                {
                    return;
                }
            }
            else if (!item.CanDelete(user, collectionFolders))
            {
                return;
            }

            var enableDeepDelete = Plugin.Instance.ExperienceEnhanceStore.GetOptions().EnableDeepDelete;
            var enableNotification = Plugin.Instance.ExperienceEnhanceStore.GetOptions().EnhanceNotificationSystem;

            var deleteItems = item is Episode episode && request.DeleteParent
                ? GetSeasonEpisodesSameVersion(episode)
                : new List<BaseItem> { item };

            var allMountPaths = new Dictionary<string, bool?>();

            foreach (var deleteItem in deleteItems)
            {
                var mountPaths = enableDeepDelete || enableNotification
                    ? Plugin.LibraryApi.PrepareDeepDelete(deleteItem)
                    : null;
                
                var proceedToDelete = true;
                var deletePaths = Plugin.LibraryApi.GetVersionDeletePaths(deleteItem);

                foreach (var path in deletePaths)
                {
                    try
                    {
                        if (!path.IsDirectory)
                        {
                            _logger.Info("DeleteVersion - Attempting to delete file: " + path.FullName);
                            _fileSystem.DeleteFile(path.FullName, true);
                        }
                    }
                    catch (Exception e)
                    {
                        if (e is IOException || e is UnauthorizedAccessException)
                        {
                            proceedToDelete = false;
                            _logger.Error("DeleteVersion - Failed to delete file: " + path.FullName);
                            _logger.Error(e.Message);
                            _logger.Debug(e.StackTrace);
                        }
                    }
                }

                if (proceedToDelete)
                {
                    _itemRepository.DeleteItems(new[] { deleteItem });

                    try
                    {
                        _fileSystem.DeleteDirectory(item.GetInternalMetadataPath(), true, true);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (proceedToDelete && mountPaths != null)
                {
                    allMountPaths = allMountPaths
                        .Concat(mountPaths)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);;
                }
            }

            var localMountPaths = new HashSet<string>(allMountPaths.Where(kv => kv.Value is true).Select(kv => kv.Key));

            if (enableDeepDelete && localMountPaths.Count > 0)
            {
                Task.Run(() => Plugin.LibraryApi.ExecuteDeepDelete(localMountPaths)).ConfigureAwait(false);
            }

            if (enableNotification && allMountPaths.Count > 0 && user != null)
            {
                Task.Run(() => Plugin.NotificationApi.DeepDeleteSendNotification(item, user,
                        new HashSet<string>(allMountPaths.Keys)))
                    .ConfigureAwait(false);
            }
        }

        private List<BaseItem> GetSeasonEpisodesSameVersion(Episode episode)
        {
            var seasonFolderChildren = episode.Parent.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    Recursive = false,
                    GroupByPresentationUniqueKey = false,
                })
                .ToList();

            var seasonEpisodesCount = episode.Season.GetEpisodeIds(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) }, GroupByPresentationUniqueKey = true
                })
                .Length;

            if (seasonFolderChildren.Count == seasonEpisodesCount)
            {
                return seasonFolderChildren;
            }

            var targetCleaned = CleanEpisodeName(episode.FileNameWithoutExtension);

            var allEpisodes = episode.Season
                .GetEpisodes(new InternalItemsQuery
                {
                    GroupByPresentationUniqueKey = false, EnableTotalRecordCount = false
                })
                .Items.ToList();

            var similarEpisodes = new List<BaseItem>();

            foreach (var ep in allEpisodes)
            {
                var cleanedName = CleanEpisodeName(ep.FileNameWithoutExtension);
                var similarity = LevenshteinDistance(targetCleaned, cleanedName);

                if (similarity > 0.92)
                {
                    similarEpisodes.Add(ep);
                }
            }

            return similarEpisodes;
        }
    }
}
