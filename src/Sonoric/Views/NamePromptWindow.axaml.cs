using Avalonia.Controls;
using Avalonia.Interactivity;
using Sonoric.ViewModels;

namespace Sonoric.Views;

public partial class NamePromptWindow : Window
{
    public NamePromptWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NamePromptViewModel viewModel && viewModel.TryAccept())
        {
            Close(true);
        }
    }
}
