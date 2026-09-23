using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sonoric.Models;

namespace Sonoric.Services;

public sealed class LibraryService
{
    private static readonly FrozenSet<string> AudioExtensions = new[]
    {
        ".mp3", ".wav", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".aac",
        ".wma", ".aiff", ".aif", ".ape", ".ac3", ".mp2", ".mpga", ".mka",
        ".weba", ".alac", ".amr", ".3gp", ".mp4", ".webm", ".m4b", ".wv"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private LibraryIndex _index = new();

    public IReadOnlyList<TrackRecord> Tracks
    {
        get
        {
            lock (_gate)
            {
                return _index.Tracks.ToList();
            }
        }
    }

    public IReadOnlyList<CollectionRecord> Collections
    {
        get
        {
            lock (_gate)
            {
                return _index.Collections.ToList();
            }
        }
    }

    public IReadOnlyList<TagRecord> Tags
    {
        get
        {
            lock (_gate)
            {
                return _index.Tags.Select(CloneTag).ToList();
            }
        }
    }

    public void Load()
    {
        AppPaths.EnsureCreated();
        lock (_gate)
        {
            if (!File.Exists(AppPaths.LibraryIndexPath))
            {
                _index = new LibraryIndex();
                PersistUnlocked();
                return;
            }

            var json = File.ReadAllText(AppPaths.LibraryIndexPath);
            _index = JsonSerializer.Deserialize<LibraryIndex>(json, JsonOptions) ?? new LibraryIndex();
            _index.Tracks ??= new List<TrackRecord>();
            _index.Collections ??= new List<CollectionRecord>();
            _index.Tags ??= new List<TagRecord>();
            _index.Tracks = _index.Tracks
                .Where(track => File.Exists(GetTrackPath(track)))
                .OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(track => track.OriginalFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var tagIds = _index.Tags.Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var track in _index.Tracks)
            {
                track.Tags ??= new List<string>();
                track.Tags = track.Tags
                    .Where(tagIds.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
            var trackIds = _index.Tracks.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var collection in _index.Collections)
            {
                collection.TrackIds = collection.TrackIds.Where(trackIds.Contains).ToList();
            }

            PersistUnlocked();
        }
    }

    public IReadOnlyList<TrackRecord> Import(IEnumerable<string> paths)
    {
        var imported = new List<TrackRecord>();
        var files = ExpandToAudioFiles(paths);

        lock (_gate)
        {
            var knownHashes = _index.Tracks
                .Select(track => track.Hash)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                string hash;
                try
                {
                    hash = ComputeHash(file);
                }
                catch
                {
                    continue;
                }

                if (!knownHashes.Add(hash))
                {
                    continue;
                }

                try
                {
                    imported.Add(ImportFileUnlocked(file, hash));
                }
                catch
                {
                    knownHashes.Remove(hash);
                }
            }

            if (imported.Count > 0)
            {
                _index.Tracks = _index.Tracks
                    .OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(track => track.OriginalFileName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                PersistUnlocked();
            }
        }

        return imported;
    }

    public (CollectionRecord? Collection, int Added) AddTracksToCollection(string collectionId, IEnumerable<string> paths)
    {
        var files = ExpandToAudioFiles(paths);
        lock (_gate)
        {
            var collection = _index.Collections.FirstOrDefault(item => item.Id == collectionId);
            if (collection is null)
            {
                return (null, 0);
            }

            var byHash = new Dictionary<string, TrackRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in _index.Tracks)
            {
                byHash.TryAdd(track.Hash, track);
            }
            var knownIds = collection.TrackIds.ToHashSet(StringComparer.Ordinal);
            var added = 0;
            var importedNew = false;

            foreach (var file in files)
            {
                string hash;
                try
                {
                    hash = ComputeHash(file);
                }
                catch
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out var record))
                {
                    try
                    {
                        record = ImportFileUnlocked(file, hash);
                        byHash[hash] = record;
                        importedNew = true;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (!knownIds.Add(record.Id))
                {
                    continue;
                }

                collection.TrackIds.Add(record.Id);
                added++;
            }

            if (importedNew)
            {
                _index.Tracks = _index.Tracks
                    .OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(track => track.OriginalFileName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            if (added > 0 || importedNew)
            {
                PersistUnlocked();
            }

            return (CloneCollection(collection), added);
        }
    }

    public IReadOnlyList<TrackRecord> ResolveAndImport(IEnumerable<string> paths)
    {
        var files = ExpandToAudioFiles(paths);
        var resolved = new List<TrackRecord>();
        lock (_gate)
        {
            var byHash = new Dictionary<string, TrackRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in _index.Tracks)
            {
                byHash.TryAdd(track.Hash, track);
            }
            var importedNew = false;

            foreach (var file in files)
            {
                string hash;
                try
                {
                    hash = ComputeHash(file);
                }
                catch
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out var record))
                {
                    try
                    {
                        record = ImportFileUnlocked(file, hash);
                        byHash[hash] = record;
                        importedNew = true;
                    }
                    catch
                    {
                        continue;
                    }
                }

                resolved.Add(record);
            }

            if (importedNew)
            {
                _index.Tracks = _index.Tracks
                    .OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(track => track.OriginalFileName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                PersistUnlocked();
            }
        }

        return resolved;
    }

    public string GetTrackPath(TrackRecord track)
    {
        return Path.Combine(AppPaths.LibraryDirectory, track.StoredFileName);
    }

    public string? GetCoverPath(TrackRecord track)
    {
        if (string.IsNullOrWhiteSpace(track.CoverFileName))
        {
            return null;
        }

        var path = Path.Combine(AppPaths.CoversDirectory, track.CoverFileName);
        return File.Exists(path) ? path : null;
    }

    public string? GetCollectionCoverPath(CollectionRecord collection)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(collection.CoverFileName))
            {
                var custom = Path.Combine(AppPaths.CollectionCoversDirectory, collection.CoverFileName);
                if (File.Exists(custom))
                {
                    return custom;
                }
            }

            foreach (var trackId in collection.TrackIds)
            {
                var track = _index.Tracks.FirstOrDefault(item => item.Id == trackId);
                if (track is null)
                {
                    continue;
                }

                var cover = GetCoverPath(track);
                if (!string.IsNullOrWhiteSpace(cover))
                {
                    return cover;
                }
            }

            return null;
        }
    }

    public void SetFavorite(string trackId, bool isFavorite)
    {
        lock (_gate)
        {
            var track = _index.Tracks.FirstOrDefault(item => item.Id == trackId);
            if (track is null)
            {
                return;
            }

            track.IsFavorite = isFavorite;
            PersistUnlocked();
        }
    }

    public void SetTrackTags(string trackId, IReadOnlyList<string> tags)
    {
        lock (_gate)
        {
            var track = _index.Tracks.FirstOrDefault(item => item.Id == trackId);
            if (track is null)
            {
                return;
            }

            track.Tags = tags
                .Where(id => _index.Tags.Any(tag => tag.Id == id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            PersistUnlocked();
        }
    }

    public void DeleteTrack(string trackId)
    {
        lock (_gate)
        {
            var track = _index.Tracks.FirstOrDefault(item => item.Id == trackId);
            if (track is null)
            {
                return;
            }

            TryDeleteFile(GetTrackPath(track));
            var cover = GetCoverPath(track);
            if (!string.IsNullOrWhiteSpace(cover))
            {
                TryDeleteFile(cover);
            }

            _index.Tracks.RemoveAll(item => item.Id == trackId);
            foreach (var collection in _index.Collections)
            {
                collection.TrackIds.RemoveAll(id => id == trackId);
            }

            PersistUnlocked();
        }
    }

    public CollectionRecord UpsertCollection(CollectionRecord collection, string? coverSourcePath, bool clearCover)
    {
        lock (_gate)
        {
            var existing = _index.Collections.FirstOrDefault(item => item.Id == collection.Id);
            if (existing is null)
            {
                existing = new CollectionRecord { Id = collection.Id, CreatedAt = DateTimeOffset.Now };
                _index.Collections.Add(existing);
            }

            existing.Title = collection.Title.Trim();
            existing.Author = collection.Author.Trim();
            existing.Year = collection.Kind == CollectionKind.Album ? collection.Year : null;
            existing.Description = collection.Description.Trim();
            existing.Kind = collection.Kind;
            existing.TrackIds = collection.TrackIds.Distinct(StringComparer.Ordinal).ToList();

            if (clearCover)
            {
                existing.CoverFileName = null;
            }
            else if (!string.IsNullOrWhiteSpace(coverSourcePath) && File.Exists(coverSourcePath))
            {
                existing.CoverFileName = CopyCollectionCover(existing.Id, coverSourcePath);
            }

            PersistUnlocked();
            return CloneCollection(existing);
        }
    }

    public void DeleteCollection(string collectionId)
    {
        lock (_gate)
        {
            _index.Collections.RemoveAll(item => item.Id == collectionId);
            PersistUnlocked();
        }
    }

    public void SetPlaylistAuthors(string name)
    {
        lock (_gate)
        {
            var author = name.Trim();
            foreach (var collection in _index.Collections.Where(item => item.Kind == CollectionKind.Playlist))
            {
                collection.Author = author;
            }

            PersistUnlocked();
        }
    }

    public TagRecord UpsertTag(TagRecord tag)
    {
        lock (_gate)
        {
            var name = tag.Name.Trim();
            var hex = NormalizeHex(tag.Hex);
            var existing = _index.Tags.FirstOrDefault(item => item.Id == tag.Id);
            if (existing is null)
            {
                existing = new TagRecord { Id = string.IsNullOrWhiteSpace(tag.Id) ? Guid.NewGuid().ToString("N") : tag.Id };
                _index.Tags.Add(existing);
            }

            existing.Name = name;
            existing.Hex = hex;
            PersistUnlocked();
            return CloneTag(existing);
        }
    }

    public void DeleteTag(string tagId)
    {
        lock (_gate)
        {
            _index.Tags.RemoveAll(item => item.Id == tagId);
            foreach (var track in _index.Tracks)
            {
                track.Tags.RemoveAll(id => id == tagId);
            }

            PersistUnlocked();
        }
    }

    public bool TagNameTaken(string name, string? exceptId)
    {
        lock (_gate)
        {
            return _index.Tags.Any(item =>
                !string.Equals(item.Id, exceptId, StringComparison.Ordinal) &&
                string.Equals(item.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            PersistUnlocked();
        }
    }

    private static CollectionRecord CloneCollection(CollectionRecord source)
    {
        return new CollectionRecord
        {
            Id = source.Id,
            Title = source.Title,
            Author = source.Author,
            Year = source.Year,
            Description = source.Description,
            Kind = source.Kind,
            CoverFileName = source.CoverFileName,
            TrackIds = source.TrackIds.ToList(),
            CreatedAt = source.CreatedAt
        };
    }

    private static TagRecord CloneTag(TagRecord source)
    {
        return new TagRecord
        {
            Id = source.Id,
            Name = source.Name,
            Hex = source.Hex
        };
    }

    private static string NormalizeHex(string hex)
    {
        var value = hex.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        return value.ToUpperInvariant();
    }

    private static string CopyCollectionCover(string collectionId, string sourcePath)
    {
        AppPaths.EnsureCreated();
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var fileName = collectionId + extension.ToLowerInvariant();
        var destination = Path.Combine(AppPaths.CollectionCoversDirectory, fileName);
        File.Copy(sourcePath, destination, overwrite: true);
        return fileName;
    }

    public static bool IsAudioFile(string path)
    {
        return AudioExtensions.Contains(Path.GetExtension(path));
    }

    private TrackRecord ImportFileUnlocked(string sourcePath, string hash)
    {
        var id = Guid.NewGuid().ToString("N");
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var storedFileName = id + extension.ToLowerInvariant();
        var destination = Path.Combine(AppPaths.LibraryDirectory, storedFileName);
        File.Copy(sourcePath, destination, overwrite: false);

        var record = new TrackRecord
        {
            Id = id,
            StoredFileName = storedFileName,
            OriginalFileName = Path.GetFileName(sourcePath),
            Hash = hash,
            ImportedAt = DateTimeOffset.Now
        };

        ApplyTags(destination, record);
        _index.Tracks.Add(record);
        return record;
    }

    private static void ApplyTags(string filePath, TrackRecord record)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var title = file.Tag.Title;
            var artist = file.Tag.FirstPerformer ?? file.Tag.FirstAlbumArtist;
            var album = file.Tag.Album;
            record.Title = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(record.OriginalFileName)
                : title.Trim();
            record.Artist = string.IsNullOrWhiteSpace(artist) ? string.Empty : artist.Trim();
            record.Album = string.IsNullOrWhiteSpace(album) ? string.Empty : album.Trim();
            record.DurationMs = (long)file.Properties.Duration.TotalMilliseconds;

            if (file.Tag.Pictures is { Length: > 0 })
            {
                var picture = file.Tag.Pictures[0];
                var coverName = record.Id + GuessImageExtension(picture.MimeType);
                var coverPath = Path.Combine(AppPaths.CoversDirectory, coverName);
                File.WriteAllBytes(coverPath, picture.Data.Data);
                record.CoverFileName = coverName;
            }
        }
        catch (TagLib.UnsupportedFormatException)
        {
            record.Title = Path.GetFileNameWithoutExtension(record.OriginalFileName);
        }
        catch (TagLib.CorruptFileException)
        {
            record.Title = Path.GetFileNameWithoutExtension(record.OriginalFileName);
        }
        catch (Exception)
        {
            if (string.IsNullOrWhiteSpace(record.Title))
            {
                record.Title = Path.GetFileNameWithoutExtension(record.OriginalFileName);
            }
        }
    }

    private static string GuessImageExtension(string? mime)
    {
        if (string.Equals(mime, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return ".png";
        }

        if (string.Equals(mime, "image/webp", StringComparison.OrdinalIgnoreCase))
        {
            return ".webp";
        }

        return ".jpg";
    }

    private static List<string> ExpandToAudioFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(IsAudioFile));
            }
            else if (File.Exists(path) && IsAudioFile(path))
            {
                files.Add(path);
            }
        }

        return files.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void PersistUnlocked()
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(_index, JsonOptions);
        var temp = AppPaths.LibraryIndexPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, AppPaths.LibraryIndexPath, overwrite: true);
        File.Delete(temp);
    }
}
