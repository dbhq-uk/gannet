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
    private const string AttributesMessage = "the page has an element with too many attributes";

    /// <summary>The most attributes one element may have. The busiest real page measured has 14.</summary>
    public const int MaxAttributes = 32;

    /// <summary>
    /// The most the depths of all the elements may add up to. SmartReader's time grows with that sum, not with the depth or the count alone: 250 divs and then
    /// 29,000 closed spans (inside both other limits) held a parse slot for 762 seconds. Real pages: Wikipedia Gannet 38,051, Bird 351,507.
    /// </summary>
    public const long MaxDepthSum = 1_000_000;

    /// <summary>
    /// The most that every text node's length times its depth may add up to. SmartReader is very slow on text that sits deep: 250 divs and one 4 MB paragraph ran
    /// for over 600 seconds and allocated 294 GB, and 120 nested list levels in 197 KB took 67 seconds. Real pages: Wikipedia Gannet 481,663, Bird 3,448,386.
    /// </summary>
    public const long MaxTextDepth = 20_000_000;

    /// <summary>
    /// The most that all attribute names and values may add up to, over every element in the tree, clones included. SmartReader writes an attribute value out once per
    /// clone when it serialises the article: one 1 MB title on a formatting element, then "&lt;/p&gt;&lt;p&gt;x" rounds, peaked at 3 GB over 1,000 rounds. Real pages:
    /// Wikipedia Gannet 150,726, Bird 1,367,804.
    /// </summary>
    public const long MaxAttributeText = 5_000_000;

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

        // A stray <html> or <body> tag merges its attributes onto these two and creates no element, so the check on created elements never sees it.
        IElement? htmlElement = null;
        IElement? bodyElement = null;
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

                // The callback fires before the element's attributes are set, so the element before this one is the newest whose attributes are known. Checking it here
                // means a clone only ever copies attributes that have passed (a formatting element with 10,000 attributes, cloned 2,000 times, peaked at 2.5 GB).
                if (TooManyAttributes(previous))
                {
                    throw new LimitException(AttributesMessage);
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
                if (element is IHtmlHtmlElement)
                {
                    htmlElement = element;
                }
                else if (element is IHtmlBodyElement)
                {
                    bodyElement = element;
                }
            },
        };

        try
        {
            // The parser reports an error for each stray html or body tag, just before it merges that tag's attributes. A handler that throws stops the parse.
            var parser = new HtmlParser(options);
            parser.Error += (_, _) =>
            {
                if (TooManyAttributes(htmlElement) || TooManyAttributes(bodyElement))
                {
                    throw new LimitException(AttributesMessage);
                }
            };

            var document = parser.ParseDocument(html);
            if (document.All.Length > HtmlDepth.MaxElements)
            {
                throw new LimitException(TooManyElements);
            }

            // The last element created has not been checked by a later callback, and the last stray tag has not been checked by a later error.
            if (TooManyAttributes(previous) || TooManyAttributes(htmlElement) || TooManyAttributes(bodyElement))
            {
                throw new LimitException(AttributesMessage);
            }

            if (Refusal(document) is { } refusal)
            {
                throw new LimitException(refusal);
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

    private static bool TooManyAttributes(IElement? element) => element is not null && element.Attributes.Length > MaxAttributes;

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

    /// <summary>
    /// The reason to refuse the tree, or null. Refuses a tree whose deepest element is past <see cref="HtmlDepth.MaxDepth"/>, whose element depths add up to more than
    /// <see cref="MaxDepthSum"/>, whose text lengths times their depths add up to more than <see cref="MaxTextDepth"/>, or whose attribute names and values add up to
    /// more than <see cref="MaxAttributeText"/>. The root element is at depth 1 and a text node is one deeper than its parent. An explicit stack, so a deep tree
    /// cannot overflow this walk.
    /// </summary>
    private static string? Refusal(IHtmlDocument document)
    {
        if (document.DocumentElement is not { } root)
        {
            return null;
        }

        long attributeText = 0;
        var deepest = 0;
        long sum = 0;
        long textSum = 0;
        var pending = new Stack<(INode Node, int Depth)>();
        pending.Push((root, 1));
        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            if (node is IText text)
            {
                textSum += (long)text.Length * depth;
                if (textSum > MaxTextDepth)
                {
                    return TooDeep;
                }

                continue;
            }

            if (node is not IElement element)
            {
                continue;
            }

            sum += depth;
            if (depth > deepest)
            {
                deepest = depth;
            }

            if (deepest > HtmlDepth.MaxDepth || sum > MaxDepthSum)
            {
                return TooDeep;
            }

            foreach (var attribute in element.Attributes)
            {
                attributeText += attribute.Name.Length + (long)(attribute.Value?.Length ?? 0);
            }

            if (attributeText > MaxAttributeText)
            {
                return AttributesMessage;
            }

            foreach (var child in element.ChildNodes)
            {
                pending.Push((child, depth + 1));
            }

            // A template keeps its content in a separate fragment, which ChildNodes does not reach.
            if (element is IHtmlTemplateElement template)
            {
                foreach (var child in template.Content.ChildNodes)
                {
                    pending.Push((child, depth + 1));
                }
            }
        }

        return null;
    }
}
