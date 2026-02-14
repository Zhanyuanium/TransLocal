using local_translate_provider.Models;

namespace local_translate_provider.Services;

/// <summary>
/// Facade that routes translation requests to the configured backend (Phi Silica or Foundry Local).
/// </summary>
public sealed class TranslationService : ITranslationService
{
    private readonly AppSettings _settings;
    private readonly PhiSilicaTranslationService _phiSilica;
    private readonly FoundryLocalTranslationService _foundryLocal;

    public TranslationService(AppSettings settings)
    {
        _settings = settings;
        _phiSilica = new PhiSilicaTranslationService();
        _foundryLocal = new FoundryLocalTranslationService(settings);
    }

    public void UpdateSettings(AppSettings settings)
    {
        _foundryLocal.InvalidateLoadedModel();
    }

    private ITranslationService GetBackend() =>
        _settings.TranslationBackend == TranslationBackend.PhiSilica ? _phiSilica : _foundryLocal;

    public Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default) =>
        GetBackend().TranslateAsync(text, sourceLang, targetLang, cancellationToken);

    public Task<TranslationServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        GetBackend().GetStatusAsync(cancellationToken);
}
