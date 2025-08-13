using Emby.Api;
using Emby.Api.Images;
using Emby.Api.Library;
using Emby.Api.UserLibrary;
using HarmonyLib;
using System.Reflection;

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
            _downloadImage = AccessTools.Method(typeof(RemoteImageService), "DownloadImage");
            _deleteItemsRequest = AccessTools.Method(typeof(LibraryService), "Any", new[] { typeof(DeleteItems) });
            _addVirtualFolder = AccessTools.Method(typeof(LibraryStructureService), "Post",
                new[] { typeof(AddVirtualFolder) });
            _removeVirtualFolder = AccessTools.Method(typeof(LibraryStructureService), "Any",
                new[] { typeof(RemoveVirtualFolder) });
            _addMediaPath = AccessTools.Method(typeof(LibraryStructureService), "Post", new[] { typeof(AddMediaPath) });
            _removeMediaPath =
                AccessTools.Method(typeof(LibraryStructureService), "Any", new[] { typeof(RemoveMediaPath) });
            _getRefreshOptions = AccessTools.Method(typeof(ItemRefreshService), "GetRefreshOptions");
            _getPrefixes = AccessTools.Method(typeof(TagService), "Get", new[] { typeof(GetPrefixes) });
            _getArtistPrefixes = AccessTools.Method(typeof(TagService), "Get", new[] { typeof(GetArtistPrefixes) });
        }
    }
}
