using System.Text.Json;
using Gannet.Core.Json;
using Gannet.Core.Models;

namespace Gannet.Providers.Models;

internal static class ModelCapabilityTable
{
    /// <summary>Thinking is "adaptive" (the default) or "extended", for Claude families that predate adaptive thinking. Exact matches the whole model name, not a prefix.</summary>
    private sealed record Entry(string Prefix, int ContextWindow, bool SupportsTools, string Thinking = "adaptive", bool Exact = false);

    private static readonly Lazy<IReadOnlyList<Entry>> Entries = new(() =>
    {
        using var stream = typeof(ModelCapabilityTable).Assembly.GetManifestResourceStream("Gannet.Providers.model-capabilities.json")
            ?? throw new InvalidOperationException("The model capability table is missing from the assembly.");
        return JsonSerializer.Deserialize<List<Entry>>(stream, GannetJson.Options) ?? [];
    });

    /// <summary>The documented window for a model family, by prefix; the first entry that matches wins, so specific prefixes come before general ones.</summary>
    public static ModelCapabilities? Lookup(string model) =>
        Find(model) is { } entry ? new ModelCapabilities(entry.ContextWindow, entry.SupportsTools) : null;

    /// <summary>True for a Claude family that rejects <c>thinking.type=adaptive</c> and takes the manual <c>enabled</c> form (the Anthropic SDK's Extended mode).</summary>
    public static bool UsesExtendedThinking(string model) =>
        string.Equals(Find(model)?.Thinking, "extended", StringComparison.OrdinalIgnoreCase);

    private static Entry? Find(string model) =>
        Entries.Value.FirstOrDefault(e => e.Exact
            ? string.Equals(model, e.Prefix, StringComparison.OrdinalIgnoreCase)
            : model.StartsWith(e.Prefix, StringComparison.OrdinalIgnoreCase));
}
