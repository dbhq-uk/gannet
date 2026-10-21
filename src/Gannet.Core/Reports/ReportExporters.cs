using System.Globalization;
using System.Net;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Gannet.Core.Reports;

/// <summary>A report export format. Add one by adding a class and listing it in <see cref="ReportExporters.All"/>.</summary>
public interface IReportExporter
{
    string Format { get; }
    string ContentType { get; }
    string FileExtension { get; }

    string Export(string markdown, string title);
}

public static class ReportExporters
{
    // Built from an explicit list on purpose. The generic attributes extension (part of the advanced set)
    // would let model output add onclick or style attributes, and raw HTML is switched off. No auto identifiers:
    // a heading id such as "app" would clash with an element id on the page that shows the fragment.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseListExtras()
        .DisableHtml()
        .Build();

    public static IReadOnlyList<IReportExporter> All { get; } = [new MarkdownExporter(), new HtmlExporter(), new TextExporter()];

    public static IReportExporter Get(string format) =>
        All.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown export format '{format}'. Use one of: {string.Join(", ", All.Select(e => e.Format))}.", nameof(format));

    /// <summary>Safe HTML for showing a report in the UI: raw HTML escaped, only http(s) links kept, and no <img> at all.</summary>
    public static string RenderHtmlFragment(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        RemoveUnsafeTargets(document);

        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>
    /// Walks every inline that carries a target (inline, reference and automatic links, and images). A target that is
    /// not absolute http or https loses the link and keeps its text. This runs after parsing, so entity-encoded and
    /// reference-defined targets are already decoded and resolved. Images are never rendered as images: a remote image
    /// is fetched automatically, which would let a prompt-injected page track or leak data. A safe image becomes a link
    /// whose text is the alt text (or the URL when there is none); an unsafe one becomes its alt text.
    /// </summary>
    private static void RemoveUnsafeTargets(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            var safe = IsWebUrl(link.Url);

            if (link.IsImage)
            {
                link.IsImage = false;
                if (safe && link.FirstChild is null)
                {
                    link.AppendChild(new LiteralInline(link.Url!));
                }
            }

            if (safe)
            {
                link.GetAttributes().AddPropertyIfNotExist("rel", "noopener noreferrer");
                continue;
            }

            // Move the label (or an image's alt text) out, then remove the link node itself.
            while (link.FirstChild is { } child)
            {
                child.Remove();
                link.InsertBefore(child);
            }

            link.Remove();
        }

        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (!autolink.IsEmail && IsWebUrl(autolink.Url))
            {
                autolink.GetAttributes().AddPropertyIfNotExist("rel", "noopener noreferrer");
                continue;
            }

            autolink.ReplaceBy(new LiteralInline(autolink.Url));
        }
    }

    private static bool IsWebUrl(string? url) =>
        url is not null
        && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private sealed class MarkdownExporter : IReportExporter
    {
        public string Format => "md";
        public string ContentType => "text/markdown; charset=utf-8";
        public string FileExtension => ".md";

        public string Export(string markdown, string title) => markdown;
    }

    private sealed class HtmlExporter : IReportExporter
    {
        public string Format => "html";
        public string ContentType => "text/html; charset=utf-8";
        public string FileExtension => ".html";

        public string Export(string markdown, string title) => $$"""
            <!DOCTYPE html>
            <html lang="en-GB">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <meta name="referrer" content="no-referrer">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>body{max-width:46rem;margin:2rem auto;padding:0 1rem;font:16px/1.6 system-ui,sans-serif}a{overflow-wrap:anywhere}</style>
            </head>
            <body>
            {{RenderHtmlFragment(markdown)}}
            </body>
            </html>
            """;
    }

    private sealed class TextExporter : IReportExporter
    {
        public string Format => "txt";
        public string ContentType => "text/plain; charset=utf-8";
        public string FileExtension => ".txt";

        public string Export(string markdown, string title)
        {
            var document = Markdown.Parse(markdown, Pipeline);
            RemoveUnsafeTargets(document);
            var text = PlainText.Blocks(document, tight: false);
            return text.Length == 0 ? string.Empty : text + "\n";
        }
    }

    /// <summary>Readable plain text from the Markdig tree: list markers, table rows and link addresses survive.</summary>
    private static class PlainText
    {
        public static string Blocks(IEnumerable<Block> blocks, bool tight) =>
            string.Join(tight ? "\n" : "\n\n", blocks.Select(Block).Where(t => t.Length > 0));

        private static string Block(Block block) => block switch
        {
            ListBlock list => List(list),
            Table table => string.Join("\n", table.OfType<TableRow>().Select(Row)),
            QuoteBlock quote => Prefix(Blocks(quote, tight: false), "> ", "> "),
            ThematicBreakBlock => "---",
            LeafBlock { Inline: { } inline } => Inlines(inline),
            LeafBlock leaf => leaf.Lines.ToString(),
            ContainerBlock container => Blocks(container, tight: false),
            _ => string.Empty,
        };

        private static string List(ListBlock list)
        {
            var number = int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;
            var items = new List<string>();
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var marker = list.IsOrdered ? $"{number++}. " : "- ";
                items.Add(Prefix(Blocks(item, list.IsLoose is false), marker, new string(' ', marker.Length)));
            }

            return string.Join(list.IsLoose ? "\n\n" : "\n", items);
        }

        private static string Row(TableRow row) =>
            string.Join(" | ", row.OfType<TableCell>().Select(cell => Blocks(cell, tight: true).Replace('\n', ' ')));

        private static string Prefix(string text, string first, string rest)
        {
            var lines = text.Split('\n');
            return string.Join("\n", lines.Select((line, i) => line.Length == 0 && i > 0 ? line : (i == 0 ? first : rest) + line));
        }

        private static string Inlines(ContainerInline container)
        {
            var sb = new StringBuilder();
            for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
            {
                sb.Append(Inline(inline));
            }

            return sb.ToString();
        }

        private static string Inline(Inline inline) => inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            LineBreakInline => "\n",
            HtmlEntityInline entity => entity.Transcoded.ToString(),
            AutolinkInline autolink => autolink.Url,
            TaskList task => task.Checked ? "[x]" : "[ ]",
            LinkInline link => Link(link),
            DelimiterInline delimiter => delimiter.ToLiteral() + Inlines(delimiter),
            ContainerInline container => Inlines(container),
            _ => string.Empty,
        };

        private static string Link(LinkInline link)
        {
            var label = Inlines(link);
            var url = link.Url;
            if (url is null || !IsWebUrl(url) || label == url)
            {
                return label;
            }

            return label.Length == 0 ? url : $"{label} ({url})";
        }
    }
}
