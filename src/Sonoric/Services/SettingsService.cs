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
            var created = new AppSettings();
            Save(created);
            return created;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
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
