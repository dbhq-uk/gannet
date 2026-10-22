namespace Gannet.Providers.Http;

/// <summary>What Bright Data's REST API says in its headers when an account or a zone has run out.</summary>
internal static class BrightDataErrors
{
    // Bright Data's proxy error catalogue: client_10020 is "Account is suspended" (automatic when the balance goes negative) and client_10100 is
    // "Zone has reached usage limit". On the Direct API the outer status can be 200, so the headers are the signal. The code comes in x-brd-err-code and,
    // in the RFC 9209 Proxy-Status header, as details="<code>: ...".
    private static readonly string[] QuotaCodes = ["client_10020", "client_10100"];

    public static bool HasQuotaCode(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-brd-err-code", out var codes) && codes.Any(c => QuotaCodes.Contains(c.Trim(), StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Only the code that opens the details text counts, so a code mentioned later in the message does not.
        return response.Headers.TryGetValues("Proxy-Status", out var statuses)
            && statuses.Any(status => QuotaCodes.Any(code => status.Contains($"details=\"{code}:", StringComparison.OrdinalIgnoreCase)));
    }
}
