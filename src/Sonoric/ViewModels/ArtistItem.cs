using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;

namespace Sonoric.ViewModels;

public sealed partial class ArtistItem : ObservableObject
{
    public ArtistItem(ArtistRecord record, string? photoPath)
    {
        Record = record;
        PhotoPath = photoPath;
        Name = string.IsNullOrWhiteSpace(record.Name) ? "Без имени" : record.Name;
        Description = record.Description;
        HasDescription = !string.IsNullOrWhiteSpace(record.Description);
        AlbumCount = record.AlbumIds.Count;
        CountText = AlbumCount == 1 ? "1 альбом" : $"{AlbumCount} альбомов";
        HasPhoto = !string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath);
        if (HasPhoto)
        {
            Photo = new Bitmap(photoPath!);
        }

        record.Tags ??= new List<string>();
        TagToggles = new ObservableCollection<MoodTagToggle>();
    }

    public ArtistRecord Record { get; }
    public string? PhotoPath { get; }
    public string Name { get; }
    public string Description { get; }
    public bool HasDescription { get; }
    public int AlbumCount { get; }
    public string CountText { get; }
    public bool HasPhoto { get; }
    public Bitmap? Photo { get; }
    public string Id => Record.Id;
    public ObservableCollection<MoodTagToggle> TagToggles { get; }
    public Action<ArtistItem>? OpenRequested { get; set; }
    public Action<ArtistItem>? EditRequested { get; set; }
    public Action<ArtistItem>? DeleteRequested { get; set; }
    public Action<ArtistItem>? TagsChanged { get; set; }

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

    public void DisposePhoto()
    {
        Photo?.Dispose();
    }

    private void OnTagToggleChanged(MoodTagToggle toggle)
    {
        Record.Tags = TagToggles.Where(item => item.IsSelected).Select(item => item.Visual.Id).ToList();
        TagsChanged?.Invoke(this);
    }
}
