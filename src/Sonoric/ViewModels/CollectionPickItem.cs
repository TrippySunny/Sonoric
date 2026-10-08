using CommunityToolkit.Mvvm.ComponentModel;
using Sonoric.Models;

namespace Sonoric.ViewModels;

public sealed partial class CollectionPickItem : ObservableObject
{
    public CollectionPickItem(CollectionRecord record, bool isSelected)
    {
        Record = record;
        Title = string.IsNullOrWhiteSpace(record.Title) ? "Без названия" : record.Title;
        KindLabel = record.Kind == CollectionKind.Album ? "Альбом" : "Плейлист";
        IsSelected = isSelected;
    }

    public CollectionRecord Record { get; }
    public string Title { get; }
    public string KindLabel { get; }
    public string Id => Record.Id;

    [ObservableProperty]
    private bool isSelected;
}
