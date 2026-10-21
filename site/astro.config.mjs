import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';
import { lastmodFor } from './src/lib/content-dates.mjs';
import { remarkSamples, rehypeExternal, rehypeTables, rehypeCode } from './src/lib/markdown.mjs';

// Per-path sitemap hints. The priorities are the measured search demand
// (dbhq repo, docs/research/gannet-seo-worldwide.md, 21 Oct 2026), not the
// site's own opinion of itself: the source credibility cluster is 17,180 a
// month and SearXNG's 9,700, against 3,380 and 2,480 for the two comparison
// guides. The home page targets "deep research agent" (2,450) and keeps 1.0 as
// the entry point.
const HINTS = {
  '/': { changefreq: 'monthly', priority: 1.0 },
  '/source-credibility/': { changefreq: 'monthly', priority: 0.9 },
  '/searxng-search-api/': { changefreq: 'monthly', priority: 0.9 },
  '/open-source-deep-research/': { changefreq: 'monthly', priority: 0.8 },
  '/perplexity-alternative/': { changefreq: 'monthly', priority: 0.8 },
};

export default defineConfig({
  site: 'https://gannet.dbhq.uk',
  // Trailing slash everywhere, matching dbhq.uk and the other DBHQ sites: one
  // canonical form per page, so /docs and /docs/ never both look canonical.
  trailingSlash: 'always',
  // 'always', not 'auto': the pages are small and every stylesheet inlined is a
  // render-blocking request removed from the critical path. Same setting, and
  // the same reasoning, as the other DBHQ Astro sites.
  build: { inlineStylesheets: 'always' },
  // No syntax highlighter: Shiki writes inline style attributes, and the
  // captured samples are output, not code to colour. src/lib/markdown.mjs
  // explains each plugin.
  markdown: {
    syntaxHighlight: false,
    remarkPlugins: [remarkSamples],
    rehypePlugins: [rehypeExternal, rehypeTables, rehypeCode],
  },
  integrations: [
    sitemap({
      filter: (page) => !new URL(page).pathname.startsWith('/404'),
      serialize(item) {
        const { pathname } = new URL(item.url);
        const hint = HINTS[pathname] ?? { changefreq: 'monthly', priority: 0.7 };
        // Real per-page dates from git, not the build stamp - see
        // src/lib/content-dates.mjs for why that distinction matters.
        const lastmod = lastmodFor(pathname);
        return { ...item, ...hint, ...(lastmod ? { lastmod } : {}) };
      },
    }),
  ],
});
