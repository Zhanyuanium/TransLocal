using System.Text;
using System.Text.Json;
using TransLocal.Services;

namespace TransLocal.ApiAdapters;

/// <summary>
/// Handles DeepL-format translation requests and responses.
/// </summary>
public static class DeepLApiAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public record DeepLRequest(string[] Text, string TargetLang, string? SourceLang);
    public record DeepLTranslation(string? DetectedSourceLanguage, string Text);
    public record DeepLResponse(DeepLTranslation[] Translations);

    public static async Task<(string[] texts, string targetLang, string? sourceLang)> ParseRequestAsync(Stream body, CancellationToken ct)
    {
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(body, JsonOptions, ct);
        var textArr = doc.GetProperty("text");
        var texts = textArr.ValueKind == JsonValueKind.Array
            ? textArr.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : new[] { textArr.GetString() ?? "" };
        var targetLang = doc.TryGetProperty("target_lang", out var tl) ? tl.GetString() ?? "en" : "en";
        var sourceLang = doc.TryGetProperty("source_lang", out var sl) ? sl.GetString() : null;
        return (texts, targetLang, sourceLang);
    }

    public static string ToResponse(string translatedText, string? detectedSourceLang)
    {
        var resp = new DeepLResponse(new[]
        {
            new DeepLTranslation(detectedSourceLang ?? "en", translatedText)
        });
        return JsonSerializer.Serialize(resp, JsonOptions);
    }
}
