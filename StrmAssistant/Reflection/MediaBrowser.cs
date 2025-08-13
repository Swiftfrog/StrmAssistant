using HarmonyLib;
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
            _isShortcutGetter = AccessTools.PropertyGetter(typeof(BaseItem), "IsShortcut");
            _isShortcutProperty = AccessTools.Property(typeof(BaseItem), "IsShortcut");
            _supportsThumbnailsGetter = AccessTools.PropertyGetter(typeof(Video), "SupportsThumbnails");
            _createSortName =
                AccessTools.Method(typeof(BaseItem), "CreateSortName", new[] { typeof(ReadOnlySpan<char>) });
            _afterMetadataRefresh = AccessTools.Method(typeof(BaseItem), "AfterMetadataRefresh");
            _addPerson = AccessTools.Method(typeof(PeopleHelper), "AddPerson");
            _getUserForRequest = AccessTools.Method(typeof(BaseApiService), "GetUserForRequest",
                new[] { typeof(string), typeof(bool) });
            _addLibrariesToPresentationUniqueKey =
                AccessTools.Method(typeof(Series), "AddLibrariesToPresentationUniqueKey");
        }
    }
}
