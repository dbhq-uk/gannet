using System.Globalization;

namespace Gannet.Core.Citations;

public sealed record ReferenceSource(Uri Url, string Title);

/// <summary>A citation style for the reference list. Add a style by adding a class and listing it in <see cref="CitationStyles.All"/>.</summary>
public interface ICitationStyle
{
    string Name { get; }

    string Format(ReferenceSource source, DateOnly accessed);
}

public static class CitationStyles
{
    public static IReadOnlyList<ICitationStyle> All { get; } = [new Apa(), new Mla(), new Chicago(), new Ieee()];

    public static bool TryGet(string name, out ICitationStyle style)
    {
        style = All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return style is not null;
    }

    public static ICitationStyle Get(string name) =>
        TryGet(name, out var style)
            ? style
            : throw new ArgumentException($"Unknown citation style '{name}'. Use one of: {string.Join(", ", All.Select(s => s.Name))}.", nameof(name));

    private static string TitleOf(ReferenceSource source) =>
        string.IsNullOrWhiteSpace(source.Title) ? source.Url.Host : string.Join(' ', source.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // Fixed English month forms for MLA and IEEE, so the output never depends on the machine's locale.
    // May, June and July are in full and September is "Sept.".
    private static readonly string[] AbbreviatedMonths = ["Jan.", "Feb.", "Mar.", "Apr.", "May", "June", "July", "Aug.", "Sept.", "Oct.", "Nov.", "Dec."];

    // Chicago: "March 7, 2026".
    private static string Long(DateOnly date) => date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    // IEEE: "Mar. 7, 2026".
    private static string Abbreviated(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{AbbreviatedMonths[date.Month - 1]} {date.Day}, {date.Year}");

    // MLA: "7 Mar. 2026".
    private static string DayMonthYear(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{date.Day} {AbbreviatedMonths[date.Month - 1]} {date.Year}");

    private sealed class Apa : ICitationStyle
    {
        public string Name => "apa";

        public string Format(ReferenceSource source, DateOnly accessed) => $"{TitleOf(source)}. (n.d.). Retrieved from {source.Url.AbsoluteUri}";
    }

    private sealed class Mla : ICitationStyle
    {
        public string Name => "mla";

        public string Format(ReferenceSource source, DateOnly accessed) =>
            $"\"{TitleOf(source)}\". Web. {DayMonthYear(accessed)}. <{source.Url.AbsoluteUri}>";
    }

    private sealed class Chicago : ICitationStyle
    {
        public string Name => "chicago";

        public string Format(ReferenceSource source, DateOnly accessed) => $"\"{TitleOf(source)}.\" Accessed {Long(accessed)}. {source.Url.AbsoluteUri}.";
    }

    private sealed class Ieee : ICitationStyle
    {
        public string Name => "ieee";

        public string Format(ReferenceSource source, DateOnly accessed) => $"\"{TitleOf(source)},\" {source.Url.AbsoluteUri}, accessed {Abbreviated(accessed)}.";
    }
}
