using System.Collections.Generic;
using System.Threading;
using local_translate_provider.Models;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;

namespace local_translate_provider.Services;

/// <summary>
/// Translation service using Foundry Local (local LLM via WinML).
/// Supports multiple models and execution strategies.
/// </summary>
public sealed class FoundryLocalTranslationService : ITranslationService
{
    private readonly AppSettings _settings;
    private readonly ILogger? _logger;
    private Microsoft.AI.Foundry.Local.Model? _loadedModel;
    private Betalgo.Ranul.OpenAI.Interfaces.IChatCompletionService? _chatClient;
    private string? _loadedAlias;
    private FoundryExecutionStrategy _loadedStrategy;
    private FoundryDeviceType _loadedDevice;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public FoundryLocalTranslationService(AppSettings settings, ILogger? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        var src = LanguageCodeHelper.Normalize(sourceLang);
        var tgt = LanguageCodeHelper.Normalize(targetLang);
        var prompt = $"Translate the following text from {src} to {tgt}. Only output the translation, nothing else:\n\n{text}";

        var (model, chatClient) = await EnsureModelLoadedAsync(cancellationToken);
        if (model == null || chatClient == null)
            throw new InvalidOperationException("Foundry Local model not loaded. Check model alias and execution strategy.");

        var messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } };
        var modelId = model.Id ?? _settings.FoundryModelAlias;
        var request = new ChatCompletionCreateRequest { Messages = messages, Model = modelId };
        var result = await chatClient.CreateCompletion(request, modelId, cancellationToken);
        if (result.Successful && result.Choices?.Count > 0)
            return (result.Choices[0].Message?.Content ?? "").Trim();
        throw new InvalidOperationException(result.Error?.Message ?? "Translation failed");
    }

    public async Task<TranslationServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await FoundryLocalManager.CreateAsync(new Configuration
            {
                AppName = "local-translate-provider",
                LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Information
            }, _logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ConfigureAwait(false);

            var mgr = FoundryLocalManager.Instance;
            var catalog = await mgr.GetCatalogAsync().ConfigureAwait(false);
            var model = await catalog.GetModelAsync(_settings.FoundryModelAlias).ConfigureAwait(false);

            if (model == null)
                return new TranslationServiceStatus(false, $"模型 '{_settings.FoundryModelAlias}' 未找到", "请检查模型别名或运行 foundry model list 查看可用模型");

            var cached = await model.IsCachedAsync().ConfigureAwait(false);
            return new TranslationServiceStatus(
                true,
                cached ? "已就绪（模型已缓存）" : "需首次下载模型",
                cached ? null : "首次翻译时将自动下载");
        }
        catch (Exception ex)
        {
            return new TranslationServiceStatus(false, "Foundry Local 不可用", ex.Message);
        }
    }

    private async Task<(Microsoft.AI.Foundry.Local.Model?, Betalgo.Ranul.OpenAI.Interfaces.IChatCompletionService?)> EnsureModelLoadedAsync(CancellationToken ct)
    {
        var needsReload = _loadedModel == null
            || _loadedAlias != _settings.FoundryModelAlias
            || _loadedStrategy != _settings.ExecutionStrategy
            || (_settings.ExecutionStrategy == FoundryExecutionStrategy.Manual && _loadedDevice != _settings.ManualDeviceType);

        if (!needsReload && _loadedModel != null && _chatClient != null)
            return (_loadedModel, _chatClient);

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            needsReload = _loadedModel == null
                || _loadedAlias != _settings.FoundryModelAlias
                || _loadedStrategy != _settings.ExecutionStrategy
                || (_settings.ExecutionStrategy == FoundryExecutionStrategy.Manual && _loadedDevice != _settings.ManualDeviceType);

            if (!needsReload && _loadedModel != null && _chatClient != null)
                return (_loadedModel, _chatClient);

            if (_loadedModel != null)
            {
                await _loadedModel.UnloadAsync().ConfigureAwait(false);
                _loadedModel = null;
                _chatClient = null;
            }

            await FoundryLocalManager.CreateAsync(new Configuration
            {
                AppName = "local-translate-provider",
                LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Information
            }, _logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ConfigureAwait(false);

            var mgr = FoundryLocalManager.Instance;
            var catalog = await mgr.GetCatalogAsync().ConfigureAwait(false);

            var modelIdOrAlias = ResolveModelIdOrAlias(_settings);
            var model = await catalog.GetModelAsync(modelIdOrAlias).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Model '{modelIdOrAlias}' not found.");

            await model.DownloadAsync(_ => { }).ConfigureAwait(false);
            await model.LoadAsync().ConfigureAwait(false);

            var chatClientRaw = await model.GetChatClientAsync().ConfigureAwait(false);
            var chatClient = chatClientRaw as Betalgo.Ranul.OpenAI.Interfaces.IChatCompletionService;
            if (chatClient == null)
                throw new InvalidOperationException("Foundry Local chat client does not implement IChatCompletionService.");
            _loadedModel = model;
            _chatClient = chatClient;
            _loadedAlias = _settings.FoundryModelAlias;
            _loadedStrategy = _settings.ExecutionStrategy;
            _loadedDevice = _settings.ManualDeviceType;

            return (model, chatClient);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string ResolveModelIdOrAlias(AppSettings settings)
    {
        if (settings.ExecutionStrategy == FoundryExecutionStrategy.HighPerformance)
            return settings.FoundryModelAlias;

        if (settings.ExecutionStrategy == FoundryExecutionStrategy.PowerSaving)
        {
            var cpuSuffix = "-generic-cpu";
            var alias = settings.FoundryModelAlias;
            if (alias.EndsWith(cpuSuffix, StringComparison.OrdinalIgnoreCase))
                return alias;
            return alias + cpuSuffix;
        }

        if (settings.ExecutionStrategy == FoundryExecutionStrategy.Manual)
        {
            var suffix = settings.ManualDeviceType switch
            {
                FoundryDeviceType.CPU => "-generic-cpu",
                FoundryDeviceType.GPU => "-generic-cuda",
                FoundryDeviceType.NPU => "-generic-qnn",
                FoundryDeviceType.WebGPU => "-generic-webgpu",
                _ => "-generic-cpu"
            };
            var alias = settings.FoundryModelAlias;
            if (alias.EndsWith("-generic-cpu", StringComparison.OrdinalIgnoreCase) ||
                alias.EndsWith("-generic-cuda", StringComparison.OrdinalIgnoreCase) ||
                alias.EndsWith("-generic-qnn", StringComparison.OrdinalIgnoreCase) ||
                alias.EndsWith("-generic-webgpu", StringComparison.OrdinalIgnoreCase))
            {
                var idx = alias.LastIndexOf('-');
                if (idx > 0) alias = alias[..idx];
            }
            return alias + suffix;
        }

        return settings.FoundryModelAlias;
    }

    /// <summary>
    /// Call when settings change to force reload on next translate.
    /// </summary>
    public void InvalidateLoadedModel()
    {
        _loadedModel = null;
        _chatClient = null;
        _loadedAlias = null;
    }
}
