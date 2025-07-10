using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using StrmAssistant.Web.Api;
using System.Collections.Generic;

namespace StrmAssistant.Web.Service
{
    public class ChapterService : BaseApiService
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public ChapterService(ILibraryManager libraryManager)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
        }

        public void Post(ClearIntro request)
        {
            var itemById = _libraryManager.GetItemById(request.Id);

            if (!(itemById is Series || itemById is Season)) return;

            var episodes = Plugin.ChapterApi.FetchClearTaskItems(new List<BaseItem> { itemById });

            foreach (var item in episodes)
            {
                Plugin.ChapterApi.RemoveIntroCreditsMarkers(item);
                Plugin.FingerprintApi.ClearFingerprintCache(item);
                _logger.Info("IntroSkipClear - " + item.Name + " - " + item.Path);
            }
        }
    }
}
