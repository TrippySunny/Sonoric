using System.Text.Json.Serialization;

namespace Sonorics.Models;

public sealed class AppSettings
{
    [JsonPropertyName("volume")]
    public int Volume { get; set; } = 80;

    [JsonPropertyName("lastTrackId")]
    public string? LastTrackId { get; set; }
}
