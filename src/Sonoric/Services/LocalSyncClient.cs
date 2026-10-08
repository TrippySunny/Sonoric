using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Sonoric.Models;
using Zeroconf;

namespace Sonoric.Services;

public sealed class LocalSyncClient
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;

    public LocalSyncClient(AppSettings settings, SettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;
    }

    public async Task<IReadOnlyList<DiscoveredSyncHost>> ScanAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IZeroconfHost> hosts;
        try
        {
            hosts = await ZeroconfResolver.ResolveAsync(
                SyncProtocol.ServiceTypeLocal,
                scanTime: TimeSpan.FromSeconds(4),
                retries: 2,
                retryDelayMilliseconds: 400,
                callback: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or IOException or TimeoutException)
        {
            throw new InvalidOperationException(
                "Не удалось просканировать локальную сеть. Проверьте multicast/mDNS в брандмауэре.",
                exception);
        }

        var ownId = _settings.DeviceId;
        var discovered = new List<DiscoveredSyncHost>();
        foreach (var host in hosts)
        {
            foreach (var service in host.Services.Values)
            {
                var properties = ReadProperties(service);
                if (!properties.TryGetValue("uuid", out var uuidText) ||
                    !Guid.TryParse(uuidText, out var uuid) ||
                    string.Equals(uuid.ToString("N"), ownId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!properties.TryGetValue("pinhash", out var pinHash) || string.IsNullOrWhiteSpace(pinHash))
                {
                    continue;
                }

                if (!TryGetAddress(host, out var address) || address is null)
                {
                    continue;
                }

                properties.TryGetValue("name", out var name);
                discovered.Add(new DiscoveredSyncHost
                {
                    Uuid = uuid,
                    PinHash = pinHash.Trim().ToLowerInvariant(),
                    Address = address,
                    Port = service.Port,
                    DisplayName = string.IsNullOrWhiteSpace(name) ? host.DisplayName : name.Trim()
                });
            }
        }

        return discovered
            .GroupBy(item => item.Uuid)
            .Select(group => group.First())
            .ToList();
    }

    public bool IsTrusted(Guid uuid)
    {
        return FindTrusted(uuid) is not null;
    }

    public SyncPeer? TryPair(DiscoveredSyncHost host, string pinOrQr)
    {
        if (!TryExtractPin(host, pinOrQr, out var pin) || string.IsNullOrWhiteSpace(pin))
        {
            return null;
        }

        if (!SyncProtocol.PinsMatch(pin, host.PinHash))
        {
            return null;
        }

        return new SyncPeer
        {
            Address = host.Address,
            Uuid = host.Uuid,
            Port = host.Port,
            DisplayName = host.DisplayName,
            Token = string.Empty
        };
    }

    public SyncPeer? TryAutoPair(DiscoveredSyncHost host)
    {
        var trusted = FindTrusted(host.Uuid);
        if (trusted is null || string.IsNullOrWhiteSpace(trusted.Token))
        {
            return null;
        }

        return new SyncPeer
        {
            Address = host.Address,
            Uuid = host.Uuid,
            Port = host.Port,
            DisplayName = string.IsNullOrWhiteSpace(trusted.DisplayName) ? host.DisplayName : trusted.DisplayName,
            Token = trusted.Token
        };
    }

    public async Task<int> PullLibraryAsync(
        SyncPeer peer,
        string localDisplayName,
        string pin,
        LibraryService library,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var staging = Path.Combine(Path.GetTempPath(), "sonoric-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var client = new TcpClient();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(peer.Address, peer.Port, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "Не удалось подключиться. Порт может быть закрыт брандмауэром на другом компьютере.",
                    exception);
            }

            client.NoDelay = true;
            var stream = client.GetStream();
            await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage
            {
                Op = "auth",
                Pin = string.IsNullOrWhiteSpace(peer.Token) ? pin : null,
                Token = string.IsNullOrWhiteSpace(peer.Token) ? null : peer.Token,
                Name = string.IsNullOrWhiteSpace(localDisplayName) ? "Sonoric" : localDisplayName.Trim(),
                Device = _settings.DeviceId
            }, cancellationToken).ConfigureAwait(false);

            var reply = await SyncProtocol.ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(reply.Op, "ok", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(reply.Message ?? "Сопряжение отклонено.");
            }

            RememberHost(peer.Uuid, reply.Token ?? peer.Token, reply.Name ?? peer.DisplayName);

            await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage { Op = "pull" }, cancellationToken).ConfigureAwait(false);
            var manifest = await SyncProtocol.ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(manifest.Op, "manifest", StringComparison.Ordinal) || manifest.Index is null)
            {
                throw new InvalidOperationException("Хост не отдал библиотеку.");
            }

            var files = manifest.Files ?? new List<SyncFileEntry>();
            var total = Math.Max(1L, files.Sum(item => item.Size));
            long received = 0;
            while (true)
            {
                var message = await SyncProtocol.ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (string.Equals(message.Op, "done", StringComparison.Ordinal))
                {
                    break;
                }

                if (!string.Equals(message.Op, "file", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(message.Kind) ||
                    string.IsNullOrWhiteSpace(message.File))
                {
                    throw new InvalidDataException("Неизвестный кадр синхронизации.");
                }

                var destination = StagingPath(staging, message.Kind, message.File);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var output = File.Create(destination))
                {
                    var fileStart = received;
                    var reporter = progress is null
                        ? null
                        : new Progress<long>(copied =>
                        {
                            progress.Report(Math.Clamp((fileStart + copied) / (double)total, 0, 1) * 100);
                        });
                    await SyncProtocol.CopyExactAsync(stream, output, message.Size, cancellationToken, reporter).ConfigureAwait(false);
                }

                received += message.Size;
                progress?.Report(Math.Clamp(received / (double)total, 0, 1) * 100);
            }

            progress?.Report(100);
            return library.MergeSyncedLibrary(manifest.Index, staging);
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch
            {
            }
        }
    }

    private void RememberHost(Guid uuid, string token, string displayName)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var id = uuid.ToString("N");
        var existing = _settings.TrustedDevices.FirstOrDefault(item =>
            string.Equals(item.Uuid, id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _settings.TrustedDevices.Add(new TrustedDevice
            {
                Uuid = id,
                Token = token,
                DisplayName = displayName
            });
        }
        else
        {
            existing.Token = token;
            existing.DisplayName = displayName;
        }

        _settingsService.Save(_settings);
    }

    private TrustedDevice? FindTrusted(Guid uuid)
    {
        var id = uuid.ToString("N");
        return _settings.TrustedDevices.FirstOrDefault(item =>
            string.Equals(item.Uuid, id, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Token));
    }

    private static bool TryExtractPin(DiscoveredSyncHost host, string pinOrQr, out string pin)
    {
        pin = string.Empty;
        var raw = pinOrQr.Trim();
        if (raw.StartsWith('{'))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<SyncPairingPayload>(raw);
                if (payload is null || string.IsNullOrWhiteSpace(payload.Pin))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(payload.Uuid) &&
                    Guid.TryParse(payload.Uuid, out var uuid) &&
                    uuid != host.Uuid)
                {
                    return false;
                }

                pin = payload.Pin.Trim();
                return pin.Length == 6 && pin.All(char.IsDigit);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        pin = new string(raw.Where(char.IsDigit).ToArray());
        return pin.Length == 6;
    }

    private static string StagingPath(string root, string kind, string name)
    {
        var folder = kind switch
        {
            "library" => "library",
            "covers" => "covers",
            "collection-covers" => "collection-covers",
            _ => throw new InvalidDataException("Неизвестный тип файла.")
        };
        return Path.Combine(root, folder, Path.GetFileName(name));
    }

    private static Dictionary<string, string> ReadProperties(IService service)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in service.Properties)
        {
            foreach (var pair in set)
            {
                map[pair.Key] = pair.Value;
            }
        }

        return map;
    }

    private static bool TryGetAddress(IZeroconfHost host, out IPAddress? address)
    {
        if (!string.IsNullOrWhiteSpace(host.IPAddress) && IPAddress.TryParse(host.IPAddress, out address))
        {
            return true;
        }

        foreach (var text in host.IPAddresses)
        {
            if (IPAddress.TryParse(text, out address) && address.AddressFamily == AddressFamily.InterNetwork)
            {
                return true;
            }
        }

        foreach (var text in host.IPAddresses)
        {
            if (IPAddress.TryParse(text, out address))
            {
                return true;
            }
        }

        address = null;
        return false;
    }
}
