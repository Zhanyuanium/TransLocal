using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TransLocal;
using TransLocal.Models;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;

namespace TransLocal.Services;

/// <summary>Foundry Local 翻译服务，支持多模型与运行策略。</summary>
public sealed class FoundryLocalTranslationService : ITranslationService
{
    private AppSettings _settings;
    private readonly ILogger? _logger;
    private Microsoft.AI.Foundry.Local.IModel? _loadedModel;
    private Microsoft.AI.Foundry.Local.OpenAIChatClient? _chatClient;
    private string? _loadedAlias;
    private FoundryExecutionStrategy _loadedStrategy;
    private FoundryDeviceType _loadedDevice;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public FoundryLocalTranslationService(AppSettings settings, ILogger? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    internal void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        InvalidateLoadedModel();
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
        var result = await chatClient.CompleteChatAsync(messages, cancellationToken);
        if (result.Choices?.Count > 0)
            return (result.Choices[0].Message?.Content ?? "").Trim();
        throw new InvalidOperationException("Translation returned no choices.");
    }

    public async Task<TranslationServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureManagerCreatedAsync(cancellationToken).ConfigureAwait(false);
            var mgr = FoundryLocalManager.Instance;
            var catalog = await mgr.GetCatalogAsync().ConfigureAwait(false);
            var model = await catalog.GetModelAsync(_settings.FoundryModelAlias).ConfigureAwait(false);

            if (model == null)
                return new TranslationServiceStatus(false, "StatusModelNotFoundFormat", new object[] { _settings.FoundryModelAlias }, "StatusModelNotFoundHint");

            var cached = await model.IsCachedAsync().ConfigureAwait(false);
            return new TranslationServiceStatus(
                true,
                cached ? "StatusReadyCached" : "StatusNeedDownload",
                MessageFormatArgs: null,
                DetailResourceKey: cached ? null : "StatusAutoDownloadHint",
                DetailRaw: null);
        }
        catch (Exception ex)
        {
            return new TranslationServiceStatus(false, "StatusFoundryUnavailable", DetailRaw: ex.Message);
        }
    }

    private async Task<(Microsoft.AI.Foundry.Local.IModel?, Microsoft.AI.Foundry.Local.OpenAIChatClient?)> EnsureModelLoadedAsync(CancellationToken ct)
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
                MemoryHelper.TrimWorkingSetAsync();
            }

            await EnsureManagerCreatedAsync(ct).ConfigureAwait(false);
            var mgr = FoundryLocalManager.Instance;
            var catalog = await mgr.GetCatalogAsync().ConfigureAwait(false);

            var modelIdOrAlias = await ResolveModelIdOrAliasAsync(catalog, _settings, ct).ConfigureAwait(false);
            Microsoft.AI.Foundry.Local.IModel? model = await catalog.GetModelVariantAsync(modelIdOrAlias, ct).ConfigureAwait(false)
                ?? (Microsoft.AI.Foundry.Local.IModel?)await catalog.GetModelAsync(modelIdOrAlias, ct).ConfigureAwait(false);
            if (model == null)
                throw new InvalidOperationException($"Model '{modelIdOrAlias}' not found.");

            await model.DownloadAsync(_ => { }).ConfigureAwait(false);
            await model.LoadAsync().ConfigureAwait(false);

            var chatClient = await model.GetChatClientAsync(ct).ConfigureAwait(false) as Microsoft.AI.Foundry.Local.OpenAIChatClient
                ?? throw new InvalidOperationException("Foundry Local chat client is not OpenAIChatClient.");
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

    /// <summary>按策略与设备类型选择模型变体。</summary>
    private static async Task<string> ResolveModelIdOrAliasAsync(
        Microsoft.AI.Foundry.Local.ICatalog catalog,
        AppSettings settings,
        CancellationToken ct)
    {
        if (settings.ExecutionStrategy == FoundryExecutionStrategy.HighPerformance)
            return settings.FoundryModelAlias;

        var targetDevice = settings.ExecutionStrategy == FoundryExecutionStrategy.PowerSaving
            ? DeviceType.CPU
            : MapToDeviceType(settings.ManualDeviceType);

        var models = await catalog.ListModelsAsync(ct).ConfigureAwait(false);
        var alias = settings.FoundryModelAlias;

        foreach (var m in models)
        {
            if (m.Variants == null) continue;
            foreach (var v in m.Variants)
            {
                var vAlias = v.Alias ?? m.Alias;
                if (!string.Equals(vAlias, alias, StringComparison.OrdinalIgnoreCase))
                    continue;
                var vDevice = v.Info?.Runtime?.DeviceType ?? DeviceType.Invalid;
                if (vDevice == targetDevice)
                {
                    var id = v.Id ?? "";
                    if (id.Length > 0) return id;
                }
            }
        }

        return settings.FoundryModelAlias;
    }

    private static DeviceType MapToDeviceType(FoundryDeviceType dt) => dt switch
    {
        FoundryDeviceType.CPU => DeviceType.CPU,
        FoundryDeviceType.GPU => DeviceType.GPU,
        FoundryDeviceType.NPU => DeviceType.NPU,
        _ => DeviceType.CPU
    };

    /// <summary>重复创建时复用 Instance。</summary>
    private static async Task EnsureManagerCreatedAsync(CancellationToken ct)
    {
        try
        {
            await FoundryLocalManager.CreateAsync(new Configuration
            {
                AppName = "TransLocal",
                LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Information,
                ModelCacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".foundry", "cache", "models")
            }, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.Message.Contains("already been created", StringComparison.OrdinalIgnoreCase)) { }
    }

    /// <summary>设置变更时强制重新加载，后台卸载当前模型并回收内存。</summary>
    public void InvalidateLoadedModel()
    {
        var toUnload = _loadedModel;
        _loadedModel = null;
        _chatClient = null;
        _loadedAlias = null;
        _loadedStrategy = default;
        _loadedDevice = default;
        if (toUnload != null)
            _ = UnloadAndTrimAsync(toUnload);
    }

    private static async Task UnloadAndTrimAsync(Microsoft.AI.Foundry.Local.IModel model)
    {
        try
        {
            await model.UnloadAsync().ConfigureAwait(false);
        }
        finally
        {
            MemoryHelper.TrimWorkingSetAsync();
        }
    }

    /// <summary>手动卸载当前模型，释放内存并归还工作集。</summary>
    public async Task UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadedModel != null)
            {
                await _loadedModel.UnloadAsync().ConfigureAwait(false);
                _loadedModel = null;
                _chatClient = null;
                _loadedAlias = null;
                _loadedStrategy = default;
                _loadedDevice = default;
            }
        }
        finally
        {
            _initLock.Release();
        }
        MemoryHelper.TrimWorkingSetAsync();
    }
}
