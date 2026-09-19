using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Sonoric.Services;
using Sonoric.ViewModels;
using Sonoric.Views;

namespace Sonoric;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths.EnsureCreated();
            var library = new LibraryService();
            var playback = new PlaybackService();
            var window = new MainWindow();
            var dialogs = new FileDialogService(window);
            var collections = new CollectionDialogService(window);
            var viewModel = new MainViewModel(library, playback, dialogs, collections);
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                viewModel.PersistState();
                playback.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
