using System.Globalization;
using Sonoric.Models;
using Sonoric.Services;

namespace Sonoric.ViewModels;

public sealed class StatisticsViewModel
{
    public StatisticsViewModel(LibraryService library)
    {
        var tracks = library.Tracks;
        var collections = library.Collections;
        SongCountText = tracks.Count.ToString(CultureInfo.CurrentCulture);
        AlbumCountText = collections.Count(item => item.Kind == CollectionKind.Album).ToString(CultureInfo.CurrentCulture);
        PlaylistCountText = collections.Count(item => item.Kind == CollectionKind.Playlist).ToString(CultureInfo.CurrentCulture);
        LibrarySizeText = FormatSize(SumLibraryBytes(library, tracks));
    }

    public string SongCountText { get; }
    public string LibrarySizeText { get; }
    public string AlbumCountText { get; }
    public string PlaylistCountText { get; }

    private static long SumLibraryBytes(LibraryService library, IReadOnlyList<TrackRecord> tracks)
    {
        long total = 0;
        foreach (var track in tracks)
        {
            var path = library.GetTrackPath(track);
            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
        }

        return total;
    }

    private static string FormatSize(long bytes)
    {
        const double kilo = 1024d;
        if (bytes < kilo)
        {
            return $"{bytes} Б";
        }

        var kilobytes = bytes / kilo;
        if (kilobytes < kilo)
        {
            return $"{kilobytes:0.##} КБ";
        }

        var megabytes = kilobytes / kilo;
        if (megabytes < kilo)
        {
            return $"{megabytes:0.##} МБ";
        }

        return $"{megabytes / kilo:0.##} ГБ";
    }
}
