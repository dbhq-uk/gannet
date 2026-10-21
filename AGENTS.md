# AGENTS.md

Guidance for AI agents (and people) working in this repository.

## What this is

**gannet** - give it a question and it plans the research, searches the web, weighs each source, and writes a report in which every claim is cited. Built on Microsoft Agent Framework Workflows, with a Blazor UI, a REST API, an MCP server and a command line. The source of the four-stage shape is [deep-research-agent](https://github.com/tarun7r/deep-research-agent) (MIT); never describe gannet as "X for .NET".

## Layout

```
src/Gannet.Core        domain types, interfaces, citations, credibility, report, storage
src/Gannet.Providers   models, search services, readers, S3 storage
src/Gannet.Research    the workflow graph, the search agent, RunService
src/Gannet.Web         Blazor UI, REST API (/api), MCP (/mcp), sign-in
src/Gannet.Cli         the gannet command
tests/                 one test project per source project, plus TestSupport
deploy/cloudflare      the Worker and Container config for a Cloudflare deployment
```

## The rules that define this tool

1. **Every citation points at a source the run fetched.** Sources get IDs (`S1`, `S2`) the moment a tool fetches them. The writer cites IDs; `CitationChecker` removes anything else; `CitationNumberer` turns IDs into numbers. Never parse citation numbers out of model text any other way.
2. **No search or reader result text is ever written to disk.** Page text lives in `RunContext.Pages` for one run. Checkpoints, run records and event logs carry metadata and findings only. Some providers' terms forbid storing results (Parallel Customer Terms s.2(c)(xi)).
3. **Executor IDs never change.** Checkpoint resume matches executors by ID (`ExecutorIds`). Renaming one breaks every interrupted run.
4. **Keys never leave configuration.**

## Commands

```
dotnet test Gannet.slnx
dotnet format Gannet.slnx --verify-no-changes
dotnet run --project src/Gannet.Web
dotnet run --project src/Gannet.Cli -- research "your question"
```
