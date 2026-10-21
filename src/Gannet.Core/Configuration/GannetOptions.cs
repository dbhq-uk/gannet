using Gannet.Core.Domain;

namespace Gannet.Core.Configuration;

/// <summary>
/// The "Gannet" configuration section. Keys live here and nowhere else.
/// This type holds secrets (API keys, the S3 secret, the proxy password). It must never be serialised or logged.
/// </summary>
public sealed class GannetOptions
{
    public const string Section = "Gannet";

    public DefaultsOptions Defaults { get; set; } = new();

    public ResearchBudget Budget { get; set; } = new();

    public RunsOptions Runs { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();

    public ReadersOptions Readers { get; set; } = new();

    public Dictionary<string, ModelOptions> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ServiceOptions> Search { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? PromptsDirectory { get; set; }

    public string? CredibilityRulesFile { get; set; }

    /// <summary>The address people reach this instance at, for live links in API and MCP answers.</summary>
    public string? PublicUrl { get; set; }
}

public sealed class DefaultsOptions
{
    public string? Main { get; set; }

    public string? Fast { get; set; }

    public string? Search { get; set; }

    /// <summary>Reader names as configured. Null when unset; read <see cref="EffectiveReaders"/> instead.</summary>
    public List<string>? Readers { get; set; }

    /// <summary>The readers to use, in order: the configured list, or just "builtin" when none is configured.</summary>
    public IReadOnlyList<string> EffectiveReaders => Readers is { Count: > 0 } ? Readers : ["builtin"];

    public string CitationStyle { get; set; } = "apa";
}

public sealed class RunsOptions
{
    public int MaxConcurrent { get; set; } = 2;

    /// <summary>How many model calls one run makes at once (extraction, writing). Raise it on big local machines.</summary>
    public int ModelConcurrency { get; set; } = 4;
}

public sealed class StorageOptions
{
    /// <summary>"Local" or "S3".</summary>
    public string Kind { get; set; } = "Local";

    /// <summary>Local folder. Default: the user's local application data folder plus "gannet".</summary>
    public string? Path { get; set; }

    public S3StorageOptions S3 { get; set; } = new();
}

public sealed class S3StorageOptions
{
    public string? ServiceUrl { get; set; }

    public string? Bucket { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string Region { get; set; } = "auto";
}

public sealed class ReadersOptions
{
    public int MaxCharacters { get; set; } = 8000;

    public ProxyOptions? Proxy { get; set; }

    public Dictionary<string, ServiceOptions> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProxyOptions
{
    public string? Url { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }
}

/// <summary>One search service or reader. Type defaults to the entry's name.</summary>
public sealed class ServiceOptions
{
    public string? Type { get; set; }

    public string? ApiKey { get; set; }

    public string? Endpoint { get; set; }

    public string? Zone { get; set; }

    public bool Keyless { get; set; }

    public string? Country { get; set; }
}

public sealed class ModelOptions
{
    /// <summary>Ollama, OpenAI, OpenAICompatible, AzureOpenAI, Anthropic or Gemini.</summary>
    public string Provider { get; set; } = string.Empty;

    public string? Endpoint { get; set; }

    public string Model { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public int? ContextWindow { get; set; }

    public bool? SupportsTools { get; set; }

    public int MaxOutputTokens { get; set; } = 4096;
}
