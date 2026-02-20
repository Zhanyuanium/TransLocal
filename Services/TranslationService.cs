using TransLocal.Models;

namespace TransLocal.Services;

/// <summary>
/// Facade that routes translation requests to the configured backend (Phi Silica or Foundry Local).
/// </summary>
public sealed class TranslationService : ITranslationService
{
    private AppSettings _settings;
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
        _settings = settings;
        _foundryLocal.UpdateSettings(settings);
    }

    private ITranslationService GetBackend() =>
        _settings.TranslationBackend == TranslationBackend.PhiSilica ? _phiSilica : _foundryLocal;

    public Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default) =>
        GetBackend().TranslateAsync(text, sourceLang, targetLang, cancellationToken);

    public Task<TranslationServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        GetBackend().GetStatusAsync(cancellationToken);

    /// <summary>卸载 Foundry Local 模型，释放内存。Phi Silica 无持久模型，无操作。</summary>
    public Task UnloadModelAsync(CancellationToken cancellationToken = default) =>
        _foundryLocal.UnloadModelAsync(cancellationToken);
}
