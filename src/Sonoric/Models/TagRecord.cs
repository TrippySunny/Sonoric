using System.Text.Json.Serialization;

namespace Sonoric.Models;

public sealed class TagRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("hex")]
    public string Hex { get; set; } = "#FFFFFF";
}
