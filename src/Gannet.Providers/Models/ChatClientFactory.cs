using Gannet.Core.Models;
using Gannet.Core.Services;

namespace Gannet.Providers.Models;

// A placeholder, replaced in Task 19.
internal sealed class ChatClientFactory : IChatClientFactory
{
    public IReadOnlyList<ModelDescriptor> Models => [];

    public Task<ModelHandle> GetAsync(string name, CancellationToken ct) => throw new UnknownServiceException("model", name, []);
}
