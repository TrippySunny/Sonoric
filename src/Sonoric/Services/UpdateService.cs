using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Sonoric.Models;

namespace Sonoric.Services;

public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = CreateClient();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var rid = AppRuntime.Rid;
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{AppRuntime.GitHubOwner}/{AppRuntime.GitHubRepo}/releases");
        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return UpdateCheckResult.Fail("Не удалось связаться с GitHub");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, JsonOptions, cancellationToken);
        if (releases is null || releases.Count == 0)
        {
            return UpdateCheckResult.None("Релизов пока нет");
        }

        foreach (var release in releases)
        {
            if (release.Draft || release.Prerelease)
            {
                continue;
            }

            if (!TryParseVersion(release.TagName, out var version) || version <= AppRuntime.CurrentVersion)
            {
                continue;
            }

            var asset = release.Assets?.FirstOrDefault(item => MatchesPlatform(item.Name, rid));
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                continue;
            }

            return UpdateCheckResult.Found(new UpdateInfo
            {
                Version = version,
                Tag = release.TagName ?? version.ToString(),
                Title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName ?? version.ToString() : release.Name,
                AssetName = asset.Name ?? string.Empty,
                DownloadUrl = asset.BrowserDownloadUrl,
                SizeBytes = asset.Size,
                Rid = rid
            });
        }

        var newerWithoutAsset = releases.Any(release =>
            !release.Draft &&
            !release.Prerelease &&
            TryParseVersion(release.TagName, out var version) &&
            version > AppRuntime.CurrentVersion);
        if (newerWithoutAsset)
        {
            return UpdateCheckResult.None($"Для {rid} обновлений нет");
        }

        return UpdateCheckResult.None("Установлена последняя версия");
    }

    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "sonoric-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var archive = Path.Combine(work, update.AssetName);
        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? update.SizeBytes;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(archive))
        {
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                read += n;
                if (total > 0)
                {
                    progress?.Report(Math.Clamp(read * 100.0 / total, 0, 100));
                }
            }
        }

        progress?.Report(100);
        var extracted = Path.Combine(work, "payload");
        Directory.CreateDirectory(extracted);
        Extract(archive, extracted);
        File.Delete(archive);
        if (FindPayloadDirectory(extracted) is not { } payload)
        {
            throw new InvalidOperationException("В архиве нет файлов Sonoric");
        }

        return payload;
    }

    public void LaunchInstaller(string payloadDirectory)
    {
        var appDir = AppRuntime.AppDirectory;
        var script = Path.Combine(Path.GetTempPath(), "sonoric-apply.sh");
        File.WriteAllText(script, """
#!/usr/bin/env bash
set -euo pipefail
APPDIR="$1"
SRC="$2"
PID="$3"
BIN="$APPDIR/Sonoric"
while kill -0 "$PID" 2>/dev/null; do
  sleep 0.2
done
sleep 0.4
cp -a "$SRC"/. "$APPDIR"/
chmod +x "$BIN" || true
rm -rf "$(dirname "$SRC")"
cd "$APPDIR"
exec "$BIN"
""");
        var chmod = Process.Start(new ProcessStartInfo("/bin/chmod")
        {
            ArgumentList = { "+x", script },
            UseShellExecute = false
        });
        chmod?.WaitForExit();
        Process.Start(new ProcessStartInfo("/bin/bash")
        {
            ArgumentList = { script, appDir, payloadDirectory, Environment.ProcessId.ToString() },
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    public static bool MatchesPlatform(string? assetName, string rid)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return false;
        }

        var name = assetName.ToLowerInvariant();
        if (rid.StartsWith("linux", StringComparison.Ordinal))
        {
            if (ContainsAny(name, "win", "windows", "osx", "macos", "darwin"))
            {
                return false;
            }

            if (!name.Contains("linux", StringComparison.Ordinal))
            {
                return false;
            }

            var arm = ContainsAny(name, "arm64", "aarch64");
            var x64 = ContainsAny(name, "x64", "x86_64", "amd64");
            if (rid == "linux-arm64")
            {
                return arm;
            }

            return x64 || !arm;
        }

        if (rid.StartsWith("win", StringComparison.Ordinal))
        {
            if (ContainsAny(name, "linux", "osx", "macos", "darwin"))
            {
                return false;
            }

            return name.Contains("win", StringComparison.Ordinal) || name.Contains("windows", StringComparison.Ordinal);
        }

        if (rid.StartsWith("osx", StringComparison.Ordinal))
        {
            if (ContainsAny(name, "linux", "win", "windows"))
            {
                return false;
            }

            var arm = ContainsAny(name, "arm64", "aarch64");
            if (rid == "osx-arm64")
            {
                return ContainsAny(name, "osx", "macos", "darwin") && arm;
            }

            return ContainsAny(name, "osx", "macos", "darwin") && !arm;
        }

        return false;
    }

    private static void Extract(string archive, string destination)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
            return;
        }

        var process = Process.Start(new ProcessStartInfo("tar")
        {
            ArgumentList = { "-xzf", archive, "-C", destination },
            UseShellExecute = false,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Не удалось распаковать архив");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Не удалось распаковать архив");
        }
    }

    private static string? FindPayloadDirectory(string extracted)
    {
        var executable = Directory
            .EnumerateFiles(extracted, "Sonoric", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (executable is null)
        {
            executable = Directory
                .EnumerateFiles(extracted, "Sonoric.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
        }

        return executable is null ? null : Path.GetDirectoryName(executable);
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var dash = value.IndexOf('-');
        if (dash > 0)
        {
            value = value[..dash];
        }

        return Version.TryParse(value, out var parsed) && parsed is not null && (version = parsed) is not null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.Ordinal));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sonoric", AppRuntime.VersionText));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed class GitHubRelease
    {
        public string? TagName { get; set; }
        public string? Name { get; set; }
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        public string? Name { get; set; }
        public string? BrowserDownloadUrl { get; set; }
        public long Size { get; set; }
    }
}

public sealed class UpdateCheckResult
{
    public bool Ok { get; private init; }
    public string Message { get; private init; } = string.Empty;
    public UpdateInfo? Update { get; private init; }

    public static UpdateCheckResult Found(UpdateInfo update)
    {
        return new UpdateCheckResult
        {
            Ok = true,
            Update = update,
            Message = $"Доступна версия {update.Version.ToString(3)}"
        };
    }

    public static UpdateCheckResult None(string message)
    {
        return new UpdateCheckResult { Ok = true, Message = message };
    }

    public static UpdateCheckResult Fail(string message)
    {
        return new UpdateCheckResult { Ok = false, Message = message };
    }
}
