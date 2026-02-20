namespace TransLocal.Models;

/// <summary>
/// Application settings for the translation provider.
/// </summary>
public sealed class AppSettings
{
    /// <summary>HTTP server port. Default 52860.</summary>
    public int Port { get; set; } = 52860;

    /// <summary>Enable DeepL-format endpoint.</summary>
    public bool EnableDeepLEndpoint { get; set; } = true;

    /// <summary>Enable Google Translate-format endpoint.</summary>
    public bool EnableGoogleEndpoint { get; set; } = true;

    /// <summary>Optional API key for access control.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Run at Windows startup.</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>Minimize to tray on startup.</summary>
    public bool MinimizeToTrayOnStartup { get; set; } = true;

    /// <summary>Translation backend: PhiSilica or FoundryLocal.</summary>
    public TranslationBackend TranslationBackend { get; set; } = TranslationBackend.FoundryLocal;

    /// <summary>Foundry Local model alias (e.g. phi-4-mini, qwen2.5-0.5b).</summary>
    public string FoundryModelAlias { get; set; } = "phi-4-mini";

    /// <summary>Foundry Local execution strategy.</summary>
    public FoundryExecutionStrategy ExecutionStrategy { get; set; } = FoundryExecutionStrategy.HighPerformance;

    /// <summary>Manually specified device when ExecutionStrategy is Manual.</summary>
    public FoundryDeviceType ManualDeviceType { get; set; } = FoundryDeviceType.CPU;

    /// <summary>Enable IPC debug log (GUI About page only; CLI uses --debug-log).</summary>
    public bool DebugLogEnabled { get; set; }
}

public enum TranslationBackend
{
    PhiSilica,
    FoundryLocal
}

public enum FoundryExecutionStrategy
{
    /// <summary>Prefer NPU/CPU for low power.</summary>
    PowerSaving,

    /// <summary>Auto-select best hardware (GPU/NPU first).</summary>
    HighPerformance,

    /// <summary>User manually selects device.</summary>
    Manual
}

public enum FoundryDeviceType
{
    CPU,
    GPU,
    NPU
}
