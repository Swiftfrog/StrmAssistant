using Emby.Media.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using static StrmAssistant.Options.GeneralOptions;
using static StrmAssistant.Options.IntroSkipOptions;
using static StrmAssistant.Options.MediaInfoExtractOptions;
using static StrmAssistant.Options.ModOptions;

namespace StrmAssistant.Options
{
    public static class Utility
    {
        private static HashSet<string> _selectedExclusiveFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<string, byte>> ItemExclusiveFeatures =
            new ConcurrentDictionary<long, ConcurrentDictionary<string, byte>>();

        private static HashSet<string> _selectedCatchupTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _selectedIntroSkipPreferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string[] _includeItemTypes = Array.Empty<string>();

        public static readonly HashSet<string> ExcludedCollectionTypes = new HashSet<string>
        {
            CollectionType.Books.ToString(),
            CollectionType.Photos.ToString(),
            CollectionType.Games.ToString(),
            CollectionType.LiveTv.ToString(),
            CollectionType.Playlists.ToString(),
            CollectionType.BoxSets.ToString()
        };

        public static readonly ExtraType[] IncludeExtraTypes =
        {
            ExtraType.AdditionalPart, ExtraType.BehindTheScenes, ExtraType.Clip, ExtraType.DeletedScene,
            ExtraType.Interview, ExtraType.Sample, ExtraType.Scene, ExtraType.ThemeSong, ExtraType.ThemeVideo,
            ExtraType.Trailer
        };

        public static readonly HashSet<string> MediaInfoExcludeMediaExtensions = Plugin.Instance.MediaInfoExtractStore
            .GetOptions()
            .MediaInfoExcludeMediaContainers.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(c =>
            {
                if (!Enum.TryParse<MediaContainers>(c.Trim(), true, out var container)) return Array.Empty<string>();
                var aliases = container.GetAliases();
                return aliases?.Where(a => !string.IsNullOrWhiteSpace(a)) ?? Array.Empty<string>();
            })
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .ToHashSet();

        public static readonly HashSet<MediaContainers> ImageCaptureExcludeMediaContainers = Plugin.Instance
            .MediaInfoExtractStore.GetOptions()
            .ImageCaptureExcludeMediaContainers.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(c =>
                Enum.TryParse<MediaContainers>(c.Trim(), true, out var container) ? container : (MediaContainers?)null)
            .Where(container => container.HasValue)
            .Select(container => container.Value)
            .ToHashSet();

        public static readonly HashSet<MediaContainers> VideoThumbnailExcludeMediaContainers = Plugin.Instance
            .MediaInfoExtractStore.GetOptions()
            .VideoThumbnailExcludeMediaContainers.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(c =>
                Enum.TryParse<MediaContainers>(c.Trim(), true, out var container) ? container : (MediaContainers?)null)
            .Where(container => container.HasValue)
            .Select(container => container.Value)
            .ToHashSet();

        public static readonly Version AppVer = Plugin.Instance.ApplicationHost.ApplicationVersion;
        public static readonly Version Ver4830 = new Version("4.8.3.0");
        public static readonly Version Ver4840 = new Version("4.8.4.0");
        public static readonly Version Ver4900 = new Version("4.9.0.0");
        public static readonly Version Ver49015 = new Version("4.9.0.15");
        public static readonly Version Ver49025 = new Version("4.9.0.25");
        public static readonly Version Ver49036 = new Version("4.9.0.36");
        public static readonly Version Ver49037 = new Version("4.9.0.37");
        public static readonly Version VerMax = new Version("4.9.0.48");

        public static void InitializeOptionCache()
        {
            UpdateExclusiveControlFeatures(Plugin.Instance.MediaInfoExtractStore.GetOptions());
            UpdateCatchupScope(Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.CatchupTaskScope);
            UpdateIntroSkipPreferences(Plugin.Instance.IntroSkipStore.GetOptions().IntroSkipPreferences);
            UpdateSearchScope(Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.SearchScope);
        }

        public static void UpdateExclusiveControlFeatures(MediaInfoExtractOptions options)
        {
            var featureSet = new HashSet<string>(
                options.ExclusiveControlFeatures?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            if (options.PersistMediaInfoMode == PersistMediaInfoOption.Restore.ToString())
            {
                featureSet.Add(ExclusiveControl.IgnoreFileChange.ToString());
                featureSet.Add(ExclusiveControl.CatchAllBlock.ToString());
            }
            else if (options.PersistMediaInfoMode == PersistMediaInfoOption.None.ToString())
            {
                featureSet.Add(ExclusiveControl.NoPersistIntegration.ToString());
            }

            _selectedExclusiveFeatures = featureSet;
        }

        public static bool IsExclusiveFeatureSelected(long? itemId = null, params ExclusiveControl[] featuresToCheck)
        {
            if (itemId.HasValue && ItemExclusiveFeatures.TryGetValue(itemId.Value, out var itemFeatures))
            {
                return featuresToCheck.Any(f => itemFeatures.ContainsKey(f.ToString()));
            }

            return featuresToCheck.Any(f => _selectedExclusiveFeatures.Contains(f.ToString()));
        }

        public static bool IsExclusiveFeatureSelected(params ExclusiveControl[] featuresToCheck)
        {
            return IsExclusiveFeatureSelected(null, featuresToCheck);
        }

        public static void EnableItemExclusiveFeatures(long itemId, params ExclusiveControl[] features)
        {
            var itemFeatures = ItemExclusiveFeatures.GetOrAdd(itemId, _ => new ConcurrentDictionary<string, byte>());

            foreach (var feature in features)
            {
                itemFeatures.TryAdd(feature.ToString(), 0);
            }
        }

        public static void DisableItemExclusiveFeatures(long itemId, params ExclusiveControl[] features)
        {
            if (ItemExclusiveFeatures.TryGetValue(itemId, out var itemFeatures))
            {
                foreach (var feature in features)
                {
                    itemFeatures.TryRemove(feature.ToString(), out _);
                }

                if (itemFeatures.IsEmpty)
                {
                    ItemExclusiveFeatures.TryRemove(itemId, out _);
                }
            }
        }

        public static void ClearItemExclusiveFeatures(long itemId)
        {
            ItemExclusiveFeatures.TryRemove(itemId, out _);
        }

        public static string GetSelectedExclusiveFeatureDescription()
        {
            return string.Join(", ",
                _selectedExclusiveFeatures
                    .Select(feature =>
                        Enum.TryParse(feature.Trim(), true, out ExclusiveControl type)
                            ? type
                            : (ExclusiveControl?)null)
                    .Where(type => type.HasValue)
                    .OrderBy(type => type)
                    .Select(type => type.Value.GetDescription()));
        }

        public static void UpdateCatchupScope(string currentScope)
        {
            _selectedCatchupTasks = new HashSet<string>(
                currentScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsCatchupTaskSelected(params CatchupTask[] tasksToCheck)
        {
            return tasksToCheck.Any(f => _selectedCatchupTasks.Contains(f.ToString()));
        }

        public static string GetSelectedCatchupTaskDescription()
        {
            return string.Join(", ",
                _selectedCatchupTasks
                    .Select(task =>
                        Enum.TryParse(task.Trim(), true, out CatchupTask type)
                            ? type
                            : (CatchupTask?)null)
                    .Where(type => type.HasValue)
                    .OrderBy(type => type)
                    .Select(type => type.Value.GetDescription()));
        }

        public static void UpdateIntroSkipPreferences(string currentPreferences)
        {
            _selectedIntroSkipPreferences = new HashSet<string>(
                currentPreferences?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsIntroSkipPreferenceSelected(params IntroSkipPreference[] preferencesToCheck)
        {
            return preferencesToCheck.Any(f => _selectedIntroSkipPreferences.Contains(f.ToString()));
        }

        public static string GetSelectedIntroSkipPreferenceDescription()
        {
            return string.Join(", ",
                _selectedIntroSkipPreferences
                    .Select(pref =>
                        Enum.TryParse(pref.Trim(), true, out IntroSkipPreference type)
                            ? type
                            : (IntroSkipPreference?)null)
                    .Where(type => type.HasValue)
                    .OrderBy(type => type)
                    .Select(type => type.Value.GetDescription()));
        }

        public static void UpdateSearchScope(string currentScope)
        {
            var searchScope = currentScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ??
                              Array.Empty<string>();

            var includeItemTypes = new List<string>();

            foreach (var scope in searchScope)
            {
                if (Enum.TryParse(scope, true, out SearchItemType type))
                {
                    switch (type)
                    {
                        case SearchItemType.Book:
                            includeItemTypes.AddRange(new[] { nameof(Book) });
                            break;
                        case SearchItemType.Collection:
                            includeItemTypes.AddRange(new[] { nameof(BoxSet) });
                            break;
                        case SearchItemType.Episode:
                            includeItemTypes.AddRange(new[] { nameof(Episode) });
                            break;
                        case SearchItemType.Game:
                            includeItemTypes.AddRange(new[] { nameof(Game), nameof(GameSystem) });
                            break;
                        case SearchItemType.Genre:
                            includeItemTypes.AddRange(new[] { nameof(MusicGenre), nameof(GameGenre), nameof(Genre) });
                            break;
                        case SearchItemType.LiveTv:
                            includeItemTypes.AddRange(new[]
                            {
                                nameof(LiveTvChannel), nameof(LiveTvProgram), "LiveTVSeries"
                            });
                            break;
                        case SearchItemType.Movie:
                            includeItemTypes.AddRange(new[] { nameof(Movie) });
                            break;
                        case SearchItemType.Music:
                            includeItemTypes.AddRange(new[] { nameof(Audio), nameof(MusicVideo) });
                            break;
                        case SearchItemType.MusicAlbum:
                            includeItemTypes.AddRange(new[] { nameof(MusicAlbum) });
                            break;
                        case SearchItemType.Person:
                            includeItemTypes.AddRange(new[] { nameof(Person) });
                            break;
                        case SearchItemType.MusicArtist:
                            includeItemTypes.AddRange(new[] { nameof(MusicArtist) });
                            break;
                        case SearchItemType.Photo:
                            includeItemTypes.AddRange(new[] { nameof(Photo) });
                            break;
                        case SearchItemType.PhotoAlbum:
                            includeItemTypes.AddRange(new[] { nameof(PhotoAlbum) });
                            break;
                        case SearchItemType.Playlist:
                            includeItemTypes.AddRange(new[] { nameof(Playlist) });
                            break;
                        case SearchItemType.Series:
                            includeItemTypes.AddRange(new[] { nameof(Series) });
                            break;
                        case SearchItemType.Season:
                            includeItemTypes.AddRange(new[] { nameof(Season) });
                            break;
                        case SearchItemType.Studio:
                            includeItemTypes.AddRange(new[] { nameof(Studio) });
                            break;
                        case SearchItemType.Tag:
                            includeItemTypes.AddRange(new[] { nameof(Tag) });
                            break;
                        case SearchItemType.Trailer:
                            includeItemTypes.AddRange(new[] { nameof(Trailer) });
                            break;
                        case SearchItemType.Video:
                            includeItemTypes.AddRange(new[] { nameof(Video) });
                            break;
                    }
                }
            }

            _includeItemTypes = includeItemTypes.ToArray();
        }

        public static string[] GetSearchScope()
        {
            return _includeItemTypes;
        }

        public static string[] GetValidLibraryIds(string scope)
        {
            var libraryIds = scope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            var validLibraryIds = Array.Empty<string>();

            if (libraryIds?.Any() is true)
            {
                var parsedIds = libraryIds.Select(id => long.TryParse(id, out var result) ? result : (long?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id.Value)
                    .ToArray();

                if (parsedIds.Any())
                {
                    validLibraryIds = BaseItem.LibraryManager
                        .GetInternalItemIds(new InternalItemsQuery { ItemIds = parsedIds })
                        .Select(id => id.ToString())
                        .ToArray();
                }
            }

            return validLibraryIds;
        }
    }
}
