import json
import sys
import urllib.parse
import urllib.request

SEARXNG_URL = "http://127.0.0.1:8888/search"

# The tool definition you give the model. This is the shape the Anthropic
# Messages API expects. Other providers use a very similar JSON schema.
WEB_SEARCH_TOOL = {
    "name": "web_search",
    "description": "Search the web. Returns the title, URL and a short snippet for each result.",
    "input_schema": {
        "type": "object",
        "properties": {"query": {"type": "string", "description": "The search query"}},
        "required": ["query"],
    },
}


def web_search(query: str, limit: int = 5) -> list[dict]:
    """Call SearXNG and return a short, model-sized list of results."""
    url = SEARXNG_URL + "?" + urllib.parse.urlencode({"q": query, "format": "json"})
    with urllib.request.urlopen(url, timeout=30) as response:
        data = json.load(response)
    return [
        {"title": r["title"], "url": r["url"], "snippet": r.get("content", "")[:200]}
        for r in data["results"][:limit]
    ]


if __name__ == "__main__":
    print(json.dumps(web_search(sys.argv[1], limit=3), indent=2, ensure_ascii=False))
