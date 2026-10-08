using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Makaretu.Dns;
using QRCoder;
using Sonoric.Models;

namespace Sonoric.Services;

public sealed class LocalSyncHost : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly LibraryService _library;
    private readonly object _gate = new();
    private CancellationTokenSource? _lifetime;
    private TcpListener? _listener;
    private ServiceDiscovery? _discovery;
    private ServiceProfile? _profile;
    private string? _pin;
    private string? _pinHash;
    private string _displayName = "Sonoric";

    public LocalSyncHost(AppSettings settings, SettingsService settingsService, LibraryService library)
    {
        _settings = settings;
        _settingsService = settingsService;
        _library = library;
        // Вечный паспорт устройства хранится в ~/.local/share/Sonoric/settings.json (поле deviceId).
        if (!Guid.TryParse(settings.DeviceId, out var id) || id == Guid.Empty)
        {
            id = Guid.NewGuid();
            settings.DeviceId = id.ToString("N");
            settingsService.Save(settings);
        }

        DeviceId = id;
    }

    public Guid DeviceId { get; }
    public string? Pin
    {
        get
        {
            lock (_gate)
            {
                return _pin;
            }
        }
    }

    public bool IsAdvertising
    {
        get
        {
            lock (_gate)
            {
                return _listener is not null;
            }
        }
    }

    public async Task StartAsync(string displayName, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        var name = string.IsNullOrWhiteSpace(displayName) ? "Sonoric" : displayName.Trim();
        var pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var pinHash = SyncProtocol.HashPin(pin);
        var lifetime = new CancellationTokenSource();

        TcpListener listener;
        int port;
        try
        {
            listener = new TcpListener(IPAddress.Any, SyncProtocol.DefaultPort);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException(
                "Не удалось открыть порт синхронизации. Разрешите входящие подключения для Sonoric в брандмауэре локальной сети.",
                exception);
        }

        ServiceDiscovery? discovery = null;
        ServiceProfile? profile = null;
        try
        {
            discovery = new ServiceDiscovery();
            profile = new ServiceProfile($"Sonoric-{DeviceId.ToString("N")[..8]}", SyncProtocol.ServiceType, (ushort)port);
            profile.AddProperty("uuid", DeviceId.ToString("N"));
            profile.AddProperty("pinhash", pinHash);
            profile.AddProperty("name", name);
            discovery.Advertise(profile);
            discovery.Announce(profile);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            listener.Stop();
            discovery?.Dispose();
            throw new InvalidOperationException(
                "Не удалось объявить устройство в локальной сети (mDNS). Проверьте, что multicast не блокируется брандмауэром.",
                exception);
        }

        lock (_gate)
        {
            _pin = pin;
            _pinHash = pinHash;
            _displayName = name;
            _listener = listener;
            _discovery = discovery;
            _profile = profile;
            _lifetime = lifetime;
        }

        _ = Task.Run(() => AcceptLoopAsync(listener, lifetime.Token), cancellationToken);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetime;
        TcpListener? listener;
        ServiceDiscovery? discovery;
        ServiceProfile? profile;
        lock (_gate)
        {
            lifetime = _lifetime;
            listener = _listener;
            discovery = _discovery;
            profile = _profile;
            _lifetime = null;
            _listener = null;
            _discovery = null;
            _profile = null;
            _pin = null;
            _pinHash = null;
        }

        if (lifetime is not null)
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            listener?.Stop();
        }
        catch (SocketException)
        {
        }

        try
        {
            if (profile is not null)
            {
                discovery?.Unadvertise(profile);
            }
        }
        catch
        {
        }

        discovery?.Dispose();
        lifetime?.Dispose();
    }

    public string CreatePairingPayload()
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_pin))
            {
                throw new InvalidOperationException("Синхронизация выключена.");
            }

            return JsonSerializer.Serialize(new SyncPairingPayload
            {
                Uuid = DeviceId.ToString("N"),
                Pin = _pin
            });
        }
    }

    public byte[] CreateQrPng()
    {
        var payload = CreatePairingPayload();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        return qr.GetGraphic(8, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var hello = await SyncProtocol.ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(hello.Op, "auth", StringComparison.Ordinal))
                {
                    await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage { Op = "err", Message = "Ожидался вход." }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                string? pin;
                string? pinHash;
                string displayName;
                lock (_gate)
                {
                    pin = _pin;
                    pinHash = _pinHash;
                    displayName = _displayName;
                }

                var tokenOk = !string.IsNullOrWhiteSpace(hello.Token) &&
                              _settings.TrustedDevices.Any(item =>
                                  string.Equals(item.Token, hello.Token, StringComparison.Ordinal));
                var pinOk = !string.IsNullOrWhiteSpace(hello.Pin) &&
                            !string.IsNullOrWhiteSpace(pinHash) &&
                            SyncProtocol.PinsMatch(hello.Pin, pinHash) &&
                            string.Equals(hello.Pin.Trim(), pin, StringComparison.Ordinal);

                if (!tokenOk && !pinOk)
                {
                    await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage { Op = "err", Message = "Неверный PIN или нет доверия." }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var peerId = hello.Device ?? Guid.NewGuid().ToString("N");
                var peerName = string.IsNullOrWhiteSpace(hello.Name) ? "Sonoric" : hello.Name.Trim();
                var token = tokenOk
                    ? hello.Token!
                    : RememberPeer(peerId, peerName);

                await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage
                {
                    Op = "ok",
                    Token = token,
                    Name = displayName,
                    Uuid = DeviceId.ToString("N")
                }, cancellationToken).ConfigureAwait(false);

                var request = await SyncProtocol.ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(request.Op, "pull", StringComparison.Ordinal))
                {
                    return;
                }

                var index = _library.ExportIndex();
                var files = BuildFileList(index);
                await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage
                {
                    Op = "manifest",
                    Index = index,
                    Files = files
                }, cancellationToken).ConfigureAwait(false);

                foreach (var file in files)
                {
                    var path = ResolveLocalFile(file.Kind, file.Name);
                    if (path is null)
                    {
                        continue;
                    }

                    await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage
                    {
                        Op = "file",
                        Kind = file.Kind,
                        File = file.Name,
                        Size = file.Size
                    }, cancellationToken).ConfigureAwait(false);

                    await using var input = File.OpenRead(path);
                    await SyncProtocol.CopyExactAsync(input, stream, file.Size, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                await SyncProtocol.WriteMessageAsync(stream, new SyncWireMessage { Op = "done" }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or InvalidDataException or EndOfStreamException or OperationCanceledException)
            {
            }
        }
    }

    private string RememberPeer(string peerId, string peerName)
    {
        var existing = _settings.TrustedDevices.FirstOrDefault(item =>
            string.Equals(item.Uuid, peerId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Token))
        {
            existing.DisplayName = peerName;
            _settingsService.Save(_settings);
            return existing.Token;
        }

        var token = Guid.NewGuid().ToString("N");
        if (existing is null)
        {
            _settings.TrustedDevices.Add(new TrustedDevice
            {
                Uuid = peerId,
                Token = token,
                DisplayName = peerName
            });
        }
        else
        {
            existing.Token = token;
            existing.DisplayName = peerName;
        }

        _settingsService.Save(_settings);
        return token;
    }

    private List<SyncFileEntry> BuildFileList(LibraryIndex index)
    {
        var files = new List<SyncFileEntry>();
        foreach (var track in index.Tracks)
        {
            var path = _library.GetTrackPath(track);
            if (File.Exists(path))
            {
                files.Add(new SyncFileEntry
                {
                    Kind = "library",
                    Name = track.StoredFileName,
                    Size = new FileInfo(path).Length
                });
            }

            var cover = _library.GetCoverPath(track);
            if (cover is not null && !string.IsNullOrWhiteSpace(track.CoverFileName))
            {
                files.Add(new SyncFileEntry
                {
                    Kind = "covers",
                    Name = track.CoverFileName,
                    Size = new FileInfo(cover).Length
                });
            }
        }

        foreach (var collection in index.Collections)
        {
            if (string.IsNullOrWhiteSpace(collection.CoverFileName))
            {
                continue;
            }

            var path = Path.Combine(AppPaths.CollectionCoversDirectory, collection.CoverFileName);
            if (!File.Exists(path))
            {
                continue;
            }

            files.Add(new SyncFileEntry
            {
                Kind = "collection-covers",
                Name = collection.CoverFileName,
                Size = new FileInfo(path).Length
            });
        }

        return files;
    }

    private static string? ResolveLocalFile(string kind, string name)
    {
        var safe = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(safe))
        {
            return null;
        }

        var directory = kind switch
        {
            "library" => AppPaths.LibraryDirectory,
            "covers" => AppPaths.CoversDirectory,
            "collection-covers" => AppPaths.CollectionCoversDirectory,
            _ => null
        };
        if (directory is null)
        {
            return null;
        }

        var path = Path.Combine(directory, safe);
        return File.Exists(path) ? path : null;
    }
}
