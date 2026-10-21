using System.Text.Json;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Search;

internal sealed class ExaSearch(HttpClient http, string name, ServiceOptions options) : SearchProviderBase(http, name, options)
{
    private const int TextCharacters = 8000;
    private const int SnippetCharacters = 300;

    // Exa accepts numResults 1 to 100 (spec, SearchRequest.numResults).
    private const int MaxResultsAllowed = 100;

    public override string Type => "exa";

    public override string DisplayName => "Exa";

    public override bool ReturnsPageContent => true;

    public override ServiceTerms Terms => new(false,
        "Terms s.4.2(a) restrict copying or displaying results without written permission; gannet keeps them in memory for the one report.");

    public override async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        var key = RequireKey();
        var request = PostJson("https://api.exa.ai/search", new
        {
            query,
            numResults = Math.Clamp(maxResults, 1, MaxResultsAllowed),
            contents = new { text = new { maxCharacters = TextCharacters } },
        });
        request.Headers.Add("x-api-key", key);
        using var json = await SendAsync(request, ct);
        return Take(json.RootElement.Prop("results").Items().Select(Map), maxResults);
    }

    private static SearchHit? Map(JsonElement r)
    {
        var url = JsonRead.WebUri(r.Str("url"));
        if (url is null)
        {
            return null;
        }

        var text = r.Str("text");
        return new SearchHit(r.Str("title"), url, SnippetOf(text), text.Length == 0 ? null : text, JsonRead.Date(r.Str("publishedDate")));
    }

    // Never cut between the two halves of a surrogate pair.
    private static string SnippetOf(string text)
    {
        if (text.Length <= SnippetCharacters)
        {
            return text;
        }

        return text[..(char.IsHighSurrogate(text[SnippetCharacters - 1]) ? SnippetCharacters - 1 : SnippetCharacters)];
    }
}
