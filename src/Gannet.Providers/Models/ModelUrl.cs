namespace Gannet.Providers.Models;

/// <summary>Builds URLs under a configured endpoint, keeping any path prefix a reverse proxy adds.</summary>
internal static class ModelUrl
{
    private const string VersionSegment = "/v1";

    /// <summary>The endpoint with <paramref name="relative"/> appended to its path, whether or not the endpoint ends in a slash.</summary>
    public static Uri Under(Uri endpoint, string relative) => With(endpoint, TrimmedPath(endpoint), relative);

    /// <summary>The server root: the endpoint without a trailing "/v1". llama.cpp serves /props there, not under /v1.</summary>
    public static Uri Root(Uri endpoint, string relative)
    {
        var path = TrimmedPath(endpoint);
        return With(endpoint, path.EndsWith(VersionSegment, StringComparison.OrdinalIgnoreCase) ? path[..^VersionSegment.Length] : path, relative);
    }

    /// <summary>The endpoint with its path ending in "/", so relative request paths resolve under any prefix.</summary>
    public static Uri WithTrailingSlash(Uri endpoint) => new UriBuilder(endpoint) { Path = $"{TrimmedPath(endpoint)}/", Query = string.Empty, Fragment = string.Empty }.Uri;

    public static bool EndsWithVersion(Uri endpoint) => TrimmedPath(endpoint).EndsWith(VersionSegment, StringComparison.OrdinalIgnoreCase);

    private static string TrimmedPath(Uri endpoint) => endpoint.AbsolutePath.TrimEnd('/');

    private static Uri With(Uri endpoint, string path, string relative) =>
        new UriBuilder(endpoint) { Path = $"{path}/{relative}", Query = string.Empty, Fragment = string.Empty }.Uri;
}
