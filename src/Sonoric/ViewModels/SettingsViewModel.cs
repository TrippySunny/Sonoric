using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;
using Sonoric.Services;

namespace Sonoric.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly UpdateService _updates;
    private readonly LibraryService _library;
    private readonly Action<string> _applyDisplayName;
    private readonly Action _persist;
    private readonly Action _shutdown;
    private UpdateInfo? _pending;
    private string? _payloadDirectory;
    private bool _updatingTagColor;
    private string? _editingTagId;

    public SettingsViewModel(UpdateService updates, LibraryService library, string displayName, Action<string> applyDisplayName, Action persist, Action shutdown)
    {
        _updates = updates;
        _library = library;
        _applyDisplayName = applyDisplayName;
        _persist = persist;
        _shutdown = shutdown;
        CustomTags = new ObservableCollection<TagListItem>();
        DisplayName = displayName;
        VersionText = AppRuntime.VersionText;
        PlatformText = AppRuntime.Rid;
        StatusText = "Проверка не выполнялась";
        ResetTagEditor();
        ReloadTags();
    }

    public string VersionText { get; }
    public string PlatformText { get; }
    public ObservableCollection<TagListItem> CustomTags { get; }

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isDownloading;

    [ObservableProperty]
    private double downloadPercent;

    [ObservableProperty]
    private bool canInstall;

    [ObservableProperty]
    private string installLabel = "Обновить";

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
    private async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        CanInstall = false;
        _pending = null;
        StatusText = "Проверка…";
        try
        {
            var result = await _updates.CheckAsync();
            _pending = result.Update;
            CanInstall = result.Update is not null;
            StatusText = result.Update is null
                ? result.Message
                : $"{result.Message} ({result.Update.AssetName}). Обновиться?";
        }
        catch
        {
            StatusText = "Не удалось проверить обновления";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_pending is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsDownloading = true;
        DownloadPercent = 0;
        StatusText = "Загрузка…";
        try
        {
            var progress = new Progress<double>(value =>
            {
                Dispatcher.UIThread.Post(() => DownloadPercent = value);
            });
            _payloadDirectory = await _updates.DownloadAsync(_pending, progress);
            StatusText = "Перезапуск…";
            CommitProfile();
            _persist();
            _updates.LaunchInstaller(_payloadDirectory);
            _shutdown();
        }
        catch
        {
            StatusText = "Не удалось скачать или установить обновление";
            IsBusy = false;
            IsDownloading = false;
        }
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
        ReloadTags();
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
        ReloadTags();
    }

    private void ReloadTags()
    {
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

    public void CommitProfile()
    {
        _applyDisplayName(DisplayName);
    }
}
