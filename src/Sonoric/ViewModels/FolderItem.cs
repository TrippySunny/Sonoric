using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;

namespace Sonoric.ViewModels;

public sealed partial class FolderItem : ObservableObject
{
    public FolderItem(FolderRecord record, string? coverPath)
    {
        Record = record;
        CoverPath = coverPath;
        Title = string.IsNullOrWhiteSpace(record.Title) ? "Без названия" : record.Title;
        Description = record.Description;
        HasDescription = !string.IsNullOrWhiteSpace(record.Description);
        ItemCount = record.CollectionIds.Count;
        CountText = ItemCount == 1 ? "1 набор" : $"{ItemCount} наборов";
        HasCover = !string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath);
        if (HasCover)
        {
            Cover = new Bitmap(coverPath!);
        }

        record.Tags ??= new List<string>();
        TagToggles = new ObservableCollection<MoodTagToggle>();
    }

    public FolderRecord Record { get; }
    public string? CoverPath { get; }
    public string Title { get; }
    public string Description { get; }
    public bool HasDescription { get; }
    public int ItemCount { get; }
    public string CountText { get; }
    public bool HasCover { get; }
    public Bitmap? Cover { get; }
    public string Id => Record.Id;
    public ObservableCollection<MoodTagToggle> TagToggles { get; }
    public Action<FolderItem>? OpenRequested { get; set; }
    public Action<FolderItem>? EditRequested { get; set; }
    public Action<FolderItem>? DeleteRequested { get; set; }
    public Action<FolderItem>? TagsChanged { get; set; }

    [RelayCommand]
    private void Open()
    {
        OpenRequested?.Invoke(this);
    }

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

    public void SyncCatalog(IReadOnlyList<MoodVisual> visuals)
    {
        var selected = new HashSet<string>(Record.Tags, StringComparer.Ordinal);
        foreach (var toggle in TagToggles)
        {
            toggle.Changed = null;
        }

        TagToggles.Clear();
        foreach (var visual in visuals)
        {
            var toggle = new MoodTagToggle(visual, selected.Contains(visual.Id));
            toggle.Changed = OnTagToggleChanged;
            TagToggles.Add(toggle);
        }

        Record.Tags = TagToggles.Where(item => item.IsSelected).Select(item => item.Visual.Id).ToList();
    }

    public void DisposeCover()
    {
        Cover?.Dispose();
    }

    private void OnTagToggleChanged(MoodTagToggle toggle)
    {
        Record.Tags = TagToggles.Where(item => item.IsSelected).Select(item => item.Visual.Id).ToList();
        TagsChanged?.Invoke(this);
    }
}
