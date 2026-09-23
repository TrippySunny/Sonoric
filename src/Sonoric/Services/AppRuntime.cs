using System.Reflection;
using System.Runtime.InteropServices;

namespace Sonoric.Services;

public static class AppRuntime
{
    public const string GitHubOwner = "TrippySunny";
    public const string GitHubRepo = "Sonoric";

    public static Version CurrentVersion { get; } = ReadVersion();

    public static string VersionText => CurrentVersion.ToString(3);

    public static string Rid { get; } = ReadRid();

    public static string AppDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static Version ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var value = informational.Split('+', 2)[0].TrimStart('v', 'V');
            if (Version.TryParse(value, out var parsed))
            {
                return parsed;
            }
        }

        return assembly.GetName().Version ?? new Version(1, 0, 0);
    }

    private static string ReadRid()
    {
        var arch = RuntimeInformation.OSArchitecture;
        if (OperatingSystem.IsWindows())
        {
            return arch == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }

        return arch == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }
}
