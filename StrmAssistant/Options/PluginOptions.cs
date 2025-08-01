using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Mod;
using StrmAssistant.Properties;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => Resources.PluginOptions_EditorTitle_Strm_Assistant;

        public override string EditorDescription => string.Empty;

        public ButtonItem DisclaimerButton { get; set; } = new ButtonItem
        {
            Icon = IconNames.privacy_tip, Data1 = "DisclaimerDialog"
        };

        [VisibleCondition(nameof(IsModSuccess), SimpleCondition.IsFalse)]
        public StatusItem ModStatus { get; set; } = new StatusItem();

        [DisplayNameL("GeneralOptions_EditorTitle_General_Options", typeof(Resources))]
        public GeneralOptions GeneralOptions { get; set; } = new GeneralOptions();

        [DisplayNameL("PluginOptions_ModOptions_Mod_Features", typeof(Resources))]
        public ModOptions ModOptions { get; set; } = new ModOptions();

        [DisplayNameL("NetworkOptions_EditorTitle_Network", typeof(Resources))]
        public NetworkOptions NetworkOptions { get; set; } = new NetworkOptions();

        [DisplayNameL("AboutOptions_EditorTitle_About", typeof(Resources))]
        public AboutOptions AboutOptions { get; set; } = new AboutOptions();

        [Browsable(false)]
        public bool IsModSuccess => PatchManager.IsModSuccess();

        [Browsable(false)]
        public void Initialize()
        {
            DisclaimerButton.Caption = Resources.DisclaimerButtonText;

            if (IsModSuccess is false)
            {
                ModStatus.Caption = Resources.PluginOptions_IsHarmonyModFailed_Harmony_Mod_Failed;
                ModStatus.StatusText = string.Empty;
                ModStatus.Status = ItemStatus.Warning;
            }
            else
            {
                ModStatus.Caption = string.Empty;
                ModStatus.StatusText = string.Empty;
                ModStatus.Status = ItemStatus.None;
            }
        }
    }
}
