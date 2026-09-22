using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Sonoric.ViewModels;

namespace Sonoric.Views;

public partial class MainWindow : Window
{
    private Point? _coverDragPoint;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        CollectionPane.AddHandler(DragDrop.DropEvent, OnCollectionDrop);
        CollectionPane.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsCoverViewerOpen && e.Key == Key.Escape)
        {
            viewModel.CloseCoverViewerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space)
        {
            return;
        }

        viewModel.TogglePlayPauseCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCoverViewerBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Image or Button)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CloseCoverViewerCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCoverViewerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        var newZoom = Math.Clamp(viewModel.CoverViewerZoom * factor, 1, 12);
        viewModel.CoverViewerZoom = newZoom;
        if (newZoom <= 1)
        {
            viewModel.CoverViewerOffsetX = 0;
            viewModel.CoverViewerOffsetY = 0;
        }

        e.Handled = true;
    }

    private void OnCoverViewerImagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.CoverViewerZoom <= 1)
        {
            e.Handled = true;
            return;
        }

        _coverDragPoint = e.GetPosition(this);
        if (sender is IInputElement element)
        {
            e.Pointer.Capture(element);
        }

        e.Handled = true;
    }

    private void OnCoverViewerImageMoved(object? sender, PointerEventArgs e)
    {
        if (_coverDragPoint is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _coverDragPoint.Value;
        _coverDragPoint = position;
        viewModel.CoverViewerOffsetX += delta.X;
        viewModel.CoverViewerOffsetY += delta.Y;
    }

    private void OnCoverViewerImageReleased(object? sender, PointerReleasedEventArgs e)
    {
        _coverDragPoint = null;
        e.Pointer.Capture(null);
    }

    private void OnCoverViewerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _coverDragPoint = null;
    }

    private void OnTrackListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PlaySelectedTrack();
        }
    }

    private void OnCollectionTrackListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PlaySelectedCollectionTrack();
        }
    }

    private void OnTimelinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.BeginSeek();
        }
    }

    private void OnTimelineReleased(object? sender, PointerReleasedEventArgs e)
    {
        CommitTimeline();
    }

    private void OnTimelineCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CommitTimeline();
    }

    private void OnTimelineValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PreviewSeek(e.NewValue);
        }
    }

    private void CommitTimeline()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CommitSeek(TimelineSlider.Value);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await ImportDroppedPathsAsync(viewModel.ImportPathsAsync, e);
    }

    private async void OnCollectionDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.OpenedCollection is null)
        {
            return;
        }

        e.Handled = true;
        var collectionId = viewModel.OpenedCollection.Id;
        await ImportDroppedPathsAsync(paths => viewModel.ImportPathsIntoCollectionAsync(collectionId, paths), e);
    }

    private static async Task ImportDroppedPathsAsync(Func<IReadOnlyList<string>, Task> import, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items is null || items.Length == 0)
        {
            return;
        }

        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        if (paths.Count > 0)
        {
            await import(paths);
        }
    }
}
