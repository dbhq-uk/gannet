import json
import sys
import urllib.parse
import urllib.request

BASE_URL = "http://127.0.0.1:8888/search"


def search(query, **params):
    params.update(q=query, format="json")
    url = BASE_URL + "?" + urllib.parse.urlencode(params)
    with urllib.request.urlopen(url, timeout=30) as response:
        return json.load(response)


query = sys.argv[1] if len(sys.argv) > 1 else "searxng docker"
data = search(query)

print(f"query: {data['query']}")
print(f"results: {len(data['results'])}")
for result in data["results"][:3]:
    print(f"- {result['title']}")
    print(f"  {result['url']}")
    print(f"  engines: {', '.join(result['engines'])}")
