using Avalonia.Controls;
using Avalonia.Interactivity;
using Sonoric.ViewModels;

namespace Sonoric.Views;

public partial class ArtistEditorWindow : Window
{
    public ArtistEditorWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArtistEditorViewModel viewModel && viewModel.TryCommit(out _))
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
