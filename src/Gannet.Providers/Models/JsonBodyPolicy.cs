using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace Gannet.Providers.Models;

/// <summary>
/// Sets one top-level property on every JSON request body an SDK client sends, replacing any value already there. The OpenAI SDK has no field for provider-specific body
/// properties, so this is how DeepSeek's <c>thinking</c> switch is sent. A body that is not a JSON object is passed on unchanged.
/// </summary>
internal sealed class JsonBodyPolicy(string name, string valueJson) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (message.Request.Content is { } content)
        {
            using var buffer = new MemoryStream();
            content.WriteTo(buffer, message.CancellationToken);
            Replace(message, buffer);
        }

        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (message.Request.Content is { } content)
        {
            using var buffer = new MemoryStream();
            await content.WriteToAsync(buffer, message.CancellationToken);
            Replace(message, buffer);
        }

        await ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Replace(PipelineMessage message, MemoryStream buffer)
    {
        JsonObject? body;
        try
        {
            body = JsonNode.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length).Span) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        if (body is null)
        {
            return;
        }

        body[name] = JsonNode.Parse(valueJson);
        message.Request.Content = BinaryContent.Create(BinaryData.FromString(body.ToJsonString()));
    }
}
