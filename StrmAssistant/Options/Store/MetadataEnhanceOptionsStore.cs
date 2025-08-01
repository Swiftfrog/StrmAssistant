using Emby.Media.Common.Extensions;
using Emby.Web.GenericEdit.PropertyDiff;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Common;
using StrmAssistant.Mod;
using StrmAssistant.Mod.Metadata;
using StrmAssistant.Options.UIBaseClasses.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using static StrmAssistant.Options.MetadataEnhanceOptions;

namespace StrmAssistant.Options.Store
{
    public class MetadataEnhanceOptionsStore : SimpleFileStore<MetadataEnhanceOptions>
    {
        private readonly ILogger _logger;

        public MetadataEnhanceOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger= logger;

            FileSaved += OnFileSaved;
            FileSaving += OnFileSaving;
        }

        public MetadataEnhanceOptions MetadataEnhanceOptions => GetOptions();

        private void OnFileSaving(object sender, FileSavingEventArgs e)
        {
            if (e.Options is MetadataEnhanceOptions options)
            {
                if (string.IsNullOrEmpty(options.FallbackLanguages))
                {
                    options.FallbackLanguages = "zh-sg";
                }
                else
                {
                    var languages = options.FallbackLanguages.Split(',');
                    options.FallbackLanguages = string.Join(",",
                        LanguageUtility.MovieDbFallbackLanguages
                            .Where(l => languages.Contains(l, StringComparer.OrdinalIgnoreCase))
                            .Select(l => l.ToLowerInvariant()));
                }

                if (string.IsNullOrEmpty(options.TvdbFallbackLanguages))
                {
                    options.TvdbFallbackLanguages = "zhtw";
                }
                else
                {
                    var languages = options.TvdbFallbackLanguages.Split(',');
                    options.TvdbFallbackLanguages = string.Join(",",
                        LanguageUtility.TvdbFallbackLanguages
                            .Where(l => languages.Contains(l, StringComparer.OrdinalIgnoreCase)));
                }

                if (string.IsNullOrEmpty(options.EpisodeRefreshScope))
                {
                    options.EpisodeRefreshScope = EpisodeRefreshOption.NoOverview.ToString();
                }

                options.AltMovieDbApiUrl =
                    !string.IsNullOrWhiteSpace(options.AltMovieDbApiUrl)
                        ? options.AltMovieDbApiUrl.Trim().TrimEnd('/')
                        : options.AltMovieDbApiUrl?.Trim();

                options.AltMovieDbImageUrl =
                    !string.IsNullOrWhiteSpace(options.AltMovieDbImageUrl)
                        ? options.AltMovieDbImageUrl.Trim().TrimEnd('/')
                        : options.AltMovieDbImageUrl?.Trim();

                var changes = PropertyChangeDetector.DetectObjectPropertyChanges(MetadataEnhanceOptions, options);
                var changedProperties = new HashSet<string>(changes.Select(c => c.PropertyName));

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.ChineseMovieDb)))
                {
                    if (options.ChineseMovieDb)
                    {
                        PatchManager.GetMod<ChineseMovieDb>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<ChineseMovieDb>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.ChineseTvdb)))
                {
                    if (options.ChineseTvdb)
                    {
                        PatchManager.GetMod<ChineseTvdb>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<ChineseTvdb>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.MovieDbEpisodeGroup)))
                {
                    if (options.MovieDbEpisodeGroup)
                    {
                        PatchManager.GetMod<MovieDbEpisodeGroup>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<MovieDbEpisodeGroup>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.EnhanceMovieDbPerson)))
                {
                    if (options.EnhanceMovieDbPerson)
                    {
                        PatchManager.GetMod<EnhanceMovieDbPerson>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<EnhanceMovieDbPerson>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.AltMovieDbConfig)) ||
                    changedProperties.Contains(nameof(MetadataEnhanceOptions.AltMovieDbApiUrl)) ||
                    changedProperties.Contains(nameof(MetadataEnhanceOptions.AltMovieDbImageUrl)) ||
                    changedProperties.Contains(nameof(MetadataEnhanceOptions.AltMovieDbApiKey)))
                {
                    if (options.AltMovieDbConfig)
                    {
                        if (!string.IsNullOrEmpty(options.AltMovieDbApiUrl) ||
                            !string.IsNullOrEmpty(options.AltMovieDbApiKey))
                        {
                            PatchManager.GetMod<AltMovieDbConfig>().PatchApiUrl();
                        }
                        else
                        {
                            PatchManager.GetMod<AltMovieDbConfig>().UnpatchApiUrl();
                        }

                        if (!string.IsNullOrEmpty(options.AltMovieDbImageUrl))
                        {
                            PatchManager.GetMod<AltMovieDbConfig>().PatchImageUrl();
                        }
                        else
                        {
                            PatchManager.GetMod<AltMovieDbConfig>().UnpatchImageUrl();
                        }
                    }
                    else
                    {
                        PatchManager.GetMod<AltMovieDbConfig>().UnpatchApiUrl();
                        PatchManager.GetMod<AltMovieDbConfig>().UnpatchImageUrl();
                    }

                    AltMovieDbConfig.UpdateMovieDbConfig(options);
                }

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.PreferOriginalPoster)))
                {
                    if (options.PreferOriginalPoster)
                    {
                        PatchManager.GetMod<PreferOriginalPoster>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<PreferOriginalPoster>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.PinyinSortName)))
                {
                    if (options.PinyinSortName)
                    {
                        PatchManager.GetMod<PinyinSortName>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<PinyinSortName>().Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(MetadataEnhanceOptions.EnhanceNfoMetadata)))
                {
                    if (options.EnhanceNfoMetadata)
                    {
                        PatchManager.GetMod<EnhanceNfoMetadata>().Patch();
                    }
                    else
                    {
                        PatchManager.GetMod<EnhanceNfoMetadata>().Unpatch();
                    }
                }
            }
        }

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (e.Options is MetadataEnhanceOptions options)
            {
                _logger.Info("ChineseMovieDb is set to {0}", options.ChineseMovieDb);
                _logger.Info("MovieDbFallbackLanguages is set to {0}", options.FallbackLanguages);
                _logger.Info("ChineseTvdb is set to {0}", options.ChineseTvdb);
                _logger.Info("TvdbFallbackLanguages is set to {0}", options.TvdbFallbackLanguages);
                _logger.Info("BlockNonFallbackLanguage is set to {0}", options.BlockNonFallbackLanguage);
                _logger.Info("MovieDbEpisodeGroup is set to {0}", options.MovieDbEpisodeGroup);
                _logger.Info("LocalEpisodeGroup is set to {0}", options.LocalEpisodeGroup);
                _logger.Info("EnhanceMovieDbPerson is set to {0}", options.EnhanceMovieDbPerson);
                _logger.Info("PreferOriginalPoster is set to {0}", options.PreferOriginalPoster);
                _logger.Info("PinyinSortName is set to {0}", options.PinyinSortName);
                _logger.Info("EnhanceNfoMetadata is set to {0}", options.EnhanceNfoMetadata);
                var episodeRefreshScope = string.Join(", ",
                    options.EpisodeRefreshScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s =>
                            Enum.TryParse(s.Trim(), true, out EpisodeRefreshOption option)
                                ? option.GetDescription()
                                : null)
                        .Where(d => d != null) ?? Enumerable.Empty<string>());
                _logger.Info("EpisodeRefreshScope is set to {0}", episodeRefreshScope);
                _logger.Info("EpisodeRefreshLookbackDays is set to {0}", options.EpisodeRefreshLookbackDays);
                _logger.Info("AltMovieDbConfig is set to {0}", options.AltMovieDbConfig);
                _logger.Info("AltMovieDbApiUrl is set to {0}",
                    !string.IsNullOrEmpty(options.AltMovieDbApiUrl)
                        ? options.AltMovieDbApiUrl
                        : "EMPTY");
                _logger.Info("AltMovieDbImageUrl is set to {0}",
                    !string.IsNullOrEmpty(options.AltMovieDbImageUrl)
                        ? options.AltMovieDbImageUrl
                        : "EMPTY");
                _logger.Info("AltMovieDbApiKey is set to {0}",
                    !string.IsNullOrEmpty(options.AltMovieDbApiKey)
                        ? options.AltMovieDbApiKey
                        : "EMPTY");
            }
        }
    }
}
