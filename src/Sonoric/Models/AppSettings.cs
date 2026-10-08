using System.Text.Json.Serialization;

namespace Sonoric.Models;

public sealed class AppSettings
{
    [JsonPropertyName("volume")]
    public int Volume { get; set; } = 80;

    [JsonPropertyName("lastTrackId")]
    public string? LastTrackId { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("trustedDevices")]
    public List<TrustedDevice> TrustedDevices { get; set; } = new();
}
