using System.Text.Json;
using TransLocal.Services;

namespace TransLocal.ApiAdapters;

/// <summary>
/// Handles Google Translate v3-format translation requests and responses.
/// </summary>
public static class GoogleApiAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public record GoogleRequest(string[] Contents, string? SourceLanguageCode, string TargetLanguageCode);
    public record GoogleTranslation(string TranslatedText);
    public record GoogleResponse(GoogleTranslation[] Translations, object[] GlossaryTranslations);

    public static async Task<(string[] contents, string? sourceLang, string targetLang)> ParseRequestAsync(Stream body, CancellationToken ct)
    {
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(body, JsonOptions, ct);
        var contentsArr = doc.GetProperty("contents");
        var contents = contentsArr.ValueKind == JsonValueKind.Array
            ? contentsArr.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : new[] { contentsArr.GetString() ?? "" };
        var targetLang = doc.TryGetProperty("targetLanguageCode", out var tl) ? tl.GetString() ?? "en" : "en";
        var sourceLang = doc.TryGetProperty("sourceLanguageCode", out var sl) ? sl.GetString() : null;
        return (contents, sourceLang, targetLang);
    }

    public static string ToResponse(string translatedText)
    {
        var resp = new GoogleResponse(
            new[] { new GoogleTranslation(translatedText) },
            Array.Empty<object>());
        return JsonSerializer.Serialize(resp, JsonOptions);
    }
}
