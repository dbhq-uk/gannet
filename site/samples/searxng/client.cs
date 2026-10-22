#:property PublishAot=false
using System.Net.Http.Json;
using System.Text.Json.Serialization;

var query = args.Length > 0 ? args[0] : "searxng docker";
var url = "http://127.0.0.1:8888/search?q=" + Uri.EscapeDataString(query) + "&format=json";

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var data = await http.GetFromJsonAsync<SearchResponse>(url)
    ?? throw new InvalidOperationException("Empty response");

Console.WriteLine($"query: {data.Query}");
Console.WriteLine($"results: {data.Results.Count}");
foreach (var result in data.Results.Take(3))
{
    Console.WriteLine($"- {result.Title}");
    Console.WriteLine($"  {result.Url}");
    Console.WriteLine($"  engines: {string.Join(", ", result.Engines)}");
}

record SearchResponse(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("results")] List<SearchResult> Results);

record SearchResult(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("engines")] List<string> Engines);
