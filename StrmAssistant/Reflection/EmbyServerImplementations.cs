using Emby.Server.Implementations;
using Emby.Server.Implementations.Collections;
using Emby.Server.Implementations.Data;
using Emby.Server.Implementations.Dto;
using Emby.Server.Implementations.Library;
using Emby.Server.Implementations.Updates;
using HarmonyLib;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        internal static MethodInfo _ensureLibraryFolder;
        internal static MethodInfo _getAvailablePluginUpdates;

        static EmbyServerImplementations()
        {
            new EmbyServerImplementations().Initialize();
        }

        protected override void OnInitialize()
        {
            _createHttpClientHandler = AccessTools.Method(typeof(ApplicationHost), "CreateHttpClientHandler");
            _getBaseItemDtos = AccessTools.GetDeclaredMethods(typeof(DtoService))
                .Where(m => m.Name == "GetBaseItemDtos")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
            _getBaseItemDto = AccessTools.Method(typeof(DtoService), "GetBaseItemDto",
                new[] { typeof(BaseItem), typeof(DtoOptions), typeof(User) });
            _attachPeople = AccessTools.Method(typeof(DtoService), "AttachPeople");

            _getUserViews = AccessTools.GetDeclaredMethods(typeof(UserViewManager))
                .Where(m => m.Name == "GetUserViews")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();

            _deleteItem = AccessTools.Method(typeof(LibraryManager), "DeleteItem",
                new[] { typeof(BaseItem), typeof(DeleteOptions), typeof(BaseItem), typeof(bool) });

            _enableJoinFtsSearch = AccessTools.Method(typeof(SqliteItemRepository), "EnableJoinFtsSearch");
            _getJoinCommandText = AccessTools.Method(typeof(SqliteItemRepository), "GetJoinCommandText");
            _createSearchTerm = AccessTools.Method(typeof(SqliteItemRepository), "CreateSearchTerm");
            _cacheIdsFromTextParams = AccessTools.Method(typeof(SqliteItemRepository), "CacheIdsFromTextParams");
            _saveChapters = AccessTools.Method(typeof(SqliteItemRepository), "SaveChapters",
                new[] { typeof(long), typeof(bool), typeof(List<ChapterInfo>) });
            _deleteChapters = AccessTools.Method(typeof(SqliteItemRepository), "DeleteChapters");
            _logThumbnailImageExtractionFailure =
                AccessTools.Method(typeof(SqliteItemRepository), "LogThumbnailImageExtractionFailure");

            _ensureLibraryFolder = AccessTools.Method(typeof(CollectionManager), "EnsureLibraryFolder");

            _getAvailablePluginUpdates = AccessTools.GetDeclaredMethods(typeof(InstallationManager))
                .Where(m => m.Name == "GetAvailablePluginUpdates")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
        }
    }
}
