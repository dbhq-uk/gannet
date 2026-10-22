---
title: "Perplexity alternative: hosted and open-source options"
description: "The best Perplexity alternatives compared. Hosted answer engines and self-hosted open-source ones, with prices, licences and how to test a replacement."
h1: "Perplexity alternative: hosted and open-source answer engines compared"
---

If you want a hosted Perplexity alternative, the main choices are ChatGPT search, Google's AI Mode and Gemini, Kagi Assistant and Brave's Ask Brave. If you want an open-source one you can run yourself, look at Vane (the project formerly called Perplexica), Morphic and SearXNG paired with a model.

All facts below were read from each product's own site or repository on 21 October 2026. Prices are in US dollars as shown on that day and change often. Check the vendor's page before you pay.

## What Perplexity does

Perplexity is an answer engine. You ask a question. It searches the web, reads what it finds and writes a short answer with citations that point to the pages it used. Its home page says: "Get fast and accurate answers from the most trusted sources."

Its pricing page lists these individual plans:

- **Free**, $0 a month. Perplexity describes it as "search only with accurate answers and citations", with limited weekly usage.
- **Pro**, $17 a month when billed annually. It adds Deep Research and a choice of AI models.
- **Max**, $167 a month when billed annually. It has the highest limits.

There is also an Enterprise plan. The page does not show a monthly-billing price for Pro. No self-hosted edition is listed on it.

Deep Research is the longer mode. Perplexity's launch post of 14 February 2025 says it "performs dozens of searches, reads hundreds of sources" and delivers a report in 2 to 4 minutes. At launch, free users got a limited number of Deep Research answers a day.

People look for an alternative for three common reasons. They want a cheaper or free option. They want more privacy. Or they want to choose the model and search source themselves.

## Hosted alternatives

These are services run by someone else. You sign in or use them in the browser.

### ChatGPT search

OpenAI's help page says web search is available on the Free, Go, Plus, Pro, Business, Enterprise and Edu plans, and people who are not signed in can use it. Responses "may include citations", and a Sources button opens the list of links.

OpenAI is plain about the limits: "Search results and citations can be incomplete, outdated, or incorrect. Open a cited source to check that it supports the answer." On privacy, the page says ChatGPT rewrites your question into targeted queries and sends them to search providers, and it names Microsoft and Shopify. The pricing page lists Free, Go, Plus and Pro. The dollar prices did not appear in the page text that could be read, so none are given here.

### Google AI Mode and Gemini

AI Mode is part of Google Search. Google's help page calls it "Google's most powerful AI search experience". It shows links to the web beside its answers, and says: "AI Mode will provide a set of web links if there's not high enough confidence." The page does not state a price.

Gemini is Google's separate chat app. Its plans page shows a Free plan at "$0 / month with a Google Account" that includes Deep Research. Google AI Plus is $4.99 a month and Google AI Pro is $19.99 a month. The page read did not cover privacy.

### Microsoft Copilot and Bing

Microsoft's Copilot Search page says it "has been replaced" by AI Mode and AI Overview in Bing. It says AI Mode provides "links to sources so you can explore and verify information for yourself". The page does not state a price.

Copilot is also sold inside Microsoft 365. Microsoft's consumer page lists Personal at $9.99 a month, Family at $12.99 and Premium at $19.99. That page did not say whether Copilot cites web sources, so check this yourself before relying on it for research.

### Kagi Assistant

Kagi is a paid search engine with no ads. Its Assistant combines large language models with optional results from Kagi Search, and you can switch web access on or off.

Kagi's pricing page shows a free trial of 100 searches, then Starter at $5 a month, Professional at $10 and Ultimate at $25, plus sales tax. The longer Research mode is on Ultimate only. The docs state that your data is "never used to train AI models (not by us or by the LLM providers)" and that threads are deleted after 24 hours of inactivity by default. The pages read did not describe how citations are shown, so test that in the trial.

### You.com

You.com's site now leads with web search APIs for developers, not a consumer app. Its Answer API turns a question into "a cited answer with the sources it used" and costs $5 per 1,000 calls. Its Research API runs from $12 to $1,200 per 1,000 calls depending on depth. A free tier gives 100 search queries a day through MCP. This is a building block for your own tool, so it suits developers, not people who want a chat page.

### Brave Search (Ask Brave)

Brave's launch post of 29 September 2025 says Ask Brave "is free and available on any browser or platform". Its help page says AI Answers give a summary "along with references to sources", and Ask Brave adds follow-up chat and a Deep Research mode. On privacy, Brave says conversations are never used for training, chats are encrypted, and they expire by default after 24 hours of inactivity. All AI features can be turned off in settings.

### Phind

Phind appears to have closed. Its site returned an error on 21 October 2026, and third-party reports date the closure to 16 January 2026. No statement from Phind itself was found, so treat this as unconfirmed and do not plan around it.

## Open-source and self-hosted alternatives

Here you run the software. It needs a model to write the answer and a search service to find pages. The model can be a cloud API or a local one through Ollama. Stars and commit dates are from GitHub on 21 October 2026.

### Vane (formerly Perplexica)

Many guides still say Perplexica. That repository now sits at github.com/ItzCrazyKns/Vane, and GitHub redirects the old name. It is MIT licensed, has 37,595 stars and its last commit was on 1 September 2026.

The README calls it a "privacy-focused AI answering engine" that gives answers with cited sources. It uses SearXNG for web search, and the main Docker image bundles SearXNG, so one command starts it. It works with local models through Ollama and with OpenAI, Claude, Gemini and Groq. It has Speed, Balanced and Quality modes, file uploads and searches limited to chosen domains. Tavily and Exa support is listed as "coming soon".

### Morphic

Morphic is Apache-2.0 licensed with 9,190 stars and a commit on 21 October 2026. It describes itself as "an AI-powered search engine with a generative UI". Search providers are Tavily, SearXNG, Brave and Exa. Models can be OpenAI, Anthropic, Google, Ollama or any OpenAI-compatible provider.

The Docker Compose setup starts PostgreSQL, Redis, SearXNG and Morphic together. That gives you chat history, accounts, a guest mode and shareable result links, but it is more to run than Vane's single container.

### Farfalle

Farfalle is Apache-2.0 licensed with 3,544 stars. Its last commit was on 27 September 2024, more than two years before this guide. It supports SearXNG, Tavily, Serper and Bing, and local models through Ollama. It is an open-source Perplexity-style tool, but nobody has changed it for two years. Pick Vane or Morphic for anything you plan to keep running.

### SearXNG with a local model

SearXNG is not an answer engine. It is a metasearch engine, AGPL-3.0 licensed, with 38,708 stars and a commit on 19 October 2026. It returns lists of results and says users "are neither tracked nor profiled". Pair it with a model and it becomes the search half of an answer engine. That is what Vane, Morphic and Farfalle do for you. Run Ollama (MIT licensed) for a local model and you can keep both halves on your own machine.

### Others worth a look

Khoj (AGPL-3.0, 37,780 stars, last commit 2 August 2026) is a personal AI app that says it gets "answers from the internet and your docs" with local or online models. It is broader than an answer engine. LLocalSearch is archived on GitHub, so skip it. MindSearch had no commit after July 2025.

A separate page covers open-source agents that write long reports, such as open_deep_research and GPT Researcher. See [open-source deep research agents](/open-source-deep-research/). This page stays on quick answer engines.

## Comparison tables

### Hosted

| Service | Price (21 Oct 2026) | Cites sources | Privacy note |
| --- | --- | --- | --- |
| Perplexity (baseline) | Free; Pro $17 a month billed annually | Inline citations on every answer, per its pricing page | Policy not reviewed here |
| ChatGPT search | Free plan includes search; paid plans not priced here | "May include citations" | Rewritten queries go to search providers |
| Google AI Mode | Not stated on help page | Links to the web | Not covered on page read |
| Gemini | Free; AI Plus $4.99; AI Pro $19.99 a month | Not covered on page read | Not covered on page read |
| Microsoft Copilot and Bing AI Mode | Not stated; Microsoft 365 Personal $9.99 a month | Bing AI Mode links to sources | Not covered on page read |
| Kagi Assistant | Trial; $5, $10 or $25 a month | Not described on pages read | Not used for training; threads deleted after 24 hours by default |
| You.com | API only; Answer API $5 per 1,000 calls | Answer API returns cited answers | Not covered on page read |
| Ask Brave | Free, per launch post | References to sources | Not used for training; chats expire after 24 hours by default |
| Phind | Reported closed | n/a | n/a |

### Open source and self-hosted

| Project | Licence | Stars | Last commit | Search backends | Local models | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Vane (formerly Perplexica) | MIT | 37,595 | 1 Sep 2026 | SearXNG (bundled) | Yes, Ollama | Maintained |
| Morphic | Apache-2.0 | 9,190 | 21 Oct 2026 | Tavily, SearXNG, Brave, Exa | Yes, Ollama | Maintained |
| Farfalle | Apache-2.0 | 3,544 | 27 Sep 2024 | SearXNG, Tavily, Serper, Bing | Yes, Ollama | No commits for two years |
| SearXNG | AGPL-3.0 | 38,708 | 19 Oct 2026 | It is the search layer | Not applicable | Maintained, but not an answer engine |
| Khoj | AGPL-3.0 | 37,780 | 2 Aug 2026 | Web and your own documents | Yes | Maintained, broader scope |
| Gannet | MIT | Not released | Not released | Planned: a choice of search services, including SearXNG | Planned: Ollama, vLLM, llama.cpp | In development, not released |

## How to choose

**Privacy.** A hosted service sees your questions. Kagi and Brave state they do not train on them and delete chats after 24 hours by default. Self-hosting helps most when the model is local too. A self-hosted setup that calls a cloud model still sends your questions to that provider, and the search service still sees your queries.

**Cost.** Perplexity Free, Gemini Free and Ask Brave cost nothing. Open-source tools are free to download, but you pay for hardware or model API calls.

**Citation quality.** No table can settle this. Every tool above can cite a page that does not support the claim next to it. OpenAI says so in its own help page. Test it with the process in the next section.

**Local models.** Vane, Morphic and Farfalle all list Ollama. Small local models usually write weaker answers than large hosted ones, so try your real questions before you commit.

**Maintenance burden.** A hosted service has none. Vane is one Docker command. Morphic needs a multi-container stack. Any self-hosted tool needs you to update it and fix it when a search backend changes.

## How to tell it is a good replacement

Do not judge by one question. Run the same five questions through Perplexity and the candidate:

1. A news question where the answer changed this week.
2. A factual question with a number you can check in one primary source.
3. A question where good sources disagree.
4. A narrow question from your own work.
5. A question with a false premise, where the honest answer is "that is not right".

For each answer, open every cited source and check that the page says what the answer claims. Mark each claim as supported, partly supported, not supported or not found on the page. Note the time taken and any cost.

The candidate is a good replacement if it matches Perplexity on at least four of the five questions and rarely cites a page that does not back the claim. If it fails the false-premise question, it is agreeing with you instead of checking.

## A different shape: Gannet

Gannet is a self-hosted deep research tool being built in C# on Microsoft Agent Framework. It is MIT licensed and made by DBHQ, and the code is at github.com/dbhq-uk/gannet. It is in development and not released, so nothing can be installed or used today.

It is also a different shape from Perplexity. It is being built to plan a piece of research, search, weigh each source and write a report in which every citation points at a page the run fetched. It is not designed to give a quick answer in a few seconds. It is planned to offer a choice of model, cloud or local through Ollama, vLLM and llama.cpp, and a choice of search service including self-hosted SearXNG. The design stores no search result text on disk. A web UI, command line, REST API and MCP server are planned.

If you need a Perplexity replacement now, use one of the tools above.

## Frequently asked questions

### Is there a free alternative to Perplexity?

Yes. Gemini has a Free plan at $0 a month with a Google Account, and it includes Deep Research. Ask Brave is described as free by Brave. ChatGPT's Free plan includes web search. Open-source tools such as Vane cost nothing to download, but you pay for hardware or model calls.

### Is there an open-source Perplexity?

Yes. Vane, formerly called Perplexica, is MIT licensed and gives answers with cited sources. Morphic is Apache-2.0 licensed. Both can use SearXNG for search and Ollama for local models. Perplexity itself is a hosted service, and its pricing page lists no open-source or self-hosted edition.

### Can I self-host Perplexity?

Not that this guide could find. Perplexity's pricing page lists Free, Pro, Max and Enterprise plans, and none is described as self-hosted. To get a similar tool on your own server, run an open-source answer engine such as Vane or Morphic, and choose your own model and search backend.

### What is the best open-source Perplexity alternative?

Vane is the best starting point for most people. It has the most stars of the three Perplexity-style answer engines listed, an MIT licence, a one-command Docker image that bundles SearXNG, and a recent commit. Morphic suits you if you want accounts and chat history. Test both on your own questions.

### Are open-source alternatives as good as Perplexity?

It depends on the model and search service you connect. The software only organises the work. A small local model will usually write weaker answers than a large hosted one. Run the five-question test above and judge by what you find, not by the project's own description.

### Which Perplexity alternative is best for privacy?

Self-hosting with a local model keeps the most on your own machine. Among hosted options, Kagi and Brave both state that your data is not used for training and that chats are deleted after 24 hours by default. Neither claim has been tested here, so read their privacy policies.

## Sources

All pages were read on 21 October 2026. Raw copies and notes are in the research folder `research/perplexity-alternative/`.

- Perplexity home: https://www.perplexity.ai/
- Perplexity pricing: https://www.perplexity.ai/hub/pricing
- Perplexity Deep Research launch post, 14 February 2025: https://www.perplexity.ai/hub/blog/introducing-perplexity-deep-research
- OpenAI, Searching the web with ChatGPT: https://help.openai.com/en/articles/9237897-chatgpt-search
- ChatGPT pricing: https://chatgpt.com/pricing
- Google, AI Mode help: https://support.google.com/websearch/answer/16011537
- Google, Gemini plans: https://gemini.google/subscriptions/
- Microsoft, Copilot Search and Bing AI Mode: https://explore.microsoft.com/en-us/bing/copilot-search/
- Microsoft, Copilot for individuals: https://www.microsoft.com/en-us/microsoft-copilot/for-individuals
- Kagi pricing: https://kagi.com/pricing
- Kagi Assistant docs: https://help.kagi.com/kagi/ai/assistant.html
- You.com pricing: https://you.com/pricing
- Brave, Introducing Ask Brave, 29 September 2025: https://brave.com/blog/ask-brave/
- Brave, AI in Brave Search: https://search.brave.com/help/ai
- Phind site (error on 21 October 2026): https://www.phind.com/
- Vane repository and README: https://github.com/ItzCrazyKns/Vane
- Morphic repository and README: https://github.com/miurla/morphic
- Farfalle repository and README: https://github.com/rashadphz/farfalle
- SearXNG repository: https://github.com/searxng/searxng
- Khoj repository: https://github.com/khoj-ai/khoj
- LLocalSearch repository (archived): https://github.com/nilsherzig/LLocalSearch
- MindSearch repository: https://github.com/InternLM/MindSearch
- Ollama repository: https://github.com/ollama/ollama
- Gannet repository: https://github.com/dbhq-uk/gannet
