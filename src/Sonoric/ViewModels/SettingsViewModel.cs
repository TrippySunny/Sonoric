using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private readonly LocalSyncHost _syncHost;
    private readonly LocalSyncClient _syncClient;
    private readonly Action<string> _applyDisplayName;
    private readonly Action _libraryChanged;
    private readonly Action _persist;
    private readonly Action _shutdown;
    private UpdateInfo? _pending;
    private string? _payloadDirectory;
    private bool _updatingTagColor;
    private string? _editingTagId;

    public SettingsViewModel(
        UpdateService updates,
        LibraryService library,
        LocalSyncHost syncHost,
        LocalSyncClient syncClient,
        string displayName,
        Action<string> applyDisplayName,
        Action libraryChanged,
        Action persist,
        Action shutdown)
    {
        _updates = updates;
        _library = library;
        _syncHost = syncHost;
        _syncClient = syncClient;
        _applyDisplayName = applyDisplayName;
        _libraryChanged = libraryChanged;
        _persist = persist;
        _shutdown = shutdown;
        CustomTags = new ObservableCollection<TagListItem>();
        DiscoveredDevices = new ObservableCollection<SyncDeviceItem>();
        DisplayName = displayName;
        VersionText = AppRuntime.VersionText;
        PlatformText = AppRuntime.Rid;
        StatusText = "Проверка не выполнялась";
        ResetTagEditor();
        ReloadTags();
        RefreshBroadcastUi();
    }

    public string VersionText { get; }
    public string PlatformText { get; }
    public ObservableCollection<TagListItem> CustomTags { get; }
    public ObservableCollection<SyncDeviceItem> DiscoveredDevices { get; }

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

    [ObservableProperty]
    private bool isAdvertising;

    [ObservableProperty]
    private string syncToggleLabel = "Включить синхронизацию";

    [ObservableProperty]
    private string pinText = string.Empty;

    [ObservableProperty]
    private Bitmap? qrImage;

    [ObservableProperty]
    private bool hasQrImage;

    [ObservableProperty]
    private string pairingInput = string.Empty;

    [ObservableProperty]
    private bool isSyncBusy;

    [ObservableProperty]
    private string syncStatusText = "Синхронизация выключена";

    [ObservableProperty]
    private double syncProgress;

    [ObservableProperty]
    private bool isSyncTransferring;

    [ObservableProperty]
    private bool hasDiscoveredDevices;

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

    [RelayCommand]
    private async Task ToggleSyncAsync()
    {
        if (IsSyncBusy)
        {
            return;
        }

        IsSyncBusy = true;
        try
        {
            if (_syncHost.IsAdvertising)
            {
                await _syncHost.StopAsync();
                SyncStatusText = "Синхронизация выключена";
            }
            else
            {
                await _syncHost.StartAsync(DisplayName);
                SyncStatusText = $"Виден в локальной сети как «{(string.IsNullOrWhiteSpace(DisplayName) ? "Sonoric" : DisplayName.Trim())}»";
            }

            RefreshBroadcastUi();
        }
        catch (Exception exception)
        {
            SyncStatusText = exception.Message;
        }
        finally
        {
            IsSyncBusy = false;
        }
    }

    [RelayCommand]
    private async Task ScanNetworkAsync()
    {
        if (IsSyncBusy)
        {
            return;
        }

        IsSyncBusy = true;
        try
        {
            await ScanCoreAsync();
        }
        finally
        {
            IsSyncBusy = false;
        }
    }

    private async Task ScanCoreAsync()
    {
        SyncStatusText = "Поиск компьютеров в локальной сети…";
        var hosts = await _syncClient.ScanAsync();
        DiscoveredDevices.Clear();
        foreach (var host in hosts)
        {
            var item = new SyncDeviceItem(host, _syncClient.IsTrusted(host.Uuid))
            {
                PullRequested = device => _ = PullFromAsync(device)
            };
            DiscoveredDevices.Add(item);
        }

        HasDiscoveredDevices = DiscoveredDevices.Count > 0;
        SyncStatusText = HasDiscoveredDevices
            ? $"Найдено: {DiscoveredDevices.Count}"
            : "В сети нет других компьютеров с включённой синхронизацией";
    }

    private async Task PullFromAsync(SyncDeviceItem item)
    {
        if (IsSyncBusy)
        {
            return;
        }

        IsSyncBusy = true;
        IsSyncTransferring = true;
        SyncProgress = 0;
        try
        {
            var peer = _syncClient.TryAutoPair(item.Host);
            var pin = PairingInput;
            if (peer is null)
            {
                peer = _syncClient.TryPair(item.Host, pin);
                if (peer is null)
                {
                    SyncStatusText = "Неверный PIN. Введите код или JSON из QR.";
                    return;
                }

                SyncStatusText = $"Сопряжение с «{item.Name}» успешно. Загрузка библиотеки…";
            }
            else
            {
                SyncStatusText = $"Доверенный компьютер «{item.Name}». Загрузка библиотеки…";
            }

            var progress = new Progress<double>(value =>
            {
                Dispatcher.UIThread.Post(() => SyncProgress = value);
            });
            var added = await _syncClient.PullLibraryAsync(peer, DisplayName, pin, _library, progress);
            _libraryChanged();
            var addedStatus = added == 0
                ? $"Библиотека с «{item.Name}» уже была у вас"
                : $"С «{item.Name}» добавлено песен: {added}";
            try
            {
                await ScanCoreAsync();
            }
            catch
            {
            }

            SyncStatusText = addedStatus;
        }
        catch (Exception exception)
        {
            SyncStatusText = exception.Message;
        }
        finally
        {
            IsSyncTransferring = false;
            IsSyncBusy = false;
        }
    }

    private void RefreshBroadcastUi()
    {
        IsAdvertising = _syncHost.IsAdvertising;
        SyncToggleLabel = IsAdvertising ? "Выключить синхронизацию" : "Включить синхронизацию";
        PinText = _syncHost.Pin ?? string.Empty;
        QrImage?.Dispose();
        QrImage = null;
        HasQrImage = false;
        if (!IsAdvertising)
        {
            return;
        }

        try
        {
            var png = _syncHost.CreateQrPng();
            using var stream = new MemoryStream(png);
            QrImage = new Bitmap(stream);
            HasQrImage = true;
        }
        catch
        {
            HasQrImage = false;
        }
    }

    public void CommitProfile()
    {
        _applyDisplayName(DisplayName);
    }
}
