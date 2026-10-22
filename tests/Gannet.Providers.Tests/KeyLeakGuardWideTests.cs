using System.Reflection;
using System.Runtime.CompilerServices;
using Gannet.Core.Configuration;

namespace Gannet.Providers.Tests;

/// <summary>
/// Widens the Core guard to every type in Gannet.Core and Gannet.Providers, public and internal. A record prints every member from ToString, and a type that
/// overrides ToString can print anything, so neither may hold a member whose name suggests a key.
/// </summary>
public class KeyLeakGuardWideTests
{
    private static readonly string[] RiskyWords = ["key", "secret", "password", "token", "credential"];

    // Members whose names contain a risky word but which hold no secret. Each entry is "Type.Member" with the reason beside it.
    private static readonly HashSet<string> NotSecrets = new(StringComparer.Ordinal)
    {
        // Counts of model tokens, not credentials.
        "Gannet.Core.Events.ModelUsage.InputTokens",
        "Gannet.Core.Events.ModelUsage.OutputTokens",
        "Gannet.Core.Domain.UsageTotals.InputTokens",
        "Gannet.Core.Domain.UsageTotals.OutputTokens",
    };

    private static IEnumerable<Type> AllTypes() =>
        typeof(GannetOptions).Assembly.GetTypes()
            .Concat(typeof(ProvidersServiceCollectionExtensions).Assembly.GetTypes())
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null);

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private static bool OverridesToString(Type type) =>
        type.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, Type.EmptyTypes) is not null;

    private static IEnumerable<string> RiskyMembers(Type type) =>
        type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m is PropertyInfo or FieldInfo && m.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Select(m => m.Name)
            .Where(name => RiskyWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Where(name => !NotSecrets.Contains($"{type.FullName}.{name}"));

    [Fact]
    public void The_guard_sees_internal_types_of_both_assemblies()
    {
        var names = AllTypes().Select(t => t.Name).ToList();
        Assert.Contains("ServiceCatalog", names);
        Assert.Contains("BuiltinReader", names);
        Assert.Contains("RunStore", names);
        Assert.Contains(nameof(ServiceOptions), names);
    }

    [Fact]
    public void No_record_or_ToString_override_has_a_member_that_could_carry_a_key()
    {
        var offenders = AllTypes()
            .Where(t => IsRecord(t) || OverridesToString(t))
            .SelectMany(t => RiskyMembers(t).Select(m => $"{t.FullName}.{m}"))
            .ToList();

        Assert.Empty(offenders);
    }
}
