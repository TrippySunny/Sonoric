using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;
using Sonoric.Services;

namespace Sonoric.ViewModels;

public partial class CollectionEditorViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly LibraryService _library;
    private readonly List<TrackChoice> _libraryOrder = new();
    private readonly string _playlistAuthor;
    private Bitmap? _preview;
    private bool _clearCover;

    public CollectionEditorViewModel(
        IFileDialogService dialogs,
        LibraryService library,
        IEnumerable<TrackItem> tracks,
        CollectionKind defaultKind,
        CollectionRecord? existing,
        string playlistAuthor)
    {
        _dialogs = dialogs;
        _library = library;
        _playlistAuthor = playlistAuthor.Trim();
        ExistingId = existing?.Id ?? Guid.NewGuid().ToString("N");
        IsNew = existing is null;
        TitleText = existing?.Title ?? string.Empty;
        Author = existing?.Author ?? string.Empty;
        YearText = existing?.Year?.ToString() ?? string.Empty;
        Description = existing?.Description ?? string.Empty;
        Kind = existing?.Kind ?? defaultKind;
        CoverSourcePath = null;
        Header = IsNew ? "Новый набор" : "Изменить набор";
        var selectedIds = existing?.TrackIds ?? new List<string>();
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        var byId = tracks.ToDictionary(track => track.Id, StringComparer.Ordinal);
        foreach (var id in selectedIds)
        {
            if (byId.TryGetValue(id, out var track))
            {
                AddChoice(track, isSelected: true);
            }
        }

        foreach (var track in tracks)
        {
            if (!selected.Contains(track.Id))
            {
                AddChoice(track, isSelected: false);
            }
        }

        RefreshLists();

        if (existing is not null && !string.IsNullOrWhiteSpace(existing.CoverFileName))
        {
            var stored = Path.Combine(AppPaths.CollectionCoversDirectory, existing.CoverFileName);
            if (File.Exists(stored))
            {
                SetPreview(stored);
            }
        }
    }

    public ObservableCollection<TrackChoice> Choices { get; } = new();
    public ObservableCollection<TrackChoice> LibraryChoices { get; } = new();
    public ObservableCollection<TrackChoice> SelectedChoices { get; } = new();
    public string ExistingId { get; }
    public bool IsNew { get; }
    public string Header { get; }
    public string? CoverSourcePath { get; private set; }
    public bool ClearCover => _clearCover;

    [ObservableProperty]
    private string titleText = string.Empty;

    [ObservableProperty]
    private string author = string.Empty;

    [ObservableProperty]
    private string yearText = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private CollectionKind kind;

    [ObservableProperty]
    private Bitmap? coverPreview;

    [ObservableProperty]
    private bool hasCoverPreview;

    [ObservableProperty]
    private string? validationMessage;

    [ObservableProperty]
    private string trackSearchQuery = string.Empty;

    [ObservableProperty]
    private bool isLibraryTab = true;

    public bool IsAlbum
    {
        get => Kind == CollectionKind.Album;
        set
        {
            if (value)
            {
                Kind = CollectionKind.Album;
            }
        }
    }

    public bool IsPlaylist
    {
        get => Kind == CollectionKind.Playlist;
        set
        {
            if (value)
            {
                Kind = CollectionKind.Playlist;
            }
        }
    }

    public bool ShowAlbumFields => Kind == CollectionKind.Album;

    public bool IsSelectedTab => !IsLibraryTab;

    public string SelectedTabLabel => $"Выбранные ({Choices.Count(choice => choice.IsSelected)})";

    public bool IsTracksEmpty => IsLibraryTab ? LibraryChoices.Count == 0 : SelectedChoices.Count == 0;

    public string EmptyTracksText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(TrackSearchQuery))
            {
                return "Ничего не найдено";
            }

            return IsLibraryTab ? "Нет песен в библиотеке" : "Песни не выбраны";
        }
    }

    partial void OnKindChanged(CollectionKind value)
    {
        OnPropertyChanged(nameof(IsAlbum));
        OnPropertyChanged(nameof(IsPlaylist));
        OnPropertyChanged(nameof(ShowAlbumFields));
    }

    partial void OnTrackSearchQueryChanged(string value)
    {
        RefreshLists();
    }

    partial void OnIsLibraryTabChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSelectedTab));
        OnPropertyChanged(nameof(IsTracksEmpty));
        OnPropertyChanged(nameof(EmptyTracksText));
    }

    [RelayCommand]
    private void ShowLibraryTab()
    {
        IsLibraryTab = true;
    }

    [RelayCommand]
    private void ShowSelectedTab()
    {
        IsLibraryTab = false;
    }

    [RelayCommand]
    private async Task ImportFilesAsync()
    {
        var paths = await _dialogs.PickAudioFilesAsync();
        if (paths.Count == 0)
        {
            return;
        }

        var records = _library.ResolveAndImport(paths);
        foreach (var record in records)
        {
            var existing = Choices.FirstOrDefault(choice => choice.Track.Id == record.Id);
            if (existing is null)
            {
                var item = new TrackItem(record, _library.GetTrackPath(record), _library.GetCoverPath(record));
                AddChoice(item, isSelected: true);
                continue;
            }

            existing.IsSelected = true;
        }

        IsLibraryTab = false;
        RefreshLists();
    }

    [RelayCommand]
    private async Task PickCoverAsync()
    {
        var path = await _dialogs.PickImageAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        CoverSourcePath = path;
        _clearCover = false;
        SetPreview(path);
    }

    [RelayCommand]
    private void RemoveCover()
    {
        CoverSourcePath = null;
        _clearCover = true;
        SetPreview(null);
    }

    private void AddChoice(TrackItem track, bool isSelected)
    {
        var choice = new TrackChoice(track, isSelected)
        {
            MoveUpRequested = MoveChoiceUp,
            MoveDownRequested = MoveChoiceDown,
            SelectionChanged = OnChoiceSelectionChanged
        };
        Choices.Add(choice);
        _libraryOrder.Add(choice);
    }

    private void OnChoiceSelectionChanged(TrackChoice choice)
    {
        var from = Choices.IndexOf(choice);
        if (from < 0)
        {
            return;
        }

        Choices.RemoveAt(from);
        if (choice.IsSelected)
        {
            var insertAt = 0;
            while (insertAt < Choices.Count && Choices[insertAt].IsSelected)
            {
                insertAt++;
            }

            Choices.Insert(insertAt, choice);
        }
        else
        {
            Choices.Insert(FindLibraryInsertIndex(choice), choice);
        }

        RefreshLists();
    }

    private int FindLibraryInsertIndex(TrackChoice choice)
    {
        var libraryIndex = _libraryOrder.IndexOf(choice);
        for (var i = 0; i < Choices.Count; i++)
        {
            if (Choices[i].IsSelected)
            {
                continue;
            }

            if (_libraryOrder.IndexOf(Choices[i]) > libraryIndex)
            {
                return i;
            }
        }

        return Choices.Count;
    }

    private void MoveChoiceUp(TrackChoice choice)
    {
        MoveChoice(choice, -1);
    }

    private void MoveChoiceDown(TrackChoice choice)
    {
        MoveChoice(choice, 1);
    }

    private void MoveChoice(TrackChoice choice, int offset)
    {
        var visibleIndex = SelectedChoices.IndexOf(choice);
        var targetVisible = visibleIndex + offset;
        if (visibleIndex < 0 || targetVisible < 0 || targetVisible >= SelectedChoices.Count)
        {
            return;
        }

        var other = SelectedChoices[targetVisible];
        var from = Choices.IndexOf(choice);
        var to = Choices.IndexOf(other);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        Choices.Move(from, to);
        RefreshLists();
    }

    private void RefreshLists()
    {
        ReplaceItems(LibraryChoices, Choices.Where(choice => !choice.IsSelected && MatchesTrackSearch(choice)));
        ReplaceItems(SelectedChoices, Choices.Where(choice => choice.IsSelected && MatchesTrackSearch(choice)));
        RefreshMoveFlags();
        OnPropertyChanged(nameof(SelectedTabLabel));
        OnPropertyChanged(nameof(IsTracksEmpty));
        OnPropertyChanged(nameof(EmptyTracksText));
    }

    private static void ReplaceItems(ObservableCollection<TrackChoice> target, IEnumerable<TrackChoice> source)
    {
        target.Clear();
        foreach (var choice in source)
        {
            target.Add(choice);
        }
    }

    private void RefreshMoveFlags()
    {
        for (var i = 0; i < SelectedChoices.Count; i++)
        {
            SelectedChoices[i].CanMoveUp = i > 0;
            SelectedChoices[i].CanMoveDown = i < SelectedChoices.Count - 1;
        }
    }

    public bool TryCommit(out CollectionRecord record)
    {
        if (string.IsNullOrWhiteSpace(TitleText))
        {
            ValidationMessage = "Укажите название";
            record = new CollectionRecord();
            return false;
        }

        int? year = null;
        if (Kind == CollectionKind.Album && !string.IsNullOrWhiteSpace(YearText))
        {
            if (!int.TryParse(YearText.Trim(), out var parsed) || parsed is < 1 or > 9999)
            {
                ValidationMessage = "Укажите корректный год";
                record = new CollectionRecord();
                return false;
            }

            year = parsed;
        }

        ValidationMessage = null;
        record = new CollectionRecord
        {
            Id = ExistingId,
            Title = TitleText.Trim(),
            Author = Kind == CollectionKind.Album ? Author.Trim() : _playlistAuthor,
            Year = year,
            Description = Description.Trim(),
            Kind = Kind,
            TrackIds = Choices.Where(choice => choice.IsSelected).Select(choice => choice.Track.Id).ToList()
        };
        return true;
    }

    private void SetPreview(string? path)
    {
        _preview?.Dispose();
        _preview = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            _preview = new Bitmap(path);
        }

        CoverPreview = _preview;
        HasCoverPreview = _preview is not null;
    }

    private bool MatchesTrackSearch(TrackChoice choice)
    {
        if (string.IsNullOrWhiteSpace(TrackSearchQuery))
        {
            return true;
        }

        var query = TrackSearchQuery.Trim();
        return ContainsQuery(choice.Title, query) ||
               ContainsQuery(choice.Subtitle, query) ||
               ContainsQuery(choice.Track.Artist, query) ||
               ContainsQuery(choice.Track.Album, query) ||
               ContainsQuery(choice.Track.Record.OriginalFileName, query);
    }

    private static bool ContainsQuery(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
