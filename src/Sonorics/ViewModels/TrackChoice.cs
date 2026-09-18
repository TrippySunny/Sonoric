using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sonorics.ViewModels;

public sealed partial class TrackChoice : ObservableObject
{
    public TrackChoice(TrackItem track, bool isSelected)
    {
        Track = track;
        IsSelected = isSelected;
    }

    public TrackItem Track { get; }
    public string Title => Track.Title;
    public string Subtitle => Track.Subtitle;
    public Action<TrackChoice>? MoveUpRequested { get; set; }
    public Action<TrackChoice>? MoveDownRequested { get; set; }
    public Action<TrackChoice>? SelectionChanged { get; set; }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool canMoveUp;

    [ObservableProperty]
    private bool canMoveDown;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this);
    }

    [RelayCommand]
    private void MoveUp()
    {
        MoveUpRequested?.Invoke(this);
    }

    [RelayCommand]
    private void MoveDown()
    {
        MoveDownRequested?.Invoke(this);
    }
}
