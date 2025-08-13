using HarmonyLib;
using MediaBrowser.Controller.Entities;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.NfoMetadata;

namespace StrmAssistant.Mod.Metadata
{
    public class EnhanceNfoMetadata : PatchBase<EnhanceNfoMetadata>
    {
        private static readonly AsyncLocal<string> PersonContent = new AsyncLocal<string>();

        private static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
        {
            ValidationType = ValidationType.None,
            Async = true,
            CheckCharacters = false,
            IgnoreProcessingInstructions = true,
            IgnoreComments = true
        };

        private static readonly XmlWriterSettings WriterSettings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            CheckCharacters = false
        };

        private static readonly object _lock = new object();

        public EnhanceNfoMetadata()
        {
            Initialize();

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().EnhanceNfoMetadata)
            {
                Patch();
            }
        }

        protected override void OnInitialize()
        {
            if (!IsSupported)
            {
                PatchTracker.FallbackPatchApproach = PatchApproach.None;
                PatchTracker.IsSupported = false;
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _genericBaseNfoParserConstructor,
                prefix: nameof(GenericBaseNfoParserConstructorPrefix));

            if (!apply)
            {
                PatchUnpatch(PatchTracker, false, _getPersonFromXmlNode, prefix: nameof(GetPersonFromXmlNodePrefix),
                    postfix: nameof(GetPersonFromXmlNodePostfix));
            }
        }

        [HarmonyPrefix]
        private static void GenericBaseNfoParserConstructorPrefix()
        {
            lock (_lock)
            {
                PatchUnpatch(Instance.PatchTracker, false, _getPersonFromXmlNode,
                    prefix: nameof(GetPersonFromXmlNodePrefix), postfix: nameof(GetPersonFromXmlNodePostfix),
                    suppress: true);
                PatchUnpatch(Instance.PatchTracker, true, _getPersonFromXmlNode,
                    prefix: nameof(GetPersonFromXmlNodePrefix), postfix: nameof(GetPersonFromXmlNodePostfix),
                    suppress: true);
            }
        }

        [HarmonyPrefix]
        private static bool GetPersonFromXmlNodePrefix(ref XmlReader reader)
        {
            try
            {
                var sb = new StringBuilder();

                using (var writer = new StringWriter(sb))
                {
                    using (var xmlWriter = XmlWriter.Create(writer, WriterSettings))
                    {
                        while (reader.Read())
                        {
                            xmlWriter.WriteNode(reader, true);

                            if (reader.NodeType == XmlNodeType.EndElement)
                                break;
                        }
                    }
                }

                PersonContent.Value = sb.ToString();

                reader = XmlReader.Create(new StringReader(sb.ToString()), ReaderSettings);
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug(e.Message);
                    Plugin.Instance.Logger.Debug(e.StackTrace);
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void GetPersonFromXmlNodePostfix(XmlReader reader, Task<PersonInfo> __result)
        {
            Task.Run(async () => await SetImageUrlAsync(__result)).ConfigureAwait(false);
        }

        private static async Task SetImageUrlAsync(Task<PersonInfo> personInfoTask)
        {
            try
            {
                var personInfo = await personInfoTask;

                var personContent = PersonContent.Value;
                PersonContent.Value = null;

                if (personContent != null)
                {
                    using var reader = XmlReader.Create(new StringReader(personContent), ReaderSettings);

                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        if (reader.IsStartElement("thumb"))
                        {
                            var thumb = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);

                            if (IsValidHttpUrl(thumb))
                            {
                                personInfo.ImageUrl = thumb;
                                //Plugin.Instance.logger.Debug("EnhanceNfoMetadata - Imported " + personInfo.Name +
                                //                             " " + personInfo.ImageUrl);
                            }

                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    Plugin.Instance.Logger.Debug(e.Message);
                    Plugin.Instance.Logger.Debug(e.StackTrace);
                }
            }
        }
    }
}
