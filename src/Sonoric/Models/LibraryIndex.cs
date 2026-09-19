using System.Text.Json.Serialization;

namespace Sonoric.Models;

public sealed class LibraryIndex
{
    [JsonPropertyName("tracks")]
    public List<TrackRecord> Tracks { get; set; } = new();

    [JsonPropertyName("collections")]
    public List<CollectionRecord> Collections { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<TagRecord> Tags { get; set; } = new();
}
