---
title: "SearXNG search API: run it in Docker and call it as JSON"
description: "Run SearXNG in Docker, turn on its JSON API and call it from curl, Python and C#. Real captured output, plus MCP, agent use and paid API prices."
h1: "SearXNG search API: run it in Docker and call it as JSON"
---

SearXNG is a free, open-source metasearch engine. Run it in Docker, enable the `json` format in `settings.yml`, and you have a free web search API at `/search?q=...&format=json`.

Everything below was run on 21 October 2026 against SearXNG `2026.10.19-81371861f`, pulled as the image `searxng/searxng@sha256:5ed0323b…`. Each command and its output was written to a file by one script, `capture.sh`, and is shown here unedited. The only cut is where a `jq` command in the capture trims the results array, and that command is shown with it.

<<sample: 00-environment.txt>>

## What SearXNG is, and what it is not

SearXNG describes itself as "a free internet metasearch engine which aggregates results from up to 261 search services", and says users are neither tracked nor profiled (docs.searxng.org home page, read 21 October 2026). It is AGPL-3.0 licensed. It has no release numbers in the usual sense. The version is a date plus a commit, such as `2026.10.19-81371861f`.

A metasearch engine does not keep its own web index. When you send a query, SearXNG forwards it to upstream engines such as Brave, Bing, Google, DuckDuckGo and Wikipedia, then merges, scores and de-duplicates what comes back. That design has three limits you should know before you build on it.

- **It scrapes.** Upstream engines did not agree to be queried by your instance. They can show a CAPTCHA, return HTTP 429 or block your IP. The capture further down shows this happening on a quiet single-user instance.
- **Quality is the upstream's quality.** There is no ranking model of its own. The score is worked out from how many engines returned a result and at what position (`searx/results.py`, `calculate_score`).
- **There is no service level.** If you run it, you are the operator. Engines break when upstream pages change, and the image updates often.

For a personal tool or an agent on your own machine, those limits are usually fine. For a product with paying users, read the comparison with paid APIs below first.

## Run SearXNG in Docker

You need two things: a `settings.yml` and one `docker run`. The official docs offer a Docker Compose route as the recommended one and a plain `docker run` as the advanced one (docs.searxng.org, installation container page). The plain command is the shortest way to a working instance, so it is used here.

This is the whole settings file. `use_default_settings: true` keeps every default and the file only lists changes.

<<sample: settings.yml>>

Three lines matter.

- `search.formats` lists the output formats the instance allows. The default is `html` only (`searx/settings.yml`, lines 85 and 86, at commit `81371861f`). Adding `json` is what turns the API on.
- `server.secret_key` is used for cryptography. The shipped default is `ultrasecretkey`, so replace it. Make a real one with `openssl rand -hex 32`. The value in this file is a placeholder.
- `server.limiter` is `false` by default (`searx/settings.yml`, line 97). Leave it off for a private instance. More on that below.

Now start the container.

<<sample: 01-docker-run.txt>>

The `-p 127.0.0.1:8888:8080` part matters. Docker publishes a port on all interfaces unless you give an address. Writing `127.0.0.1` keeps the instance reachable from your own machine only. An open SearXNG instance is an open proxy to search engines, and strangers will find it.

If you prefer Compose, the official file at `container/docker-compose.yml` publishes the port with `${SEARXNG_HOST:+${SEARXNG_HOST}:}${SEARXNG_PORT:-8080}:${SEARXNG_PORT:-8080}`. If `SEARXNG_HOST` is unset, that line has no address, so the port opens on every interface. Set `SEARXNG_HOST=127.0.0.1` in the `.env` file. The Compose file also starts a Valkey container for the limiter.

Check the version the container reports.

<<sample: 02-version.txt>>

## Why the JSON format returns 403 by default

A fresh instance allows `html` only. The official Search API page says so directly: "Requesting an unset format will return a 403 Forbidden error", and "many public instances have these formats disabled" (docs.searxng.org, Search API). The check is in the source. If the requested format is not in `settings['search']['formats']`, the handler calls `flask.abort(403)` (`searx/webapp.py`, line 655).

This is the capture from a second container that uses the same settings but leaves out the `json` line.

<<sample: 03-json-403.txt>>

The 403 body is a plain HTML page, not JSON. It says nothing about the real cause, which is why this error costs people an hour. The fix is the `formats` list in your settings file, then a container restart. The same rule applies to `csv` and `rss`.

## The query URL and its parameters

The endpoint is `/search`. It takes GET with URL parameters, or POST with `application/x-www-form-urlencoded` form data. Both `/` and `/search` accept both methods (docs.searxng.org, Search API). A POST capture is below.

| Parameter | Values | Notes |
|---|---|---|
| `q` | any string | Required. Sent on to each engine, so operators like `site:` only work where the engine supports them. |
| `format` | `json`, `csv`, `rss` | Must be enabled under `search.formats`, or the answer is 403. |
| `categories` | comma-separated, such as `general`, `news`, `it` | Picks every engine in those categories. Unknown names are ignored. |
| `engines` | comma-separated engine names, such as `brave,bing` | Not listed on the docs page. Read from `searx/webadapter.py`, line 181. Unknown names are ignored. |
| `language` | a language code, such as `de` | Defaults to the `search.default_lang` setting. |
| `pageno` | integer, 1 or more | Defaults to 1. Anything else is rejected (`webadapter.py`, line 48). |
| `time_range` | `day`, `month`, `year` | The docs list these three. The code also accepts `week` (`webadapter.py`, line 99). Engines that cannot filter by time ignore it. |
| `safesearch` | `0`, `1`, `2` | None, moderate, strict. Defaults to the `search.safe_search` setting. |

An invalid value in one of the checked parameters returns HTTP 400 with a JSON body that holds an `error` message (`webapp.py`, lines 681 to 685).

Here are the parameters running for real. First `engines`. Only the two named engines answer.

<<sample: 06-param-engines.txt>>

Now `categories` with `time_range`. The `news` category sends the query to news engines only.

<<sample: 07-param-categories-time-range.txt>>

`publishedDate` is `null` for these three results. Only some engines pass a date through, so do not rely on the field being filled.

Paging with `pageno`. The second page gives different URLs.

<<sample: 08-param-pageno.txt>>

`language` and `safesearch` together.

<<sample: 09-param-language-safesearch.txt>>

And POST, which returns the same shape.

<<sample: 10-post.txt>>

A bang in the query also works, for example `!wp paris` to select Wikipedia, or `:fr` to set the language. This is the same syntax as the web page's search box (docs.searxng.org, search syntax). For an API, the `engines` and `language` parameters are easier to read and to generate in code.

## Call it with curl and read the response

This is the full response for `q=searxng docker`. The capture keeps everything and cuts only the `results` array to its first two items with `jq '.results |= .[:2]'`.

<<sample: 04-search-json.txt>>

The top level has seven keys.

- `query` repeats your query.
- `results` is the list you want. Each item has `title`, `url`, `content` (the snippet), `engine`, `engines` (every engine that returned it), `score`, `category`, `publishedDate` and `thumbnail`.
- `answers`, `infoboxes` and `corrections` are filled for some queries.
- `suggestions` holds related queries.
- `unresponsive_engines` lists the engines that failed on this request, and why. This is the most useful field when something goes wrong.

The `score` is higher when more engines return the same URL near the top. The code multiplies a weight by the number of engines, then adds the weight divided by the position for each engine (`searx/results.py`, `calculate_score`). The first item above was returned by two engines at position 1, so its score is 4.0.

## How to tell it is working

Run this one command. It should print a number above zero.

<<sample: 05-working-check.txt>>

If it prints `35` or any other positive number, your instance works as a search API. Stop here and test your client. If not, find your symptom below.

**403 Forbidden.** The `json` format is not enabled. Add it under `search.formats` in `settings.yml`, restart the container, and run the command again. See the 403 capture above.

**429 Too Many Requests, with the body `Too Many Requests`.** The limiter is on and has decided your request looks like a bot. A private instance does not need the limiter, so set `server.limiter: false`. The full capture and the fix for when you do want it are in the next section.

**An empty results array.** The instance is up, but the upstream engines refused it. Look at `unresponsive_engines`. This is the capture from the same instance, asking for one engine that was suspended at the time.

<<sample: 17-empty-results.txt>>

The `results` array is empty and the reason is in `unresponsive_engines`. SearXNG suspends an engine for a while after an error. The documented defaults are 3,600 seconds for HTTP 429 and 86,400 seconds for a CAPTCHA or an access denied answer (docs.searxng.org, `search:` settings). On a normal query, other engines still fill the list. This is the same field on the main query.

<<sample: 16-unresponsive-engines.txt>>

DuckDuckGo showed a CAPTCHA and Wikipedia was suspended, yet the same query returned 35 results from the other engines. The fixes are to send fewer queries, to keep several engines enabled so one failure does not empty the list, and to wait out the suspension.

## Call it from Python and C#

Both clients below use only the standard library of each language. They send `format=json`, read `results` and print the first three.

Python, with `urllib` so there is nothing to install.

<<sample: client.py>>

<<sample: 11-python.txt>>

C#, as a single-file app on the .NET 10 SDK. The first line turns off native AOT publishing, which the single-file mode enables by default and which blocks reflection-based JSON binding.

<<sample: client.cs>>

<<sample: 12-csharp.txt>>

Both runs returned the same top three results, which is expected for the same query a few seconds apart. The result counts differ by one between runs (35 and 36), which is normal. Upstream engines do not return identical lists every time.

## Rate limiting: the limiter and botdetection

SearXNG has a built-in limiter. It exists to protect public instances, because bot traffic makes upstream engines serve CAPTCHAs or block the instance (docs.searxng.org, limiter). You turn it on with `server.limiter: true`, and it needs a Valkey database. Its rules live in `/etc/searxng/limiter.toml`.

The limiter reads the client address from the `X-Forwarded-For` header, as set by a reverse proxy. A client that talks to the container directly sends no such header. This is what happens with the limiter on and no extra configuration.

<<sample: settings-limiter.yml>>

<<sample: 18-limiter-429.txt>>

The log line says it: `X-Forwarded-For nor X-Real-IP header is set!`. The request is refused with 429 before any search runs.

There are two real fixes. If a reverse proxy sits in front, make it set `X-Forwarded-For` and list the proxy in `trusted_proxies`. If only your own code calls the instance, add that address to `pass_ip`. The docs say an address in `pass_ip` is not checked for bot-like signals such as a `curl` user agent. This `limiter.toml` passes the default Docker bridge range.

<<sample: limiter.toml>>

<<sample: 19-limiter-fixed.txt>>

Only do this while the published port is bound to `127.0.0.1`, as above. For a private instance, the simplest answer is still `limiter: false`.

## Use it as a tool for an LLM agent

An agent needs a function that takes a query and returns a short list the model can read. The raw response is too long to send, since every result carries thumbnails, positions and a parsed URL. Keep the title, URL and a trimmed snippet.

The file below defines the tool in the JSON schema format the Anthropic Messages API expects, and a `web_search` function that calls the instance.

<<sample: agent_tool.py>>

This is what the model would receive back as the tool result.

<<sample: 13-agent-tool.txt>>

That run calls the instance and prints the tool result. It did not call a model. To use it, pass `WEB_SEARCH_TOOL` in your `tools` list, and when the model answers with a `web_search` tool call, run the function and return its output as the tool result. Other providers use the same idea with a slightly different wrapper.

Give the model the URLs and have it cite them. If your agent also fetches pages, fetch only URLs that came from a search result, so every claim can be traced to a page that was actually read.

## Use SearXNG through MCP

The Model Context Protocol lets a client such as Claude Desktop or Cursor call tools in a separate process. A SearXNG MCP server wraps the JSON API above, so you do not have to write the tool yourself.

The most active one found on 21 October 2026 is [ihor-sokoliuk/mcp-searxng](https://github.com/ihor-sokoliuk/mcp-searxng). Its repository shows the following, read the same day.

- TypeScript, MIT licence, not archived, 1,322 stars.
- Last push on 20 October 2026. Latest release `v2.7.0`, published the same day. It has shipped a release about every week since late August.
- Install with `npx -y mcp-searxng` (Node.js 22 or later), or use the Docker image `isokoliuk/mcp-searxng`.
- It points at your instance with the `SEARXNG_URL` environment variable.

The README says you need "an existing SearXNG instance with JSON search enabled". The server does not install SearXNG. The client configuration for Claude Desktop, taken from the README, is:

```json
{
  "mcpServers": {
    "searxng": {
      "command": "npx",
      "args": ["-y", "mcp-searxng"],
      "env": { "SEARXNG_URL": "http://127.0.0.1:8888" }
    }
  }
}
```

The script talks to version 2.7.0 over stdio with raw JSON-RPC, the same way a client does, against the instance started above. `mcp_session.sh` sends `initialize`, then `tools/list` or `tools/call`.

<<sample: mcp_session.sh>>

The server exposes four tools. `searxng_web_search` and `searxng_search_suggestions` need a `query`. `web_url_read` needs a `url`.

<<sample: 14-mcp-tools.txt>>

A real `searxng_web_search` call returns the results as plain text. The capture keeps the first 14 lines.

<<sample: 15-mcp-search.txt>>

Its own documentation advises against relying on a public instance: "Do not automate against an instance whose policy is missing, unclear, or incompatible with your use." Point `SEARXNG_URL` at an instance you run.

## Clean up

The script stops and removes every container and the network it started, and the last capture shows zero left. For your own instance, `docker rm -f searxng` does the same.

<<sample: 20-cleanup.txt>>

## SearXNG compared with paid search APIs for LLMs

Paid search APIs sell what SearXNG cannot promise: an agreement with the search provider, a rate limit you can plan around, and support. They cost money and some have terms on what you may do with results. The figures below come from each vendor's own page, fetched on 21 October 2026. Prices change, so check the page before you commit.

| Service | Free tier | Paid price | Source |
|---|---|---|---|
| SearXNG, self-hosted | Free software. You pay for the machine. | None | docs.searxng.org |
| Tavily | 1,000 credits a month, no card | $0.008 per credit on pay as you go. A basic search costs 1 credit, so about $8 per 1,000 searches (calculated). An advanced search costs 2. | tavily.com/pricing, docs.tavily.com |
| Brave Search API | $5 of credit each month. A card is asked for "to prevent abuse". | $5 per 1,000 requests, 50 requests a second | api-dashboard.search.brave.com |
| Exa | $10 of credit each month, plus a one-time $10 bonus. No card. | Lite search $4 per 1,000, auto $7, deep $12 | exa.ai/pricing |
| Serper | 2,500 free queries on sign-up, no card | $1.00 per 1,000 on the $50 pack, down to $0.30 per 1,000 on the largest | serper.dev |

Some notes on reading the table.

- Brave's terms, last updated 1 September 2026, say you may not "store, cache, or create a database of Search Results", or use results to train or improve AI models. Its FAQ says a plan with storage rights lifts that, but no such plan is listed on the pricing page. Read the terms if your agent keeps what it finds.
- Serper's pack credits are valid for 6 months. The page does not say whether the free queries expire.
- Google's Custom Search JSON API is closed to new customers and ends on 1 January 2027, so it is not an option for a new project.
- Tavily, Brave and Exa also sell answer, extraction or research endpoints. The table compares plain search only.

**If you are looking for a Tavily alternative**, SearXNG is the free one. It returns links and snippets only. It does not extract page text or summarise, so you need your own reader for that. If you only need a free web search API for prototypes, the Tavily, Exa and Serper free tiers are quicker to start, and each gives you a few thousand searches before you pay anything. SearXNG wins when the volume is high, when you want no third party to see your queries, or when you do not want a card on file.

**A rule of thumb for choosing.** Use SearXNG when a person or a small agent sends a few hundred queries a day and you can live with an occasional blocked engine. Use a paid API when a failed search means a failed product, when you need a contract that allows your use, or when you need a rate limit you can plan around. Many teams start on SearXNG for development and keep a paid key as a fallback.

## Public instances are not an API

Many people try a public SearXNG instance first, to skip Docker. This usually fails, and the docs warn about it. Public instances are listed on searx.space, which says they "have a higher chance of being blocked by search providers". SearXNG's own page on private instances says public ones "are operated by unknown parties", that their users "have to trust the administrator", and that requests to an unprotected instance can lead to CAPTCHAs or an IP ban. It advises you to use an instance operated by people you trust.

A test on 21 October 2026 sent one `format=json` request to each of 71 clearnet instances from the searx.space list, from one IP address. None returned JSON. Most answered 429. The rest returned 403, other errors, redirects, bot checks or login pages. That shows how scripts are treated, and it is one IP at one moment. It does not show how many instances have JSON on. Each instance has its own policy. Run your own.

## Where Gannet fits

[Gannet](https://github.com/dbhq-uk/gannet) is a deep research tool in development and not released. SearXNG with a local Ollama model is designed to be its fully free path, with no API keys.

## FAQ

### Does SearXNG have an API?

Yes. The `/search` endpoint returns JSON when you pass `format=json`, and it also supports `csv` and `rss`. The instance must allow the format under `search.formats` in `settings.yml`. A default instance allows `html` only, so a JSON request returns 403 until you enable it.

### Is SearXNG free?

Yes. It is open-source software under the AGPL-3.0 licence, and it has no usage fees or API keys. You pay only for the machine that runs it. It queries upstream engines on your behalf, and those engines may block or rate limit you, so heavy use can need care.

### How do I enable JSON in SearXNG?

Open `settings.yml` and add `json` to the list under `search.formats`, next to `html`. Restart the container. Then request `/search?q=test&format=json`. Without that change, SearXNG answers 403 Forbidden for every `format=json` request, because the format is off by default.

### Can I use SearXNG with an LLM?

Yes. Wrap the JSON API in a tool function that returns the title, URL and snippet of the top results, then pass that tool to the model. You can also use a SearXNG MCP server such as mcp-searxng, which gives any MCP client a search tool that points at your instance.

### Why do I get 429 from SearXNG?

If the limiter is on, SearXNG returns 429 when it cannot read a client address from `X-Forwarded-For`, or when it judges a request to be a bot. For a private instance, set `server.limiter: false`. Otherwise set `X-Forwarded-For` through a proxy, or add your address to `pass_ip`.

### Does SearXNG work without a card or an API key?

Yes. A self-hosted instance needs no account, card or key. Paid search APIs ask for a key, and some ask for a card even for the free tier. The cost of self-hosting is your own upkeep, and the chance that an upstream engine blocks you.

## Sources

All pages were read on 21 October 2026.

- SearXNG, Search API: https://docs.searxng.org/dev/search_api.html
- SearXNG, installation container: https://docs.searxng.org/admin/installation-docker.html
- SearXNG, search syntax: https://docs.searxng.org/user/search-syntax.html
- SearXNG, `search:` settings: https://docs.searxng.org/admin/settings/settings_search.html
- SearXNG, `server:` settings: https://docs.searxng.org/admin/settings/settings_server.html
- SearXNG, limiter: https://docs.searxng.org/admin/searx.limiter.html
- SearXNG, why use a private instance: https://docs.searxng.org/own-instance.html
- SearXNG source at commit `81371861f67226091d1c4df2e3908a11d3b54eb9` (`searx/webapp.py`, `searx/webadapter.py`, `searx/settings.yml`, `container/docker-compose.yml`): https://github.com/searxng/searxng/tree/81371861f67226091d1c4df2e3908a11d3b54eb9
- SearXNG public instances list: https://searx.space
- mcp-searxng repository and its public instances note: https://github.com/ihor-sokoliuk/mcp-searxng
- Tavily pricing: https://www.tavily.com/pricing and https://docs.tavily.com/documentation/api-credits
- Brave Search API pricing and terms: https://api-dashboard.search.brave.com/documentation/pricing and https://api-dashboard.search.brave.com/terms-of-service
- Exa pricing: https://exa.ai/pricing and https://docs.exa.ai/reference/pricing
- Serper pricing: https://serper.dev/
- Google Custom Search JSON API: https://developers.google.com/custom-search/v1/overview
- Captures: `samples/searxng/capture.sh` and its numbered output files, run on 21 October 2026
