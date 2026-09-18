using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Sonorics.Services;

public interface IFileDialogService
{
    Task<IReadOnlyList<string>> PickAudioFilesAsync();
    Task<IReadOnlyList<string>> PickAudioFolderAsync();
    Task<string?> PickImageAsync();
}

public sealed class FileDialogService : IFileDialogService
{
    public static readonly FilePickerFileType Audio = new("Аудиофайлы")
    {
        Patterns = new[]
        {
            "*.mp3", "*.wav", "*.flac", "*.ogg", "*.oga", "*.opus", "*.m4a", "*.aac",
            "*.wma", "*.aiff", "*.aif", "*.ape", "*.ac3", "*.mp2", "*.mpga", "*.mka",
            "*.weba", "*.alac", "*.amr", "*.3gp", "*.mp4", "*.webm", "*.m4b", "*.wv"
        },
        MimeTypes = new[] { "audio/*" },
        AppleUniformTypeIdentifiers = new[] { "public.audio" }
    };

    private readonly Window _window;

    public FileDialogService(Window window)
    {
        _window = window;
    }

    public async Task<IReadOnlyList<string>> PickAudioFilesAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт аудиофайлов",
            AllowMultiple = true,
            FileTypeFilter = new[] { Audio, FilePickerFileTypes.All }
        });

        return ToLocalPaths(files);
    }

    public async Task<IReadOnlyList<string>> PickAudioFolderAsync()
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Импорт папки с аудио",
            AllowMultiple = false
        });

        return ToLocalPaths(folders);
    }

    public async Task<string?> PickImageAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Фото набора",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll, FilePickerFileTypes.All }
        });

        return ToLocalPaths(files).FirstOrDefault();
    }

    private static IReadOnlyList<string> ToLocalPaths(IEnumerable<IStorageItem> items)
    {
        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
