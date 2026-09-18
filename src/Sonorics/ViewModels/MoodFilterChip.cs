using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;

namespace Sonorics.ViewModels;

public sealed partial class MoodFilterChip : ObservableObject
{
    public MoodFilterChip(MoodVisual visual, Action<MoodFilterChip> changed)
    {
        Visual = visual;
        Name = visual.Name;
        _changed = changed;
    }

    private readonly Action<MoodFilterChip> _changed;

    public MoodVisual Visual { get; }
    public string Name { get; }
    public IBrush Accent => Visual.Accent;
    public IBrush SelectedForeground => Visual.Foreground;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayBackground))]
    [NotifyPropertyChangedFor(nameof(DisplayForeground))]
    private bool isSelected;

    public IBrush DisplayBackground => IsSelected ? Visual.Accent : Visual.IdleBackground;
    public IBrush DisplayForeground => IsSelected ? Visual.Foreground : Visual.Accent;

    [RelayCommand]
    private void Toggle()
    {
        IsSelected = !IsSelected;
        _changed(this);
    }
}
