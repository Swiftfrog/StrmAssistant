using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using StrmAssistant.Common;
using StrmAssistant.ScheduledTask;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Common.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.MediaBrowser;
using static StrmAssistant.Reflection.MovieDb;

namespace StrmAssistant.Mod.Metadata
{
    public class EnhanceMovieDbPerson : PatchBase<EnhanceMovieDbPerson>
    {
        private static readonly ConcurrentDictionary<Season, List<PersonInfo>> SeasonPersonInfoDictionary =
            new ConcurrentDictionary<Season, List<PersonInfo>>();

        public EnhanceMovieDbPerson()
        {
            Initialize();

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().EnhanceMovieDbPerson)
            {
                Patch();
            }
        }

        protected override void OnInitialize()
        {
            if (_movieDbAssembly is null)
            {
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                PatchTracker.IsSupported = false;
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _personProviderImportData,
                prefix: nameof(PersonImportDataPrefix));
            PatchUnpatch(PatchTracker, apply, _ensurePersonInfoAsync, transpiler: nameof(EnsurePersonInfoAsyncTranspiler));
            PatchUnpatch(PatchTracker, apply, _seasonProviderImportData,
                prefix: nameof(SeasonImportDataPrefix));
            PatchUnpatch(PatchTracker, apply, _seasonGetMetadata, postfix: nameof(SeasonGetMetadataPostfix));
            PatchUnpatch(PatchTracker, apply, _addPerson, prefix: nameof(AddPersonPrefix));
        }

        private static TimeSpan GetPersonCacheTime()
        {
            if (RefreshPersonTask.IsRunning)
            {
                return TimeSpan.FromHours(48);
            }

            return MetadataApi.DefaultCacheTime;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> EnsurePersonInfoAsyncTranspiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codeMatcher = new CodeMatcher(instructions, generator);

            codeMatcher.MatchStartForward(CodeMatch.LoadsField(_cacheTime))
                .ThrowIfInvalid("Could not find call to MovieDbProviderBase.CacheTime")
                .RemoveInstruction()
                .InsertAndAdvance(CodeInstruction.Call(typeof(EnhanceMovieDbPerson), nameof(GetPersonCacheTime)));

            return codeMatcher.Instructions();
        }

        private static Tuple<string, bool> ProcessPersonInfoAsExpected(string input, string placeOfBirth)
        {
            var isJapaneseFallback = HasMovieDbJapaneseFallback();

            var considerJapanese = isJapaneseFallback && !string.IsNullOrEmpty(placeOfBirth) &&
                                   placeOfBirth.Contains("Japan", StringComparison.Ordinal);

            if (IsChinese(input)) input = ConvertTraditionalToSimplified(input);

            if (!considerJapanese ? IsChineseNoJapanese(input) : IsChineseJapanese(input))
            {
                return new Tuple<string, bool>(input, true);
            }

            return new Tuple<string, bool>(input, false);
        }

        [HarmonyPrefix]
        private static bool PersonImportDataPrefix(Person item, object info, bool isFirstLanguage)
        {
            if (!RefreshPersonTask.IsRunning) return true;

            var adult = Traverse.Create(info).Property("adult").GetValue<bool>();
            if (adult && RefreshPersonTask.NoAdult) return true;

            var nameProperty = Traverse.Create(info).Property("name");
            var name = nameProperty.GetValue<string>();
            var placeOfBirthProperty = Traverse.Create(info).Property("place_of_birth");
            var placeOfBirth = placeOfBirthProperty.GetValue<string>();

            if (!string.IsNullOrEmpty(name))
            {
                var updateNameResult = ProcessPersonInfoAsExpected(name, placeOfBirth);

                if (updateNameResult.Item2)
                {
                    if (!string.Equals(name, CleanPersonName(updateNameResult.Item1),
                            StringComparison.Ordinal))
                        nameProperty.SetValue(updateNameResult.Item1);
                }
                else
                {
                    var alsoKnownAsProperty = Traverse.Create(info).Property("also_known_as");
                    var alsoKnownAsValueType = alsoKnownAsProperty.GetValueType();
                    var alsoKnownAsList = (alsoKnownAsValueType == typeof(List<string>)
                            ? alsoKnownAsProperty.GetValue<List<string>>()
                            : alsoKnownAsProperty.GetValue<List<object>>()?.OfType<string>())
                        ?.Where(alias => !string.IsNullOrEmpty(alias))
                        .ToList();

                    if (alsoKnownAsList?.Any() == true)
                    {
                        foreach (var alias in alsoKnownAsList)
                        {
                            var updateAliasResult = ProcessPersonInfoAsExpected(alias, placeOfBirth);
                            if (updateAliasResult.Item2)
                            {
                                nameProperty.SetValue(updateAliasResult.Item1);
                                break;
                            }
                        }
                    }
                }
            }

            var biographyProperty = Traverse.Create(info).Property("biography");
            var biography =biographyProperty.GetValue<string>();

            if (!string.IsNullOrEmpty(biography))
            {
                var updateBiographyResult = ProcessPersonInfoAsExpected(biography, placeOfBirth);

                if (updateBiographyResult.Item2)
                {
                    if (!string.Equals(biography, updateBiographyResult.Item1, StringComparison.Ordinal))
                        biographyProperty.SetValue(updateBiographyResult.Item1);
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool SeasonImportDataPrefix(Season item, object seasonInfo, string name, int seasonNumber,
            bool isFirstLanguage)
        {
            if (isFirstLanguage)
            {
                var cast = Traverse.Create(seasonInfo)
                    .Property("credits")
                    .Property("cast")
                    .GetValue<IEnumerable<object>>()
                    ?.OrderBy(c => Traverse.Create(c).Property("order").GetValue<int>());

                if (cast != null)
                {
                    var personInfoList = new List<PersonInfo>();

                    foreach (var actor in cast)
                    {
                        var traverseActor = Traverse.Create(actor);
                        var id = traverseActor.Property("id").GetValue<int>();
                        var actorName = traverseActor.Property("name").GetValue<string>().Trim();
                        var character = traverseActor.Property("character").GetValue<string>().Trim();
                        var profilePath = traverseActor.Property("profile_path").GetValue<string>();

                        var personInfo = new PersonInfo { Name = actorName, Role = character, Type = PersonType.Actor };

                        if (!string.IsNullOrWhiteSpace(profilePath))
                        {
                            personInfo.ImageUrl = "https://image.tmdb.org/t/p/original" + profilePath;
                        }

                        if (id > 0)
                        {
                            personInfo.SetProviderId(MetadataProviders.Tmdb, id.ToString(CultureInfo.InvariantCulture));
                        }

                        personInfoList.Add(personInfo);
                    }

                    SeasonPersonInfoDictionary[item] = personInfoList;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void SeasonGetMetadataPostfix(RemoteMetadataFetchOptions<SeasonInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Season>> __result)
        {
            MetadataResult<Season> result = null;

            try
            {
                result = __result?.Result;
            }
            catch
            {
                // ignored
            }

            if (result?.Item != null && SeasonPersonInfoDictionary.TryGetValue(result.Item, out var personInfoList))
            {
                foreach (var personInfo in personInfoList)
                {
                    result.AddPerson(personInfo);
                }

                SeasonPersonInfoDictionary.TryRemove(result.Item, out _);
            }
        }

        [HarmonyPrefix]
        private static bool AddPersonPrefix(List<PersonInfo> people, PersonInfo person)
        {
            if (string.IsNullOrWhiteSpace(person.Name))
            {
                return false;
            }

            return true;
        }
    }
}
