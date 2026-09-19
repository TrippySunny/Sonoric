using System.Text.Json.Serialization;

namespace Sonoric.Models;

public sealed class CollectionRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public CollectionKind Kind { get; set; } = CollectionKind.Playlist;

    [JsonPropertyName("coverFileName")]
    public string? CoverFileName { get; set; }

    [JsonPropertyName("trackIds")]
    public List<string> TrackIds { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
