using Gannet.Core.Domain;

namespace Gannet.Core.Tests.Domain;

public class ResearchBudgetTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        var budget = new ResearchBudget();
        Assert.Equal(6, budget.MaxSearches);
        Assert.Equal(5, budget.ResultsPerSearch);
        Assert.Equal(12, budget.MaxPages);
        Assert.Equal(1, budget.MaxRounds);
        Assert.Equal(8, budget.MaxSections);
        Assert.Equal(40, budget.MinCredibility);
    }

    [Fact]
    public void Validated_clamps_every_limit()
    {
        var wild = new ResearchBudget { MaxSearches = 500, ResultsPerSearch = 0, MaxPages = -3, MaxRounds = 9, MaxSections = 0, MinCredibility = 101 };
        var budget = wild.Validated();
        Assert.Equal(30, budget.MaxSearches);
        Assert.Equal(1, budget.ResultsPerSearch);
        Assert.Equal(0, budget.MaxPages);
        Assert.Equal(3, budget.MaxRounds);
        Assert.Equal(1, budget.MaxSections);
        Assert.Equal(100, budget.MinCredibility);
    }
}
