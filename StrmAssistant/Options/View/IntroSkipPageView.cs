using Emby.Media.Common.Extensions;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class IntroSkipPageView : PluginPageView
    {
        private readonly ILogger _logger;
        private readonly IntroSkipOptionsStore _store;

        private static Task _clearIntroTask;

        public IntroSkipPageView(PluginInfo pluginInfo, ILibraryManager libraryManager,
            IntroSkipOptionsStore store)
            : base(pluginInfo.Id)
        {
            _logger = Plugin.Instance.Logger;
            _store = store;
            ContentData = store.GetOptions();
            IntroSkipOptions.Initialize(libraryManager);

            if (_clearIntroTask is null || _clearIntroTask.IsCompleted)
            {
                IntroSkipOptions.ClearIntroButton.IsEnabled = true;
                IntroSkipOptions.ClearIntroResult.Clear();
            }
        }

        public IntroSkipOptions IntroSkipOptions => ContentData as IntroSkipOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            if (ContentData is IntroSkipOptions options)
            {
                options.ValidateOrThrow();
            }

            _store.SetOptions(IntroSkipOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            switch (commandId)
            {
                case "ClearIntroCreditsMarkers":
                    if (_clearIntroTask is null || _clearIntroTask.IsCompleted)
                    {
                        _clearIntroTask = Task.Run(HandleClearIntroButton);
                        _clearIntroTask.FireAndForget(_logger);
                    }

                    return Task.FromResult<IPluginUIView>(this);
            }

            return base.RunCommand(itemId, commandId, data);
        }

        private async Task HandleClearIntroButton()
        {
            IntroSkipOptions.ClearIntroButton.IsEnabled = false;
            IntroSkipOptions.ClearIntroResult.Clear();
            var progressItem = new GenericListItem
            {
                Icon = IconNames.work_outline,
                IconMode = ItemListIconMode.SmallRegular,
                Status = ItemStatus.InProgress,
                HasPercentage = true
            };
            IntroSkipOptions.ClearIntroResult.Add(progressItem);
            RaiseUIViewInfoChanged();
            await Task.Delay(10.ms());

            var episodes = Plugin.ChapterApi.FetchClearTaskItems(new List<BaseItem>());

            progressItem.PercentComplete = 20;
            RaiseUIViewInfoChanged();
            await Task.Delay(10.ms());

            var total = episodes.Count;
            var current = 0;

            foreach (var item in episodes)
            {
                Plugin.ChapterApi.RemoveIntroCreditsMarkers(item);
                current++;
                progressItem.PercentComplete = 20 + current * 80 / total;
                _logger.Info("IntroSkipClear - Task " + current + "/" + total + " - " + item.Path);

                if (current % 10 == 0)
                {
                    RaiseUIViewInfoChanged();
                    await Task.Delay(10.ms());
                }
            }

            if (current % 10 != 0)
            {
                RaiseUIViewInfoChanged();
                await Task.Delay(10.ms());
            }

            IntroSkipOptions.ClearIntroButton.IsEnabled = true;
            progressItem.HasPercentage = false;
            progressItem.SecondaryText = Resources.Operation_Success;
            progressItem.Icon = IconNames.info;
            progressItem.Status = ItemStatus.Succeeded;
            RaiseUIViewInfoChanged();
            await Task.Delay(2000);
            IntroSkipOptions.ClearIntroResult.Clear();
            RaiseUIViewInfoChanged();
        }
    }
}
