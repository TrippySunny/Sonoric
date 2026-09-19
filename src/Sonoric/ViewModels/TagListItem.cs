using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;

namespace Sonoric.ViewModels;

public sealed partial class TagListItem : ObservableObject
{
    public TagListItem(TagRecord record, MoodVisual visual)
    {
        Record = record;
        Visual = visual;
        Name = record.Name;
        Accent = visual.Accent;
    }

    public TagRecord Record { get; }
    public MoodVisual Visual { get; }
    public string Name { get; }
    public IBrush Accent { get; }
    public Action<TagListItem>? EditRequested { get; set; }
    public Action<TagListItem>? DeleteRequested { get; set; }

    [RelayCommand]
    private void Edit()
    {
        EditRequested?.Invoke(this);
    }

    [RelayCommand]
    private void Delete()
    {
        DeleteRequested?.Invoke(this);
    }
}
