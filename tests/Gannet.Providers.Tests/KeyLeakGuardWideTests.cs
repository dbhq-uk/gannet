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

    // Members whose names contain a risky word but which hold no secret. Each entry is "Type.Member". A stale entry fails the test below.
    private static readonly HashSet<string> NotSecrets = new(StringComparer.Ordinal)
    {
        // Counts of model tokens, not credentials.
        "Gannet.Core.Events.ModelUsage.InputTokens",
        "Gannet.Core.Events.ModelUsage.OutputTokens",
        "Gannet.Core.Domain.UsageTotals.InputTokens",
        "Gannet.Core.Domain.UsageTotals.OutputTokens",
    };

    // Test-only types for the positive control: the guard must flag both.
    internal sealed record LeakyRecord(string ApiKey);

    internal sealed class LeakyToString
    {
        public string Password { get; set; } = string.Empty;

        public override string ToString() => Password;
    }

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
            .Where(name => RiskyWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));

    private static List<string> Offenders(IEnumerable<Type> types) =>
        [.. types
            .Where(t => IsRecord(t) || OverridesToString(t))
            .SelectMany(t => RiskyMembers(t).Where(m => !NotSecrets.Contains($"{t.FullName}.{m}")).Select(m => $"{t.FullName}.{m}"))];

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
    public void No_record_or_ToString_override_has_a_member_that_could_carry_a_key() => Assert.Empty(Offenders(AllTypes()));

    [Fact]
    public void Positive_control_a_record_with_an_ApiKey_member_is_flagged() =>
        Assert.Equal([$"{typeof(LeakyRecord).FullName}.ApiKey"], Offenders([typeof(LeakyRecord)]));

    [Fact]
    public void Positive_control_a_ToString_override_with_a_Password_member_is_flagged() =>
        Assert.Equal([$"{typeof(LeakyToString).FullName}.Password"], Offenders([typeof(LeakyToString)]));

    [Fact]
    public void Every_allow_list_entry_is_still_needed()
    {
        var types = AllTypes().Where(t => t.FullName is not null).GroupBy(t => t.FullName!).ToDictionary(g => g.Key, g => g.First());
        foreach (var entry in NotSecrets)
        {
            var split = entry.LastIndexOf('.');
            var typeName = entry[..split];
            var member = entry[(split + 1)..];

            Assert.True(types.TryGetValue(typeName, out var type), $"The allow-list names {typeName}, which no longer exists.");
            Assert.True(IsRecord(type!) || OverridesToString(type!), $"{typeName} is no longer a record or a ToString override, so {member} needs no allow-list entry.");
            Assert.Contains(member, RiskyMembers(type!));
        }
    }
}
