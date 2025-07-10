using Emby.Media.Common.Extensions;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using StrmAssistant.Properties;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class ExperienceEnhancePageView : PluginPageView
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly ExperienceEnhanceOptionsStore _store;

        private static Task _splitMovieTask;

        public ExperienceEnhancePageView(PluginInfo pluginInfo, ILibraryManager libraryManager,
            ExperienceEnhanceOptionsStore store) : base(pluginInfo.Id)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _store = store;
            ContentData = store.GetOptions();
            ExperienceEnhanceOptions.UIFunctionOptions.Initialize();

            if (_splitMovieTask is null || _splitMovieTask.IsCompleted)
            {
                ExperienceEnhanceOptions.SplitMoviesButton.IsEnabled = true;
                ExperienceEnhanceOptions.SplitMoviesProgress.Clear();
            }
        }

        public ExperienceEnhanceOptions ExperienceEnhanceOptions => ContentData as ExperienceEnhanceOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            _store.SetOptions(ExperienceEnhanceOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            switch (commandId)
            {
                case "SplitMovies":
                    if (_splitMovieTask is null || _splitMovieTask.IsCompleted)
                    {
                        _splitMovieTask = Task.Run(HandleSplitMovieButton);
                        _splitMovieTask.FireAndForget(_logger);
                    }

                    return Task.FromResult<IPluginUIView>(this);
            }

            return base.RunCommand(itemId, commandId, data);
        }

        private async Task HandleSplitMovieButton()
        {
            ExperienceEnhanceOptions.SplitMoviesButton.IsEnabled = false;
            ExperienceEnhanceOptions.SplitMoviesProgress.Clear();
            var progressItem = new GenericListItem
            {
                Icon = IconNames.work_outline,
                IconMode = ItemListIconMode.SmallRegular,
                Status = ItemStatus.InProgress,
                HasPercentage = true
            };
            ExperienceEnhanceOptions.SplitMoviesProgress.Add(progressItem);
            RaiseUIViewInfoChanged();
            await Task.Delay(10.ms());

            var movies = Plugin.LibraryApi.FetchSplitMovieItems();
            progressItem.PercentComplete = 20;
            RaiseUIViewInfoChanged();
            await Task.Delay(10.ms());

            var total = movies.Count;
            var current = 0;

            foreach (var item in movies)
            {
                _libraryManager.SplitItems(item);
                current++;
                progressItem.PercentComplete = 20 + current * 80 / total;
                _logger.Info("MergeMovie - Split group " + current + "/" + total + " - " + item.Path);

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

            ExperienceEnhanceOptions.SplitMoviesButton.IsEnabled = true;
            progressItem.HasPercentage = false;
            progressItem.SecondaryText = Resources.Operation_Success;
            progressItem.Icon = IconNames.info;
            progressItem.Status = ItemStatus.Succeeded;
            RaiseUIViewInfoChanged();
            await Task.Delay(2000);
            ExperienceEnhanceOptions.SplitMoviesProgress.Clear();
            RaiseUIViewInfoChanged();
        }
    }
}
