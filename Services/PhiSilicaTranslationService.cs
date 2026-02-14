using System.Threading;
using local_translate_provider.Models;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;

namespace local_translate_provider.Services;

/// <summary>
/// Translation service using Phi Silica (Windows AI LanguageModel).
/// Requires Copilot+ PC with NPU. Not available in China.
/// </summary>
public sealed class PhiSilicaTranslationService : ITranslationService
{
    public async Task<string> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        var src = LanguageCodeHelper.Normalize(sourceLang);
        var tgt = LanguageCodeHelper.Normalize(targetLang);
        var prompt = $"Translate the following text from {src} to {tgt}. Only output the translation, nothing else:\n\n{text}";

        if (LanguageModel.GetReadyState() == AIFeatureReadyState.NotReady)
        {
            await LanguageModel.EnsureReadyAsync().AsTask(cancellationToken);
        }

        var options = new LanguageModelOptions();
        using var model = await LanguageModel.CreateAsync().AsTask(cancellationToken);
        var result = await model.GenerateResponseAsync(prompt, options).AsTask(cancellationToken);
        return result?.Text ?? string.Empty;
    }

    public async Task<TranslationServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var state = LanguageModel.GetReadyState();
            if (state == AIFeatureReadyState.NotReady)
            {
                try
                {
                    await LanguageModel.EnsureReadyAsync().AsTask(cancellationToken);
                }
                catch (Exception ex)
                {
                    return new TranslationServiceStatus(
                        false,
                        "Phi Silica 未就绪",
                        ex.Message);
                }
            }

            return state == AIFeatureReadyState.Ready
                ? new TranslationServiceStatus(true, "已就绪")
                : new TranslationServiceStatus(false, "需下载模型或申请 Limited Access 令牌");
        }
        catch (Exception ex)
        {
            return new TranslationServiceStatus(
                false,
                "Phi Silica 不可用（中国区域不可用，或需 Copilot+ PC）",
                ex.Message);
        }
    }
}
