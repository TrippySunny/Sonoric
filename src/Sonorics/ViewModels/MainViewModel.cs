using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonorics.Models;
using Sonorics.Services;

namespace Sonorics.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly PlaybackService _playback;
    private readonly IFileDialogService _dialogs;
    private readonly ICollectionDialogService _collectionDialogs;
    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;
    private readonly List<TrackItem> _playQueue = new();
    private readonly List<CollectionItem> _collectionCache = new();
    private bool _suppressSelectionPlay;
    private bool _isSeeking;
    private bool _updatingPosition;
    private Bitmap? _cover;
    private string? _activeTrackId;
    private bool _queueFromCollection;
    private bool _updatingTagColor;
    private string? _editingTagId;

    public MainViewModel(
        LibraryService library,
        PlaybackService playback,
        IFileDialogService dialogs,
        ICollectionDialogService collectionDialogs)
    {
        _library = library;
        _playback = playback;
        _dialogs = dialogs;
        _collectionDialogs = collectionDialogs;
        _settings = _settingsService.Load();
        AllTracks = new ObservableCollection<TrackItem>();
        VisibleTracks = new ObservableCollection<TrackItem>();
        CollectionTracks = new ObservableCollection<TrackItem>();
        Albums = new ObservableCollection<CollectionItem>();
        Playlists = new ObservableCollection<CollectionItem>();
        CustomTags = new ObservableCollection<TagListItem>();
        MoodFilters = new ObservableCollection<MoodFilterChip>();
        StatusText = playback.IsAvailable
            ? "Нет аудиофайлов. Нажмите «Импорт», чтобы добавить."
            : $"Плеер недоступен: {playback.InitError}";
        Volume = Math.Clamp(_settings.Volume, 0, 100);
        _playback.Volume = (int)Math.Round(Volume);
        _playback.StateChanged += () => Dispatcher.UIThread.Post(RefreshPlaybackState);
        _playback.PositionChanged += () => Dispatcher.UIThread.Post(RefreshPosition);
        _playback.TrackEnded += () => Dispatcher.UIThread.Post(PlayNext);
        LoadLibrary();
    }

    public ObservableCollection<TrackItem> AllTracks { get; }
    public ObservableCollection<TrackItem> VisibleTracks { get; }
    public ObservableCollection<TrackItem> CollectionTracks { get; }
    public ObservableCollection<CollectionItem> Albums { get; }
    public ObservableCollection<CollectionItem> Playlists { get; }
    public ObservableCollection<TagListItem> CustomTags { get; }
    public ObservableCollection<MoodFilterChip> MoodFilters { get; }

    [ObservableProperty]
    private TrackItem? selectedTrack;

    [ObservableProperty]
    private TrackItem? selectedCollectionTrack;

    [ObservableProperty]
    private bool isCollectionTracksEmpty;

    [ObservableProperty]
    private string nowPlayingTitle = "Ничего не играет";

    [ObservableProperty]
    private string nowPlayingArtist = "Выберите трек из списка";

    [ObservableProperty]
    private string nowPlayingAlbum = string.Empty;

    [ObservableProperty]
    private string nowPlayingFormat = string.Empty;

    [ObservableProperty]
    private Bitmap? nowPlayingCover;

    [ObservableProperty]
    private bool hasCover;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string playPauseLabel = "Старт";

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool canControlPlayback;

    [ObservableProperty]
    private double durationSeconds = 1;

    [ObservableProperty]
    private double positionSeconds;

    [ObservableProperty]
    private string elapsedText = "0:00";

    [ObservableProperty]
    private string remainingText = "0:00";

    [ObservableProperty]
    private double volume = 80;

    [ObservableProperty]
    private bool isTrackListEmpty = true;

    [ObservableProperty]
    private LibrarySection section = LibrarySection.All;

    [ObservableProperty]
    private CollectionRecord? openedCollection;

    [ObservableProperty]
    private string openedCollectionTitle = string.Empty;

    [ObservableProperty]
    private string openedCollectionAuthor = string.Empty;

    [ObservableProperty]
    private bool hasOpenedAuthor;

    [ObservableProperty]
    private string openedCollectionYearText = string.Empty;

    [ObservableProperty]
    private bool hasOpenedYear;

    [ObservableProperty]
    private string openedCollectionDescription = string.Empty;

    [ObservableProperty]
    private string openedCollectionKindLabel = string.Empty;

    [ObservableProperty]
    private string openedCollectionCountText = string.Empty;

    [ObservableProperty]
    private Bitmap? openedCollectionCover;

    [ObservableProperty]
    private bool openedCollectionHasCover;

    [ObservableProperty]
    private bool hasOpenedDescription;

    [ObservableProperty]
    private bool isCollectionOpen;

    [ObservableProperty]
    private bool isCoverViewerOpen;

    [ObservableProperty]
    private Bitmap? coverViewerImage;

    [ObservableProperty]
    private double coverViewerZoom = 1;

    [ObservableProperty]
    private double coverViewerOffsetX;

    [ObservableProperty]
    private double coverViewerOffsetY;

    [ObservableProperty]
    private bool isTrackListVisible = true;

    [ObservableProperty]
    private bool isAlbumGridVisible;

    [ObservableProperty]
    private bool isPlaylistListVisible;

    [ObservableProperty]
    private bool isCreateVisible;

    [ObservableProperty]
    private string createLabel = "Создать набор";

    [ObservableProperty]
    private string emptyListText = "Нет аудиофайлов";

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private int typeFilterIndex;

    [ObservableProperty]
    private int sortIndex = 1;

    [ObservableProperty]
    private bool sortDescending;

    [ObservableProperty]
    private string sortDirectionLabel = "А→Я";

    [ObservableProperty]
    private string moodFilterLabel = "Настроение";

    [ObservableProperty]
    private bool isTagListVisible;

    [ObservableProperty]
    private bool isFilterBarVisible = true;

    [ObservableProperty]
    private bool hasCustomTags;

    [ObservableProperty]
    private string tagName = string.Empty;

    [ObservableProperty]
    private double tagRed = 255;

    [ObservableProperty]
    private double tagGreen = 255;

    [ObservableProperty]
    private double tagBlue = 255;

    [ObservableProperty]
    private string tagHex = "#FFFFFF";

    [ObservableProperty]
    private IBrush tagPreview = new SolidColorBrush(Colors.White);

    [ObservableProperty]
    private string tagSaveLabel = "Создать тег";

    [ObservableProperty]
    private bool isEditingTag;

    [ObservableProperty]
    private string? tagValidationMessage;

    public IReadOnlyList<string> TypeFilterOptions { get; } =
    [
        "Все",
        "Песня",
        "Альбом",
        "Плейлист"
    ];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "По дате добавления",
        "По алфавиту",
        "По автору"
    ];

    public bool IsAllSection => Section == LibrarySection.All;
    public bool IsFavoritesSection => Section == LibrarySection.Favorites;
    public bool IsAlbumsSection => Section == LibrarySection.Albums;
    public bool IsPlaylistsSection => Section == LibrarySection.Playlists;
    public bool IsTagsSection => Section == LibrarySection.Tags;

    partial void OnSelectedTrackChanged(TrackItem? value)
    {
        if (value is null)
        {
            return;
        }

        BindNowPlaying(value, playing: IsPlaying && _activeTrackId == value.Id);
        CanControlPlayback = _playback.IsAvailable && AllTracks.Count > 0;
        if (_suppressSelectionPlay)
        {
            return;
        }

        if (IsPlaying)
        {
            PlayTrack(value);
        }
    }

    partial void OnSelectedCollectionTrackChanged(TrackItem? value)
    {
        if (value is null || _suppressSelectionPlay)
        {
            return;
        }

        BindNowPlaying(value, playing: IsPlaying && _activeTrackId == value.Id);
        CanControlPlayback = _playback.IsAvailable && AllTracks.Count > 0;
        _suppressSelectionPlay = true;
        SelectedTrack = value;
        _suppressSelectionPlay = false;
        if (IsPlaying)
        {
            PlayTrack(value, fromCollection: true);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        var clamped = (int)Math.Clamp(Math.Round(value), 0, 100);
        _playback.Volume = clamped;
        _settings.Volume = clamped;
    }

    partial void OnSearchQueryChanged(string value)
    {
        RefreshSection();
    }

    partial void OnTypeFilterIndexChanged(int value)
    {
        RefreshSection();
    }

    partial void OnSortIndexChanged(int value)
    {
        RefreshSection();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        SortDirectionLabel = value ? "Я→А" : "А→Я";
        RefreshSection();
    }

    partial void OnTagRedChanged(double value)
    {
        RefreshTagPreviewFromRgb();
    }

    partial void OnTagGreenChanged(double value)
    {
        RefreshTagPreviewFromRgb();
    }

    partial void OnTagBlueChanged(double value)
    {
        RefreshTagPreviewFromRgb();
    }

    partial void OnTagHexChanged(string value)
    {
        if (_updatingTagColor)
        {
            return;
        }

        var hex = value.Trim();
        if (!hex.StartsWith('#'))
        {
            hex = "#" + hex;
        }

        if (!Color.TryParse(hex, out var color))
        {
            return;
        }

        _updatingTagColor = true;
        TagRed = color.R;
        TagGreen = color.G;
        TagBlue = color.B;
        TagPreview = new SolidColorBrush(color);
        _updatingTagColor = false;
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortDescending = !SortDescending;
    }

    [RelayCommand]
    private void ShowAll()
    {
        SetSection(LibrarySection.All);
    }

    [RelayCommand]
    private void ShowFavorites()
    {
        SetSection(LibrarySection.Favorites);
    }

    [RelayCommand]
    private void ShowAlbums()
    {
        SetSection(LibrarySection.Albums);
    }

    [RelayCommand]
    private void ShowPlaylists()
    {
        SetSection(LibrarySection.Playlists);
    }

    [RelayCommand]
    private void ShowTags()
    {
        SetSection(LibrarySection.Tags);
    }

    [RelayCommand]
    private void CloseCollection()
    {
        HideCoverViewer();
        OpenedCollection = null;
        OpenedCollectionCover = null;
        OpenedCollectionHasCover = false;
        SelectedCollectionTrack = null;
        CollectionTracks.Clear();
        RefreshSection();
    }

    [RelayCommand]
    private void OpenOpenedCollectionCover()
    {
        if (OpenedCollectionCover is null || !OpenedCollectionHasCover)
        {
            return;
        }

        ShowCoverViewer(OpenedCollectionCover);
    }

    [RelayCommand]
    private void CloseCoverViewer()
    {
        HideCoverViewer();
    }

    private void OpenCollectionCover(CollectionItem item)
    {
        if (item.Cover is null || !item.HasCover)
        {
            return;
        }

        ShowCoverViewer(item.Cover);
    }

    private void ShowCoverViewer(Bitmap image)
    {
        CoverViewerImage = image;
        CoverViewerZoom = 1;
        CoverViewerOffsetX = 0;
        CoverViewerOffsetY = 0;
        IsCoverViewerOpen = true;
    }

    private void HideCoverViewer()
    {
        IsCoverViewerOpen = false;
        CoverViewerImage = null;
        CoverViewerZoom = 1;
        CoverViewerOffsetX = 0;
        CoverViewerOffsetY = 0;
    }

    [RelayCommand]
    private async Task ImportFilesAsync()
    {
        var paths = await _dialogs.PickAudioFilesAsync();
        await ImportPathsAsync(paths);
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var paths = await _dialogs.PickAudioFolderAsync();
        await ImportPathsAsync(paths);
    }

    [RelayCommand]
    private async Task CreateCollectionAsync()
    {
        var kind = Section == LibrarySection.Albums ? CollectionKind.Album : CollectionKind.Playlist;
        await EditCollectionAsync(null, kind);
    }

    [RelayCommand]
    private async Task EditOpenedCollectionAsync()
    {
        if (OpenedCollection is null)
        {
            return;
        }

        await EditCollectionAsync(OpenedCollection, OpenedCollection.Kind);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!_playback.IsAvailable)
        {
            StatusText = string.IsNullOrWhiteSpace(_playback.InitError)
                ? "Плеер недоступен"
                : $"Плеер недоступен: {_playback.InitError}";
            return;
        }

        var fromCollection = false;
        var track = SelectedTrack;
        if (track is null && IsCollectionOpen)
        {
            track = SelectedCollectionTrack ?? CollectionTracks.FirstOrDefault();
            fromCollection = track is not null;
        }

        track ??= VisibleTracks.FirstOrDefault() ?? AllTracks.FirstOrDefault();
        if (track is null)
        {
            StatusText = "Сначала импортируйте аудиофайл";
            return;
        }

        if (SelectedTrack is null)
        {
            _suppressSelectionPlay = true;
            SelectedTrack = track;
            _suppressSelectionPlay = false;
            BindNowPlaying(track, playing: false);
        }

        if (_playback.IsPlaying && _activeTrackId == track.Id)
        {
            _playback.SetPaused(true);
            return;
        }

        if (_playback.HasMedia && _activeTrackId == track.Id)
        {
            _playback.SetPaused(false);
            return;
        }

        PlayTrack(track, fromCollection || _queueFromCollection);
    }

    [RelayCommand]
    private void SkipPrevious()
    {
        EnsurePlayQueue();
        if (_playQueue.Count == 0)
        {
            return;
        }

        if (_playback.PositionMs > 3000)
        {
            _playback.Seek(0);
            PositionSeconds = 0;
            ElapsedText = "0:00";
            return;
        }

        var index = CurrentQueueIndex();
        var previous = index <= 0 ? _playQueue[^1] : _playQueue[index - 1];
        PlayQueued(previous);
    }

    [RelayCommand]
    private void SkipNext()
    {
        EnsurePlayQueue();
        if (_playQueue.Count == 0)
        {
            return;
        }

        var index = CurrentQueueIndex();
        var next = index < 0 || index >= _playQueue.Count - 1 ? _playQueue[0] : _playQueue[index + 1];
        PlayQueued(next);
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var imported = await Task.Run(() => _library.Import(paths));
        ReloadLibrary(_settings.LastTrackId);
        if (imported.Count == 0 && AllTracks.Count == 0)
        {
            StatusText = "Нет аудиофайлов. Нажмите «Импорт», чтобы добавить.";
        }
    }

    public void BeginSeek()
    {
        _isSeeking = true;
    }

    public void PreviewSeek(double seconds)
    {
        if (!_isSeeking)
        {
            return;
        }

        ElapsedText = FormatTime(TimeSpan.FromSeconds(Math.Max(0, seconds)));
    }

    public void CommitSeek(double seconds)
    {
        _playback.Seek((long)(Math.Max(0, seconds) * 1000.0));
        _isSeeking = false;
        RefreshPosition();
    }

    public void PlaySelectedTrack()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        PlayTrack(SelectedTrack);
    }

    public void PlaySelectedCollectionTrack()
    {
        if (SelectedCollectionTrack is null)
        {
            return;
        }

        PlayTrack(SelectedCollectionTrack, fromCollection: true);
    }

    public void PersistState()
    {
        _settings.Volume = (int)Math.Round(Volume);
        _settings.LastTrackId = SelectedTrack?.Id;
        _settingsService.Save(_settings);
        _library.Save();
    }

    private async Task EditCollectionAsync(CollectionRecord? existing, CollectionKind defaultKind)
    {
        var editor = new CollectionEditorViewModel(_dialogs, AllTracks, defaultKind, existing);
        if (!await _collectionDialogs.EditAsync(editor))
        {
            return;
        }

        if (!editor.TryCommit(out var record))
        {
            return;
        }

        var saved = _library.UpsertCollection(record, editor.CoverSourcePath, editor.ClearCover);
        if (OpenedCollection?.Id == saved.Id)
        {
            OpenedCollection = saved;
        }

        ReloadCollections();
        RefreshSection();
    }

    private void SetSection(LibrarySection section)
    {
        Section = section;
        OnPropertyChanged(nameof(IsAllSection));
        OnPropertyChanged(nameof(IsFavoritesSection));
        OnPropertyChanged(nameof(IsAlbumsSection));
        OnPropertyChanged(nameof(IsPlaylistsSection));
        OnPropertyChanged(nameof(IsTagsSection));
        RefreshSection();
    }

    private void LoadLibrary()
    {
        _library.Load();
        ReloadLibrary(_settings.LastTrackId);
    }

    private void ReloadLibrary(string? selectedId)
    {
        AllTracks.Clear();
        foreach (var record in _library.Tracks)
        {
            var item = new TrackItem(record, _library.GetTrackPath(record), _library.GetCoverPath(record))
            {
                FavoriteRequested = OnFavoriteRequested,
                DeleteRequested = OnDeleteRequested,
                TagsChanged = OnTagsChanged,
                PlayRequested = OnPlayRequested
            };
            AllTracks.Add(item);
        }

        RebuildTagCatalog();
        ReloadCollections();
        _suppressSelectionPlay = true;
        SelectedTrack = AllTracks.FirstOrDefault(track => track.Id == selectedId) ?? AllTracks.FirstOrDefault();
        _suppressSelectionPlay = false;
        CanControlPlayback = AllTracks.Count > 0 && _playback.IsAvailable;
        if (SelectedTrack is not null)
        {
            BindNowPlaying(SelectedTrack, playing: false);
        }

        RefreshSection();
    }

    private void ReloadCollections()
    {
        var openedId = OpenedCollection?.Id;
        HideCoverViewer();
        foreach (var item in _collectionCache)
        {
            item.DisposeCover();
        }

        _collectionCache.Clear();
        foreach (var record in _library.Collections)
        {
            var item = new CollectionItem(record, _library.GetCollectionCoverPath(record), record.TrackIds.Count)
            {
                OpenRequested = OpenCollection,
                PreviewCoverRequested = OpenCollectionCover,
                EditRequested = OnEditCollection,
                DeleteRequested = OnDeleteCollection
            };
            _collectionCache.Add(item);
        }

        if (openedId is null)
        {
            return;
        }

        var opened = _collectionCache.FirstOrDefault(item => item.Id == openedId);
        if (opened is null)
        {
            OpenedCollection = null;
            OpenedCollectionCover = null;
            OpenedCollectionHasCover = false;
            CollectionTracks.Clear();
            return;
        }

        OpenedCollection = opened.Record;
        BindOpenedCollection(opened);
    }

    private void OpenCollection(CollectionItem item)
    {
        if (OpenedCollection?.Id != item.Id)
        {
            _suppressSelectionPlay = true;
            SelectedCollectionTrack = null;
            _suppressSelectionPlay = false;
        }

        OpenedCollection = item.Record;
        BindOpenedCollection(item);
        RefreshSection();
    }

    private void BindOpenedCollection(CollectionItem item)
    {
        OpenedCollectionTitle = item.Title;
        OpenedCollectionAuthor = item.Author;
        HasOpenedAuthor = item.HasAuthor;
        OpenedCollectionYearText = item.YearText;
        HasOpenedYear = item.HasYear;
        OpenedCollectionDescription = item.Description;
        HasOpenedDescription = !string.IsNullOrWhiteSpace(item.Description);
        OpenedCollectionKindLabel = item.KindLabel;
        OpenedCollectionCountText = item.CountText;
        OpenedCollectionCover = item.Cover;
        OpenedCollectionHasCover = item.HasCover;
    }

    private void OnEditCollection(CollectionItem item)
    {
        _ = EditCollectionAsync(item.Record, item.Kind);
    }

    private void OnDeleteCollection(CollectionItem item)
    {
        if (OpenedCollection?.Id == item.Id)
        {
            HideCoverViewer();
            OpenedCollection = null;
            OpenedCollectionCover = null;
            OpenedCollectionHasCover = false;
        }

        _library.DeleteCollection(item.Id);
        ReloadCollections();
        RefreshSection();
    }

    private void OnDeleteRequested(TrackItem item)
    {
        var wasPlaying = _activeTrackId == item.Id;
        var queueIndex = _playQueue.FindIndex(track => track.Id == item.Id);
        _library.DeleteTrack(item.Id);
        _playQueue.RemoveAll(track => track.Id == item.Id);
        AllTracks.Remove(item);
        if (wasPlaying)
        {
            if (_playQueue.Count == 0)
            {
                _playback.Stop();
                _activeTrackId = null;
                IsPlaying = false;
                PlayPauseLabel = "Старт";
                NowPlayingTitle = "Ничего не играет";
                NowPlayingArtist = "Выберите трек из списка";
                NowPlayingCover = null;
                HasCover = false;
                UpdatePlayingFlags();
            }
            else
            {
                var nextIndex = Math.Min(queueIndex, _playQueue.Count - 1);
                PlayQueued(_playQueue[Math.Max(0, nextIndex)]);
            }
        }

        ReloadCollections();
        RefreshSection();
    }

    private void OnPlayRequested(TrackItem item)
    {
        _suppressSelectionPlay = true;
        SelectedTrack = item;
        _suppressSelectionPlay = false;
        if (_playback.IsPlaying && _activeTrackId == item.Id)
        {
            _playback.SetPaused(true);
            return;
        }

        if (_playback.HasMedia && _activeTrackId == item.Id)
        {
            _playback.SetPaused(false);
            return;
        }

        PlayTrack(item, fromCollection: IsCollectionOpen && CollectionTracks.Any(track => track.Id == item.Id));
    }

    private void OnFavoriteRequested(TrackItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        item.Record.IsFavorite = item.IsFavorite;
        _library.SetFavorite(item.Id, item.IsFavorite);
        if (Section == LibrarySection.Favorites && !IsCollectionOpen)
        {
            RefreshSection();
        }
        else
        {
            RefreshStatus();
        }
    }

    private void OnTagsChanged(TrackItem item)
    {
        _library.SetTrackTags(item.Id, item.Record.Tags);
        if (MoodFilters.Any(filter => filter.IsSelected))
        {
            RefreshSection();
        }
    }

    private void OnMoodFilterChanged(MoodFilterChip chip)
    {
        var count = MoodFilters.Count(filter => filter.IsSelected);
        MoodFilterLabel = count == 0 ? "Настроение" : $"Настроение · {count}";
        RefreshSection();
    }

    [RelayCommand]
    private void SaveTag()
    {
        var name = TagName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TagValidationMessage = "Укажите название тега";
            return;
        }

        if (_library.TagNameTaken(name, _editingTagId))
        {
            TagValidationMessage = "Такой тег уже есть";
            return;
        }

        RefreshTagPreviewFromRgb();
        _library.UpsertTag(new TagRecord
        {
            Id = _editingTagId ?? Guid.NewGuid().ToString("N"),
            Name = name,
            Hex = TagHex
        });
        ResetTagEditor();
        RebuildTagCatalog();
        RefreshSection();
    }

    [RelayCommand]
    private void CancelTagEdit()
    {
        ResetTagEditor();
    }

    private void BeginEditTag(TagListItem item)
    {
        _editingTagId = item.Record.Id;
        IsEditingTag = true;
        TagSaveLabel = "Сохранить";
        TagValidationMessage = null;
        _updatingTagColor = true;
        TagName = item.Name;
        TagRed = item.Visual.Color.R;
        TagGreen = item.Visual.Color.G;
        TagBlue = item.Visual.Color.B;
        TagHex = item.Visual.Hex;
        TagPreview = item.Visual.Accent;
        _updatingTagColor = false;
    }

    private void DeleteCustomTag(TagListItem item)
    {
        if (_editingTagId == item.Record.Id)
        {
            ResetTagEditor();
        }

        _library.DeleteTag(item.Record.Id);
        RebuildTagCatalog();
        RefreshSection();
    }

    private void RebuildTagCatalog()
    {
        var visuals = _library.Tags.Select(record => new MoodVisual(record)).ToList();
        var selected = MoodFilters.Where(filter => filter.IsSelected).Select(filter => filter.Visual.Id).ToHashSet(StringComparer.Ordinal);
        MoodFilters.Clear();
        foreach (var visual in visuals)
        {
            var chip = new MoodFilterChip(visual, OnMoodFilterChanged);
            chip.IsSelected = selected.Contains(visual.Id);
            MoodFilters.Add(chip);
        }

        CustomTags.Clear();
        foreach (var record in _library.Tags)
        {
            var item = new TagListItem(record, new MoodVisual(record))
            {
                EditRequested = BeginEditTag,
                DeleteRequested = DeleteCustomTag
            };
            CustomTags.Add(item);
        }

        HasCustomTags = CustomTags.Count > 0;
        foreach (var track in AllTracks)
        {
            track.SyncCatalog(visuals);
        }

        var count = MoodFilters.Count(filter => filter.IsSelected);
        MoodFilterLabel = count == 0 ? "Настроение" : $"Настроение · {count}";
    }

    private void ResetTagEditor()
    {
        _editingTagId = null;
        IsEditingTag = false;
        TagSaveLabel = "Создать тег";
        TagName = string.Empty;
        TagValidationMessage = null;
        _updatingTagColor = true;
        TagRed = 255;
        TagGreen = 255;
        TagBlue = 255;
        TagHex = "#FFFFFF";
        TagPreview = new SolidColorBrush(Colors.White);
        _updatingTagColor = false;
    }

    private void RefreshTagPreviewFromRgb()
    {
        if (_updatingTagColor)
        {
            return;
        }

        _updatingTagColor = true;
        var color = Color.FromRgb(
            (byte)Math.Clamp(Math.Round(TagRed), 0, 255),
            (byte)Math.Clamp(Math.Round(TagGreen), 0, 255),
            (byte)Math.Clamp(Math.Round(TagBlue), 0, 255));
        TagPreview = new SolidColorBrush(color);
        TagHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _updatingTagColor = false;
    }

    private SearchKind CurrentSearchKind()
    {
        return TypeFilterIndex switch
        {
            1 => SearchKind.Song,
            2 => SearchKind.Album,
            3 => SearchKind.Playlist,
            _ => SearchKind.All
        };
    }

    private void RefreshSection()
    {
        IsCollectionOpen = OpenedCollection is not null;
        var kind = CurrentSearchKind();
        var showAlbums = kind is SearchKind.All or SearchKind.Album &&
                         (kind == SearchKind.Album || Section == LibrarySection.Albums);
        var showPlaylists = kind is SearchKind.All or SearchKind.Playlist &&
                            (kind == SearchKind.Playlist || Section == LibrarySection.Playlists);
        var showTracks = kind is SearchKind.All or SearchKind.Song &&
                         Section is LibrarySection.All or LibrarySection.Favorites;

        if (kind == SearchKind.Song)
        {
            showAlbums = false;
            showPlaylists = false;
            showTracks = true;
        }

        if (kind == SearchKind.Album)
        {
            showTracks = false;
            showAlbums = true;
            showPlaylists = false;
        }

        if (kind == SearchKind.Playlist)
        {
            showTracks = false;
            showAlbums = false;
            showPlaylists = true;
        }

        if (Section == LibrarySection.Tags)
        {
            showAlbums = false;
            showPlaylists = false;
            showTracks = false;
        }

        IsAlbumGridVisible = showAlbums;
        IsPlaylistListVisible = showPlaylists;
        IsTrackListVisible = showTracks && !showAlbums && !showPlaylists;
        IsTagListVisible = Section == LibrarySection.Tags;
        IsFilterBarVisible = Section != LibrarySection.Tags;
        IsCreateVisible = (Section == LibrarySection.Albums || Section == LibrarySection.Playlists) &&
                          kind == SearchKind.All &&
                          string.IsNullOrWhiteSpace(SearchQuery);
        CreateLabel = Section == LibrarySection.Albums ? "Создать альбом" : "Создать плейлист";

        var selectedCollectionId = SelectedCollectionTrack?.Id;
        VisibleTracks.Clear();
        foreach (var track in GetVisibleTrackSource())
        {
            VisibleTracks.Add(track);
        }

        CollectionTracks.Clear();
        if (OpenedCollection is not null)
        {
            var map = AllTracks.ToDictionary(track => track.Id, StringComparer.Ordinal);
            foreach (var id in OpenedCollection.TrackIds)
            {
                if (map.TryGetValue(id, out var track) && MatchesMood(track))
                {
                    CollectionTracks.Add(track);
                }
            }

            OpenedCollectionCountText = CollectionTracks.Count == 1 ? "1 трек" : $"{CollectionTracks.Count} треков";
            _suppressSelectionPlay = true;
            SelectedCollectionTrack = CollectionTracks.FirstOrDefault(track => track.Id == selectedCollectionId);
            _suppressSelectionPlay = false;
        }

        IsCollectionTracksEmpty = IsCollectionOpen && CollectionTracks.Count == 0;

        Albums.Clear();
        foreach (var album in SortCollections(FilterCollections(CollectionKind.Album)))
        {
            Albums.Add(album);
        }

        Playlists.Clear();
        foreach (var playlist in SortCollections(FilterCollections(CollectionKind.Playlist)))
        {
            Playlists.Add(playlist);
        }

        IsTrackListEmpty = IsTrackListVisible && VisibleTracks.Count == 0;
        if (IsAlbumGridVisible)
        {
            IsTrackListEmpty = Albums.Count == 0;
        }
        else if (IsPlaylistListVisible)
        {
            IsTrackListEmpty = Playlists.Count == 0;
        }
        else if (IsTagListVisible)
        {
            IsTrackListEmpty = false;
        }

        RefreshStatus();
    }

    private IEnumerable<TrackItem> GetVisibleTrackSource()
    {
        IEnumerable<TrackItem> source = AllTracks;
        if (CurrentSearchKind() == SearchKind.Song)
        {
            source = AllTracks;
        }
        else if (Section == LibrarySection.Favorites)
        {
            source = AllTracks.Where(track => track.IsFavorite);
        }
        else if (Section is LibrarySection.Albums or LibrarySection.Playlists or LibrarySection.Tags)
        {
            source = Array.Empty<TrackItem>();
        }

        return SortTracks(source.Where(track => MatchesSearch(track) && MatchesMood(track)));
    }

    private IEnumerable<CollectionItem> FilterCollections(CollectionKind kind)
    {
        return _collectionCache.Where(item => item.Kind == kind && MatchesSearch(item) && MatchesMood(item));
    }

    private bool MatchesSearch(TrackItem track)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return true;
        }

        return ContainsQuery(track.Title) ||
               ContainsQuery(track.Artist) ||
               ContainsQuery(track.Album) ||
               ContainsQuery(track.Record.OriginalFileName) ||
               track.TagToggles.Any(toggle => toggle.IsSelected && ContainsQuery(toggle.Name));
    }

    private bool MatchesSearch(CollectionItem item)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return true;
        }

        return ContainsQuery(item.Title) ||
               ContainsQuery(item.Author) ||
               ContainsQuery(item.Description) ||
               ContainsQuery(item.YearText);
    }

    private bool MatchesMood(TrackItem track)
    {
        var selected = MoodFilters.Where(filter => filter.IsSelected).Select(filter => filter.Visual.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return true;
        }

        return track.TagToggles.Any(toggle => toggle.IsSelected && selected.Contains(toggle.Visual.Id));
    }

    private bool MatchesMood(CollectionItem item)
    {
        var selected = MoodFilters.Where(filter => filter.IsSelected).Select(filter => filter.Visual.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return true;
        }

        var ids = item.Record.TrackIds.ToHashSet(StringComparer.Ordinal);
        return AllTracks.Any(track => ids.Contains(track.Id) && MatchesMood(track));
    }

    private bool ContainsQuery(string value)
    {
        return value.Contains(SearchQuery.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private IEnumerable<TrackItem> SortTracks(IEnumerable<TrackItem> source)
    {
        var ordered = SortIndex switch
        {
            0 => source.OrderBy(track => track.Record.ImportedAt),
            2 => source.OrderBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => source.OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        return SortDescending ? ordered.Reverse() : ordered;
    }

    private IEnumerable<CollectionItem> SortCollections(IEnumerable<CollectionItem> source)
    {
        var ordered = SortIndex switch
        {
            0 => source.OrderBy(item => item.Record.CreatedAt),
            2 => source.OrderBy(item => item.Author, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => source.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        return SortDescending ? ordered.Reverse() : ordered;
    }

    private void RefreshStatus()
    {
        var moodActive = MoodFilters.Any(filter => filter.IsSelected);
        switch (Section)
        {
            case LibrarySection.Favorites:
                StatusText = VisibleTracks.Count == 0
                    ? (moodActive ? "Нет избранных с выбранным настроением" : "Нет избранных песен")
                    : $"{VisibleTracks.Count} в избранном";
                EmptyListText = moodActive ? "Нет избранных с выбранным настроением" : "Нет избранных песен";
                break;
            case LibrarySection.Albums:
                StatusText = Albums.Count == 0
                    ? (moodActive ? "Нет альбомов с выбранным настроением" : "Нет альбомов")
                    : $"{Albums.Count} альбомов";
                EmptyListText = moodActive ? "Нет альбомов с выбранным настроением" : "Нет альбомов";
                break;
            case LibrarySection.Playlists:
                StatusText = Playlists.Count == 0
                    ? (moodActive ? "Нет плейлистов с выбранным настроением" : "Нет плейлистов")
                    : $"{Playlists.Count} плейлистов";
                EmptyListText = moodActive ? "Нет плейлистов с выбранным настроением" : "Нет плейлистов";
                break;
            case LibrarySection.Tags:
                StatusText = CustomTags.Count == 0 ? "Нет своих тегов" : $"{CustomTags.Count} тегов";
                EmptyListText = "Нет своих тегов";
                break;
            default:
                if (AllTracks.Count == 0)
                {
                    StatusText = "Нет аудиофайлов. Нажмите «Импорт», чтобы добавить.";
                    EmptyListText = "Нет аудиофайлов";
                }
                else if (moodActive)
                {
                    StatusText = VisibleTracks.Count == 0
                        ? "Нет треков с выбранным настроением"
                        : $"{VisibleTracks.Count} треков";
                    EmptyListText = "Нет треков с выбранным настроением";
                }
                else
                {
                    StatusText = $"{AllTracks.Count} файлов";
                    EmptyListText = "Нет аудиофайлов";
                }

                break;
        }
    }

    private void PlayTrack(TrackItem track, bool fromCollection = false)
    {
        _queueFromCollection = fromCollection;
        PlayQueued(track, rebuildQueue: true);
    }

    private void PlayQueued(TrackItem track)
    {
        PlayQueued(track, rebuildQueue: false);
    }

    private void PlayQueued(TrackItem track, bool rebuildQueue)
    {
        if (!_playback.IsAvailable)
        {
            return;
        }

        if (rebuildQueue)
        {
            _playQueue.Clear();
            IEnumerable<TrackItem> queue;
            if (_queueFromCollection && CollectionTracks.Count > 0)
            {
                queue = CollectionTracks;
            }
            else if (VisibleTracks.Any(item => item.Id == track.Id))
            {
                queue = VisibleTracks;
            }
            else
            {
                queue = AllTracks;
            }

            _playQueue.AddRange(queue);
            if (_playQueue.All(item => item.Id != track.Id))
            {
                _playQueue.Insert(0, track);
            }
        }

        _settings.LastTrackId = track.Id;
        _settingsService.Save(_settings);
        _suppressSelectionPlay = true;
        SelectedTrack = track;
        _suppressSelectionPlay = false;
        BindNowPlaying(track, playing: true);
        _playback.Volume = (int)Math.Round(Volume);
        _activeTrackId = track.Id;
        _playback.Play(track.FilePath);
        CanControlPlayback = true;
        IsPlaying = true;
        PlayPauseLabel = "Пауза";
        UpdatePlayingFlags();
    }

    private void EnsurePlayQueue()
    {
        if (_playQueue.Count > 0)
        {
            return;
        }

        IEnumerable<TrackItem> queue;
        if (_queueFromCollection && CollectionTracks.Count > 0)
        {
            queue = CollectionTracks;
        }
        else if (VisibleTracks.Count > 0)
        {
            queue = VisibleTracks;
        }
        else
        {
            queue = AllTracks;
        }

        _playQueue.AddRange(queue);
    }

    private int CurrentQueueIndex()
    {
        return _playQueue.FindIndex(track => track.Id == _activeTrackId);
    }

    private void BindNowPlaying(TrackItem track, bool playing)
    {
        NowPlayingTitle = track.Title;
        NowPlayingArtist = track.Artist;
        NowPlayingAlbum = track.Album;
        NowPlayingFormat = string.IsNullOrWhiteSpace(track.Extension) ? "AUDIO" : track.Extension;
        if (!playing)
        {
            NowPlayingTitle = track.Title;
            NowPlayingArtist = "Готово к воспроизведению";
        }

        var previousCover = _cover;
        _cover = track.LoadCover();
        NowPlayingCover = _cover;
        HasCover = _cover is not null;
        previousCover?.Dispose();
        DurationSeconds = Math.Max(1, track.Record.Duration.TotalSeconds);
        RemainingText = FormatTime(track.Record.Duration);
    }

    private void RefreshPlaybackState()
    {
        CanControlPlayback = AllTracks.Count > 0 && _playback.IsAvailable;
        IsPlaying = _playback.IsPlaying;
        PlayPauseLabel = IsPlaying ? "Пауза" : "Старт";
        if (IsPlaying && SelectedTrack is not null)
        {
            NowPlayingArtist = SelectedTrack.Artist;
        }

        UpdatePlayingFlags();
    }

    private void UpdatePlayingFlags()
    {
        foreach (var track in AllTracks)
        {
            track.IsCurrentPlaying = IsPlaying && track.Id == _activeTrackId;
        }
    }

    private void RefreshPosition()
    {
        if (_isSeeking || _updatingPosition)
        {
            return;
        }

        _updatingPosition = true;
        var duration = _playback.DurationMs > 0
            ? TimeSpan.FromMilliseconds(_playback.DurationMs)
            : SelectedTrack?.Record.Duration ?? TimeSpan.Zero;
        var position = TimeSpan.FromMilliseconds(_playback.PositionMs);
        DurationSeconds = Math.Max(1, duration.TotalSeconds);
        PositionSeconds = Math.Clamp(position.TotalSeconds, 0, DurationSeconds);
        ElapsedText = FormatTime(position);
        var remaining = duration - position;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        RemainingText = FormatTime(remaining);
        _updatingPosition = false;
    }

    private void PlayNext()
    {
        if (_playQueue.Count == 0)
        {
            return;
        }

        var index = _playQueue.FindIndex(track => track.Id == _activeTrackId);
        if (index < 0 || index >= _playQueue.Count - 1)
        {
            _playback.Stop();
            _activeTrackId = null;
            PositionSeconds = 0;
            ElapsedText = "0:00";
            IsPlaying = false;
            PlayPauseLabel = "Старт";
            UpdatePlayingFlags();
            return;
        }

        PlayQueued(_playQueue[index + 1]);
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    }
}
