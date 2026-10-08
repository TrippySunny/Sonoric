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

    public IReadOnlyList<ArtistRecord> Artists
    {
        get
        {
            lock (_gate)
            {
                return _index.Artists.Select(CloneArtist).ToList();
            }
        }
    }

    public IReadOnlyList<FolderRecord> Folders
    {
        get
        {
            lock (_gate)
            {
                return _index.Folders.Select(CloneFolder).ToList();
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
            _index.Artists ??= new List<ArtistRecord>();
            _index.Folders ??= new List<FolderRecord>();
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

            var collectionIds = _index.Collections.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var albumIds = _index.Collections
                .Where(item => item.Kind == CollectionKind.Album)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var artist in _index.Artists)
            {
                artist.Tags ??= new List<string>();
                artist.Tags = artist.Tags.Where(tagIds.Contains).Distinct(StringComparer.Ordinal).ToList();
                artist.AlbumIds = artist.AlbumIds.Where(albumIds.Contains).Distinct(StringComparer.Ordinal).ToList();
            }

            foreach (var folder in _index.Folders)
            {
                folder.Tags ??= new List<string>();
                folder.Tags = folder.Tags.Where(tagIds.Contains).Distinct(StringComparer.Ordinal).ToList();
                folder.CollectionIds = folder.CollectionIds.Where(collectionIds.Contains).Distinct(StringComparer.Ordinal).ToList();
            }

            foreach (var collection in _index.Collections)
            {
                if (string.IsNullOrWhiteSpace(collection.CustomAccentHex))
                {
                    collection.CustomAccentHex = "#5A5A5A";
                }
            }

            EnsureArtistsFromAlbumsUnlocked();
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

    public string? GetArtistPhotoPath(ArtistRecord artist)
    {
        if (string.IsNullOrWhiteSpace(artist.PhotoFileName))
        {
            return null;
        }

        var path = Path.Combine(AppPaths.ArtistPhotosDirectory, artist.PhotoFileName);
        return File.Exists(path) ? path : null;
    }

    public string? GetFolderCoverPath(FolderRecord folder)
    {
        if (string.IsNullOrWhiteSpace(folder.CoverFileName))
        {
            return null;
        }

        var path = Path.Combine(AppPaths.FolderCoversDirectory, folder.CoverFileName);
        return File.Exists(path) ? path : null;
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

            var previousAuthor = existing.Author;
            var previousKind = existing.Kind;
            existing.Title = collection.Title.Trim();
            existing.Author = collection.Author.Trim();
            existing.Year = collection.Kind == CollectionKind.Album ? collection.Year : null;
            existing.Description = collection.Description.Trim();
            existing.Kind = collection.Kind;
            existing.AccentMode = collection.Kind == CollectionKind.Album ? collection.AccentMode : AlbumAccentMode.Dominant;
            existing.CustomAccentHex = string.IsNullOrWhiteSpace(collection.CustomAccentHex) ? "#5A5A5A" : collection.CustomAccentHex.Trim();
            existing.TrackIds = collection.TrackIds.Distinct(StringComparer.Ordinal).ToList();

            if (clearCover)
            {
                existing.CoverFileName = null;
            }
            else if (!string.IsNullOrWhiteSpace(coverSourcePath) && File.Exists(coverSourcePath))
            {
                existing.CoverFileName = CopyCollectionCover(existing.Id, coverSourcePath);
            }

            EnsureArtistForAlbumUnlocked(existing, previousKind == CollectionKind.Album ? previousAuthor : null);
            PersistUnlocked();
            return CloneCollection(existing);
        }
    }

    public void DeleteCollection(string collectionId)
    {
        lock (_gate)
        {
            _index.Collections.RemoveAll(item => item.Id == collectionId);
            foreach (var artist in _index.Artists)
            {
                artist.AlbumIds.RemoveAll(id => id == collectionId);
            }

            foreach (var folder in _index.Folders)
            {
                folder.CollectionIds.RemoveAll(id => id == collectionId);
            }

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

    private void EnsureArtistsFromAlbumsUnlocked()
    {
        foreach (var album in _index.Collections.Where(item => item.Kind == CollectionKind.Album))
        {
            EnsureArtistForAlbumUnlocked(album, null);
        }
    }

    private void EnsureArtistForAlbumUnlocked(CollectionRecord album, string? previousAuthor)
    {
        if (album.Kind != CollectionKind.Album)
        {
            UnlinkAlbumFromAuthorUnlocked(album.Id, previousAuthor);
            return;
        }

        var author = album.Author.Trim();
        if (!string.Equals(previousAuthor?.Trim(), author, StringComparison.CurrentCultureIgnoreCase))
        {
            UnlinkAlbumFromAuthorUnlocked(album.Id, previousAuthor);
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            return;
        }

        var artist = FindArtistByNameUnlocked(author);
        if (artist is null)
        {
            artist = new ArtistRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = author,
                CreatedAt = DateTimeOffset.Now
            };
            _index.Artists.Add(artist);
        }

        if (!artist.AlbumIds.Contains(album.Id, StringComparer.Ordinal))
        {
            artist.AlbumIds.Add(album.Id);
        }
    }

    private void UnlinkAlbumFromAuthorUnlocked(string albumId, string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return;
        }

        var artist = FindArtistByNameUnlocked(author);
        artist?.AlbumIds.RemoveAll(id => id == albumId);
    }

    private ArtistRecord? FindArtistByNameUnlocked(string name)
    {
        var trimmed = name.Trim();
        return _index.Artists.FirstOrDefault(item =>
            string.Equals(item.Name.Trim(), trimmed, StringComparison.CurrentCultureIgnoreCase));
    }

    public ArtistRecord UpsertArtist(ArtistRecord artist, string? photoSourcePath, bool clearPhoto)
    {
        lock (_gate)
        {
            var existing = _index.Artists.FirstOrDefault(item => item.Id == artist.Id);
            if (existing is null)
            {
                existing = new ArtistRecord { Id = artist.Id, CreatedAt = DateTimeOffset.Now };
                _index.Artists.Add(existing);
            }

            var albumIds = _index.Collections
                .Where(item => item.Kind == CollectionKind.Album)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            existing.Name = artist.Name.Trim();
            existing.Description = artist.Description.Trim();
            existing.AlbumIds = artist.AlbumIds.Where(albumIds.Contains).Distinct(StringComparer.Ordinal).ToList();
            existing.Tags = (artist.Tags ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
            if (clearPhoto)
            {
                existing.PhotoFileName = null;
            }
            else if (!string.IsNullOrWhiteSpace(photoSourcePath) && File.Exists(photoSourcePath))
            {
                existing.PhotoFileName = CopyNamedImage(AppPaths.ArtistPhotosDirectory, existing.Id, photoSourcePath);
            }

            PersistUnlocked();
            return CloneArtist(existing);
        }
    }

    public void DeleteArtist(string artistId)
    {
        lock (_gate)
        {
            var existing = _index.Artists.FirstOrDefault(item => item.Id == artistId);
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.PhotoFileName))
            {
                TryDeleteFile(Path.Combine(AppPaths.ArtistPhotosDirectory, existing.PhotoFileName));
            }

            _index.Artists.RemoveAll(item => item.Id == artistId);
            PersistUnlocked();
        }
    }

    public void SetArtistTags(string artistId, IEnumerable<string> tags)
    {
        lock (_gate)
        {
            var existing = _index.Artists.FirstOrDefault(item => item.Id == artistId);
            if (existing is null)
            {
                return;
            }

            existing.Tags = tags.Where(id => _index.Tags.Any(tag => tag.Id == id)).Distinct(StringComparer.Ordinal).ToList();
            PersistUnlocked();
        }
    }

    public FolderRecord UpsertFolder(FolderRecord folder, string? coverSourcePath, bool clearCover)
    {
        lock (_gate)
        {
            var existing = _index.Folders.FirstOrDefault(item => item.Id == folder.Id);
            if (existing is null)
            {
                existing = new FolderRecord { Id = folder.Id, CreatedAt = DateTimeOffset.Now };
                _index.Folders.Add(existing);
            }

            var collectionIds = _index.Collections.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            existing.Title = folder.Title.Trim();
            existing.Description = folder.Description.Trim();
            existing.CollectionIds = folder.CollectionIds.Where(collectionIds.Contains).Distinct(StringComparer.Ordinal).ToList();
            existing.Tags = (folder.Tags ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
            if (clearCover)
            {
                existing.CoverFileName = null;
            }
            else if (!string.IsNullOrWhiteSpace(coverSourcePath) && File.Exists(coverSourcePath))
            {
                existing.CoverFileName = CopyNamedImage(AppPaths.FolderCoversDirectory, existing.Id, coverSourcePath);
            }

            PersistUnlocked();
            return CloneFolder(existing);
        }
    }

    public void DeleteFolder(string folderId)
    {
        lock (_gate)
        {
            var existing = _index.Folders.FirstOrDefault(item => item.Id == folderId);
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.CoverFileName))
            {
                TryDeleteFile(Path.Combine(AppPaths.FolderCoversDirectory, existing.CoverFileName));
            }

            _index.Folders.RemoveAll(item => item.Id == folderId);
            PersistUnlocked();
        }
    }

    public void SetFolderTags(string folderId, IEnumerable<string> tags)
    {
        lock (_gate)
        {
            var existing = _index.Folders.FirstOrDefault(item => item.Id == folderId);
            if (existing is null)
            {
                return;
            }

            existing.Tags = tags.Where(id => _index.Tags.Any(tag => tag.Id == id)).Distinct(StringComparer.Ordinal).ToList();
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

            foreach (var artist in _index.Artists)
            {
                artist.Tags.RemoveAll(id => id == tagId);
            }

            foreach (var folder in _index.Folders)
            {
                folder.Tags.RemoveAll(id => id == tagId);
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

    public LibraryIndex ExportIndex()
    {
        lock (_gate)
        {
            return new LibraryIndex
            {
                Tracks = _index.Tracks.Select(CloneTrack).ToList(),
                Collections = _index.Collections.Select(CloneCollection).ToList(),
                Tags = _index.Tags.Select(CloneTag).ToList(),
                Artists = _index.Artists.Select(CloneArtist).ToList(),
                Folders = _index.Folders.Select(CloneFolder).ToList()
            };
        }
    }

    public int MergeSyncedLibrary(LibraryIndex remote, string stagingRoot)
    {
        AppPaths.EnsureCreated();
        lock (_gate)
        {
            remote.Tracks ??= new List<TrackRecord>();
            remote.Collections ??= new List<CollectionRecord>();
            remote.Tags ??= new List<TagRecord>();
            remote.Artists ??= new List<ArtistRecord>();
            remote.Folders ??= new List<FolderRecord>();

            var tagMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in remote.Tags)
            {
                var local = _index.Tags.FirstOrDefault(item => item.Id == tag.Id)
                    ?? _index.Tags.FirstOrDefault(item =>
                        string.Equals(item.Name.Trim(), tag.Name.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (local is null)
                {
                    local = CloneTag(tag);
                    if (_index.Tags.Any(item => item.Id == local.Id))
                    {
                        local.Id = Guid.NewGuid().ToString("N");
                    }

                    _index.Tags.Add(local);
                }

                tagMap[tag.Id] = local.Id;
            }

            var trackMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var stagingLibrary = Path.Combine(stagingRoot, "library");
            var stagingCovers = Path.Combine(stagingRoot, "covers");
            var stagingCollectionCovers = Path.Combine(stagingRoot, "collection-covers");
            var added = 0;

            foreach (var track in remote.Tracks)
            {
                var existing = _index.Tracks.FirstOrDefault(item =>
                    string.Equals(item.Hash, track.Hash, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    trackMap[track.Id] = existing.Id;
                    if (track.IsFavorite)
                    {
                        existing.IsFavorite = true;
                    }

                    existing.Tags ??= new List<string>();
                    foreach (var tagId in track.Tags ?? new List<string>())
                    {
                        if (tagMap.TryGetValue(tagId, out var localTag) && !existing.Tags.Contains(localTag, StringComparer.Ordinal))
                        {
                            existing.Tags.Add(localTag);
                        }
                    }

                    continue;
                }

                var source = Path.Combine(stagingLibrary, track.StoredFileName);
                if (!File.Exists(source))
                {
                    continue;
                }

                var newId = _index.Tracks.Any(item => item.Id == track.Id)
                    ? Guid.NewGuid().ToString("N")
                    : track.Id;
                var extension = Path.GetExtension(track.StoredFileName);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".bin";
                }

                var storedFileName = newId + extension.ToLowerInvariant();
                File.Copy(source, Path.Combine(AppPaths.LibraryDirectory, storedFileName), overwrite: false);

                string? coverFileName = null;
                if (!string.IsNullOrWhiteSpace(track.CoverFileName))
                {
                    var coverSource = Path.Combine(stagingCovers, track.CoverFileName);
                    if (File.Exists(coverSource))
                    {
                        coverFileName = newId + Path.GetExtension(track.CoverFileName).ToLowerInvariant();
                        File.Copy(coverSource, Path.Combine(AppPaths.CoversDirectory, coverFileName), overwrite: true);
                    }
                }

                var mappedTags = new List<string>();
                foreach (var tagId in track.Tags ?? new List<string>())
                {
                    if (tagMap.TryGetValue(tagId, out var localTag) && !mappedTags.Contains(localTag, StringComparer.Ordinal))
                    {
                        mappedTags.Add(localTag);
                    }
                }

                _index.Tracks.Add(new TrackRecord
                {
                    Id = newId,
                    StoredFileName = storedFileName,
                    OriginalFileName = track.OriginalFileName,
                    Title = track.Title,
                    Artist = track.Artist,
                    Album = track.Album,
                    DurationMs = track.DurationMs,
                    CoverFileName = coverFileName,
                    Hash = track.Hash,
                    IsFavorite = track.IsFavorite,
                    Tags = mappedTags,
                    ImportedAt = track.ImportedAt
                });
                trackMap[track.Id] = newId;
                added++;
            }

            foreach (var collection in remote.Collections)
            {
                var mappedIds = collection.TrackIds
                    .Where(trackMap.ContainsKey)
                    .Select(id => trackMap[id])
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var existing = _index.Collections.FirstOrDefault(item => item.Id == collection.Id)
                    ?? _index.Collections.FirstOrDefault(item =>
                        item.Kind == collection.Kind &&
                        string.Equals(item.Title, collection.Title, StringComparison.CurrentCultureIgnoreCase));
                var collectionId = existing?.Id ?? collection.Id;
                if (existing is null && _index.Collections.Any(item => item.Id == collectionId))
                {
                    collectionId = Guid.NewGuid().ToString("N");
                }

                string? coverFileName = existing?.CoverFileName;
                if (!string.IsNullOrWhiteSpace(collection.CoverFileName))
                {
                    var coverSource = Path.Combine(stagingCollectionCovers, collection.CoverFileName);
                    if (File.Exists(coverSource))
                    {
                        coverFileName = collectionId + Path.GetExtension(collection.CoverFileName).ToLowerInvariant();
                        File.Copy(coverSource, Path.Combine(AppPaths.CollectionCoversDirectory, coverFileName), overwrite: true);
                    }
                }

                if (existing is null)
                {
                    var clone = CloneCollection(collection);
                    clone.Id = collectionId;
                    clone.TrackIds = mappedIds;
                    clone.CoverFileName = coverFileName;
                    _index.Collections.Add(clone);
                }
                else
                {
                    foreach (var id in mappedIds)
                    {
                        if (!existing.TrackIds.Contains(id, StringComparer.Ordinal))
                        {
                            existing.TrackIds.Add(id);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(coverFileName))
                    {
                        existing.CoverFileName = coverFileName;
                    }
                }
            }

            var stagingArtistPhotos = Path.Combine(stagingRoot, "artist-photos");
            var localAlbumIds = _index.Collections
                .Where(item => item.Kind == CollectionKind.Album)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var artist in remote.Artists)
            {
                var existing = _index.Artists.FirstOrDefault(item => item.Id == artist.Id)
                    ?? _index.Artists.FirstOrDefault(item =>
                        string.Equals(item.Name, artist.Name, StringComparison.CurrentCultureIgnoreCase));
                var mappedAlbums = artist.AlbumIds.Where(localAlbumIds.Contains).Distinct(StringComparer.Ordinal).ToList();
                var mappedTags = new List<string>();
                foreach (var tagId in artist.Tags ?? new List<string>())
                {
                    if (tagMap.TryGetValue(tagId, out var localTag) && !mappedTags.Contains(localTag, StringComparer.Ordinal))
                    {
                        mappedTags.Add(localTag);
                    }
                }

                string? photo = existing?.PhotoFileName;
                if (!string.IsNullOrWhiteSpace(artist.PhotoFileName))
                {
                    var source = Path.Combine(stagingArtistPhotos, artist.PhotoFileName);
                    if (File.Exists(source))
                    {
                        var id = existing?.Id ?? artist.Id;
                        photo = CopyNamedImage(AppPaths.ArtistPhotosDirectory, id, source);
                    }
                }

                if (existing is null)
                {
                    var clone = CloneArtist(artist);
                    if (_index.Artists.Any(item => item.Id == clone.Id))
                    {
                        clone.Id = Guid.NewGuid().ToString("N");
                    }

                    clone.AlbumIds = mappedAlbums;
                    clone.Tags = mappedTags;
                    clone.PhotoFileName = photo;
                    _index.Artists.Add(clone);
                }
                else
                {
                    foreach (var id in mappedAlbums)
                    {
                        if (!existing.AlbumIds.Contains(id, StringComparer.Ordinal))
                        {
                            existing.AlbumIds.Add(id);
                        }
                    }

                    existing.Tags = mappedTags.Count > 0 ? mappedTags : existing.Tags;
                    if (!string.IsNullOrWhiteSpace(photo))
                    {
                        existing.PhotoFileName = photo;
                    }

                    if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(artist.Description))
                    {
                        existing.Description = artist.Description;
                    }
                }
            }

            var stagingFolderCovers = Path.Combine(stagingRoot, "folder-covers");
            var localCollectionIds = _index.Collections.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var folder in remote.Folders)
            {
                var existing = _index.Folders.FirstOrDefault(item => item.Id == folder.Id)
                    ?? _index.Folders.FirstOrDefault(item =>
                        string.Equals(item.Title, folder.Title, StringComparison.CurrentCultureIgnoreCase));
                var mappedCollections = folder.CollectionIds.Where(localCollectionIds.Contains).Distinct(StringComparer.Ordinal).ToList();
                var mappedTags = new List<string>();
                foreach (var tagId in folder.Tags ?? new List<string>())
                {
                    if (tagMap.TryGetValue(tagId, out var localTag) && !mappedTags.Contains(localTag, StringComparer.Ordinal))
                    {
                        mappedTags.Add(localTag);
                    }
                }

                string? cover = existing?.CoverFileName;
                if (!string.IsNullOrWhiteSpace(folder.CoverFileName))
                {
                    var source = Path.Combine(stagingFolderCovers, folder.CoverFileName);
                    if (File.Exists(source))
                    {
                        var id = existing?.Id ?? folder.Id;
                        cover = CopyNamedImage(AppPaths.FolderCoversDirectory, id, source);
                    }
                }

                if (existing is null)
                {
                    var clone = CloneFolder(folder);
                    if (_index.Folders.Any(item => item.Id == clone.Id))
                    {
                        clone.Id = Guid.NewGuid().ToString("N");
                    }

                    clone.CollectionIds = mappedCollections;
                    clone.Tags = mappedTags;
                    clone.CoverFileName = cover;
                    _index.Folders.Add(clone);
                }
                else
                {
                    foreach (var id in mappedCollections)
                    {
                        if (!existing.CollectionIds.Contains(id, StringComparer.Ordinal))
                        {
                            existing.CollectionIds.Add(id);
                        }
                    }

                    existing.Tags = mappedTags.Count > 0 ? mappedTags : existing.Tags;
                    if (!string.IsNullOrWhiteSpace(cover))
                    {
                        existing.CoverFileName = cover;
                    }
                }
            }

            EnsureArtistsFromAlbumsUnlocked();
            PersistUnlocked();
            return added;
        }
    }

    private static TrackRecord CloneTrack(TrackRecord source)
    {
        return new TrackRecord
        {
            Id = source.Id,
            StoredFileName = source.StoredFileName,
            OriginalFileName = source.OriginalFileName,
            Title = source.Title,
            Artist = source.Artist,
            Album = source.Album,
            DurationMs = source.DurationMs,
            CoverFileName = source.CoverFileName,
            Hash = source.Hash,
            IsFavorite = source.IsFavorite,
            Tags = source.Tags.ToList(),
            ImportedAt = source.ImportedAt
        };
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
            AccentMode = source.AccentMode,
            CustomAccentHex = string.IsNullOrWhiteSpace(source.CustomAccentHex) ? "#5A5A5A" : source.CustomAccentHex,
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

    private static ArtistRecord CloneArtist(ArtistRecord source)
    {
        return new ArtistRecord
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            PhotoFileName = source.PhotoFileName,
            AlbumIds = source.AlbumIds.ToList(),
            Tags = (source.Tags ?? new List<string>()).ToList(),
            CreatedAt = source.CreatedAt
        };
    }

    private static FolderRecord CloneFolder(FolderRecord source)
    {
        return new FolderRecord
        {
            Id = source.Id,
            Title = source.Title,
            Description = source.Description,
            CoverFileName = source.CoverFileName,
            CollectionIds = source.CollectionIds.ToList(),
            Tags = (source.Tags ?? new List<string>()).ToList(),
            CreatedAt = source.CreatedAt
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
        return CopyNamedImage(AppPaths.CollectionCoversDirectory, collectionId, sourcePath);
    }

    private static string CopyNamedImage(string directory, string id, string sourcePath)
    {
        AppPaths.EnsureCreated();
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var fileName = id + extension.ToLowerInvariant();
        var destination = Path.Combine(directory, fileName);
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
