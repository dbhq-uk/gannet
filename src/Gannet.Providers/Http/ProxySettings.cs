using Gannet.Core.Configuration;

namespace Gannet.Providers.Http;

internal static class ProxySettings
{
    /// <summary>
    /// A proxy address carries no credentials: they go in Username and Password, which are kept out of logs and serialised settings. A message never echoes the
    /// address, because it may hold the very password it is complaining about.
    /// </summary>
    public static void Validate(ProxyOptions? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy?.Url))
        {
            return;
        }

        if (!Uri.TryCreate(proxy.Url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new InvalidOperationException("Gannet:Readers:Proxy:Url is not an absolute address, such as http://proxy.example:8080.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Gannet:Readers:Proxy:Url must not hold a user name or password. Put them in Gannet:Readers:Proxy:Username and Gannet:Readers:Proxy:Password.");
        }
    }
}
