using Avalonia.Controls;
using Sonorics.ViewModels;
using Sonorics.Views;

namespace Sonorics.Services;

public interface ICollectionDialogService
{
    Task<bool> EditAsync(CollectionEditorViewModel editor);
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

        var result = await window.ShowDialog<bool>(_owner);
        return result;
    }
}
