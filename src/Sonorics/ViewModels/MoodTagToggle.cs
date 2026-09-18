using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonorics.ViewModels;

public sealed partial class MoodTagToggle : ObservableObject
{
    public MoodTagToggle(MoodVisual visual, bool selected)
    {
        Visual = visual;
        Name = visual.Name;
        isSelected = selected;
    }

    public MoodVisual Visual { get; }
    public string Name { get; }
    public Action<MoodTagToggle>? Changed { get; set; }

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        Changed?.Invoke(this);
    }
}
