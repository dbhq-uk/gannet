// gannet.dbhq.uk - the structured copy for the site.
//
// WHY THIS SITE IS SHAPED THE WAY IT IS. It is a search site, as terraken's is.
// Worldwide Google Ads volume, English, measured 21 Oct 2026 and recorded in
// full in the dbhq repo at docs/research/gannet-seo-worldwide.md:
//
//   source credibility (and its cluster)     17,180 / month
//   searxng docker, api, query url, ...       9,700
//   open deep research, local deep research   3,380
//   perplexity alternative (and variants)     2,480
//   deep research agent, ai research agent    2,450
//
// THE BRAND TERM IS A BIRD. "gannet" is 49,500 a month and every result in the
// top ten is about the seabird; "gannet ai" is 50. So the home page targets
// "deep research agent", says what the tool is in its first line, and does not
// lean on the name. The phrases that describe Gannet best - "cited answers ai",
// "verify ai citations", "self hosted deep research" - measure near zero.
//
// WHAT MAY BE SAID HERE. Gannet is IN DEVELOPMENT: on 21 Oct 2026 phases 0-2 of
// ten are merged and it cannot yet answer a question. So:
//
//   - `status` is shown on every page that describes the tool;
//   - what Gannet is being built to do is written as that, never as what it
//     does today. tests/content.test.mjs fails on phrases that only make sense
//     for a released tool (UNSHIPPED in that file); each comes off the list in
//     the PR that ships the thing it names;
//   - what IS in merged code may be said plainly, naming the file. Today that is
//     the citation rules (src/Gannet.Core/Citations/) and the credibility
//     scoring (src/Gannet.Core/Credibility/).
//
// A site that oversells a research tool by one feature has spent the thing a
// research tool is selling.

export const REPO = "https://github.com/dbhq-uk/gannet";

export const site = {
  url: "https://gannet.dbhq.uk",
  name: "Gannet",
  tagline: "Deep research in which every claim is cited",
  lead:
    "Gannet is an open-source deep research agent being built in C# by DBHQ. It is designed to take a question, plan the research, search the web, weigh each source and write a report in which every claim is cited to a page it actually read.",
  // The sibling cross-link block in the footer. One-way on purpose (Dan, 21 Oct
  // 2026): Gannet lists the other sites, and their footers do not list gannet.
  also: [
    { label: "DBHQ", href: "https://dbhq.uk/", desc: "the practice that publishes this." },
    { label: "skills", href: "https://skills.dbhq.uk/", desc: "free agent skills for Claude Code and Codex." },
    { label: "terraken", href: "https://terraken.dbhq.uk/", desc: "rank a Terraform plan by how much damage it can do." },
    { label: "heliograph", href: "https://docs.heliograph.io/", desc: "debug a remote machine through an operator, using Git." },
  ],
} as const;

// The status line. Its words are pinned by tests/content.test.mjs on the home
// page; change them only when the state they describe changes.
export const status = {
  label: "In development",
  text: "Gannet is being built in the open and is not released yet.",
  link: { label: "Follow the build on GitHub", href: REPO },
} as const;

// ---------------------------------------------------------------------------
// Navigation: the four guides. The footer renders the same list, which keeps
// every page one click from every other.
// ---------------------------------------------------------------------------

export interface NavItem {
  path: string;
  label: string;
  crumb: string;
  /** One line for the cards that list the guides. */
  blurb: string;
}

export const nav: readonly NavItem[] = [
  {
    path: "/open-source-deep-research/",
    label: "Open-source deep research",
    crumb: "Open-source deep research",
    blurb: "The open deep research agents compared: what each searches, what it runs on and how it cites.",
  },
  {
    path: "/perplexity-alternative/",
    label: "Perplexity alternatives",
    crumb: "Perplexity alternatives",
    blurb: "Hosted and self-hosted answer engines, and how to tell whether one is a real replacement.",
  },
  {
    path: "/source-credibility/",
    label: "Source credibility",
    crumb: "Source credibility",
    blurb: "What makes a source credible, and the methods fact-checkers actually use to judge one.",
  },
  {
    path: "/searxng-search-api/",
    label: "SearXNG API",
    crumb: "SearXNG search API",
    blurb: "Run SearXNG in Docker, turn on its JSON API and use it as a free search API for an agent.",
  },
];

// ---------------------------------------------------------------------------
// The three rules, shown as glass cards over the hero photograph. Each is true
// of the design and of merged code; the file that holds it is named on the page.
// ---------------------------------------------------------------------------

export interface Rule {
  h: string;
  p: string;
}

export const rules: readonly Rule[] = [
  {
    h: "Every citation points at a page the run fetched",
    p: "Sources are to get an ID the moment they are fetched. The writer may cite only those IDs, and the merged citation checker removes anything else.",
  },
  {
    h: "No search result text is written to disk",
    p: "By design, page text lives in memory for one run. Run records keep the plan, the report and the source list: URL, title and score.",
  },
  {
    h: "Your model, your search service",
    p: "A cloud model or a local one, and a choice of search service, picked per run. Ollama with self-hosted SearXNG is planned as the path with no keys.",
  },
];

// ---------------------------------------------------------------------------
// The four stages, from the design (docs in the dbhq repo:
// docs/superpowers/specs/2026-10-21-gannet-design.md). Written as what the
// workflow is being built to do.
// ---------------------------------------------------------------------------

export interface Stage {
  n: string;
  h: string;
  p: string;
}

export const stages: readonly Stage[] = [
  {
    n: "01",
    h: "Plan",
    p: "The main model breaks the question into the searches that would answer it.",
  },
  {
    n: "02",
    h: "Search",
    p: "A search agent runs the plan through the search service chosen for the run, and readers fetch each page, falling back to the next reader when a page blocks one.",
  },
  {
    n: "03",
    h: "Weigh",
    p: "Each source gets a credibility score from its domain and path, and a fast model pulls the findings out of each page.",
  },
  {
    n: "04",
    h: "Write",
    p: "The main model writes the report from the findings, citing source IDs. Unknown IDs are removed, the rest are numbered, and the reference list is set in APA, MLA, Chicago or IEEE.",
  },
];

// The model and search choices the design names. Listed as being built, never
// as available: the providers are Phase 3 and are not merged.
export const choices = {
  models: ["Ollama", "vLLM", "llama.cpp", "OpenAI", "Azure OpenAI", "Anthropic", "Gemini"],
  search: ["SearXNG", "Tavily", "Brave", "Serper", "Exa", "Firecrawl", "Parallel", "Bright Data"],
} as const;

// What exists in merged code today, and what is still to come. Phase names are
// the build plan's.
export const progress = {
  done: [
    "The repository, CI and the hosting check",
    "Citation checking and numbering, in four reference styles",
    "Credibility scoring, with rules a user can extend",
    "Report export to Markdown, HTML and text, and run storage",
  ],
  next: [
    "Model and search providers",
    "The research workflow on Microsoft Agent Framework",
    "A command line, a REST API and an MCP server",
    "The web UI, and the guide to hosting it yourself",
  ],
} as const;

// ---------------------------------------------------------------------------
// FAQs. Each renders on its page and in that page's structured data, word for
// word: tests/content.test.mjs checks the two agree.
// ---------------------------------------------------------------------------

export interface Faq {
  q: string;
  a: string;
}

export const homeFaqs: readonly Faq[] = [
  {
    q: "Is Gannet released?",
    a: "Not yet. Gannet is being built in the open on GitHub, and this page says what it is being built to do. The core pieces are merged: citation checking, credibility scoring, report export and storage. The search providers, the research workflow and the interfaces come next.",
  },
  {
    q: "Is Gannet free?",
    a: "Yes. Gannet is open source under the MIT licence. It is being built to run on your own machine with your own keys, or with no keys at all on a local model through Ollama and a SearXNG instance you host.",
  },
  {
    q: "How is it different from ChatGPT or Gemini deep research?",
    a: "Those are hosted services that use their own model and their own search. Gannet is built to run where you choose, with the model and search service you choose, and to cite only pages the run actually fetched. Anything else is removed before the report is numbered.",
  },
  {
    q: "What is Gannet built with?",
    a: "C# on .NET 10 and Microsoft Agent Framework Workflows, with a Blazor web UI. Models are reached through Microsoft.Extensions.AI, so the provider is configuration rather than code.",
  },
];
