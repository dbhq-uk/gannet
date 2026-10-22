using Xunit;

namespace Gannet.TestSupport;

/// <summary>A fact that runs only when every named environment variable is set. Live and integration tests use it; CI sets none.</summary>
public sealed class EnvironmentFactAttribute : FactAttribute
{
    public EnvironmentFactAttribute(params string[] variables)
    {
        var missing = variables.Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))).ToList();
        if (missing.Count > 0)
        {
            Skip = $"Set {string.Join(", ", missing)} to run this test.";
        }
    }
}
