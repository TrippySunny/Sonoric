using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;
using Sonoric.Services;

namespace Sonoric.ViewModels;

public partial class FolderEditorViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly List<string> _tags;
    private Bitmap? _preview;
    private bool _clearCover;

    public FolderEditorViewModel(
        IFileDialogService dialogs,
        IEnumerable<CollectionRecord> collections,
        FolderRecord? existing)
    {
        _dialogs = dialogs;
        ExistingId = existing?.Id ?? Guid.NewGuid().ToString("N");
        IsNew = existing is null;
        TitleText = existing?.Title ?? string.Empty;
        Description = existing?.Description ?? string.Empty;
        Header = IsNew ? "Новая папка" : "Изменить папку";
        _tags = (existing?.Tags ?? new List<string>()).ToList();
        var selected = (existing?.CollectionIds ?? new List<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var collection in collections)
        {
            Items.Add(new CollectionPickItem(collection, selected.Contains(collection.Id)));
        }

        if (existing is not null && !string.IsNullOrWhiteSpace(existing.CoverFileName))
        {
            var stored = Path.Combine(AppPaths.FolderCoversDirectory, existing.CoverFileName);
            if (File.Exists(stored))
            {
                SetPreview(stored);
            }
        }
    }

    public ObservableCollection<CollectionPickItem> Items { get; } = new();
    public string ExistingId { get; }
    public bool IsNew { get; }
    public string Header { get; }
    public string? CoverSourcePath { get; private set; }
    public bool ClearCover => _clearCover;

    [ObservableProperty]
    private string titleText = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private Bitmap? coverPreview;

    [ObservableProperty]
    private bool hasCoverPreview;

    [ObservableProperty]
    private string? validationMessage;

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

    public bool TryCommit(out FolderRecord record)
    {
        if (string.IsNullOrWhiteSpace(TitleText))
        {
            ValidationMessage = "Укажите название папки";
            record = new FolderRecord();
            return false;
        }

        ValidationMessage = null;
        record = new FolderRecord
        {
            Id = ExistingId,
            Title = TitleText.Trim(),
            Description = Description.Trim(),
            CollectionIds = Items.Where(item => item.IsSelected).Select(item => item.Id).ToList(),
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

        CoverPreview = _preview;
        HasCoverPreview = _preview is not null;
    }
}
