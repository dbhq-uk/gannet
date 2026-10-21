using Gannet.Core.Services;
using Microsoft.Extensions.AI;

namespace Gannet.Core.Models;

/// <summary>The context window gannet will use (not the trained maximum), and whether the model calls tools.</summary>
public sealed record ModelCapabilities(int ContextWindow, bool SupportsTools)
{
    /// <summary>Agent mode needs tool calling and at least this many tokens of context.</summary>
    public const int AgentModeMinimumContext = 16_000;

    public bool CanRunAgentMode => SupportsTools && ContextWindow >= AgentModeMinimumContext;
}

public sealed record ModelHandle(string Name, IChatClient Client, ModelCapabilities Capabilities);

public interface IChatClientFactory
{
    IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>Throws <see cref="UnknownServiceException"/> for a name that is not configured.</summary>
    Task<ModelHandle> GetAsync(string name, CancellationToken ct);
}
