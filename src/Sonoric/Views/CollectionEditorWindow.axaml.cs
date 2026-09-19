using Avalonia.Controls;
using Avalonia.Interactivity;
using Sonoric.ViewModels;

namespace Sonoric.Views;

public partial class CollectionEditorWindow : Window
{
    public CollectionEditorWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CollectionEditorViewModel viewModel && viewModel.TryCommit(out _))
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
