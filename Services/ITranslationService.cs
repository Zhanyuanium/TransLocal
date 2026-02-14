using System.Threading;

namespace local_translate_provider.Services;

/// <summary>
/// Interface for translation services (Phi Silica, Foundry Local, etc.).
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Translates text from source language to target language.
    /// </summary>
    /// <param name="text">Text to translate.</param>
    /// <param name="sourceLang">Source language code (e.g. en, EN).</param>
    /// <param name="targetLang">Target language code (e.g. de, DE).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated text.</returns>
    Task<string> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the service is ready to translate.
    /// </summary>
    Task<TranslationServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public record TranslationServiceStatus(
    bool IsReady,
    string Message,
    string? Detail = null);
