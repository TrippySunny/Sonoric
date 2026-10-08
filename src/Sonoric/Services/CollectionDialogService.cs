using Avalonia.Controls;
using Sonoric.ViewModels;
using Sonoric.Views;

namespace Sonoric.Services;

public interface ICollectionDialogService
{
    Task<bool> EditAsync(CollectionEditorViewModel editor);
    Task<bool> EditArtistAsync(ArtistEditorViewModel editor);
    Task<bool> EditFolderAsync(FolderEditorViewModel editor);
}

public sealed class CollectionDialogService : ICollectionDialogService
{
    private readonly Window _owner;

    public CollectionDialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task<bool> EditAsync(CollectionEditorViewModel editor)
    {
        var window = new CollectionEditorWindow
        {
            DataContext = editor
        };

        return await window.ShowDialog<bool>(_owner);
    }

    public async Task<bool> EditArtistAsync(ArtistEditorViewModel editor)
    {
        var window = new ArtistEditorWindow
        {
            DataContext = editor
        };

        return await window.ShowDialog<bool>(_owner);
    }

    public async Task<bool> EditFolderAsync(FolderEditorViewModel editor)
    {
        var window = new FolderEditorWindow
        {
            DataContext = editor
        };

        return await window.ShowDialog<bool>(_owner);
    }
}
