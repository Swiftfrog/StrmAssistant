using Emby.Sqlite;
using HarmonyLib;
using MediaBrowser.Controller.Entities;
using SQLitePCL.pretty;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.Utility;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod
{
    public class EnhanceChineseSearch : PatchBase<EnhanceChineseSearch>
    {
        private static MethodInfo _createConnection;

        public static string CurrentTokenizerName { get; private set; } = "unknown";

        private static string _tokenizerPath;
        private static readonly object _lock = new object();
        private static bool _patchPhase2Initialized;
        private static readonly Dictionary<string, Regex> patterns = new Dictionary<string, Regex>
        {
            { "imdb", new Regex(@"^tt\d{7,8}$", RegexOptions.IgnoreCase | RegexOptions.Compiled) },
            { "tmdb", new Regex(@"^tmdb(id)?=(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled) },
            { "tvdb", new Regex(@"^tvdb(id)?=(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled) }
        };

        public EnhanceChineseSearch()
        {
            _tokenizerPath = Path.Combine(Plugin.Instance.ApplicationPaths.PluginsPath, "libsimple.so");

            Initialize();

            if (Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.EnhanceChineseSearch ||
                Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.EnhanceChineseSearchRestore)
            {
                if (AppVer >= Ver4830)
                {
                    PatchPhase1();
                }
                else
                {
                    ResetOptions();
                }
            }
        }

        protected override void OnInitialize()
        {
            var raw = AccessTools.TypeByName("SQLitePCLEx.raw");
            var enableLoadExtension = AccessTools.Method(raw, "sqlite3_enable_load_extension");
            ReversePatch(PatchTracker, enableLoadExtension, nameof(EnableLoadExtensionStub));
            _createConnection = AccessTools.Method(typeof(BaseSqliteRepository), "CreateConnection");
        }

        protected override void Prepare(bool apply)
        {
            // No action needed
        }

        private static void PatchPhase1()
        {
            if (EnsureTokenizerExists() && PatchUnpatch(Instance.PatchTracker, true, _createConnection,
                    postfix: nameof(CreateConnectionPostfix))) return;

            if (Plugin.Instance.DebugMode)
            {
                Plugin.Instance.Logger.Debug("EnhanceChineseSearch - PatchPhase1 Failed");
            }
            
            ResetOptions();
        }

        private static void PatchPhase2(IDatabaseConnection connection)
        {
            string ftsTableName;

            if (AppVer >= Ver4830)
            {
                ftsTableName = "fts_search9";
            }
            else
            {
                ftsTableName = "fts_search8";
            }

            var tokenizerCheckQuery = $@"
                SELECT 
                    CASE 
                        WHEN instr(sql, 'tokenize=""simple""') > 0 THEN 'simple'
                        WHEN instr(sql, 'tokenize=""unicode61 remove_diacritics 2""') > 0 THEN 'unicode61 remove_diacritics 2'
                        ELSE 'unknown'
                    END AS tokenizer_name
                FROM 
                    sqlite_master 
                WHERE 
                    type = 'table' AND 
                    name = '{ftsTableName}';";

            var rebuildFtsResult = true;
            var patchSearchFunctionsResult = false;

            try
            {
                using (var statement = connection.PrepareStatement(tokenizerCheckQuery))
                {
                    try
                    {
                        if (statement.MoveNext())
                        {
                            CurrentTokenizerName = statement.Current?.GetString(0) ?? "unknown";
                        }
                    }
                    finally
                    {
                        statement.Reset();
                    }
                }

                Plugin.Instance.Logger.Info("EnhanceChineseSearch - Current tokenizer (before) is " + CurrentTokenizerName);

                if (!string.Equals(CurrentTokenizerName, "unknown", StringComparison.Ordinal))
                {
                    if (Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.EnhanceChineseSearchRestore)
                    {
                        if (string.Equals(CurrentTokenizerName, "simple", StringComparison.Ordinal))
                        {
                            rebuildFtsResult = RebuildFts(connection, ftsTableName, "unicode61 remove_diacritics 2");
                        }
                        if (rebuildFtsResult)
                        {
                            CurrentTokenizerName = "unicode61 remove_diacritics 2";
                            Plugin.Instance.Logger.Info("EnhanceChineseSearch - Restore Success");
                        }
                        ResetOptions();
                    }
                    else if (Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.EnhanceChineseSearch)
                    {
                        patchSearchFunctionsResult = PatchSearchFunctions();

                        if (patchSearchFunctionsResult)
                        {
                            if (string.Equals(CurrentTokenizerName, "unicode61 remove_diacritics 2", StringComparison.Ordinal))
                            {
                                rebuildFtsResult = RebuildFts(connection, ftsTableName, "simple");
                            }

                            if (rebuildFtsResult)
                            {
                                CurrentTokenizerName = "simple";
                                Plugin.Instance.Logger.Info("EnhanceChineseSearch - Load Success");
                            }
                        }
                    }
                }

                Plugin.Instance.Logger.Info("EnhanceChineseSearch - Current tokenizer (after) is " + CurrentTokenizerName);
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug("EnhanceChineseSearch - PatchPhase2 Failed");
                    Plugin.Instance.Logger.Debug(e.Message);
                    Plugin.Instance.Logger.Debug(e.StackTrace);
                }
            }

            if (!patchSearchFunctionsResult || !rebuildFtsResult ||
                string.Equals(CurrentTokenizerName, "unknown", StringComparison.Ordinal))
            {
                ResetOptions();
            }
        }

        private static bool RebuildFts(IDatabaseConnection connection, string ftsTableName, string tokenizerName)
        {
            string populateQuery;

            if (AppVer < Ver4900)
            {
                populateQuery =
                    $"insert into {ftsTableName}(RowId, Name, OriginalTitle, SeriesName, Album) select id, " +
                    GetSearchColumnNormalization("Name") + ", " +
                    GetSearchColumnNormalization("OriginalTitle") + ", " +
                    GetSearchColumnNormalization("SeriesName") + ", " +
                    GetSearchColumnNormalization("Album") +
                    " from MediaItems";
            }
            else
            {
                populateQuery =
                    $"insert into {ftsTableName}(RowId, Name, OriginalTitle, SeriesName, Album) select id, " +
                    GetSearchColumnNormalization("Name") + ", " +
                    GetSearchColumnNormalization("OriginalTitle") + ", " +
                    GetSearchColumnNormalization("SeriesName") + ", " +
                    GetSearchColumnNormalization(
                        "(select case when AlbumId is null then null else (select name from MediaItems where Id = AlbumId limit 1) end)") +
                    " from MediaItems";
            }

            connection.BeginTransaction(TransactionMode.Immediate);
            try
            {
                var dropFtsTableQuery = $"DROP TABLE IF EXISTS {ftsTableName}";
                connection.Execute(dropFtsTableQuery);

                var createFtsTableQuery =
                    $"CREATE VIRTUAL TABLE IF NOT EXISTS {ftsTableName} USING FTS5 (Name, OriginalTitle, SeriesName, Album, tokenize=\"{tokenizerName}\", prefix='1 2 3 4')";
                connection.Execute(createFtsTableQuery);

                Plugin.Instance.Logger.Info($"EnhanceChineseSearch - Filling {ftsTableName} Start");

                connection.Execute(populateQuery);
                connection.CommitTransaction();

                Plugin.Instance.Logger.Info($"EnhanceChineseSearch - Filling {ftsTableName} Complete");

                return true;
            }
            catch (Exception e)
            {
                connection.RollbackTransaction();

                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug("EnhanceChineseSearch - RebuildFts Failed");
                    Plugin.Instance.Logger.Debug(e.Message);
                    Plugin.Instance.Logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        private static string GetSearchColumnNormalization(string columnName)
        {
            return "replace(replace(" + columnName + ",'''',''),'.','')";
        }

        private static bool EnsureTokenizerExists()
        {
            var resourceName = GetTokenizerResourceName();
            var expectedSha1 = GetExpectedSha1();

            if (resourceName == null || expectedSha1 == null) return false;

            try
            {
                if (File.Exists(_tokenizerPath))
                {
                    var existingSha1 = ComputeSha1(_tokenizerPath);

                    if (expectedSha1.ContainsValue(existingSha1))
                    {
                        var highestVersion = expectedSha1.Keys.Max();
                        var highestSha1 = expectedSha1[highestVersion];

                        if (existingSha1 == highestSha1)
                        {
                            Plugin.Instance.Logger.Info(
                                $"EnhanceChineseSearch - Tokenizer exists with matching SHA-1 for the highest version {highestVersion}");
                        }
                        else
                        {
                            var currentVersion = expectedSha1.FirstOrDefault(x => x.Value == existingSha1).Key;
                            Plugin.Instance.Logger.Info(
                                $"EnhanceChineseSearch - Tokenizer exists for version {currentVersion} but does not match the highest version {highestVersion}. Upgrading...");
                            ExportTokenizer(resourceName);
                        }

                        return true;
                    }

                    Plugin.Instance.Logger.Info(
                        "EnhanceChineseSearch - Tokenizer exists but SHA-1 is not recognized. No action taken.");

                    return true;
                }

                Plugin.Instance.Logger.Info("EnhanceChineseSearch - Tokenizer does not exist. Exporting...");
                ExportTokenizer(resourceName);

                return true;
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug("EnhanceChineseSearch - EnsureTokenizerExists Failed");
                    Plugin.Instance.Logger.Debug(e.Message);
                    Plugin.Instance.Logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        private static void ExportTokenizer(string resourceName)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var fileStream = new FileStream(_tokenizerPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }

            Plugin.Instance.Logger.Info($"EnhanceChineseSearch - Exported {resourceName} to {_tokenizerPath}");
        }

        private static string GetTokenizerResourceName()
        {
            var tokenizerNamespace = Assembly.GetExecutingAssembly().GetName().Name + ".Tokenizer";
            var winSimpleTokenizer = $"{tokenizerNamespace}.win.libsimple.so";
            var linuxSimpleTokenizer = $"{tokenizerNamespace}.linux.libsimple.so";

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT when Environment.Is64BitOperatingSystem:
                    return winSimpleTokenizer;
                case PlatformID.Unix when Environment.Is64BitOperatingSystem:
                    return linuxSimpleTokenizer;
                default:
                    return null;
            }
        }

        private static Dictionary<Version, string> GetExpectedSha1()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return new Dictionary<Version, string>
                    {
                        { new Version(0, 4, 0), "a83d90af9fb88e75a1ddf2436c8b67954c761c83" },
                        { new Version(0, 5, 0), "aed57350b46b51bb7d04321b7fe8e5e60b0cdbdc" }
                    };
                case PlatformID.Unix:
                    return new Dictionary<Version, string>
                    {
                        { new Version(0, 4, 0), "f7fb8ba0b98e358dfaa87570dc3426ee7f00e1b6" },
                        { new Version(0, 5, 0), "8e36162f96c67d77c44b36093f31ae4d297b15c0" }
                    };
                default:
                    return null;
            }
        }

        private static string ComputeSha1(string filePath)
        {
            using (var sha1 = SHA1.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha1.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void ResetOptions()
        {
            Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.EnhanceChineseSearch = false;
            Plugin.Instance.MainOptionsStore.SavePluginOptionsSuppress();
        }

        private static bool PatchSearchFunctions()
        {
            return PatchUnpatch(Instance.PatchTracker, true, _enableJoinFtsSearch,
                       prefix: nameof(EnableJoinFtsSearchPrefix)) &&
                   PatchUnpatch(Instance.PatchTracker, true, _getJoinCommandText,
                       postfix: nameof(GetJoinCommandTextPostfix)) &&
                   PatchUnpatch(Instance.PatchTracker, true, _createSearchTerm,
                       prefix: nameof(CreateSearchTermPrefix)) &&
                   PatchUnpatch(Instance.PatchTracker, true,
                       _cacheIdsFromTextParams, prefix: nameof(CacheIdsFromTextParamsPrefix));
        }

        [HarmonyReversePatch]
        private static int EnableLoadExtensionStub(object db, int onoff) => throw new NotImplementedException();

        private static bool LoadTokenizerExtension(IDatabaseConnection connection)
        {
            try
            {
                var db = Traverse.Create(connection).Field("db").GetValue();
                EnableLoadExtensionStub(db, 1);
                connection.Execute("SELECT load_extension('" + _tokenizerPath + "')");

                return true;
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Warn("EnhanceChineseSearch - Load tokenizer failed.");
                    Plugin.Instance.Logger.Debug(e.Message);
                    Plugin.Instance.Logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        [HarmonyPostfix]
        private static void CreateConnectionPostfix(BaseSqliteRepository __instance, bool isReadOnly,
            IDatabaseConnection __result)
        {
            if (!isReadOnly && !_patchPhase2Initialized)
            {
                lock (_lock)
                {
                    if (!_patchPhase2Initialized)
                    {
                        var db = Traverse.Create(__instance).Property("DbFilePath").GetValue<string>();
                        if (db?.EndsWith("library.db", StringComparison.OrdinalIgnoreCase) != true)
                        {
                            return;
                        }

                        var tokenizerLoaded = LoadTokenizerExtension(__result);

                        if (tokenizerLoaded)
                        {
                            _patchPhase2Initialized = true;
                            PatchPhase2(__result);
                        }
                    }
                }
            }
        }

        [HarmonyPrefix]
        private static bool EnableJoinFtsSearchPrefix(InternalItemsQuery query, ref bool __result)
        {
            __result = !string.IsNullOrEmpty(query.SearchTerm);

            return false;
        }

        [HarmonyPostfix]
        private static void GetJoinCommandTextPostfix(InternalItemsQuery query,
            List<KeyValuePair<string, string>> bindParams, string mediaItemsTableQualifier, ref string __result)
        {
            if (!string.IsNullOrEmpty(query.SearchTerm) && __result.Contains("match @SearchTerm"))
            {
                if (!Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.ExcludeOriginalTitleFromSearch)
                {
                    __result = __result.Replace("match @SearchTerm", "match simple_query(@SearchTerm)");
                }
                else
                {
                    __result = __result.Replace("match @SearchTerm", "match '-OriginalTitle:' || simple_query(@SearchTerm)");
                }
            }
        }

        [HarmonyPrefix]
        private static bool CreateSearchTermPrefix(string searchTerm, ref string __result)
        {
            var parts = searchTerm.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var normalized = string.Concat(parts.Select((part, index) =>
                index < parts.Length - 1 && part.Length > 1 ? part + " " : part));

            var terms = normalized.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(i => i.Replace("\"", string.Empty).Replace("'", string.Empty));

            __result = string.Join(" ", terms);

            return false;
        }

        [HarmonyPrefix]
        private static void CacheIdsFromTextParamsPrefix(InternalItemsQuery query, IDatabaseConnection db)
        {
            var nameStartsWith = query.NameStartsWith;
            if (!string.IsNullOrEmpty(nameStartsWith))
            {
                query.SearchTerm = nameStartsWith;
                query.NameStartsWith = null;
            }

            var searchTerm = query.SearchTerm;
            if (query.IncludeItemTypes.Length == 0 && !string.IsNullOrEmpty(searchTerm))
            {
                query.IncludeItemTypes = GetSearchScope();
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                foreach (var provider in patterns)
                {
                    var match = provider.Value.Match(searchTerm.Trim());
                    if (match.Success)
                    {
                        var idValue = provider.Key == "imdb" ? match.Value : match.Groups[2].Value;

                        query.HasAnyProviderId = new[] { provider.Key };
                        query.AnyProviderIdEquals = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>(provider.Key, idValue)
                        };
                        query.SearchTerm = null;
                        break;
                    }
                }
            }

            if (AppVer >= Ver49037 && !string.IsNullOrEmpty(query.SearchTerm))
            {
                var result = LoadTokenizerExtension(db);
            }
        }
    }
}
