using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;
using System;

namespace StrmAssistant.Web.Service
{
    public class LibraryStructureService : IService, IRequiresRequest
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public LibraryStructureService(ILibraryManager libraryManager)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
        }

        public IRequest Request { get; set; }

        public void Post(CopyVirtualFolder request)
        {
            var sourceLibrary = _libraryManager.GetItemById(request.Id);
            var sourceOptions = _libraryManager.GetLibraryOptions(sourceLibrary);

            var targetOptions = LibraryApi.CopyLibraryOptions(sourceOptions);
            targetOptions.PathInfos = Array.Empty<MediaPathInfo>();

            var suffix = Random.Shared.Next(100, 999).ToString();
            _libraryManager.AddVirtualFolder(sourceLibrary.Name + " #" + suffix, targetOptions, false);
        }
    }
}
