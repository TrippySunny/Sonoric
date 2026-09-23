using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sonoric.Views;

public partial class StatisticsWindow : Window
{
    public StatisticsWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
