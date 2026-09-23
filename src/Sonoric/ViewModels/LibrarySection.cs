namespace Sonoric.ViewModels;

public enum LibrarySection
{
    All,
    Favorites,
    Albums,
    Playlists
}

public sealed class LibrarySectionOption
{
    public LibrarySectionOption(LibrarySection section, string label)
    {
        Section = section;
        Label = label;
    }

    public LibrarySection Section { get; }
    public string Label { get; }
}
