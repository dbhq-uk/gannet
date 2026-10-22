# AGENTS.md

Guidance for AI agents (and people) working in this repository.

## What this is

**Gannet** - give it a question and it plans the research, searches the web, weighs each source, and writes a report in which every claim is cited. Built on Microsoft Agent Framework Workflows, with a Blazor UI, a REST API, an MCP server and a command line.

**Naming.** "Gannet" in prose; `gannet` for the command, paths, the repository, hostnames and the wordmark (Dan, 22 Oct 2026, the same split as Terraken).

**Inspired by, never a rewrite.** Gannet was inspired by [deep-research-agent](https://github.com/tarun7r/deep-research-agent) (MIT). Never describe it as a rewrite or a port of that project, or as "X for .NET" - not in code, comments, docs, commits or the site (Dan, 22 Oct 2026). The README says "inspired by", and `LICENSE` keeps that project's notice because the default trusted-domain list in `credibility-rules.json` is adapted from it; the notice must stay while that list does.

## Layout

```
src/Gannet.Core        domain types, interfaces, citations, credibility, report, storage
src/Gannet.Providers   models, search services, readers, S3 storage
src/Gannet.Research    the workflow graph, the search agent, RunService
src/Gannet.Web         Blazor UI, REST API (/api), MCP (/mcp), sign-in
src/Gannet.Cli         the gannet command
tests/                 one test project per source project, plus TestSupport
deploy/cloudflare      the Worker and Container config for a Cloudflare deployment
site                   gannet.dbhq.uk, an Astro site; see site/README.md
assets                 the logo; every file is drawn by assets/_gen/logo.py
```

## The rules that define this tool

1. **Every citation points at a source the run fetched.** Sources get IDs (`S1`, `S2`) the moment a tool fetches them. The writer cites IDs; `CitationChecker` removes anything else; `CitationNumberer` turns IDs into numbers. Never parse citation numbers out of model text any other way.
2. **No search or reader result text is ever written to disk.** Page text lives in `RunContext.Pages` for one run. Checkpoints, run records and event logs carry metadata and findings only. Some providers' terms forbid storing results (Parallel Customer Terms s.2(c)(xi)).
3. **Executor IDs never change.** Checkpoint resume matches executors by ID (`ExecutorIds`). Renaming one breaks every interrupted run.
4. **Keys never leave configuration.**
5. **Event type names never change.** Saved event logs are read by their `type` value; a renamed type disappears from old runs.
   Never add a `required` member to `RunRecord`, `ResearchRequest` or `RunSummary`, because old saved runs would fail to read.

## Commands

```
dotnet test Gannet.slnx
dotnet format Gannet.slnx --verify-no-changes
dotnet run --project src/Gannet.Web
dotnet run --project src/Gannet.Cli -- research "your question"
cd site && npm test                 # builds the site, then checks it
python3 assets/_gen/logo.py         # redraws every logo file
```

## The site

`site/` is gannet.dbhq.uk. Gannet is in development, so the site describes what it is being built to do and never what it does today; `site/tests/content.test.mjs` fails on wording that would only fit a released tool. When a pull request ships something the site names, take that phrase off the test's `UNSHIPPED` list in the same pull request.
