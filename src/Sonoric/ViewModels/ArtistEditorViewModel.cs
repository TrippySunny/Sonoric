using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;
using Sonoric.Services;

namespace Sonoric.ViewModels;

public partial class ArtistEditorViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly List<string> _tags;
    private Bitmap? _preview;
    private bool _clearPhoto;

    public ArtistEditorViewModel(
        IFileDialogService dialogs,
        IEnumerable<CollectionRecord> albums,
        ArtistRecord? existing)
    {
        _dialogs = dialogs;
        ExistingId = existing?.Id ?? Guid.NewGuid().ToString("N");
        IsNew = existing is null;
        Name = existing?.Name ?? string.Empty;
        Description = existing?.Description ?? string.Empty;
        Header = IsNew ? "Новый артист" : "Изменить артиста";
        _tags = (existing?.Tags ?? new List<string>()).ToList();
        var selected = (existing?.AlbumIds ?? new List<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var album in albums.Where(item => item.Kind == CollectionKind.Album))
        {
            Albums.Add(new CollectionPickItem(album, selected.Contains(album.Id)));
        }

        if (existing is not null && !string.IsNullOrWhiteSpace(existing.PhotoFileName))
        {
            var stored = Path.Combine(AppPaths.ArtistPhotosDirectory, existing.PhotoFileName);
            if (File.Exists(stored))
            {
                SetPreview(stored);
            }
        }
    }

    public ObservableCollection<CollectionPickItem> Albums { get; } = new();
    public string ExistingId { get; }
    public bool IsNew { get; }
    public string Header { get; }
    public string? PhotoSourcePath { get; private set; }
    public bool ClearPhoto => _clearPhoto;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private Bitmap? photoPreview;

    [ObservableProperty]
    private bool hasPhotoPreview;

    [ObservableProperty]
    private string? validationMessage;

    [RelayCommand]
    private async Task PickPhotoAsync()
    {
        var path = await _dialogs.PickImageAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        PhotoSourcePath = path;
        _clearPhoto = false;
        SetPreview(path);
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        PhotoSourcePath = null;
        _clearPhoto = true;
        SetPreview(null);
    }

    public bool TryCommit(out ArtistRecord record)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = "Укажите имя артиста";
            record = new ArtistRecord();
            return false;
        }

        ValidationMessage = null;
        record = new ArtistRecord
        {
            Id = ExistingId,
            Name = Name.Trim(),
            Description = Description.Trim(),
            AlbumIds = Albums.Where(item => item.IsSelected).Select(item => item.Id).ToList(),
            Tags = _tags.ToList()
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

        PhotoPreview = _preview;
        HasPhotoPreview = _preview is not null;
    }
}
