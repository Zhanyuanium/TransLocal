using System.Text.Json;
using Windows.Storage;
using TransLocal.Models;

namespace TransLocal.Services;

/// <summary>
/// Persists and loads AppSettings using ApplicationData.
/// </summary>
public static class SettingsService
{
    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<AppSettings> LoadAsync()
    {
        try
        {
            var folder = ApplicationData.Current.LocalFolder;
            var file = await folder.TryGetItemAsync(FileName);
            if (file is not StorageFile sf)
                return new AppSettings();

            var json = await FileIO.ReadTextAsync(sf);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static async Task SaveAsync(AppSettings settings)
    {
        var folder = ApplicationData.Current.LocalFolder;
        var file = await folder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await FileIO.WriteTextAsync(file, json);
    }
}
