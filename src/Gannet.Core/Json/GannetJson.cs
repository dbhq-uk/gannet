using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gannet.Core.Json;

/// <summary>The one set of JSON settings for run records, events, checkpoints, the API and MCP.</summary>
public static class GannetJson
{
    public static JsonSerializerOptions Options { get; } = Freeze(CreateOptions());

    /// <summary>A fresh, mutable copy, for callers that must add converters.</summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        return options;
    }

    private static JsonSerializerOptions Freeze(JsonSerializerOptions options)
    {
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
