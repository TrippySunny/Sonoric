using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Sonoric.Services;

internal static class LinuxVlcNative
{
    private static IntPtr _libVlcHandle;

    public static void Initialize()
    {
        var libraryPath = FindLibrary();
        var libraryDirectory = libraryPath is null ? null : Path.GetDirectoryName(libraryPath);
        TrySetPluginPath(libraryDirectory);
        NativeLibrary.SetDllImportResolver(typeof(Core).Assembly, Resolve);
        if (string.IsNullOrWhiteSpace(libraryDirectory))
        {
            Core.Initialize();
            return;
        }

        try
        {
            Core.Initialize(libraryDirectory);
        }
        catch
        {
            Core.Initialize();
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!IsLibVlc(libraryName))
        {
            return IntPtr.Zero;
        }

        if (_libVlcHandle != IntPtr.Zero)
        {
            return _libVlcHandle;
        }

        var path = FindLibrary();
        if (path is not null && NativeLibrary.TryLoad(path, out _libVlcHandle))
        {
            return _libVlcHandle;
        }

        foreach (var name in new[] { "libvlc.so.5", "libvlc.so", "libvlc" })
        {
            if (NativeLibrary.TryLoad(name, out _libVlcHandle))
            {
                return _libVlcHandle;
            }
        }

        return IntPtr.Zero;
    }

    private static bool IsLibVlc(string libraryName)
    {
        return libraryName.Contains("libvlc", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(libraryName, "vlc", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindLibrary()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "libvlc.so.5");
        yield return Path.Combine(baseDir, "lib", "libvlc.so.5");
        yield return Path.Combine(baseDir, "vlc", "libvlc.so.5");

        foreach (var path in EnumerateLdconfig())
        {
            yield return path;
        }

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "aarch64-linux-gnu",
            Architecture.Arm => "arm-linux-gnueabihf",
            _ => "x86_64-linux-gnu"
        };

        yield return $"/usr/lib/{arch}/libvlc.so.5";
        yield return $"/lib/{arch}/libvlc.so.5";
        yield return "/usr/lib64/libvlc.so.5";
        yield return "/usr/lib/libvlc.so.5";
        yield return "/lib64/libvlc.so.5";
        yield return "/lib/libvlc.so.5";
        yield return "/usr/local/lib64/libvlc.so.5";
        yield return "/usr/local/lib/libvlc.so.5";
    }

    private static IEnumerable<string> EnumerateLdconfig()
    {
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo("ldconfig", "-p")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            yield break;
        }

        if (process is null)
        {
            yield break;
        }

        using (process)
        {
            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                if (!line.Contains("libvlc.so", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = line.LastIndexOf("=>", StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                var path = line[(separator + 2)..].Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }

            process.WaitForExit(1000);
        }
    }

    private static void TrySetPluginPath(string? libraryDirectory)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH")))
        {
            return;
        }

        foreach (var candidate in EnumeratePluginDirectories(libraryDirectory))
        {
            if (Directory.Exists(candidate))
            {
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", candidate);
                return;
            }
        }
    }

    private static IEnumerable<string> EnumeratePluginDirectories(string? libraryDirectory)
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "plugins");
        yield return Path.Combine(baseDir, "vlc", "plugins");

        if (!string.IsNullOrWhiteSpace(libraryDirectory))
        {
            yield return Path.Combine(libraryDirectory, "vlc", "plugins");
            var parent = Directory.GetParent(libraryDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                yield return Path.Combine(parent, "vlc", "plugins");
            }
        }

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "aarch64-linux-gnu",
            Architecture.Arm => "arm-linux-gnueabihf",
            _ => "x86_64-linux-gnu"
        };

        yield return $"/usr/lib/{arch}/vlc/plugins";
        yield return "/usr/lib64/vlc/plugins";
        yield return "/usr/lib/vlc/plugins";
        yield return "/usr/local/lib/vlc/plugins";
    }
}
