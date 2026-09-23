using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;

namespace Sonoric.ViewModels;

public sealed partial class CollectionItem : ObservableObject
{
    public CollectionItem(CollectionRecord record, string? coverPath, int trackCount)
    {
        Record = record;
        CoverPath = coverPath;
        Title = string.IsNullOrWhiteSpace(record.Title) ? "Без названия" : record.Title;
        Author = string.IsNullOrWhiteSpace(record.Author) ? "Без автора" : record.Author;
        Year = record.Kind == CollectionKind.Album ? record.Year : null;
        Description = record.Description;
        TrackCount = trackCount;
        CountText = trackCount == 1 ? "1 трек" : $"{trackCount} треков";
        KindLabel = record.Kind == CollectionKind.Album ? "Альбом" : "Плейлист";
        IsAlbum = record.Kind == CollectionKind.Album;
        HasAuthor = !string.IsNullOrWhiteSpace(record.Author);
        HasYear = Year.HasValue;
        YearText = Year?.ToString() ?? string.Empty;
        HasCover = !string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath);
        if (HasCover)
        {
            Cover = new Bitmap(coverPath!);
        }
    }

    public CollectionRecord Record { get; }
    public string? CoverPath { get; }
    public string Title { get; }
    public string Author { get; }
    public int? Year { get; }
    public string YearText { get; }
    public string Description { get; }
    public int TrackCount { get; }
    public string CountText { get; }
    public string KindLabel { get; }
    public bool IsAlbum { get; }
    public bool HasAuthor { get; }
    public bool HasYear { get; }
    public bool HasCover { get; }
    public Bitmap? Cover { get; }
    public string Id => Record.Id;
    public CollectionKind Kind => Record.Kind;

    public Action<CollectionItem>? OpenRequested { get; set; }
    public Action<CollectionItem>? PreviewCoverRequested { get; set; }
    public Action<CollectionItem>? ImportRequested { get; set; }
    public Action<CollectionItem>? EditRequested { get; set; }
    public Action<CollectionItem>? DeleteRequested { get; set; }

    [RelayCommand]
    private void Open()
    {
        OpenRequested?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanPreviewCover))]
    private void PreviewCover()
    {
        PreviewCoverRequested?.Invoke(this);
    }

    private bool CanPreviewCover() => HasCover;

    [RelayCommand]
    private void Import()
    {
        ImportRequested?.Invoke(this);
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

    public void DisposeCover()
    {
        Cover?.Dispose();
    }
}
