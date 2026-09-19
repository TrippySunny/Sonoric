namespace Sonoric.Services;

public static class AppPaths
{
    static AppPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var next = Path.Combine(local, "Sonoric");
        var previous = Path.Combine(local, "Sonorics");
        if (!Directory.Exists(next) && Directory.Exists(previous))
        {
            Directory.Move(previous, next);
        }

        Root = next;
        LibraryDirectory = Path.Combine(Root, "library");
        CoversDirectory = Path.Combine(Root, "covers");
        CollectionCoversDirectory = Path.Combine(Root, "collection-covers");
        LibraryIndexPath = Path.Combine(Root, "library.json");
        SettingsPath = Path.Combine(Root, "settings.json");
    }

    public static string Root { get; }
    public static string LibraryDirectory { get; }
    public static string CoversDirectory { get; }
    public static string CollectionCoversDirectory { get; }
    public static string LibraryIndexPath { get; }
    public static string SettingsPath { get; }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LibraryDirectory);
        Directory.CreateDirectory(CoversDirectory);
        Directory.CreateDirectory(CollectionCoversDirectory);
    }
}
