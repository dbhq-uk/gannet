using System.Text.RegularExpressions;
using Gannet.Core.Reports;

namespace Gannet.Core.Tests.Reports;

public partial class ReportExportersTests
{
    private const string Markdown = "# Title\n\nSome **bold** text [1].\n\n<script>alert('x')</script>\n\n[bad](javascript:alert(1)) [good](https://example.com)\n";

    [Fact]
    public void Markdown_export_is_unchanged()
    {
        Assert.Equal(Markdown, ReportExporters.Get("md").Export(Markdown, "Title"));
    }

    [Fact]
    public void Html_export_is_a_document_that_escapes_raw_html_and_drops_unsafe_links()
    {
        var html = ReportExporters.Get("html").Export(Markdown, "Q <&> A");
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<title>Q &lt;&amp;&gt; A</title>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_export_has_no_markdown_markers()
    {
        var text = ReportExporters.Get("txt").Export("# Title\n\nSome **bold** text.", "Title");
        Assert.Contains("Title", text, StringComparison.Ordinal);
        Assert.Contains("Some bold text.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("# ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_format_names_the_valid_ones()
    {
        var error = Assert.Throws<ArgumentException>(() => ReportExporters.Get("pdf"));
        Assert.Contains("md, html, txt", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JaVaScRiPt:alert(1))")]
    [InlineData("[x](java&#x73;cript:alert(1))")]
    [InlineData("[x](java&#115;cript:alert(1))")]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("[x](vbscript:msgbox(1))")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[x](file:///etc/passwd)")]
    [InlineData("[x](/relative/path)")]
    [InlineData("[x](//evil.example/path)")]
    public void Inline_links_with_unsafe_targets_lose_the_target_and_keep_the_text(string markdown)
    {
        var html = ReportExporters.RenderHtmlFragment(markdown);
        AssertNoUnsafeTarget(html);
        Assert.Contains("x", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_with_leading_whitespace_in_the_target_is_dropped()
    {
        var html = ReportExporters.RenderHtmlFragment("[x](<  javascript:alert(1)>)");
        AssertNoUnsafeTarget(html);
    }

    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<JAVASCRIPT:alert(1)>")]
    [InlineData("<vbscript:msgbox(1)>")]
    [InlineData("<data:text/html,hello>")]
    public void Autolinks_with_unsafe_schemes_are_not_links(string markdown)
    {
        var html = ReportExporters.RenderHtmlFragment(markdown);
        AssertNoUnsafeTarget(html);
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Bare_unsafe_scheme_text_is_not_turned_into_a_link()
    {
        var html = ReportExporters.RenderHtmlFragment("see javascript:alert(1) and data:text/html,hi");
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)\n")]
    [InlineData("[x][r]\n\n[r]: JaVaScRiPt:alert(1)\n")]
    [InlineData("[x][r]\n\n[r]: data:text/html,hi\n")]
    [InlineData("[r]\n\n[r]: javascript:alert(1)\n")]
    [InlineData("[x][]\n\n[x]: vbscript:msgbox(1)\n")]
    public void Reference_links_with_unsafe_targets_lose_the_target(string markdown)
    {
        var html = ReportExporters.RenderHtmlFragment(markdown);
        AssertNoUnsafeTarget(html);
    }

    [Theory]
    [InlineData("![a](javascript:alert(1))")]
    [InlineData("![a](data:image/svg+xml;base64,PHN2Zz4=)")]
    [InlineData("![a](DaTa:image/png;base64,AAAA)")]
    [InlineData("![a][r]\n\n[r]: javascript:alert(1)\n")]
    [InlineData("![a](/local.png)")]
    public void Images_with_unsafe_targets_lose_the_target(string markdown)
    {
        var html = ReportExporters.RenderHtmlFragment(markdown);
        AssertNoUnsafeTarget(html);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_with_https_targets_are_kept()
    {
        var html = ReportExporters.RenderHtmlFragment("![a](https://example.com/a.png)");
        Assert.Contains("src=\"https://example.com/a.png\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Kept_links_carry_rel_noopener_noreferrer()
    {
        var html = ReportExporters.RenderHtmlFragment("[good](https://example.com) [also](http://example.org)");
        Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"http://example.org\"", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(html, "rel=\"noopener noreferrer\""));
    }

    [Theory]
    [InlineData("[x](https://a.example){onclick=\"alert(1)\"}")]
    [InlineData("# Heading {onclick=\"alert(1)\"}")]
    [InlineData("**bold**{onmouseover=\"alert(1)\"}")]
    [InlineData("```js {onclick=\"alert(1)\"}\ncode\n```")]
    [InlineData("[x](https://a.example){#id .class style=\"x\"}")]
    public void Generic_attributes_cannot_add_event_handlers_or_styles(string markdown)
    {
        var html = ReportExporters.RenderHtmlFragment(markdown);
        Assert.DoesNotMatch(HandlerOrStyleAttribute(), html);
    }

    [Fact]
    public void Raw_html_blocks_and_inline_html_are_escaped()
    {
        var html = ReportExporters.RenderHtmlFragment("<img src=x onerror=alert(1)>\n\ntext <b onclick=\"x\">y</b>\n\n<style>p{}</style>");
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<style", html, StringComparison.Ordinal);
        Assert.Contains("&lt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tables_and_task_lists_still_render()
    {
        var html = ReportExporters.RenderHtmlFragment("| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done\n");
        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("checkbox", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_export_title_is_encoded_even_with_quotes()
    {
        var html = ReportExporters.Get("html").Export("text", "</title><script>alert(1)</script>");
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    // Escaped text such as "javascript:alert(1)" inside a paragraph is harmless. What matters is a tag
    // that carries the scheme as a target, or any tag that carries an event handler or style attribute.
    private static void AssertNoUnsafeTarget(string html)
    {
        Assert.DoesNotMatch(UnsafeTargetAttribute(), html);
        Assert.DoesNotMatch(NonWebTargetAttribute(), html);
    }

    [GeneratedRegex("""<[^>]*\s(?:href|src|action|formaction|xlink:href)\s*=\s*["']?\s*(?:javascript|vbscript|data|file):""", RegexOptions.IgnoreCase)]
    private static partial Regex UnsafeTargetAttribute();

    [GeneratedRegex("""<[^>]*\s(?:href|src)\s*=\s*["']?(?!https?://)""", RegexOptions.IgnoreCase)]
    private static partial Regex NonWebTargetAttribute();

    [GeneratedRegex("""<[^>]*\s(?:on\w+|style)\s*=""", RegexOptions.IgnoreCase)]
    private static partial Regex HandlerOrStyleAttribute();

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
