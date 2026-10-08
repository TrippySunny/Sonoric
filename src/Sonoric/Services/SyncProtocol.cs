using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sonoric.Models;

namespace Sonoric.Services;

internal static class SyncProtocol
{
    public const string ServiceType = "_music-sync._tcp";
    public const string ServiceTypeLocal = "_music-sync._tcp.local.";
    public const int DefaultPort = 18764;
    public const int Version = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool PinsMatch(string pin, string pinHash)
    {
        var actual = HashPin(pin);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(pinHash.Trim().ToLowerInvariant()));
    }

    public static async Task WriteMessageAsync(Stream stream, SyncWireMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var length = BitConverter.GetBytes(json.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SyncWireMessage> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await ReadExactAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length is < 2 or > 32_000_000)
        {
            throw new InvalidDataException("Некорректный размер сообщения синхронизации.");
        }

        var json = new byte[length];
        await ReadExactAsync(stream, json, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SyncWireMessage>(json, JsonOptions)
               ?? throw new InvalidDataException("Пустое сообщение синхронизации.");
    }

    public static async Task CopyExactAsync(Stream source, Stream destination, long size, CancellationToken cancellationToken, IProgress<long>? progress = null)
    {
        var buffer = new byte[64 * 1024];
        long remaining = size;
        long copied = 0;
        while (remaining > 0)
        {
            var toRead = remaining > buffer.Length ? buffer.Length : (int)remaining;
            var read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Соединение оборвалось во время передачи файла.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
            copied += read;
            progress?.Report(copied);
        }
    }

    public static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Соединение закрыто.");
            }

            offset += read;
        }
    }
}

internal sealed class SyncWireMessage
{
    [JsonPropertyName("v")]
    public int V { get; set; } = SyncProtocol.Version;

    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    [JsonPropertyName("pin")]
    public string? Pin { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("index")]
    public LibraryIndex? Index { get; set; }

    [JsonPropertyName("files")]
    public List<SyncFileEntry>? Files { get; set; }
}

internal sealed class SyncFileEntry
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
