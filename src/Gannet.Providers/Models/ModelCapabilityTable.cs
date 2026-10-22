using System.Text.Json;
using Gannet.Core.Json;
using Gannet.Core.Models;

namespace Gannet.Providers.Models;

internal static class ModelCapabilityTable
{
    private sealed record Entry(string Prefix, int ContextWindow, bool SupportsTools);

    private static readonly Lazy<IReadOnlyList<Entry>> Entries = new(() =>
    {
        using var stream = typeof(ModelCapabilityTable).Assembly.GetManifestResourceStream("Gannet.Providers.model-capabilities.json")
            ?? throw new InvalidOperationException("The model capability table is missing from the assembly.");
        return JsonSerializer.Deserialize<List<Entry>>(stream, GannetJson.Options) ?? [];
    });

    /// <summary>The documented window for a model family, by prefix; the first entry that matches wins, so specific prefixes come before general ones.</summary>
    public static ModelCapabilities? Lookup(string model)
    {
        var entry = Entries.Value.FirstOrDefault(e => model.StartsWith(e.Prefix, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : new ModelCapabilities(entry.ContextWindow, entry.SupportsTools);
    }
}
