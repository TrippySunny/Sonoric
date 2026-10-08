using System.Net;
using System.Text.Json.Serialization;

namespace Sonoric.Models;

public sealed class DiscoveredSyncHost
{
    public required Guid Uuid { get; init; }
    public required string PinHash { get; init; }
    public required IPAddress Address { get; init; }
    public required int Port { get; init; }
    public required string DisplayName { get; init; }
}

public sealed class SyncPeer
{
    public required IPAddress Address { get; init; }
    public required Guid Uuid { get; init; }
    public required int Port { get; init; }
    public required string DisplayName { get; init; }
    public required string Token { get; init; }
}

public sealed class SyncPairingPayload
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("pin")]
    public string Pin { get; set; } = string.Empty;
}
