namespace local_translate_provider.Services;

/// <summary>
/// Normalizes language codes between DeepL (EN, DE) and Google (en, de) formats.
/// </summary>
public static class LanguageCodeHelper
{
    /// <summary>
    /// Normalizes to two-letter lowercase (e.g. en, de) for internal use.
    /// </summary>
    public static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        var s = code.Trim();
        if (s.Length >= 2)
            return s[..2].ToLowerInvariant();
        return s.ToLowerInvariant();
    }

    /// <summary>
    /// Converts to DeepL format (uppercase, e.g. EN, DE).
    /// </summary>
    public static string ToDeepLFormat(string code) => Normalize(code).ToUpperInvariant();

    /// <summary>
    /// Converts to Google format (lowercase, e.g. en, de).
    /// </summary>
    public static string ToGoogleFormat(string code) => Normalize(code);
}
