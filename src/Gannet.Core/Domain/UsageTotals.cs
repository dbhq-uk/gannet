namespace Gannet.Core.Domain;

public sealed record UsageTotals(int Calls, long InputTokens, long OutputTokens)
{
    public static UsageTotals Zero { get; } = new(0, 0, 0);

    public UsageTotals Add(long inputTokens, long outputTokens) =>
        new(Calls + 1, InputTokens + inputTokens, OutputTokens + outputTokens);

    public UsageTotals Add(UsageTotals other) =>
        new(Calls + other.Calls, InputTokens + other.InputTokens, OutputTokens + other.OutputTokens);
}
