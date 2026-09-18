using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonorics.Models;

namespace Sonorics.ViewModels;

public sealed partial class TrackItem : ObservableObject
{
    public TrackItem(TrackRecord record, string filePath, string? coverPath)
    {
        Record = record;
        FilePath = filePath;
        CoverPath = coverPath;
        Title = string.IsNullOrWhiteSpace(record.Title) ? record.OriginalFileName : record.Title;
        Artist = string.IsNullOrWhiteSpace(record.Artist) ? "Неизвестный исполнитель" : record.Artist;
        Album = string.IsNullOrWhiteSpace(record.Album) ? "—" : record.Album;
        DurationText = FormatDuration(record.Duration);
        Subtitle = $"{Artist}  ·  {DurationText}";
        IsFavorite = record.IsFavorite;
        record.Tags ??= new List<string>();
        AssignedDots = new ObservableCollection<MoodDot>();
        TagToggles = new ObservableCollection<MoodTagToggle>();
    }

    public TrackRecord Record { get; }
    public string FilePath { get; }
    public string? CoverPath { get; }
    public string Title { get; }
    public string Artist { get; }
    public string Album { get; }
    public string DurationText { get; }
    public string Subtitle { get; }
    public string Id => Record.Id;
    public string Extension => Path.GetExtension(Record.OriginalFileName).TrimStart('.').ToUpperInvariant();
    public Action<TrackItem>? FavoriteRequested { get; set; }
    public Action<TrackItem>? PlayRequested { get; set; }
    public Action<TrackItem>? DeleteRequested { get; set; }
    public Action<TrackItem>? TagsChanged { get; set; }
    public ObservableCollection<MoodTagToggle> TagToggles { get; }
    public ObservableCollection<MoodDot> AssignedDots { get; }
    public bool HasAssignedDots => AssignedDots.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteMenuText))]
    private bool isFavorite;

    public string FavoriteMenuText => IsFavorite ? "Убрать из избранного" : "Добавить в избранное";

    [ObservableProperty]
    private bool isCurrentPlaying;

    [RelayCommand]
    private void Play()
    {
        PlayRequested?.Invoke(this);
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        FavoriteRequested?.Invoke(this);
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
        RebuildAssignedDots();
    }

    public Bitmap? LoadCover()
    {
        if (string.IsNullOrWhiteSpace(CoverPath) || !File.Exists(CoverPath))
        {
            return null;
        }

        return new Bitmap(CoverPath);
    }

    private void OnTagToggleChanged(MoodTagToggle toggle)
    {
        Record.Tags = TagToggles.Where(item => item.IsSelected).Select(item => item.Visual.Id).ToList();
        RebuildAssignedDots();
        TagsChanged?.Invoke(this);
    }

    private void RebuildAssignedDots()
    {
        AssignedDots.Clear();
        foreach (var group in TagToggles.Where(item => item.IsSelected).GroupBy(item => item.Visual.Hex, StringComparer.OrdinalIgnoreCase))
        {
            AssignedDots.Add(new MoodDot(group.First().Visual.Accent, group.Select(item => item.Name)));
        }

        OnPropertyChanged(nameof(HasAssignedDots));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss");
        }

        return duration.ToString(@"m\:ss");
    }
}
