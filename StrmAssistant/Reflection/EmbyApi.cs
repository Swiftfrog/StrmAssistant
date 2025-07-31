using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class EmbyApi : ReflectionBase<EmbyApi>
    {
        internal static MethodInfo _downloadImage;
        internal static MethodInfo _deleteItemsRequest;
        internal static MethodInfo _addVirtualFolder;
        internal static MethodInfo _removeVirtualFolder;
        internal static MethodInfo _addMediaPath;
        internal static MethodInfo _removeMediaPath;
        internal static MethodInfo _getRefreshOptions;
        internal static MethodInfo _getPrefixes;
        internal static MethodInfo _getArtistPrefixes;

        static EmbyApi()
        {
            new EmbyApi().Initialize();
        }

        protected override void OnInitialize()
        {
            var embyApi = GetAssemblyByName("Emby.Api");

            var remoteImageService = embyApi.GetType("Emby.Api.Images.RemoteImageService");
            _downloadImage = remoteImageService.GetMethod("DownloadImage",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var libraryService = embyApi.GetType("Emby.Api.Library.LibraryService");
            _deleteItemsRequest =
                libraryService.GetMethod("Any", new[] { embyApi.GetType("Emby.Api.Library.DeleteItems") });

            var libraryStructureService = embyApi.GetType("Emby.Api.Library.LibraryStructureService");
            _addVirtualFolder = libraryStructureService.GetMethod("Post",
                new[] { embyApi.GetType("Emby.Api.Library.AddVirtualFolder") });
            _removeVirtualFolder = libraryStructureService.GetMethod("Any",
                new[] { embyApi.GetType("Emby.Api.Library.RemoveVirtualFolder") });
            _addMediaPath = libraryStructureService.GetMethod("Post",
                new[] { embyApi.GetType("Emby.Api.Library.AddMediaPath") });
            _removeMediaPath = libraryStructureService.GetMethod("Any",
                new[] { embyApi.GetType("Emby.Api.Library.RemoveMediaPath") });

            var itemRefreshService = embyApi.GetType("Emby.Api.ItemRefreshService");
            _getRefreshOptions =
                itemRefreshService.GetMethod("GetRefreshOptions", BindingFlags.Instance | BindingFlags.NonPublic);

            var tagService = embyApi.GetType("Emby.Api.UserLibrary.TagService");
            _getPrefixes = tagService.GetMethod("Get", new[] { embyApi.GetType("Emby.Api.UserLibrary.GetPrefixes") });
            _getArtistPrefixes =
                tagService.GetMethod("Get", new[] { embyApi.GetType("Emby.Api.UserLibrary.GetArtistPrefixes") });
        }
    }
}
