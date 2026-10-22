using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Readers;

/// <summary>
/// A reader that asks a third party for the page text. The address goes to that service, so it is checked against <see cref="PublicAddressGuard"/> first,
/// whatever the AllowPrivateAddresses setting says: a remote service cannot reach a private address, and sending an intranet name to it leaks the name.
/// What comes back is capped here and is only ever held in memory.
/// </summary>
internal abstract class RemoteReaderBase(HttpClient http, string name, ServiceOptions options, Func<string, CancellationToken, Task<IPAddress[]>>? resolve) : IPageReader
{
    private const int MaxTitleCharacters = 300;

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolve = resolve ?? Dns.GetHostAddressesAsync;

    protected HttpClient Http { get; } = http;

    protected ServiceOptions Options { get; } = options;

    public string Name { get; } = name;

    public abstract string Type { get; }

    public abstract string DisplayName { get; }

    public abstract ServiceTerms Terms { get; }

    public async Task<PageContent> ReadAsync(Uri url, int maxCharacters, CancellationToken ct)
    {
        await PublicAddressGuard.EnsurePublicAsync(url, resolve, ct);

        var (title, markdown) = await FetchAsync(url, ct);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new ServiceFailedException(DisplayName, $"{DisplayName} returned no text for the page.");
        }

        return new PageContent(url, TitleOf(title, url), TextCap.Cut(markdown.Trim(), maxCharacters), Name);
    }

    protected abstract Task<(string Title, string Markdown)> FetchAsync(Uri url, CancellationToken ct);

    /// <summary>True for a status this service uses for a plan or credit limit, which is a quota error and not a plain failure.</summary>
    protected virtual bool IsQuotaStatus(HttpStatusCode status) => false;

    /// <summary>Called before the standard status mapping. The default is <see cref="IsQuotaStatus"/>.</summary>
    protected virtual Task<bool> IsQuotaResponseAsync(HttpResponseMessage response, CancellationToken ct) =>
        Task.FromResult(IsQuotaStatus(response.StatusCode));

    protected string RequireKey() =>
        string.IsNullOrWhiteSpace(Options.ApiKey)
            ? throw new AuthRequiredException(DisplayName, $"{DisplayName} needs an API key: set Gannet:Readers:Services:{Name}:ApiKey.")
            : Options.ApiKey;

    protected static HttpRequestMessage PostJson(string url, object body) => new(HttpMethod.Post, url) { Content = JsonContent.Create(body) };

    /// <summary>Sends, checks the status and parses a JSON body (at most 16 MB). The response is disposed before this returns.</summary>
    protected async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            using var response = await ServiceHttp.SendCheckedAsync(Http, request, DisplayName, IsQuotaResponseAsync, ct);
            return await JsonRead.ParseAsync(response, DisplayName, ct);
        }
    }

    // One line, and short: a title is a label, and the service decides how long it is.
    private static string TitleOf(string title, Uri url)
    {
        var line = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length == 0 ? url.Host : TextCap.Cut(line, MaxTitleCharacters);
    }
}
