using Gannet.Core.Credibility;
using Gannet.Core.Domain;

namespace Gannet.Core.Tests.Credibility;

public class CredibilityScorerTests
{
    private readonly CredibilityScorer scorer = new(CredibilityRules.LoadDefault());

    [Fact]
    public void Trusted_domain_matched_as_a_suffix_scores_high()
    {
        var score = scorer.Score(new Uri("https://www.bbc.co.uk/news/article"));
        Assert.Equal(85, score.Value);
        Assert.Equal(CredibilityLevel.High, score.Level);
        Assert.Contains("Trusted domain: bbc.co.uk", score.Rules);
        Assert.Contains("HTTPS", score.Rules);
    }

    [Fact]
    public void Lookalike_domain_is_not_trusted()
    {
        // The source scored this 85 because "cnn.com" is a substring of "notcnn.com".
        var score = scorer.Score(new Uri("https://notcnn.com/story"));
        Assert.Equal(55, score.Value);
        Assert.Equal(CredibilityLevel.Medium, score.Level);
    }

    [Theory]
    [InlineData("https://www.gov.uk/guidance/x", 85)]
    [InlineData("https://gov.uk/", 85)]
    [InlineData("https://www.ox.ac.uk/research/news", 95)]
    public void Suffix_rules_cover_the_bare_domain_and_subdomains(string url, int expected)
    {
        Assert.Equal(expected, scorer.Score(new Uri(url)).Value);
    }

    [Fact]
    public void Suspicious_top_level_domain_without_https_scores_low()
    {
        var score = scorer.Score(new Uri("http://free-stuff.xyz/"));
        Assert.Equal(20, score.Value);
        Assert.Equal(CredibilityLevel.Low, score.Level);
        Assert.Contains("Suspicious top-level domain: .xyz", score.Rules);
        Assert.Contains("No HTTPS", score.Rules);
    }

    [Fact]
    public void Shortener_rule_does_not_catch_microsoft_com()
    {
        // The source's pattern t\.co matched "microsoft.com".
        Assert.Equal(55, scorer.Score(new Uri("https://learn.microsoft.com/en-us/dotnet/")).Value);
        Assert.Equal(35, scorer.Score(new Uri("https://t.co/abc")).Value);
    }

    [Fact]
    public void Four_or_more_host_labels_cost_five_points()
    {
        Assert.Equal(50, scorer.Score(new Uri("https://a.b.example.com/")).Value);
    }

    [Fact]
    public void Academic_path_adds_ten()
    {
        var score = scorer.Score(new Uri("https://example.com/publications/2026"));
        Assert.Equal(65, score.Value);
        Assert.Contains("Academic or research path", score.Rules);
    }

    [Fact]
    public void Non_web_address_scores_zero()
    {
        var score = scorer.Score(new Uri("ftp://example.com/file"));
        Assert.Equal(0, score.Value);
        Assert.Equal(["Not a web address"], score.Rules);
    }

    [Fact]
    public void User_rules_add_trusted_domains_and_override_numbers()
    {
        var user = CredibilityRules.Parse("""{ "trustedDomains": ["fca.org.uk"], "trustedBonus": 40 }""");
        var merged = new CredibilityScorer(CredibilityRules.LoadDefault().MergeWith(user));
        Assert.Equal(95, merged.Score(new Uri("https://www.fca.org.uk/firms")).Value);
        Assert.Equal(95, merged.Score(new Uri("https://www.bbc.co.uk/")).Value);
    }
}
