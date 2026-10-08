using System.Text.Json;
using Sonoric.Models;

namespace Sonoric.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return EnsureIdentity(new AppSettings());
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return EnsureIdentity(loaded);
        }
        catch
        {
            return EnsureIdentity(new AppSettings());
        }
    }

    private AppSettings EnsureIdentity(AppSettings settings)
    {
        settings.TrustedDevices ??= new List<TrustedDevice>();
        var created = false;
        if (!Guid.TryParse(settings.DeviceId, out var id) || id == Guid.Empty)
        {
            settings.DeviceId = Guid.NewGuid().ToString("N");
            created = true;
        }

        if (created || !File.Exists(AppPaths.SettingsPath))
        {
            Save(settings);
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = AppPaths.SettingsPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, AppPaths.SettingsPath, overwrite: true);
        File.Delete(temp);
    }
}
