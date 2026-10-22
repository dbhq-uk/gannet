using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Gannet.Core.Services;

namespace Gannet.Providers.Readers;

/// <summary>
/// Parses a fetched page once, with limits that come from the parser itself and not from a guess about the text. The parser calls back for every element it
/// creates, and the callback stops the parse past <see cref="HtmlDepth.MaxElements"/>. But the callback is not exact on its own: AngleSharp clones formatting
/// elements without calling it (a paragraph holding 39 open formatting elements, then "&lt;p&gt;x" repeated 29,000 times, makes about 1.16 million elements and
/// calls it only 29,043 times). So every <see cref="ProbeEvery"/> created elements the callback also counts the elements really in the tree, and the same count is
/// checked once more after the parse. A walk of the real tree then checks its depth. This runs on the dedicated parse thread.
/// </summary>
internal static class PageParser
{
    private const string TooManyElements = "the page has too many elements to read";
    private const string TooDeep = "the page is too deeply nested to read";

    // While parsing, the tree is probed every so many created elements: its real element count (clones do not reach the callback) and its depth, so a page that
    // nests deeply or clones heavily is stopped long before the parser has built a huge tree. The overshoot is at most ProbeEvery times the number of open
    // formatting elements, which the depth probe keeps small.
    private const int ProbeEvery = 64;

    private static readonly HashSet<string> NotText = new(StringComparer.Ordinal)
    {
        "script", "style", "nav", "header", "footer", "noscript", "textarea", "template",
    };

    private sealed class LimitException(string message) : Exception(message);

    /// <summary>Throws <see cref="ServiceFailedException"/> when the page passes either limit.</summary>
    public static IHtmlDocument Parse(string html, string service)
    {
        var count = 0;
        IElement? previous = null;
        var options = new HtmlParserOptions
        {
            DisableElementPositionTracking = true,
            SkipComments = true,
            OnCreated = (element, _) =>
            {
                if (++count > HtmlDepth.MaxElements)
                {
                    throw new LimitException(TooManyElements);
                }

                if (count % ProbeEvery == 0)
                {
                    if (element.Owner is { } owner && owner.All.Length > HtmlDepth.MaxElements)
                    {
                        throw new LimitException(TooManyElements);
                    }

                    // The element before this one is in the tree by now, so its ancestors are its real depth.
                    if (previous is not null && DepthOf(previous) > HtmlDepth.MaxDepth)
                    {
                        throw new LimitException(TooDeep);
                    }
                }

                previous = element;
            },
        };

        try
        {
            var document = new HtmlParser(options).ParseDocument(html);
            if (document.All.Length > HtmlDepth.MaxElements)
            {
                throw new LimitException(TooManyElements);
            }

            if (Depth(document) > HtmlDepth.MaxDepth)
            {
                throw new LimitException(TooDeep);
            }

            return document;
        }
        catch (LimitException e)
        {
            throw new ServiceFailedException(service, e.Message);
        }
    }

    /// <summary>The body text without scripts, menus and footers, and the page title. Read from the tree before anything changes it, with no recursion.</summary>
    public static (string Text, string Title) PlainText(IHtmlDocument document)
    {
        var text = new StringBuilder();
        if (document.Body is { } body)
        {
            var pending = new Stack<INode>();
            pending.Push(body);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node is IElement element && NotText.Contains(element.LocalName))
                {
                    continue;
                }

                if (node.NodeType == NodeType.Text)
                {
                    text.Append(node.TextContent);
                    continue;
                }

                for (var i = node.ChildNodes.Length - 1; i >= 0; i--)
                {
                    pending.Push(node.ChildNodes[i]);
                }
            }
        }

        var lines = text.ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return (string.Join("\n\n", lines), document.Title ?? string.Empty);
    }

    private static int DepthOf(IElement element)
    {
        var depth = 0;
        for (var node = (INode?)element; node is IElement; node = node.ParentElement)
        {
            depth++;
            if (depth > HtmlDepth.MaxDepth)
            {
                break;
            }
        }

        return depth;
    }

    /// <summary>The depth of the deepest element, with the root element at 1. An explicit stack, so a deep tree cannot overflow this walk.</summary>
    private static int Depth(IHtmlDocument document)
    {
        if (document.DocumentElement is not { } root)
        {
            return 0;
        }

        var deepest = 0;
        var pending = new Stack<(IElement Element, int Depth)>();
        pending.Push((root, 1));
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Pop();
            if (depth > deepest)
            {
                deepest = depth;
                if (deepest > HtmlDepth.MaxDepth)
                {
                    return deepest;
                }
            }

            foreach (var child in element.Children)
            {
                pending.Push((child, depth + 1));
            }

            // A template keeps its content in a separate fragment, which Children does not reach.
            if (element is IHtmlTemplateElement template)
            {
                foreach (var child in template.Content.Children)
                {
                    pending.Push((child, depth + 1));
                }
            }
        }

        return deepest;
    }
}
