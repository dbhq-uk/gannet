---
title: "Open-source deep research: the main projects compared"
description: "A guide to open-source deep research agents: how they work, nine projects compared on licence, search and citations, and how to run one locally."
h1: "A guide to open-source deep research agents"
---

Deep research means an agent that plans a question, searches the web in several rounds, reads the pages it finds and writes a report with citations. Several open-source projects do this, and they differ in how they search, which models they run on and how they produce their citations.

This guide compares nine of them, using facts read from each repository on 21 October 2026. It also covers what you need to run one on your own machine, how to choose, and how to check that a report is honest.

## How deep research agents work

Almost every open-source deep research agent follows the same five steps.

1. **Plan.** A model turns your question into a plan. That may be a list of search queries, a report outline or a set of sub-questions.
2. **Search.** The agent calls a search service and gets back titles, URLs and snippets.
3. **Read.** It fetches the pages behind the best results and extracts the text.
4. **Reflect and loop.** A model asks what is still missing. If something is, it writes new queries and goes round again until a limit is reached.
5. **Write.** A model turns what it collected into a report and adds citations.

The projects differ in three ways.

### Fixed pipeline, loop or supervisor

Some projects run a fixed pipeline. Plan, search, synthesise and write happen once each, in order. [tarun7r/deep-research-agent](https://github.com/tarun7r/deep-research-agent) works this way, and so does the original STORM pipeline of pre-writing and then writing.

Some run a loop. [local-deep-researcher](https://github.com/langchain-ai/local-deep-researcher) searches, summarises, finds gaps and repeats for a set number of cycles. Its default is three. [dzhng/deep-research](https://github.com/dzhng/deep-research) loops with a breadth setting (queries per round, default 4) and a depth setting (rounds, default 2). [jina-ai/node-DeepResearch](https://github.com/jina-ai/node-DeepResearch) loops until it finds an answer or runs out of its token budget.

Some use a supervisor with sub-agents. In [langchain-ai/open_deep_research](https://github.com/langchain-ai/open_deep_research) a supervisor agent hands sub-questions to researcher agents that run in parallel. Its configuration file defaults to 5 concurrent research units, 6 supervisor iterations and 10 tool calls per researcher step.

### How many searches

There is no standard. tarun7r/deep-research-agent defaults to 3 queries and 3 results per query. The recursive and supervisor designs can make far more calls, because each round can spawn new queries. More searches usually means more cost and more time, and it does not guarantee a better report.

### How citations are produced

This is the difference that matters most, and it is the one least often compared. There are three common approaches.

- **The model writes citations.** The writing model is told to cite sources and to list them. open_deep_research does this through prompt rules: one number per URL and a Sources list at the end. Nothing in that approach stops a model from citing a page it never fetched.
- **The agent adds a source list.** dzhng/deep-research appends a list of the URLs its searches returned. The list shows where the agent looked. It does not tie each sentence to a page.
- **The agent matches text to sources afterwards.** jina-ai/node-DeepResearch writes the answer first. Then a function called `buildReferences` compares chunks of the answer with chunks of the fetched pages and attaches footnotes to the best matches.

Some projects add checks on top. [tarun7r/deep-research-agent](https://github.com/tarun7r/deep-research-agent) scores each source for credibility and formats citations in APA, MLA, Chicago or IEEE style.

## The main open-source deep research projects

All figures below were read on 21 October 2026. Stars, licence and last commit dates come from the GitHub API. Features come from each project's README and, where noted, its code.

**[langchain-ai/open_deep_research](https://github.com/langchain-ai/open_deep_research)** is the best known project under that name. Three projects in this list share the name "Open Deep Research": LangChain's, dzhng's and Hugging Face's. This one is a LangGraph agent with a supervisor and parallel researchers. It uses Tavily by default and supports OpenAI and Anthropic native web search and MCP servers. The README says the model must support tool calling and structured output. GitHub now marks the repository as archived, which makes it read-only. Its recent commits are dependency updates, and the last other commit was on 8 February 2026. The README also links a LangChain Academy course that explains how to build it.

**[assafelovic/gpt-researcher](https://github.com/assafelovic/gpt-researcher)** is the most featureful. It has a planner and execution agents, a web UI, a pip package, an MCP client, a recursive "deep research" mode and research over your own documents. Its `retrievers` folder holds 21 modules, including Tavily, Brave, Serper, SearXNG, DuckDuckGo, Exa, arXiv and PubMed Central. The docs include a guide for running it with Ollama. The README says it tracks the source of each summary. Its own figure for a deep research run is about five minutes and about $0.4 with o3-mini.

**[LearningCircuit/local-deep-research](https://github.com/LearningCircuit/local-deep-research)** is built for running at home. It ships a web UI, Docker images and a pip package. It works with Ollama, LM Studio, llama.cpp and OpenAI-compatible endpoints, and it searches SearXNG, Tavily, Brave, arXiv, PubMed, Semantic Scholar, Wikipedia, GitHub and Elasticsearch, among others. It can also search your own documents. The README publishes community benchmarks and lists their limits itself: small samples, grader noise and possible test contamination.

**[stanford-oval/storm](https://github.com/stanford-oval/storm)** is a research system more than a tool. It writes a Wikipedia-style article by asking questions from several perspectives, then drafts an outline and the article with inline citations. Co-STORM adds a human in the loop. Its authors say the output is not publication-ready, though Wikipedia editors found it useful for pre-writing. Its retriever module has classes for You.com, Bing, Serper, Brave, SearXNG, DuckDuckGo, Tavily, Google and Azure AI Search, and its language model module has Ollama and vLLM clients. The last release was in January 2025 and the last commit in September 2025.

**[dzhng/deep-research](https://github.com/dzhng/deep-research)** is the smallest. Its stated aim is the simplest possible implementation, kept under 500 lines of TypeScript. It uses Firecrawl for search and page extraction, and it can point at a local OpenAI-compatible server. By default it needs a Firecrawl key and an OpenAI key. Its README describes the report as including sources and references, and the code appends a plain list of URLs.

**[jina-ai/node-DeepResearch](https://github.com/jina-ai/node-DeepResearch)** is also TypeScript, and it has a different goal. Its README says it finds the right answer through an iterative process and does not write long reports. It uses Jina Reader for search and reading, and Gemini, OpenAI or a local model for reasoning. It also exposes an OpenAI-compatible server. If you want a long report, this is not the right fit.

**[huggingface/smolagents open_deep_research](https://github.com/huggingface/smolagents/tree/main/examples/open_deep_research)** is an example inside the smolagents library. It searches through Serper or SerpApi and uses a text browser tool to read pages. The example defaults to OpenAI's o1 model. The library itself supports local Transformers and Ollama models. The example README reports 55% pass@1 on the GAIA validation set against 67% for the original OpenAI product. It does not describe a citation mechanism. The star count in the table is for the whole smolagents repository.

**[langchain-ai/local-deep-researcher](https://github.com/langchain-ai/local-deep-researcher)** is a small LangGraph loop that runs on Ollama or LM Studio. It searches DuckDuckGo by default and can use SearXNG, Tavily or Perplexity. Its output is a markdown summary that lists the sources used. The README warns that some small models cannot produce the JSON the steps need. Its recent commits are dependency updates, and the last other commit was in August 2025.

**[tarun7r/deep-research-agent](https://github.com/tarun7r/deep-research-agent)** has four stages: planner, searcher, synthesiser and writer. It scores each source for credibility, filters out weak ones and checks sections before keeping them. It runs on Ollama, llama.cpp, Gemini or OpenAI. It has a Chainlit web UI and a command line. It searches with DuckDuckGo or Tavily. It is the smallest project in this list by stars.

## Comparison table

Facts read on 21 October 2026. Stars are GitHub stars on that date. "Last commit" is the latest commit on the default branch.

| Project | Licence | Language | Stars | Last commit | Search backends | Local models | Citations |
|---|---|---|---|---|---|---|---|
| langchain-ai/open_deep_research | MIT | Python | 12,678 | 2026-08-10 (archived) | Tavily, OpenAI and Anthropic native search, MCP | Possible via LangChain; README links a community Ollama note | Model-written numbered citations and a Sources list |
| assafelovic/gpt-researcher | Apache-2.0 | Python | 30,223 | 2026-10-10 | 21 retriever modules, including Tavily, Brave, Serper, SearXNG, DuckDuckGo, Exa, arXiv | Yes, Ollama guide in the docs | Report with citations; sources tracked per summary |
| LearningCircuit/local-deep-research | MIT | Python | 9,377 | 2026-10-19 | SearXNG, Tavily, Brave, SerpApi, arXiv, PubMed, Semantic Scholar, Wikipedia, GitHub, Elasticsearch | Yes: Ollama, LM Studio, llama.cpp, OpenAI-compatible | Report with citations |
| stanford-oval/storm | MIT | Python | 31,679 | 2025-09-30 | You.com, Bing, Serper, Brave, SearXNG, DuckDuckGo, Tavily, Google, Azure AI Search | Yes: Ollama and vLLM clients in code | Wikipedia-style article with inline citations |
| dzhng/deep-research | MIT | TypeScript | 19,810 | 2026-04-11 | Firecrawl | Via an OpenAI-compatible endpoint | Appended list of URLs, no per-claim citations |
| jina-ai/node-DeepResearch | Apache-2.0 | TypeScript | 5,238 | 2026-05-01 | Jina Reader and search | Via an OpenAI-compatible endpoint | Footnotes matched to page chunks after writing |
| huggingface/smolagents (open_deep_research example) | Apache-2.0 | Python | 29,962 (whole repo) | 2026-10-06 (example: 2025-12-17) | Serper or SerpApi | Library supports Ollama and Transformers; example defaults to o1 | Not documented |
| langchain-ai/local-deep-researcher | MIT | Python | 9,383 | 2026-10-11 | DuckDuckGo, SearXNG, Tavily, Perplexity | Yes: Ollama, LM Studio | Summary listing sources used |
| tarun7r/deep-research-agent | MIT | Python | 189 | 2026-04-04 | DuckDuckGo, Tavily | Yes: Ollama, llama.cpp, plus Gemini and OpenAI | Credibility-scored sources, APA, MLA, Chicago or IEEE |
| dbhq-uk/gannet (in development, not released) | MIT | C# | - | - | Planned: Tavily, SearXNG, Brave, Serper, Exa and others | Planned | Planned: only fetched source IDs may be cited |

Two cautions. Stars measure attention, not quality. And "supports local models" in a README means the plumbing exists, not that a small model will give you a good report.

## Running deep research locally

You can run the whole stack on your own hardware. Three things decide whether it works well.

**A capable model.** The agent asks the model to plan, to call tools, to return structured JSON and to write a long report. That is much harder than chatting. The open_deep_research README says the model must support tool calling and structured output. jina-ai/node-DeepResearch says the same about structured output. The local-deep-researcher README warns that two small DeepSeek R1 models struggled to produce the JSON it needs. Start with the largest model your hardware can run, and treat a failure to follow format as a model problem before you suspect the agent.

**Enough context.** Agents read pages, and pages are long. If your runtime's context window is short, text is cut off before the model sees it. Check the default context length your runtime uses and raise it. tarun7r/deep-research-agent truncates inputs step by step to fit, which is a sign that this limit is a real constraint.

**A search backend.** The model does not search. A service does. For a private setup, a self-hosted [SearXNG](https://docs.searxng.org/) instance is the usual choice, and local-deep-researcher, local-deep-research and STORM all support it. It still sends your queries to the search engines it wraps. local-deep-research says this plainly in its README, and it blocks private SearXNG addresses by default until you opt in. If your queries must not leave the machine, search only your own documents. gpt-researcher and local-deep-research both support that.

This guide gives no benchmark numbers for local models. The projects' own numbers use different tasks, models and search services, so they cannot be compared across projects. Run your own question, then check the result as described below.

The shortest path to try it: local-deep-researcher is a small loop that runs on Ollama and DuckDuckGo with no keys. local-deep-research gives you a web UI with Ollama and SearXNG in Docker.

## How to choose

- **Local and private:** local-deep-research or local-deep-researcher. Check what each sends over the network before you rely on either.
- **Citations you can check:** no README read for this guide promises a check that every cited URL was fetched. jina-ai/node-DeepResearch matches text to pages after writing. tarun7r/deep-research-agent scores sources. Read the checking section below and do it by hand whatever you choose.
- **Python, or something else:** most projects are Python. dzhng/deep-research and jina-ai/node-DeepResearch are TypeScript.
- **A UI or a library:** gpt-researcher has both a web UI and a pip package. local-deep-research has a web UI and a REST API. STORM and smolagents are libraries.
- **A reference to learn from:** dzhng/deep-research is short enough to read in one sitting. The LangChain course covers open_deep_research.
- **Long-term support:** look at the last commit date. open_deep_research is archived, and STORM has had no commit since September 2025.

## How to tell it worked

A good deep research report has these marks.

- It answers the question you asked, in the order you would want it.
- Each factual claim has a citation next to it, not a list at the end.
- It says where sources disagree, and where it found nothing.
- Every cited URL is a page the agent actually read.

To check a report, do this for a sample of claims. Pick five or six, including some numbers and some quotes. Open each cited URL. Find the sentence that supports the claim. If you cannot find it, mark the claim as unsupported. If the page does not exist, or is not about the topic, the citation was invented.

Also check three things. Open the run log if the tool keeps one, and look for URLs that were cited but never fetched. Look for claims that rest on a single weak source. And look for a confident tone on a topic where the sources were thin.

If most sampled claims check out, the setup works. If they do not, change the model, the search backend or the settings and try the same question again.

## One more project, still in development

[gannet](https://github.com/dbhq-uk/gannet) is a deep research tool being built by DBHQ in C# on Microsoft Agent Framework Workflows and .NET 10. It is in development and has not been released, so there is nothing to install or run yet. It is a rewrite of tarun7r/deep-research-agent, which it credits, and it uses the same four-stage shape: plan, search, synthesise, write.

The design aims to let you pick a model, either cloud or local through Ollama, vLLM or llama.cpp, and a search service such as Tavily, SearXNG, Brave, Serper or Exa. Sources get IDs the moment they are fetched. The writer may cite only those IDs, and anything else is removed, so each citation points at a page the run actually fetched. No search result text is meant to be stored on disk. A web UI, command line, REST API and MCP server are planned.

## Frequently asked questions

### Is there an open-source version of ChatGPT deep research?

There are several open-source agents that do the same job: plan, search, read and write a cited report. langchain-ai/open_deep_research, assafelovic/gpt-researcher and LearningCircuit/local-deep-research are the best known. None of them is identical to the hosted products. They use your choice of model and search service, so quality depends on those choices.

### Can I run deep research locally with Ollama?

Yes. local-deep-researcher, local-deep-research, gpt-researcher and tarun7r/deep-research-agent all document Ollama setups. Local runs need a model that can follow structured output and use tools, a large enough context window and a search backend such as SearXNG or DuckDuckGo. Small models often fail on the format, so start with the largest one your machine can run.

### What is LangChain open deep research?

It is langchain-ai/open_deep_research, a Python agent built on LangGraph. A supervisor splits a question into parts and sends researcher agents to search in parallel. They return findings and the agent writes the final report. It uses Tavily by default and works with many models. GitHub now marks the repository as archived, so it is read-only.

### What is the difference between open_deep_research and local-deep-researcher?

Both come from LangChain. open_deep_research is the larger multi-agent design for hosted models and tool use. local-deep-researcher is a smaller loop built for Ollama and LM Studio: search, summarise, find gaps, repeat. Neither has had recent feature work. The first is archived and the second shows only dependency updates since August 2025.

### How do I check that a deep research report is accurate?

Open the cited URLs for a sample of claims and look for the supporting sentence on each page. A citation that points to a missing page, or to a page that does not say the claim, is a failure. Repeat the check for numbers and quotes first, because they are where models invent detail most often.

## Sources

All pages were read on 2026-10-21.

| URL | Title | Date read |
|---|---|---|
| https://api.github.com/repos/langchain-ai/open_deep_research | GitHub API: licence, stars, language, archived | 2026-10-21 |
| https://github.com/langchain-ai/open_deep_research | langchain-ai/open_deep_research README | 2026-10-21 |
| https://github.com/langchain-ai/open_deep_research/blob/main/src/open_deep_research/configuration.py | open_deep_research configuration.py | 2026-10-21 |
| https://github.com/langchain-ai/open_deep_research/blob/main/src/open_deep_research/prompts.py | open_deep_research prompts.py (citation rules) | 2026-10-21 |
| https://api.github.com/repos/assafelovic/gpt-researcher | GitHub API: gpt-researcher | 2026-10-21 |
| https://github.com/assafelovic/gpt-researcher | GPT Researcher README | 2026-10-21 |
| https://github.com/assafelovic/gpt-researcher/tree/main/gpt_researcher/retrievers | gpt-researcher retrievers folder | 2026-10-21 |
| https://github.com/assafelovic/gpt-researcher/blob/main/docs/docs/gpt-researcher/llms/running-with-ollama.md | Running with Ollama | 2026-10-21 |
| https://api.github.com/repos/LearningCircuit/local-deep-research | GitHub API: local-deep-research | 2026-10-21 |
| https://github.com/LearningCircuit/local-deep-research | Local Deep Research README | 2026-10-21 |
| https://api.github.com/repos/stanford-oval/storm | GitHub API: STORM | 2026-10-21 |
| https://github.com/stanford-oval/storm | STORM README | 2026-10-21 |
| https://github.com/stanford-oval/storm/blob/main/knowledge_storm/rm.py | STORM rm.py (retrievers) | 2026-10-21 |
| https://github.com/stanford-oval/storm/blob/main/knowledge_storm/lm.py | STORM lm.py (model clients) | 2026-10-21 |
| https://api.github.com/repos/dzhng/deep-research | GitHub API: dzhng/deep-research | 2026-10-21 |
| https://github.com/dzhng/deep-research | dzhng/deep-research README | 2026-10-21 |
| https://github.com/dzhng/deep-research/blob/main/src/deep-research.ts | deep-research.ts (Sources section) | 2026-10-21 |
| https://api.github.com/repos/jina-ai/node-DeepResearch | GitHub API: node-DeepResearch | 2026-10-21 |
| https://github.com/jina-ai/node-DeepResearch | node-DeepResearch README | 2026-10-21 |
| https://github.com/jina-ai/node-DeepResearch/blob/main/src/tools/build-ref.ts | node-DeepResearch build-ref.ts | 2026-10-21 |
| https://api.github.com/repos/huggingface/smolagents | GitHub API: smolagents | 2026-10-21 |
| https://github.com/huggingface/smolagents | smolagents README | 2026-10-21 |
| https://github.com/huggingface/smolagents/tree/main/examples/open_deep_research | smolagents open_deep_research example README and run.py | 2026-10-21 |
| https://api.github.com/repos/langchain-ai/local-deep-researcher | GitHub API: local-deep-researcher | 2026-10-21 |
| https://github.com/langchain-ai/local-deep-researcher | Local Deep Researcher README | 2026-10-21 |
| https://api.github.com/repos/tarun7r/deep-research-agent | GitHub API: tarun7r/deep-research-agent | 2026-10-21 |
| https://github.com/tarun7r/deep-research-agent | Deep Research Agent README and src/config.py | 2026-10-21 |
| https://github.com/dbhq-uk/gannet | gannet repository (in development) | 2026-10-21 |
