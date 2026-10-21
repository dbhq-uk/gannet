# gannet

Deep research in C#. Give it a question and it plans the research, searches
the web, weighs each source, and writes a report in which every claim is
cited.

It is built on Microsoft Agent Framework: a planner, a searcher, a synthesiser
and a writer run as one workflow. It has a Blazor web UI, and you choose the
model (a cloud API or a local one through Ollama) and the search service. It
is self-hosted: you run it with your own keys, or with no keys at all on local
models and a search engine you host yourself.

## Where it stands

Started on 21 October 2026. The design is settled and the build has not
begun, so there is nothing to run yet. This section will say when there is.

## Where it comes from

gannet is a C# rewrite of
[deep-research-agent](https://github.com/tarun7r/deep-research-agent) by
Tarun Sai Goddu, a Python project on LangGraph and LangChain, published under
the MIT License. The four-stage shape (plan, search, synthesise, write) and the
idea of scoring sources for credibility come from there. The original notice
is reproduced in [`LICENSE`](LICENSE).

## Licence

MIT. See [`LICENSE`](LICENSE).
