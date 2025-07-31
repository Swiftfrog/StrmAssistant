using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Reflection
{
    internal class EmbyLocalMetadata : ReflectionBase<EmbyLocalMetadata>
    {
        internal static MethodInfo _addLocalImage;
        internal static MethodInfo _getLocalFiles;
        internal static MethodInfo _populateSeasonImagesFromSeasonOrSeriesFolder;

        static EmbyLocalMetadata()
        {
            new EmbyLocalMetadata().Initialize();
        }

        protected override void OnInitialize()
        {
            var embyLocalMetadata = GetAssemblyByName("Emby.LocalMetadata");

            var localImageProvider = embyLocalMetadata.GetType("Emby.LocalMetadata.Images.LocalImageProvider");
            _addLocalImage = localImageProvider.GetMethod("AddImage", BindingFlags.Instance | BindingFlags.NonPublic,
                new[]
                {
                    typeof(FileSystemMetadata[]), typeof(List<LocalImageInfo>), typeof(string), typeof(ImageType)
                });
            _getLocalFiles = localImageProvider.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(m => m.Name == "GetFiles")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
            _populateSeasonImagesFromSeasonOrSeriesFolder = localImageProvider
                ?.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name.StartsWith("PopulateSeasonImagesFrom"));
        }
    }
}
