using System.Text.Json.Serialization;

namespace Sonoric.Models;

public sealed class FolderRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("coverFileName")]
    public string? CoverFileName { get; set; }

    [JsonPropertyName("collectionIds")]
    public List<string> CollectionIds { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
