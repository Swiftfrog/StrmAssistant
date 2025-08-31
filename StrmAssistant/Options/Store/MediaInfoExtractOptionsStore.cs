using Emby.Web.GenericEdit.PropertyDiff;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using StrmAssistant.Mod.MediaInfo;
using StrmAssistant.Options.UIBaseClasses.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Options.MediaInfoExtractOptions;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant.Options.Store
{
    public class MediaInfoExtractOptionsStore : SimpleFileStore<MediaInfoExtractOptions>
    {
        private readonly ILogger _logger;

        public MediaInfoExtractOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;

            FileSaved += OnFileSaved;
            FileSaving += OnFileSaving;
        }

        public MediaInfoExtractOptions MediaInfoExtractOptions => GetOptions();

        private void OnFileSaving(object sender, FileSavingEventArgs e)
        {
            if (e.Options is MediaInfoExtractOptions options)
            {
                options.LibraryScope = string.Join(",",
                    options.LibraryScope
                        ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(v => options.LibraryList.Any(option => option.Value == v)) ??
                    Enumerable.Empty<string>());

                var controlFeatures = options.ExclusiveControlFeatures;
                var selectedFeatures = controlFeatures.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(f =>
                        !(controlFeatures.Contains(ExclusiveControl.CatchAllBlock.ToString()) &&
                          (f == ExclusiveControl.CatchAllAllow.ToString() ||
                           f == ExclusiveControl.ExtractOnFileChange.ToString())) &&
                        !(controlFeatures.Contains(ExclusiveControl.IgnoreFileChange.ToString()) &&
                          f == ExclusiveControl.ExtractOnFileChange.ToString()))
                    .ToList();
                options.ExclusiveControlFeatures = string.Join(",", selectedFeatures);

                var changes = PropertyChangeDetector.DetectObjectPropertyChanges(MediaInfoExtractOptions, options);
                var changedProperties = new HashSet<string>(changes.Select(c => c.PropertyName));

                if (changedProperties.Contains(nameof(MediaInfoExtractOptions.PersistMediaInfoMode)))
                {
                    if (options.IsModSupported)
                    {
                        if (options.PersistMediaInfoMode != PersistMediaInfoOption.None.ToString())
                        {
                            PatchManager.GetMod<PersistMediaInfoHelper>().Patch();
                        }
                        else
                        {
                            PatchManager.GetMod<PersistMediaInfoHelper>().Unpatch();
                        }
                    }
                }

                if (changedProperties.Contains(nameof(MediaInfoExtractOptions.EnableImageCapture)))
                {
                    if (options.EnableImageCapture)
                    {
                        PatchManager.GetMod<EnableImageCapture>().Patch();
                        if (Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount !=
                            EnableImageCapture.SemaphoreFfmpegMaxCount)
                        {
                            NotifyPendingRestart();
                        }
                    }
                    else
                    {
                        PatchManager.GetMod<EnableImageCapture>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MediaInfoExtractOptions.ExclusiveExtract)))
                {
                    if (options.ExclusiveExtract)
                    {
                        PatchManager.GetMod<ExclusiveExtract>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<ExclusiveExtract>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MediaInfoExtractOptions.ExclusiveControlFeatures)) ||
                    changedProperties.Contains(nameof(MediaInfoExtractOptions.PersistMediaInfoMode)))
                {
                    UpdateExclusiveControlFeatures(options);
                }

                if (changedProperties.Contains(nameof(MediaInfoExtractOptions.LibraryScope)))
                {
                    Plugin.LibraryApi.UpdateLibraryPathsInScope(options.LibraryScope);
                }
            }
        }

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (e.Options is MediaInfoExtractOptions options)
            {
                _logger.Info("PersistMediaInfoMode is set to {0}", options.PersistMediaInfoMode);
                _logger.Info("MediaInfoJsonRootFolder is set to {0}",
                    !string.IsNullOrEmpty(options.MediaInfoJsonRootFolder) ? options.MediaInfoJsonRootFolder : "EMPTY");
                _logger.Info("IncludeExtra is set to {0}", options.IncludeExtra);
                _logger.Info("EnableImageCapture is set to {0}", options.EnableImageCapture);
                _logger.Info("ImageCapturePosition is set to {0}", options.ImageCapturePosition);
                _logger.Info("ExclusiveExtract is set to {0}", options.ExclusiveExtract);

                var controlFeatures = GetSelectedExclusiveFeatureDescription();
                _logger.Info("ExclusiveExtract - ControlFeatures is set to {0}",
                    string.IsNullOrEmpty(controlFeatures) ? "EMPTY" : controlFeatures);

                var libraryScope = string.Join(", ",
                    options.LibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => options.LibraryList.FirstOrDefault(option => option.Value == v)?.Name) ??
                    Enumerable.Empty<string>());
                _logger.Info("MediaInfoExtract - LibraryScope is set to {0}",
                    string.IsNullOrEmpty(libraryScope) ? "ALL" : libraryScope);
            }
        }
    }
}
