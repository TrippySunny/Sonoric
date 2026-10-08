using System.Text.Json.Serialization;

namespace Sonoric.Models;

public sealed class ArtistRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("photoFileName")]
    public string? PhotoFileName { get; set; }

    [JsonPropertyName("albumIds")]
    public List<string> AlbumIds { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
