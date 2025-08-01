using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using System;
using System.Reflection;

namespace StrmAssistant.Reflection
{
    internal class MediaBrowser : ReflectionBase<MediaBrowser>
    {
        internal static PropertyInfo _isShortcutProperty;
        internal static MethodInfo _isShortcutGetter;
        internal static MethodInfo _supportsThumbnailsGetter;
        internal static MethodInfo _addPerson;
        internal static MethodInfo _getUserForRequest;
        internal static MethodInfo _afterMetadataRefresh;
        internal static MethodInfo _addLibrariesToPresentationUniqueKey;
        internal static MethodInfo _createSortName;

        static MediaBrowser()
        {
            new MediaBrowser().Initialize();
        }

        protected override void OnInitialize()
        {
            _isShortcutGetter = typeof(BaseItem).GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public)
                ?.GetGetMethod();
            _isShortcutProperty =
                typeof(BaseItem).GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public);
            var supportsThumbnailsProperty =
                typeof(Video).GetProperty("SupportsThumbnails", BindingFlags.Public | BindingFlags.Instance);
            _supportsThumbnailsGetter = supportsThumbnailsProperty?.GetGetMethod();
            _createSortName = typeof(BaseItem).GetMethod("CreateSortName",
                BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(ReadOnlySpan<char>) }, null);
            _afterMetadataRefresh =
                typeof(BaseItem).GetMethod("AfterMetadataRefresh", BindingFlags.Instance | BindingFlags.Public);

            _addPerson = typeof(PeopleHelper).GetMethod("AddPerson", BindingFlags.Static | BindingFlags.Public);

            _getUserForRequest = typeof(BaseApiService).GetMethod("GetUserForRequest",
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string), typeof(bool) }, null);
            _addLibrariesToPresentationUniqueKey = typeof(Series).GetMethod("AddLibrariesToPresentationUniqueKey",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }
}
