using System.ClientModel.Primitives;

namespace Gannet.Providers.Models;

/// <summary>Sets fixed headers on every request an SDK client sends. Used for OpenRouter's optional app-attribution headers.</summary>
internal sealed class HeaderPolicy(IReadOnlyDictionary<string, string> headers) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Apply(PipelineMessage message)
    {
        foreach (var (name, value) in headers)
        {
            message.Request.Headers.Set(name, value);
        }
    }
}
