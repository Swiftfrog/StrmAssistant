using Emby.Media.Model.ProbeModel;
using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using StrmAssistant.Common;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyProviders;
using static StrmAssistant.Reflection.EmbyServerMediaEncoding;

namespace StrmAssistant.Mod.MediaInfo
{
    public class ExtractMediaInfoHelper : PatchBase<ExtractMediaInfoHelper>
    {
        private static readonly AsyncLocal<bool> ShouldCleanEmbeddedMetadata = new AsyncLocal<bool>();

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, true, _runFfProcess, prefix: nameof(RunFfProcessPrefix),
                finalizer: nameof(RunFfProcessFinalizer));
            PatchUnpatch(PatchTracker, true, _getInputArgument, prefix: nameof(GetInputArgumentPrefix));
            PatchUnpatch(PatchTracker, true, _isProbingAllowed, prefix: nameof(IsProbingAllowedPrefix));
            PatchUnpatch(PatchTracker, apply, _getAnalyzeDurationArgument,
                prefix: nameof(GetAnalyzeDurationArgumentPrefix));
            PatchUnpatch(PatchTracker, apply, _getProbeSizeArgument, prefix: nameof(GetProbeSizeArgumentPrefix));
            PatchUnpatch(PatchTracker, true, _getMediaInfo, postfix: nameof(GetMediaInfoPostfix));
        }
        
        [HarmonyPrefix]
        private static void RunFfProcessPrefix(ref int timeoutMs)
        {
            if (ExclusiveExtract.ExclusiveItemValue == 0L) return;

            var baseTimeoutMs = 60000;
            var concurrency = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            timeoutMs = Math.Min(baseTimeoutMs + (concurrency - 1) * 5000, 150000);
        }
        
        [HarmonyFinalizer]
        private static void RunFfProcessFinalizer(Task __result, Exception __exception)
        {
            if (__result.IsCanceled || __result.IsFaulted) return;

            if (ExclusiveExtract.ExclusiveItemValue == 0L) return;

            var result = Traverse.Create(__result).Property("Result").GetValue();

            if (result != null)
            {
                var traverseResult = Traverse.Create(result);
                var standardOutput = traverseResult.Property("StandardOutput").GetValue().ToString();
                var standardError = traverseResult.Property("StandardError").GetValue().ToString();

                if (standardOutput != null && standardError != null)
                {
                    var partialOutput = standardOutput.Length > 20
                        ? standardOutput.Substring(0, 20)
                        : standardOutput;

                    if (Regex.Replace(partialOutput, @"\s+", "") == "{}")
                    {
                        var lines = standardError.Split(new[] { '\r', '\n' },
                            StringSplitOptions.RemoveEmptyEntries);

                        if (lines.Length > 0)
                        {
                            var errorMessage = lines[lines.Length - 1].Trim();

                            Plugin.Instance.Logger.Error("MediaInfoExtract - FfProbe Error: " + errorMessage);
                        }
                    }
                }
            }
        }

        [HarmonyPrefix]
        private static bool GetInputArgumentPrefix(ref string input, ref MediaProtocol protocol, ref string __result)
        {
            if (LibraryApi.IsFileShortcut(input))
            {
                var inputPath = input;
                var mountPath = Task.Run(async () => await Plugin.LibraryApi.GetStrmMountPath(inputPath)).Result;
                if (!string.IsNullOrEmpty(mountPath))
                {
                    input = mountPath;

                    if (mountPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        protocol = MediaProtocol.Http;
                    }
                }
            }

            if (protocol == MediaProtocol.Http)
            {
                __result = string.Format(CultureInfo.InvariantCulture, "\"{0}\"", input);
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool GetAnalyzeDurationArgumentPrefix(string inputPath, MediaProtocol protocol,
            bool isInfiniteStream, ref string __result)
        {
            if (!isInfiniteStream && protocol == MediaProtocol.Http)
            {
                __result = "";
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool GetProbeSizeArgumentPrefix(string inputPath, bool isInfiniteStream, ref string __result)
        {
            if (!isInfiniteStream && !string.IsNullOrEmpty(inputPath) &&
                inputPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                __result = "";
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static void IsProbingAllowedPrefix(BaseItem item, MetadataRefreshOptions options)
        {
            if (item is Movie || item is Episode)
            {
                ShouldCleanEmbeddedMetadata.Value = true;
            }
        }

        [HarmonyPostfix]
        private static void GetMediaInfoPostfix(ProbeResult data, bool isAudio, string path, MediaProtocol protocol,
            MediaBrowser.Model.MediaInfo.MediaInfo __result)
        {
            if (isAudio) return;

            if (!ShouldCleanEmbeddedMetadata.Value) return;

            __result.Name = null;
            __result.Overview = null;
            __result.PremiereDate = null;
            __result.ProductionYear = null;
            __result.Studios = Array.Empty<string>();
            __result.Tags = Array.Empty<string>();
            __result.Album = null;
            __result.AlbumArtists = Array.Empty<string>();
            __result.AlbumTags = Array.Empty<string>();
            __result.Artists = Array.Empty<string>();
            __result.Genres = Array.Empty<string>();
            __result.MediaStreams = __result.MediaStreams.Where(ms => ms.Type != MediaStreamType.EmbeddedImage).ToList();
        }
    }
}
