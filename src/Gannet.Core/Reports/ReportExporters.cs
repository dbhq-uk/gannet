using System.Net;
using Markdig;
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
    // would let model output add onclick or style attributes, and raw HTML is switched off.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseListExtras()
        .UseAutoIdentifiers()
        .DisableHtml()
        .Build();

    public static IReadOnlyList<IReportExporter> All { get; } = [new MarkdownExporter(), new HtmlExporter(), new TextExporter()];

    public static IReportExporter Get(string format) =>
        All.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown export format '{format}'. Use one of: {string.Join(", ", All.Select(e => e.Format))}.", nameof(format));

    /// <summary>Safe HTML for showing a report in the UI: raw HTML escaped, only http(s) links and images kept.</summary>
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
    /// reference-defined targets are already decoded and resolved.
    /// </summary>
    private static void RemoveUnsafeTargets(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (IsWebUrl(link.Url))
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

        public string Export(string markdown, string title) => Markdown.ToPlainText(markdown, Pipeline);
    }
}
