using Emby.LocalMetadata.Images;
using HarmonyLib;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
            _addLocalImage = AccessTools.Method(typeof(LocalImageProvider), "AddImage",
                new[]
                {
                    typeof(FileSystemMetadata[]), typeof(List<LocalImageInfo>), typeof(string), typeof(ImageType)
                });
            _getLocalFiles = AccessTools.GetDeclaredMethods(typeof(LocalImageProvider))
                .Where(m => m.Name == "GetFiles")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
            _populateSeasonImagesFromSeasonOrSeriesFolder = AccessTools.GetDeclaredMethods(typeof(LocalImageProvider))
                .FirstOrDefault(m => m.Name.StartsWith("PopulateSeasonImagesFrom"));
        }
    }
}
