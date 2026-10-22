# gannet.dbhq.uk

The site behind [gannet.dbhq.uk](https://gannet.dbhq.uk): five pages about
[Gannet](https://github.com/dbhq-uk/gannet), the open-source deep research agent
being built in this repository. It follows the shape of
[terraken.dbhq.uk](https://terraken.dbhq.uk)'s site, and its tests came from
there.

## Why it is shaped the way it is

**It is a search site, not a brochure.** The pages come from measured demand.
Worldwide Google Ads volume, English, measured 21 October 2026 with DataForSEO
and recorded in the dbhq repo at `docs/research/gannet-seo-worldwide.md`:

| Page | Search intent | Monthly searches |
|---|---|---|
| [/](https://gannet.dbhq.uk/) | deep research agent, ai research agent, deep research tool | 2,450 |
| [/source-credibility/](https://gannet.dbhq.uk/source-credibility/) | source credibility and its cluster | 17,180 |
| [/searxng-search-api/](https://gannet.dbhq.uk/searxng-search-api/) | searxng docker, searxng api, free web search api | 9,700 |
| [/open-source-deep-research/](https://gannet.dbhq.uk/open-source-deep-research/) | open deep research, local deep research | 3,380 |
| [/perplexity-alternative/](https://gannet.dbhq.uk/perplexity-alternative/) | perplexity alternative and variants | 2,480 |

**The brand term is a bird.** `gannet` is 49,500 a month and every result in
the top ten is the seabird; `gannet ai` is 50. So the home page targets the
category and says what Gannet is in its first line. The phrases that describe
Gannet best (`cited answers ai`, `verify ai citations`, `self hosted deep
research`) measure near zero.

**Three more guides wait for the code they describe**:
`/microsoft-agent-framework-workflows/`, `/mcp-server-csharp/` and
`/deep-research-api/`. Each is added in the pull request that ships its
feature, with a `/docs/` page once there is something to install.

**The guides are guides, not landing pages.** Each is written to be the best
answer to its query whether or not the reader ever runs Gannet: at least 1,200
words, Gannet in one section near the end and under one per cent of the words,
and a "How to tell" section that says when the reader is done.

## Gannet is in development, and the site says so

The home page carries the status from `status` in `src/lib/site.ts`, and every
description of gannet is of what it is being built to do. What is already in
merged code may be said plainly, naming the file: today that is the citation
rules (`src/Gannet.Core/Citations/`) and the credibility scoring
(`src/Gannet.Core/Credibility/`).

`tests/content.test.mjs` holds this with an `UNSHIPPED` list: phrases that only
fit a released Gannet fail the build. **Take an entry off the list in the pull
request that ships what it names, not before.**

## Every sample is captured, never typed

The SearXNG guide shows real commands and real output. `samples/searxng/`
holds the settings, the clients, `capture.sh` that ran them against
`searxng/searxng` pinned by digest, and each command's output in a numbered
file. The guide's Markdown names a file with a `<<sample: NN-name.txt>>` line
and `src/lib/markdown.mjs` puts that file into the page at build time. To change
a sample, change the script and run it again; never edit an output by hand.

The other guides cite every source by URL with the date it was read. The copies
of what was read are in the dbhq repo at `docs/research/gannet-site-guides/`,
because they are other people's text and this repository is public.

## Working on it

```bash
cd site
npm install
npm run dev        # http://100.115.72.85:4334 (Tailscale, not localhost)
npm test           # builds first, then checks the built pages
```

The copy: `src/lib/site.ts` for the home page and the shared lists;
`src/content/guides/*.md` for the guides, rendered by `src/layouts/Guide.astro`.
The FAQ structured data is read out of each guide's Markdown by
`src/lib/faqs.mjs`, so the page and the schema cannot drift apart.

The photographs in `src/assets/photo/` are generated illustrations (GPT Image,
21 Oct 2026; the prompts are in the `.jsonl` files beside them) and the footer
says so. They set the scene; nothing on the site may look like Gannet's output
until Gannet produces some.

## Deploying

Merging to `main` is the deploy, through `.github/workflows/deploy-site.yml`:
build, test, upload to the Cloudflare Pages project `gannet-site`, purge the
cache, then check every page. It runs only when the repository variable
`SITE_DEPLOY` is `true`. The Pages project, the custom domain and the DNS record
are Terraform in the dbhq repo at `infra/gannet-site/`.

## Licence

MIT, as Gannet is.
