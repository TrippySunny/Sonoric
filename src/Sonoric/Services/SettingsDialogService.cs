using Avalonia.Controls;
using Sonoric.ViewModels;
using Sonoric.Views;

namespace Sonoric.Services;

public interface ISettingsDialogService
{
    Task ShowAsync(SettingsViewModel viewModel);
    Task ShowStatisticsAsync(StatisticsViewModel viewModel);
    Task<bool> PromptDisplayNameAsync(NamePromptViewModel viewModel);
}

public sealed class SettingsDialogService : ISettingsDialogService
{
    private readonly Window _owner;

    public SettingsDialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task ShowAsync(SettingsViewModel viewModel)
    {
        var window = new SettingsWindow
        {
            DataContext = viewModel
        };

        await window.ShowDialog(_owner);
    }

    public async Task ShowStatisticsAsync(StatisticsViewModel viewModel)
    {
        var window = new StatisticsWindow
        {
            DataContext = viewModel
        };

        await window.ShowDialog(_owner);
    }

    public async Task<bool> PromptDisplayNameAsync(NamePromptViewModel viewModel)
    {
        var window = new NamePromptWindow
        {
            DataContext = viewModel
        };

        return await window.ShowDialog<bool>(_owner);
    }
}
