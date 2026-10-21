using Gannet.Core.Domain;

namespace Gannet.Core.Credibility;

/// <summary>Scores a source 0-100 from its address alone, and says which rules gave the score.</summary>
public sealed class CredibilityScorer(CredibilityRules rules)
{
    public SourceScore Score(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
        {
            return new SourceScore(0, CredibilityLevel.Low, ["Not a web address"]);
        }

        var host = url.IdnHost.ToLowerInvariant().TrimEnd('.');
        var score = rules.BaseScore ?? 50;
        var applied = new List<string>();

        var trusted = rules.TrustedDomains.FirstOrDefault(d => Matches(host, d));
        if (trusted is not null)
        {
            score += rules.TrustedBonus ?? 30;
            applied.Add($"Trusted domain: {trusted.TrimStart('.')}");
        }

        var suspicious = false;
        var tld = host[(host.LastIndexOf('.') + 1)..];
        if (rules.SuspiciousTopLevelDomains.Any(t => string.Equals(t.TrimStart('.'), tld, StringComparison.OrdinalIgnoreCase)))
        {
            score -= rules.SuspiciousPenalty ?? 20;
            applied.Add($"Suspicious top-level domain: .{tld}");
            suspicious = true;
        }
        else
        {
            var domain = rules.SuspiciousDomains.FirstOrDefault(d => Matches(host, d));
            if (domain is not null)
            {
                score -= rules.SuspiciousPenalty ?? 20;
                applied.Add($"Suspicious domain: {domain}");
                suspicious = true;
            }
        }

        if (url.Scheme == Uri.UriSchemeHttps)
        {
            score += rules.HttpsBonus ?? 5;
            applied.Add("HTTPS");
        }
        else
        {
            score -= rules.HttpPenalty ?? 10;
            applied.Add("No HTTPS");
        }

        if (trusted is null && !suspicious && host.Split('.').Length >= (rules.ComplexDomainLabels ?? 4))
        {
            score -= rules.ComplexDomainPenalty ?? 5;
            applied.Add("Complex domain structure");
        }

        var path = url.AbsolutePath.ToLowerInvariant();
        if (rules.AcademicPaths.Any(p => path.Contains(p, StringComparison.Ordinal)))
        {
            score += rules.AcademicBonus ?? 10;
            applied.Add("Academic or research path");
        }

        score = Math.Clamp(score, 0, 100);
        var level = score >= (rules.HighAt ?? 70) ? CredibilityLevel.High
            : score >= (rules.MediumAt ?? 40) ? CredibilityLevel.Medium
            : CredibilityLevel.Low;
        return new SourceScore(score, level, applied);
    }

    /// <summary>
    /// ".gov.uk" matches gov.uk and anything under it; "bbc.co.uk" matches bbc.co.uk and its subdomains.
    /// Never a bare substring: "cnn.com" does not match "notcnn.com".
    /// </summary>
    private static bool Matches(string host, string rule)
    {
        var domain = rule.TrimStart('.').ToLowerInvariant();
        return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
    }
}
