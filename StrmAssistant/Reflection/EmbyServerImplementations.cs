using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant.Reflection
{
    internal class EmbyServerImplementations : ReflectionBase<EmbyServerImplementations>
    {
        internal static MethodInfo _createHttpClientHandler;
        internal static MethodInfo _getBaseItemDtos;
        internal static MethodInfo _getBaseItemDto;
        internal static MethodInfo _attachPeople;
        internal static MethodInfo _getUserViews;
        internal static MethodInfo _deleteItem;
        internal static MethodInfo _logThumbnailImageExtractionFailure;
        internal static MethodInfo _enableJoinFtsSearch;
        internal static MethodInfo _getJoinCommandText;
        internal static MethodInfo _createSearchTerm;
        internal static MethodInfo _cacheIdsFromTextParams;
        internal static MethodInfo _saveChapters;
        internal static MethodInfo _deleteChapters;
        internal static MethodInfo _clearItemExtradata;
        internal static MethodInfo _ensureLibraryFolder;
        internal static MethodInfo _getAvailablePluginUpdates;

        static EmbyServerImplementations()
        {
            new EmbyServerImplementations().Initialize();
        }

        protected override void OnInitialize()
        {
            var embyServerImplAssembly = GetAssemblyByName("Emby.Server.Implementations");
            
            var applicationHost = embyServerImplAssembly.GetType("Emby.Server.Implementations.ApplicationHost");
            _createHttpClientHandler = applicationHost.GetMethod("CreateHttpClientHandler",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var dtoService =
                embyServerImplAssembly.GetType("Emby.Server.Implementations.Dto.DtoService");
            _getBaseItemDtos = dtoService.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "GetBaseItemDtos").OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
            _getBaseItemDto = dtoService.GetMethod("GetBaseItemDto", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(BaseItem), typeof(DtoOptions), typeof(User) }, null);
            _attachPeople =
                dtoService.GetMethod("AttachPeople", BindingFlags.NonPublic | BindingFlags.Instance);

            var userViewManager =
                embyServerImplAssembly.GetType("Emby.Server.Implementations.Library.UserViewManager");
            _getUserViews = userViewManager.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "GetUserViews")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();

            var libraryManager = embyServerImplAssembly.GetType("Emby.Server.Implementations.Library.LibraryManager");
            _deleteItem = libraryManager.GetMethod("DeleteItem",
                BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(BaseItem), typeof(DeleteOptions), typeof(BaseItem), typeof(bool) }, null);

            var sqliteItemRepository =
                embyServerImplAssembly.GetType("Emby.Server.Implementations.Data.SqliteItemRepository");
            _enableJoinFtsSearch =
                sqliteItemRepository.GetMethod("EnableJoinFtsSearch", BindingFlags.Static | BindingFlags.NonPublic);
            _getJoinCommandText = sqliteItemRepository.GetMethod("GetJoinCommandText",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _createSearchTerm =
                sqliteItemRepository.GetMethod("CreateSearchTerm", BindingFlags.NonPublic | BindingFlags.Static);
            _cacheIdsFromTextParams = sqliteItemRepository.GetMethod("CacheIdsFromTextParams",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _saveChapters = sqliteItemRepository.GetMethod("SaveChapters",
                BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(long), typeof(bool), typeof(List<ChapterInfo>) }, null);
            _deleteChapters =
                sqliteItemRepository.GetMethod("DeleteChapters", BindingFlags.Instance | BindingFlags.Public);
            _clearItemExtradata = sqliteItemRepository.GetMethod("ClearItemExtradata",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(long), typeof(long) }, null);
            _logThumbnailImageExtractionFailure = sqliteItemRepository.GetMethod("LogThumbnailImageExtractionFailure",
                BindingFlags.Public | BindingFlags.Instance);

            var collectionManager =
                embyServerImplAssembly.GetType(
                    "Emby.Server.Implementations.Collections.CollectionManager");
            _ensureLibraryFolder = collectionManager.GetMethod("EnsureLibraryFolder",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var installationManager =
                embyServerImplAssembly.GetType("Emby.Server.Implementations.Updates.InstallationManager");
            _getAvailablePluginUpdates = installationManager.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "GetAvailablePluginUpdates")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
        }
    }
}
