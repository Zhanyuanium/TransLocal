using System.Text.Json;
using local_translate_provider.Models;

namespace local_translate_provider.Services;

/// <summary>
/// File-based settings storage for CLI when Windows App SDK is not initialized (unpackaged).
/// Uses %LocalAppData%\local-translate-provider\settings.json.
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
        var dir = Path.Combine(baseDir, "local-translate-provider");
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
