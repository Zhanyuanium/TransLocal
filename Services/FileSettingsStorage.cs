using System.Text.Json;
using TransLocal.Models;

namespace TransLocal.Services;

/// <summary>
/// File-based settings storage for CLI when Windows App SDK is not initialized (unpackaged).
/// Uses %LocalAppData%\TransLocal\settings.json.
/// </summary>
public static class FileSettingsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string GetSettingsPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(baseDir, "TransLocal");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static Task<AppSettings> LoadAsync()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
                return Task.FromResult(new AppSettings());

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return Task.FromResult(settings ?? new AppSettings());
        }
        catch
        {
            return Task.FromResult(new AppSettings());
        }
    }

    public static Task SaveAsync(AppSettings settings)
    {
        var path = GetSettingsPath();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
        return Task.CompletedTask;
    }
}
