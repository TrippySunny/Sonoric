using Avalonia.Controls;
using Avalonia.Interactivity;
using Sonoric.ViewModels;

namespace Sonoric.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CommitProfile();
        }
    }
}
