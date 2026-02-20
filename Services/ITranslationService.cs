using System.Threading;

namespace TransLocal.Services;

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

/// <summary>
/// Status from translation service. Message and Detail use resource keys for localization.
/// </summary>
/// <param name="IsReady">Whether the service is ready to translate.</param>
/// <param name="MessageResourceKey">Resource key for the main status message.</param>
/// <param name="MessageFormatArgs">Optional format arguments for the message (e.g. model alias).</param>
/// <param name="DetailResourceKey">Optional resource key for the detail line (localized hint).</param>
/// <param name="DetailRaw">Optional raw detail (e.g. exception message), not localized.</param>
public record TranslationServiceStatus(
    bool IsReady,
    string MessageResourceKey,
    object[]? MessageFormatArgs = null,
    string? DetailResourceKey = null,
    string? DetailRaw = null);
