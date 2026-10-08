using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;
using Sonoric.Services;

namespace Sonoric.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly PlaybackService _playback;
    private readonly IFileDialogService _dialogs;
    private readonly ICollectionDialogService _collectionDialogs;
    private readonly ISettingsDialogService _settingsDialogs;
    private readonly UpdateService _updates = new();
    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;
    private readonly LocalSyncHost _syncHost;
    private readonly LocalSyncClient _syncClient;
    private readonly List<TrackItem> _playQueue = new();
    private readonly List<CollectionItem> _collectionCache = new();
    private readonly List<ArtistItem> _artistCache = new();
    private readonly List<FolderItem> _folderCache = new();
    private bool _suppressSelectionPlay;
    private bool _isSeeking;
    private bool _updatingPosition;
    private Bitmap? _cover;
    private string? _activeTrackId;
    private bool _queueFromCollection;

    public MainViewModel(
        LibraryService library,
        PlaybackService playback,
        IFileDialogService dialogs,
        ICollectionDialogService collectionDialogs,
        ISettingsDialogService settingsDialogs)
    {
        _library = library;
        _playback = playback;
        _dialogs = dialogs;
        _collectionDialogs = collectionDialogs;
        _settingsDialogs = settingsDialogs;
        _settings = _settingsService.Load();
        _syncHost = new LocalSyncHost(_settings, _settingsService, library);
        _syncClient = new LocalSyncClient(_settings, _settingsService);
        AllTracks = new ObservableCollection<TrackItem>();
        VisibleTracks = new ObservableCollection<TrackItem>();
        CollectionTracks = new ObservableCollection<TrackItem>();
        VisibleCollections = new ObservableCollection<CollectionItem>();
        VisibleArtists = new ObservableCollection<ArtistItem>();
        VisibleFolders = new ObservableCollection<FolderItem>();
        ArtistAlbums = new ObservableCollection<CollectionItem>();
        FolderCollections = new ObservableCollection<CollectionItem>();
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
    public ObservableCollection<CollectionItem> VisibleCollections { get; }
    public ObservableCollection<ArtistItem> VisibleArtists { get; }
    public ObservableCollection<FolderItem> VisibleFolders { get; }
    public ObservableCollection<CollectionItem> ArtistAlbums { get; }
    public ObservableCollection<CollectionItem> FolderCollections { get; }
    public ObservableCollection<MoodFilterChip> MoodFilters { get; }
    public Action? RequestShutdown { get; set; }

    [ObservableProperty]
    private TrackItem? selectedTrack;

    [ObservableProperty]
    private TrackItem? selectedCollectionTrack;

    [ObservableProperty]
    private bool isCollectionTracksEmpty;

    [ObservableProperty]
    private bool isArtistAlbumsEmpty;

    [ObservableProperty]
    private bool isFolderCollectionsEmpty;

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
    private LibraryPage page = LibraryPage.Home;

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
    private bool openedCollectionIsAlbum;

    [ObservableProperty]
    private string openedCollectionCountText = string.Empty;

    [ObservableProperty]
    private Bitmap? openedCollectionCover;

    [ObservableProperty]
    private bool openedCollectionHasCover;

    [ObservableProperty]
    private IBrush collectionPaneBackground = CoverAccentService.DefaultBackdrop;

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
    private bool isTrackListVisible;

    [ObservableProperty]
    private bool isHomeVisible = true;

    [ObservableProperty]
    private bool isCollectionGridVisible;

    [ObservableProperty]
    private bool isArtistGridVisible;

    [ObservableProperty]
    private bool isFolderGridVisible;

    [ObservableProperty]
    private bool isArtistPageVisible;

    [ObservableProperty]
    private bool isFolderPageVisible;

    [ObservableProperty]
    private bool isBackVisible;

    [ObservableProperty]
    private bool isFilterBarVisible;

    [ObservableProperty]
    private bool isFavoritesFilterVisible;

    [ObservableProperty]
    private bool isTypeFilterVisible;

    [ObservableProperty]
    private bool isCreateAlbumVisible;

    [ObservableProperty]
    private bool isCreatePlaylistVisible;

    [ObservableProperty]
    private bool isCreateArtistVisible;

    [ObservableProperty]
    private bool isCreateFolderVisible;

    [ObservableProperty]
    private bool isEditOpenedArtistVisible;

    [ObservableProperty]
    private bool isEditOpenedFolderVisible;

    [ObservableProperty]
    private bool favoritesOnly;

    [ObservableProperty]
    private string pageTitle = "Библиотека";

    [ObservableProperty]
    private string songsHubCount = "0 песен";

    [ObservableProperty]
    private string collectionsHubCount = "0 наборов";

    [ObservableProperty]
    private string artistsHubCount = "0 артистов";

    [ObservableProperty]
    private string foldersHubCount = "0 папок";

    [ObservableProperty]
    private ArtistRecord? openedArtist;

    [ObservableProperty]
    private string openedArtistName = string.Empty;

    [ObservableProperty]
    private string openedArtistDescription = string.Empty;

    [ObservableProperty]
    private bool hasOpenedArtistDescription;

    [ObservableProperty]
    private Bitmap? openedArtistPhoto;

    [ObservableProperty]
    private bool openedArtistHasPhoto;

    [ObservableProperty]
    private string openedArtistCountText = string.Empty;

    [ObservableProperty]
    private FolderRecord? openedFolder;

    [ObservableProperty]
    private string openedFolderTitle = string.Empty;

    [ObservableProperty]
    private string openedFolderDescription = string.Empty;

    [ObservableProperty]
    private bool hasOpenedFolderDescription;

    [ObservableProperty]
    private Bitmap? openedFolderCover;

    [ObservableProperty]
    private bool openedFolderHasCover;

    [ObservableProperty]
    private string openedFolderCountText = string.Empty;

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
    private bool hasCustomTags;

    public IReadOnlyList<string> TypeFilterOptions { get; } =
    [
        "Все",
        "Альбомы",
        "Плейлисты"
    ];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "По дате добавления",
        "По алфавиту",
        "По автору"
    ];

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

    partial void OnFavoritesOnlyChanged(bool value)
    {
        RefreshSection();
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortDescending = !SortDescending;
    }

    [RelayCommand]
    private void ToggleFavoritesFilter()
    {
        FavoritesOnly = !FavoritesOnly;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (IsCollectionOpen && Page is LibraryPage.Artist or LibraryPage.Folder)
        {
            CloseCollection();
            return;
        }

        switch (Page)
        {
            case LibraryPage.Artist:
                OpenArtists();
                break;
            case LibraryPage.Folder:
                OpenFolders();
                break;
            default:
                OpenHome();
                break;
        }
    }

    [RelayCommand]
    private void OpenHome()
    {
        OpenedArtist = null;
        OpenedArtistPhoto = null;
        OpenedArtistHasPhoto = false;
        OpenedFolder = null;
        OpenedFolderCover = null;
        OpenedFolderHasCover = false;
        Page = LibraryPage.Home;
        RefreshSection();
    }

    [RelayCommand]
    private void OpenSongs()
    {
        Page = LibraryPage.Songs;
        RefreshSection();
    }

    [RelayCommand]
    private void OpenCollections()
    {
        Page = LibraryPage.Collections;
        RefreshSection();
    }

    [RelayCommand]
    private void OpenArtists()
    {
        OpenedArtist = null;
        OpenedArtistPhoto = null;
        OpenedArtistHasPhoto = false;
        Page = LibraryPage.Artists;
        RefreshSection();
    }

    [RelayCommand]
    private void OpenFolders()
    {
        OpenedFolder = null;
        OpenedFolderCover = null;
        OpenedFolderHasCover = false;
        Page = LibraryPage.Folders;
        RefreshSection();
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
        CollectionPaneBackground = CoverAccentService.DefaultBackdrop;
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
    private async Task ImportOpenedCollectionAsync()
    {
        if (OpenedCollection is null)
        {
            return;
        }

        await ImportIntoCollectionAsync(OpenedCollection.Id);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var settings = new SettingsViewModel(
            _updates,
            _library,
            _syncHost,
            _syncClient,
            _settings.DisplayName,
            ApplyDisplayName,
            OnLibrarySynced,
            PersistState,
            () => RequestShutdown?.Invoke());
        await _settingsDialogs.ShowAsync(settings);
        settings.CommitProfile();
        RebuildTagCatalog();
        ReloadArtists();
        ReloadFolders();
        RefreshSection();
    }

    public async Task EnsureDisplayNameAsync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DisplayName))
        {
            return;
        }

        var prompt = new NamePromptViewModel();
        if (!await _settingsDialogs.PromptDisplayNameAsync(prompt))
        {
            return;
        }

        ApplyDisplayName(prompt.Name);
    }

    private void ApplyDisplayName(string name)
    {
        _settings.DisplayName = name.Trim();
        _settingsService.Save(_settings);
        _library.SetPlaylistAuthors(_settings.DisplayName);
        ReloadCollections();
        ReloadArtists();
        ReloadFolders();
        RefreshSection();
    }

    [RelayCommand]
    private async Task OpenStatisticsAsync()
    {
        await _settingsDialogs.ShowStatisticsAsync(new StatisticsViewModel(_library));
    }

    [RelayCommand]
    private async Task CreateAlbumAsync()
    {
        await EditCollectionAsync(null, CollectionKind.Album);
    }

    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        await EditCollectionAsync(null, CollectionKind.Playlist);
    }

    [RelayCommand]
    private async Task CreateArtistAsync()
    {
        await EditArtistAsync(null);
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        await EditFolderAsync(null);
    }

    [RelayCommand]
    private async Task EditOpenedArtistAsync()
    {
        if (OpenedArtist is null)
        {
            return;
        }

        await EditArtistAsync(OpenedArtist);
    }

    [RelayCommand]
    private async Task EditOpenedFolderAsync()
    {
        if (OpenedFolder is null)
        {
            return;
        }

        await EditFolderAsync(OpenedFolder);
    }

    [RelayCommand]
    private void OpenOpenedArtistPhoto()
    {
        if (OpenedArtistPhoto is null || !OpenedArtistHasPhoto)
        {
            return;
        }

        ShowCoverViewer(OpenedArtistPhoto);
    }

    [RelayCommand]
    private void OpenOpenedFolderCover()
    {
        if (OpenedFolderCover is null || !OpenedFolderHasCover)
        {
            return;
        }

        ShowCoverViewer(OpenedFolderCover);
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

    public async Task ImportPathsIntoCollectionAsync(string collectionId, IEnumerable<string> paths)
    {
        var (saved, added) = await Task.Run(() => _library.AddTracksToCollection(collectionId, paths));
        if (saved is null)
        {
            return;
        }

        ReloadLibrary(_settings.LastTrackId);
        var opened = _collectionCache.FirstOrDefault(item => item.Id == saved.Id);
        if (opened is not null)
        {
            OpenCollection(opened);
        }

        StatusText = added == 0
            ? "Нет новых треков для набора"
            : added == 1 ? "В набор добавлен 1 трек" : $"В набор добавлено {added} треков";
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

    public void StopSync()
    {
        _ = _syncHost.StopAsync();
    }

    private void OnLibrarySynced()
    {
        ReloadLibrary(SelectedTrack?.Id ?? _settings.LastTrackId);
    }

    private async Task EditCollectionAsync(CollectionRecord? existing, CollectionKind defaultKind)
    {
        var editor = new CollectionEditorViewModel(_dialogs, _library, AllTracks, defaultKind, existing, _settings.DisplayName);
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
        ReloadArtists();
        ReloadFolders();
        RefreshSection();
    }

    private async Task EditArtistAsync(ArtistRecord? existing)
    {
        var editor = new ArtistEditorViewModel(_dialogs, _library.Collections, existing);
        if (!await _collectionDialogs.EditArtistAsync(editor))
        {
            return;
        }

        if (!editor.TryCommit(out var record))
        {
            return;
        }

        var saved = _library.UpsertArtist(record, editor.PhotoSourcePath, editor.ClearPhoto);
        ReloadArtists();
        var opened = _artistCache.FirstOrDefault(item => item.Id == saved.Id);
        if (opened is not null && (Page == LibraryPage.Artist || existing is null))
        {
            OpenArtist(opened);
            return;
        }

        RefreshSection();
    }

    private async Task EditFolderAsync(FolderRecord? existing)
    {
        var editor = new FolderEditorViewModel(_dialogs, _library.Collections, existing);
        if (!await _collectionDialogs.EditFolderAsync(editor))
        {
            return;
        }

        if (!editor.TryCommit(out var record))
        {
            return;
        }

        var saved = _library.UpsertFolder(record, editor.CoverSourcePath, editor.ClearCover);
        ReloadFolders();
        var opened = _folderCache.FirstOrDefault(item => item.Id == saved.Id);
        if (opened is not null && (Page == LibraryPage.Folder || existing is null))
        {
            OpenFolder(opened);
            return;
        }

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
        ReloadArtists();
        ReloadFolders();
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
                ImportRequested = OnImportCollection,
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
            CollectionPaneBackground = CoverAccentService.DefaultBackdrop;
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
        OpenedCollectionIsAlbum = item.IsAlbum;
        OpenedCollectionCountText = item.CountText;
        OpenedCollectionCover = item.Cover;
        OpenedCollectionHasCover = item.HasCover;
        if (item.IsAlbum)
        {
            var accent = CoverAccentService.Resolve(item.CoverPath, item.Record.AccentMode, item.Record.CustomAccentHex);
            CollectionPaneBackground = CoverAccentService.CreateBackdrop(accent);
        }
        else
        {
            CollectionPaneBackground = CoverAccentService.DefaultBackdrop;
        }
    }

    private void ReloadArtists()
    {
        var openedId = OpenedArtist?.Id;
        foreach (var item in _artistCache)
        {
            item.DisposePhoto();
        }

        _artistCache.Clear();
        var visuals = _library.Tags.Select(record => new MoodVisual(record)).ToList();
        foreach (var record in _library.Artists)
        {
            var item = new ArtistItem(record, _library.GetArtistPhotoPath(record))
            {
                OpenRequested = OpenArtist,
                EditRequested = OnEditArtist,
                DeleteRequested = OnDeleteArtist,
                TagsChanged = OnArtistTagsChanged
            };
            item.SyncCatalog(visuals);
            _artistCache.Add(item);
        }

        if (openedId is null)
        {
            return;
        }

        var opened = _artistCache.FirstOrDefault(item => item.Id == openedId);
        if (opened is null)
        {
            OpenedArtist = null;
            OpenedArtistPhoto = null;
            OpenedArtistHasPhoto = false;
            if (Page == LibraryPage.Artist)
            {
                Page = LibraryPage.Artists;
            }

            return;
        }

        BindOpenedArtist(opened);
    }

    private void ReloadFolders()
    {
        var openedId = OpenedFolder?.Id;
        foreach (var item in _folderCache)
        {
            item.DisposeCover();
        }

        _folderCache.Clear();
        var visuals = _library.Tags.Select(record => new MoodVisual(record)).ToList();
        foreach (var record in _library.Folders)
        {
            var item = new FolderItem(record, _library.GetFolderCoverPath(record))
            {
                OpenRequested = OpenFolder,
                EditRequested = OnEditFolder,
                DeleteRequested = OnDeleteFolder,
                TagsChanged = OnFolderTagsChanged
            };
            item.SyncCatalog(visuals);
            _folderCache.Add(item);
        }

        if (openedId is null)
        {
            return;
        }

        var opened = _folderCache.FirstOrDefault(item => item.Id == openedId);
        if (opened is null)
        {
            OpenedFolder = null;
            OpenedFolderCover = null;
            OpenedFolderHasCover = false;
            if (Page == LibraryPage.Folder)
            {
                Page = LibraryPage.Folders;
            }

            return;
        }

        BindOpenedFolder(opened);
    }

    private void OpenArtist(ArtistItem item)
    {
        OpenedArtist = item.Record;
        BindOpenedArtist(item);
        Page = LibraryPage.Artist;
        RefreshSection();
    }

    private void BindOpenedArtist(ArtistItem item)
    {
        OpenedArtist = item.Record;
        OpenedArtistName = item.Name;
        OpenedArtistDescription = item.Description;
        HasOpenedArtistDescription = item.HasDescription;
        OpenedArtistPhoto = item.Photo;
        OpenedArtistHasPhoto = item.HasPhoto;
        OpenedArtistCountText = item.CountText;
    }

    private void OpenFolder(FolderItem item)
    {
        OpenedFolder = item.Record;
        BindOpenedFolder(item);
        Page = LibraryPage.Folder;
        RefreshSection();
    }

    private void BindOpenedFolder(FolderItem item)
    {
        OpenedFolder = item.Record;
        OpenedFolderTitle = item.Title;
        OpenedFolderDescription = item.Description;
        HasOpenedFolderDescription = item.HasDescription;
        OpenedFolderCover = item.Cover;
        OpenedFolderHasCover = item.HasCover;
        OpenedFolderCountText = item.CountText;
    }

    private void OnEditArtist(ArtistItem item)
    {
        _ = EditArtistAsync(item.Record);
    }

    private void OnDeleteArtist(ArtistItem item)
    {
        if (OpenedArtist?.Id == item.Id)
        {
            OpenedArtist = null;
            OpenedArtistPhoto = null;
            OpenedArtistHasPhoto = false;
            if (Page == LibraryPage.Artist)
            {
                Page = LibraryPage.Artists;
            }
        }

        _library.DeleteArtist(item.Id);
        ReloadArtists();
        RefreshSection();
    }

    private void OnArtistTagsChanged(ArtistItem item)
    {
        _library.SetArtistTags(item.Id, item.Record.Tags);
        if (MoodFilters.Any(filter => filter.IsSelected))
        {
            RefreshSection();
        }
    }

    private void OnEditFolder(FolderItem item)
    {
        _ = EditFolderAsync(item.Record);
    }

    private void OnDeleteFolder(FolderItem item)
    {
        if (OpenedFolder?.Id == item.Id)
        {
            OpenedFolder = null;
            OpenedFolderCover = null;
            OpenedFolderHasCover = false;
            if (Page == LibraryPage.Folder)
            {
                Page = LibraryPage.Folders;
            }
        }

        _library.DeleteFolder(item.Id);
        ReloadFolders();
        RefreshSection();
    }

    private void OnFolderTagsChanged(FolderItem item)
    {
        _library.SetFolderTags(item.Id, item.Record.Tags);
        if (MoodFilters.Any(filter => filter.IsSelected))
        {
            RefreshSection();
        }
    }

    private void OnImportCollection(CollectionItem item)
    {
        _ = ImportIntoCollectionAsync(item.Id);
    }

    private async Task ImportIntoCollectionAsync(string collectionId)
    {
        var paths = await _dialogs.PickAudioFilesAsync();
        await ImportPathsIntoCollectionAsync(collectionId, paths);
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
            CollectionPaneBackground = CoverAccentService.DefaultBackdrop;
        }

        _library.DeleteCollection(item.Id);
        ReloadCollections();
        ReloadArtists();
        ReloadFolders();
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
        if (FavoritesOnly)
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

        HasCustomTags = MoodFilters.Count > 0;
        foreach (var track in AllTracks)
        {
            track.SyncCatalog(visuals);
        }

        foreach (var artist in _artistCache)
        {
            artist.SyncCatalog(visuals);
        }

        foreach (var folder in _folderCache)
        {
            folder.SyncCatalog(visuals);
        }

        var count = MoodFilters.Count(filter => filter.IsSelected);
        MoodFilterLabel = count == 0 ? "Настроение" : $"Настроение · {count}";
    }

    private void RefreshSection()
    {
        IsCollectionOpen = OpenedCollection is not null;
        IsHomeVisible = Page == LibraryPage.Home;
        IsTrackListVisible = Page == LibraryPage.Songs;
        IsCollectionGridVisible = Page == LibraryPage.Collections;
        IsArtistGridVisible = Page == LibraryPage.Artists;
        IsFolderGridVisible = Page == LibraryPage.Folders;
        IsArtistPageVisible = Page == LibraryPage.Artist;
        IsFolderPageVisible = Page == LibraryPage.Folder;
        IsBackVisible = Page != LibraryPage.Home;
        IsFilterBarVisible = Page is not LibraryPage.Home;
        IsFavoritesFilterVisible = Page == LibraryPage.Songs;
        IsTypeFilterVisible = Page == LibraryPage.Collections;
        IsCreateAlbumVisible = Page == LibraryPage.Collections;
        IsCreatePlaylistVisible = Page == LibraryPage.Collections;
        IsCreateArtistVisible = Page == LibraryPage.Artists;
        IsCreateFolderVisible = Page == LibraryPage.Folders;
        IsEditOpenedArtistVisible = Page == LibraryPage.Artist;
        IsEditOpenedFolderVisible = Page == LibraryPage.Folder;
        PageTitle = Page switch
        {
            LibraryPage.Songs => "Все песни",
            LibraryPage.Collections => "Альбомы и плейлисты",
            LibraryPage.Artists => "Артисты",
            LibraryPage.Artist => OpenedArtistName,
            LibraryPage.Folders => "Папки",
            LibraryPage.Folder => OpenedFolderTitle,
            _ => "Библиотека"
        };

        SongsHubCount = FormatCount(AllTracks.Count, "песня", "песни", "песен");
        CollectionsHubCount = FormatCount(_collectionCache.Count, "набор", "набора", "наборов");
        ArtistsHubCount = FormatCount(_artistCache.Count, "артист", "артиста", "артистов");
        FoldersHubCount = FormatCount(_folderCache.Count, "папка", "папки", "папок");

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

        VisibleCollections.Clear();
        foreach (var collection in SortCollections(FilterCollections()))
        {
            VisibleCollections.Add(collection);
        }

        VisibleArtists.Clear();
        foreach (var artist in SortArtists(_artistCache.Where(item => MatchesSearch(item) && MatchesMood(item))))
        {
            VisibleArtists.Add(artist);
        }

        VisibleFolders.Clear();
        foreach (var folder in SortFolders(_folderCache.Where(item => MatchesSearch(item) && MatchesMood(item))))
        {
            VisibleFolders.Add(folder);
        }

        ArtistAlbums.Clear();
        if (OpenedArtist is not null)
        {
            var map = _collectionCache.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var id in OpenedArtist.AlbumIds)
            {
                if (map.TryGetValue(id, out var album) && MatchesSearch(album) && MatchesMood(album))
                {
                    ArtistAlbums.Add(album);
                }
            }

            OpenedArtistCountText = FormatCount(ArtistAlbums.Count, "альбом", "альбома", "альбомов");
        }

        IsArtistAlbumsEmpty = Page == LibraryPage.Artist && ArtistAlbums.Count == 0;

        FolderCollections.Clear();
        if (OpenedFolder is not null)
        {
            var map = _collectionCache.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var id in OpenedFolder.CollectionIds)
            {
                if (map.TryGetValue(id, out var collection) && MatchesSearch(collection) && MatchesMood(collection))
                {
                    FolderCollections.Add(collection);
                }
            }

            OpenedFolderCountText = FormatCount(FolderCollections.Count, "набор", "набора", "наборов");
        }

        IsFolderCollectionsEmpty = Page == LibraryPage.Folder && FolderCollections.Count == 0;

        IsTrackListEmpty = Page switch
        {
            LibraryPage.Songs => VisibleTracks.Count == 0,
            LibraryPage.Collections => VisibleCollections.Count == 0,
            LibraryPage.Artists => VisibleArtists.Count == 0,
            LibraryPage.Folders => VisibleFolders.Count == 0,
            _ => false
        };

        RefreshStatus();
    }

    private IEnumerable<TrackItem> GetVisibleTrackSource()
    {
        if (Page != LibraryPage.Songs)
        {
            return Array.Empty<TrackItem>();
        }

        IEnumerable<TrackItem> source = AllTracks;
        if (FavoritesOnly)
        {
            source = AllTracks.Where(track => track.IsFavorite);
        }

        return SortTracks(source.Where(track => MatchesSearch(track) && MatchesMood(track)));
    }

    private IEnumerable<CollectionItem> FilterCollections()
    {
        return _collectionCache.Where(item =>
            (TypeFilterIndex == 0 ||
             (TypeFilterIndex == 1 && item.Kind == CollectionKind.Album) ||
             (TypeFilterIndex == 2 && item.Kind == CollectionKind.Playlist)) &&
            MatchesSearch(item) &&
            MatchesMood(item));
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

    private bool MatchesSearch(ArtistItem item)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return true;
        }

        return ContainsQuery(item.Name) ||
               ContainsQuery(item.Description) ||
               item.TagToggles.Any(toggle => toggle.IsSelected && ContainsQuery(toggle.Name));
    }

    private bool MatchesSearch(FolderItem item)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return true;
        }

        return ContainsQuery(item.Title) ||
               ContainsQuery(item.Description) ||
               item.TagToggles.Any(toggle => toggle.IsSelected && ContainsQuery(toggle.Name));
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

    private bool MatchesMood(ArtistItem item)
    {
        var selected = MoodFilters.Where(filter => filter.IsSelected).Select(filter => filter.Visual.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return true;
        }

        return item.Record.Tags.Any(selected.Contains);
    }

    private bool MatchesMood(FolderItem item)
    {
        var selected = MoodFilters.Where(filter => filter.IsSelected).Select(filter => filter.Visual.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return true;
        }

        return item.Record.Tags.Any(selected.Contains);
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

    private IEnumerable<ArtistItem> SortArtists(IEnumerable<ArtistItem> source)
    {
        var ordered = SortIndex switch
        {
            0 => source.OrderBy(item => item.Record.CreatedAt),
            _ => source.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        return SortDescending ? ordered.Reverse() : ordered;
    }

    private IEnumerable<FolderItem> SortFolders(IEnumerable<FolderItem> source)
    {
        var ordered = SortIndex switch
        {
            0 => source.OrderBy(item => item.Record.CreatedAt),
            _ => source.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        return SortDescending ? ordered.Reverse() : ordered;
    }

    private static string FormatCount(int count, string one, string few, string many)
    {
        var n = Math.Abs(count) % 100;
        var n1 = n % 10;
        var word = n is > 10 and < 20 ? many : n1 == 1 ? one : n1 is >= 2 and <= 4 ? few : many;
        return $"{count} {word}";
    }

    private void RefreshStatus()
    {
        var moodActive = MoodFilters.Any(filter => filter.IsSelected);
        switch (Page)
        {
            case LibraryPage.Home:
                StatusText = "Выберите раздел";
                EmptyListText = string.Empty;
                break;
            case LibraryPage.Songs:
                if (AllTracks.Count == 0)
                {
                    StatusText = "Нет аудиофайлов. Нажмите «Импорт», чтобы добавить.";
                    EmptyListText = "Нет аудиофайлов";
                }
                else if (FavoritesOnly)
                {
                    StatusText = VisibleTracks.Count == 0
                        ? (moodActive ? "Нет избранных с выбранным настроением" : "Нет избранных песен")
                        : $"{VisibleTracks.Count} в избранном";
                    EmptyListText = moodActive ? "Нет избранных с выбранным настроением" : "Нет избранных песен";
                }
                else
                {
                    StatusText = VisibleTracks.Count == 0
                        ? (moodActive ? "Нет треков с выбранным настроением" : "Нет аудиофайлов")
                        : FormatCount(VisibleTracks.Count, "песня", "песни", "песен");
                    EmptyListText = moodActive ? "Нет треков с выбранным настроением" : "Нет аудиофайлов";
                }

                break;
            case LibraryPage.Collections:
                StatusText = VisibleCollections.Count == 0
                    ? (moodActive ? "Нет наборов с выбранным настроением" : "Нет альбомов и плейлистов")
                    : FormatCount(VisibleCollections.Count, "набор", "набора", "наборов");
                EmptyListText = moodActive ? "Нет наборов с выбранным настроением" : "Нет альбомов и плейлистов";
                break;
            case LibraryPage.Artists:
                StatusText = VisibleArtists.Count == 0
                    ? (moodActive ? "Нет артистов с выбранным настроением" : "Нет артистов")
                    : FormatCount(VisibleArtists.Count, "артист", "артиста", "артистов");
                EmptyListText = moodActive ? "Нет артистов с выбранным настроением" : "Нет артистов";
                break;
            case LibraryPage.Artist:
                StatusText = ArtistAlbums.Count == 0
                    ? "Нет альбомов у артиста"
                    : FormatCount(ArtistAlbums.Count, "альбом", "альбома", "альбомов");
                EmptyListText = "Нет альбомов у артиста";
                break;
            case LibraryPage.Folders:
                StatusText = VisibleFolders.Count == 0
                    ? (moodActive ? "Нет папок с выбранным настроением" : "Нет папок")
                    : FormatCount(VisibleFolders.Count, "папка", "папки", "папок");
                EmptyListText = moodActive ? "Нет папок с выбранным настроением" : "Нет папок";
                break;
            case LibraryPage.Folder:
                StatusText = FolderCollections.Count == 0
                    ? "В папке пусто"
                    : FormatCount(FolderCollections.Count, "набор", "набора", "наборов");
                EmptyListText = "В папке пусто";
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
