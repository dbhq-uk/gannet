// The contract for gannet.dbhq.uk, asserted against the BUILT output in dist/.
//
// Ported from terraken's suite (dbhq-uk/terraken, site/tests/content.test.mjs)
// and cut down to what gannet has: a home page, four guides and a 404.
//
// Testing the build rather than the source is the point: the house rules this
// file enforces - no em dashes, a status on the home page, titles inside the
// SEO band - are rules about what is PUBLISHED, and a rule checked against a
// data file cannot see what a template did to it.
//
// `npm test` runs `npm run build` first (see the pretest script), so dist/ is
// always current when these run.

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { execFileSync } from "node:child_process";
import vm from "node:vm";
import { REPO, homeFaqs, nav, site, status } from "../src/lib/site.ts";
import { faqsFrom } from "../src/lib/faqs.mjs";

const DIST = new URL("../dist/", import.meta.url);
const SRC = new URL("../src/", import.meta.url);
const SAMPLES = new URL("../samples/searxng/", import.meta.url);
const HOST = "https://gannet.dbhq.uk";
const ORG_ID = "https://dbhq.uk/#organization";

const read = (rel) => {
  const p = new URL(rel, DIST);
  assert.ok(existsSync(p), `missing built file: ${rel}`);
  return readFileSync(p, "utf8");
};

// The five indexed pages. The 404 is deliberately not here: it is noindex, it
// carries no canonical worth asserting and it is checked on its own below.
const pages = [
  { path: "/", file: "index.html" },
  { path: "/source-credibility/", file: "source-credibility/index.html" },
  { path: "/open-source-deep-research/", file: "open-source-deep-research/index.html" },
  { path: "/perplexity-alternative/", file: "perplexity-alternative/index.html" },
  { path: "/searxng-search-api/", file: "searxng-search-api/index.html" },
];

// The four guides. Each is built around one search phrase (measured demand is
// recorded in src/lib/site.ts), and the suite holds them all to the same
// standard: the phrase in the title and the H1, a full answer, the tool named
// rarely, a stopping point, and every published FAQ answer on the page.
const guides = [
  { path: "/source-credibility/", slug: "source-credibility", phrases: ["source credibility"] },
  { path: "/open-source-deep-research/", slug: "open-source-deep-research", phrases: ["open-source deep research", "open source deep research"] },
  { path: "/perplexity-alternative/", slug: "perplexity-alternative", phrases: ["perplexity alternative"] },
  { path: "/searxng-search-api/", slug: "searxng-search-api", phrases: ["searxng"] },
];

const html = new Map(pages.map((p) => [p.path, read(p.file)]));
const allHtml = [...html.values(), read("404.html")].join("\n");
const notFound = read("404.html");
const llms = read("llms.txt");

// Text with tags, script/style contents and HTML entities removed, for the
// checks that are about words a reader sees rather than markup.
function visibleText(doc) {
  return doc
    .replace(/<script[\s\S]*?<\/script>/g, " ")
    .replace(/<style[\s\S]*?<\/style>/g, " ")
    .replace(/<[^>]+>/g, " ")
    .replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCodePoint(parseInt(h, 16)))
    .replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(Number(d)))
    .replace(/&amp;/g, "&")
    .replace(/&quot;/g, '"')
    // AFTER the tags are stripped, never before: a path like `<name>` is
    // published as &lt;name&gt; and is invisible to a check that does not put
    // the angle brackets back.
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&nbsp;/g, " ");
}

const flat = (doc) => visibleText(doc).toLowerCase().replace(/\s+/g, " ");

const graphOf = (doc) => {
  const blocks = [...doc.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)];
  assert.equal(blocks.length, 1, "expected exactly one JSON-LD block");
  return JSON.parse(blocks[0][1]);
};

const nodesOf = (doc, type) => graphOf(doc)["@graph"].filter((n) => n["@type"] === type);

const titleOf = (doc) => doc.match(/<title>([\s\S]*?)<\/title>/)?.[1];
const h1Of = (doc) => doc.match(/<h1[^>]*>([\s\S]*?)<\/h1>/)?.[1];

// The article body: the .prose element, which ends where the "More guides"
// aside begins.
const proseOf = (doc) => doc.match(/<div class="prose[^"]*"[^>]*>([\s\S]*?)<aside class="onward"/)?.[1];

const guideMarkdown = (slug) => readFileSync(new URL(`content/guides/${slug}.md`, SRC), "utf8");

test("every page is built - five indexed pages, a real 404, and the edge files", () => {
  assert.equal(pages.length, 5);
  assert.equal(guides.length, 4, "a guide was added or dropped - decide which, on purpose");
  // The nav list is what the footer renders, and the footer is what keeps
  // every page one click from every other. A page that exists and is not on it
  // is a page only a search engine can reach.
  assert.deepEqual(
    nav.map((n) => n.path).sort(),
    pages.filter((p) => p.path !== "/").map((p) => p.path).sort(),
    "the nav list and the built pages disagree - a new page was added to one and not the other",
  );
  assert.deepEqual(
    guides.map((g) => g.path).sort(),
    nav.map((n) => n.path).sort(),
    "the guides list in this file and the nav list disagree",
  );
  assert.ok(existsSync(new URL("404.html", DIST)), "a real 404 page, so Pages returns a 404 status");
  assert.ok(existsSync(new URL("sitemap-index.xml", DIST)));
  assert.ok(existsSync(new URL("sitemap-0.xml", DIST)));
  assert.ok(existsSync(new URL("copy.js", DIST)));
  assert.ok(existsSync(new URL("analytics.js", DIST)));
  assert.ok(existsSync(new URL("consent.js", DIST)));
  assert.ok(existsSync(new URL("llms.txt", DIST)));
  assert.ok(existsSync(new URL("robots.txt", DIST)));
  assert.ok(existsSync(new URL("_headers", DIST)));
  assert.ok(existsSync(new URL("site.webmanifest", DIST)));
});

// ---------------------------------------------------------------------------
// House writing rules. These bind every DBHQ property, and a build that breaks
// one of them must not ship.
// ---------------------------------------------------------------------------

test("no em dashes and no en dashes anywhere in the output", () => {
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    const text = visibleText(doc);
    assert.equal(text.includes("\u2014"), false, `${path}: em dash`);
    assert.equal(text.includes("\u2013"), false, `${path}: en dash`);
    assert.equal(doc.includes("&mdash;"), false, `${path}: &mdash; entity`);
    assert.equal(doc.includes("&ndash;"), false, `${path}: &ndash; entity`);
  }
  assert.equal(llms.includes("\u2014"), false, "llms.txt: em dash");
  assert.equal(llms.includes("\u2013"), false, "llms.txt: en dash");
});

test("no heading carries a trailing full stop", () => {
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    for (const m of doc.matchAll(/<h([1-6])[^>]*>([\s\S]*?)<\/h\1>/g)) {
      const text = visibleText(m[2]).trim();
      assert.equal(text.endsWith("."), false, `${path}: heading ends with a full stop - "${text}"`);
    }
  }
  // The FAQ questions are <dt>, not headings, and they are questions, so they
  // may not end in a full stop either - they end in a question mark.
  const faqs = [...homeFaqs, ...guides.flatMap((g) => faqsFrom(guideMarkdown(g.slug)))];
  for (const f of faqs) {
    assert.ok(f.q.endsWith("?"), `FAQ is not a question: "${f.q}"`);
  }
  assert.equal(site.tagline.endsWith("."), false, "the tagline is a subtitle, not a sentence");
});

test("British English, not American", () => {
  // A short list of the spellings that actually turn up in this copy. Not a
  // dictionary: a full one produces false positives on quoted identifiers.
  //
  // "color" is NOT on it: it is a real word in code, flags and environment
  // variables (NO_COLOR), which are the software's surface and not this
  // site's prose.
  const american = [
    "organize", "organized", "organization",
    "recognize", "recognized",
    "analyze", "analyzed",
    "behavior", "behaviors",
    "catalog ", "catalogs",
    "favorite", "favorites",
    "unauthorized",
  ];
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    // Words inside curly double quotes are somebody else's, quoted as they
    // wrote them (the source credibility guide quotes Wikipedia), so they are
    // not this site's spelling.
    const text = flat(doc).replace(/“[^”]*”/g, " ");
    for (const word of american) {
      assert.equal(text.includes(word), false, `${path}: American spelling "${word.trim()}"`);
    }
  }
});

test("DBHQ never speaks as a team it does not have", () => {
  // DBHQ is one person and never writes "we".
  const banned = [
    " we wrote", " we built", " we would rather", " we kept",
    " our team", " our engineers", " we offer", " we can help",
    " we believe", " we think", " we deliver", " we recommend",
  ];
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    const text = flat(doc);
    for (const phrase of banned) {
      assert.equal(text.includes(phrase), false, `${path}: first-person plural "${phrase.trim()}"`);
    }
  }
  assert.equal(llms.toLowerCase().includes(" we "), false, "llms.txt: first-person plural");
});

test("the word corpus never appears", () => {
  for (const [path, doc] of [...html, ["/404/", notFound], ["llms.txt", llms]]) {
    assert.equal(/\bcorpus\b|\bcorpora\b/i.test(visibleText(doc)), false, `${path}: the word "corpus"`);
  }
});

test("the denylist stays off this site", () => {
  // Mirrors the standing bans in the DBHQ repo's content plan
  // (docs/website/content-plan.md). Only the generic entries are kept here.
  const banned = [
    "scentverdict",
    "security-cleared",
    "sc clearance",
    "security clearance",
    "ministry of defence",
    "fractional engineering lead",
    "fractional cto",
    "cto in all but name",
    "ai-accelerated",
    "outside ir35",
    "ir35-safe",
    "currently taking projects",
    "delivery blueprint",
    "azure production readiness review",
    "scoping pays for itself",
    "credited against the build",
  ];
  const surfaces = [...html, ["/404/", notFound], ["llms.txt", llms]];
  for (const [path, doc] of surfaces) {
    const text = visibleText(doc).toLowerCase();
    for (const phrase of banned) {
      assert.equal(text.includes(phrase), false, `${path}: denylisted phrase "${phrase}"`);
    }
  }
});

// ---------------------------------------------------------------------------
// Per-page SEO
// ---------------------------------------------------------------------------

test("every title is 50-60 characters and unique", () => {
  const seen = new Set();
  for (const [path, doc] of html) {
    const t = titleOf(doc);
    assert.ok(t, `${path}: no <title>`);
    assert.ok(t.length >= 50 && t.length <= 60, `${path}: title is ${t.length} characters, wanted 50-60: "${t}"`);
    assert.equal(seen.has(t), false, `${path}: duplicate title "${t}"`);
    seen.add(t);
  }
});

test("every meta description is written per page, 60-155 characters and unique", () => {
  const seen = new Set();
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    const m = doc.match(/<meta name="description" content="([^"]*)"/);
    assert.ok(m, `${path}: no meta description`);
    const d = m[1];
    assert.ok(d.length >= 60, `${path}: description is only ${d.length} characters`);
    assert.ok(d.length <= 155, `${path}: description is ${d.length} characters, wanted at most 155`);
    assert.equal(seen.has(d), false, `${path}: duplicate description`);
    seen.add(d);
  }
});

test("every page carries a canonical matching its own path", () => {
  for (const [path, doc] of html) {
    const m = doc.match(/<link rel="canonical" href="([^"]*)"/);
    assert.ok(m, `${path}: no canonical`);
    assert.equal(m[1], `${HOST}${path}`, `${path}: wrong canonical`);
    // og:url agrees with it, so a share and a crawl never disagree.
    const og = doc.match(/<meta property="og:url" content="([^"]*)"/);
    assert.ok(og, `${path}: no og:url`);
    assert.equal(og[1], `${HOST}${path}`, `${path}: og:url disagrees with the canonical`);
  }
});

test("the 404 is noindex, so a soft 404 never enters the index", () => {
  assert.match(notFound, /<meta name="robots" content="noindex, nofollow"/);
});

test("every page emits one JSON-LD graph that parses, with one node per page", () => {
  for (const [path, doc] of html) {
    const graph = graphOf(doc);
    assert.equal(graph["@context"], "https://schema.org");
    const types = graph["@graph"].map((n) => n["@type"]);
    assert.ok(types.includes("Organization"), `${path}: no Organization node`);
    assert.ok(types.includes("WebSite"), `${path}: no WebSite node`);
    assert.ok(types.includes("BreadcrumbList"), `${path}: no BreadcrumbList`);

    // ONE COMPANY, ONE IDENTITY. The publisher keeps dbhq.uk's own canonical
    // @id rather than minting a second Organization for the same company on a
    // third hostname.
    const org = graph["@graph"].find((n) => n["@type"] === "Organization");
    assert.equal(org["@id"], ORG_ID);
    const web = graph["@graph"].find((n) => n["@type"] === "WebSite");
    assert.equal(web["@id"], `${HOST}/#website`);
    assert.deepEqual(web.publisher, { "@id": ORG_ID });

    // ONE NODE PER PAGE. The page node IS the article on a guide, not a
    // WebPage carrying a second TechArticle beside it - two nodes for one
    // document with two @ids is the drift this graph is arranged to avoid.
    const ids = graph["@graph"].map((n) => n["@id"]).filter(Boolean);
    assert.equal(new Set(ids).size, ids.length, `${path}: two nodes share an @id`);
    const pageNodes = graph["@graph"].filter((n) => n["@id"] === `${HOST}${path}`);
    assert.equal(pageNodes.length, 1, `${path}: ${pageNodes.length} page nodes, expected exactly one`);
    assert.equal(
      graph["@graph"].filter((n) => ["WebPage", "TechArticle", "Article"].includes(n["@type"])).length,
      1,
      `${path}: more than one page-level node`,
    );
  }
});

test("gannet is published as source in development, never as a released application", () => {
  const index = html.get("/");
  const [code] = nodesOf(index, "SoftwareSourceCode");
  assert.ok(code, "index: no SoftwareSourceCode node");
  assert.equal(code.codeRepository, REPO);
  assert.equal(code.creativeWorkStatus, "In development");
  // gannet is not released, so no schema type may say there is something to
  // install, and nothing may carry a download or an offer.
  for (const [path, doc] of html) {
    const text = JSON.stringify(graphOf(doc));
    assert.equal(/SoftwareApplication|downloadUrl|"offers"|installUrl/.test(text), false, `${path}: structured data describes a released application`);
  }
  // And the repository node is on the home page alone: a guide is about its
  // subject, not about gannet.
  for (const { path } of guides) {
    assert.equal(nodesOf(html.get(path), "SoftwareSourceCode").length, 0, `${path}: publishes the gannet repository node`);
  }
});

test("each guide page is a TechArticle with a FAQPage", () => {
  for (const { path, slug } of guides) {
    const doc = html.get(path);
    const articles = nodesOf(doc, "TechArticle");
    assert.equal(articles.length, 1, `${path}: ${articles.length} TechArticle nodes, expected exactly one`);
    const [article] = articles;
    assert.equal(article["@id"], `${HOST}${path}`, `${path}: the article is not the page`);
    assert.deepEqual(article.publisher, { "@id": ORG_ID });
    assert.ok(article.headline, `${path}: the article has no headline`);
    assert.ok(faqsFrom(guideMarkdown(slug)).length > 0, `${path}: no FAQ section in the Markdown`);
    assert.equal(nodesOf(doc, "FAQPage").length, 1, `${path}: no FAQPage node`);
  }
});

// ONE DEFINITION, TWO SURFACES. Every published question and answer is on the
// page in the words the structured data claims. An answer written for an
// engine and not for a reader is a page nobody should rank. The guides render
// Markdown, so the page text is compared, not the source.
//
// Typographic quotes are folded to straight ones on both sides: the Markdown
// renderer curls them and the structured data keeps what was written.
//
// Inline elements are removed WITHOUT a space, so "<code>x</code>, then" reads
// as "x, then" as it does on screen; the glue test above is what proves the
// spaces that should exist do. The outbound-link glyph and its hidden
// "(external site)" are dropped, because the structured data carries the link
// text alone.
const readerText = (doc) =>
  fold(
    visibleText(
      doc
        .replace(/<span class="(?:out-glyph|sr-only)"[^>]*>[\s\S]*?<\/span>/g, "")
        .replace(/<\/?(?:a|code|strong|em|span|b|i)\b[^>]*>/g, ""),
    ),
  );

const fold = (s) =>
  s
    .replace(/[‘’]/g, "'")
    .replace(/[“”]/g, '"')
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase();

test("every FAQ question and answer in the structured data is on the page in the same words", () => {
  const surfaces = [
    { path: "/", faqs: homeFaqs },
    ...guides.map((g) => ({ path: g.path, faqs: faqsFrom(guideMarkdown(g.slug)) })),
  ];
  for (const { path, faqs } of surfaces) {
    const doc = html.get(path);
    const [faq] = nodesOf(doc, "FAQPage");
    assert.ok(faq, `${path}: no FAQPage node`);
    assert.equal(faq.mainEntity.length, faqs.length, `${path}: the structured data and the source disagree on how many questions`);
    const text = readerText(doc);
    for (const q of faq.mainEntity) {
      assert.equal(q["@type"], "Question");
      assert.ok(text.includes(fold(q.name)), `${path}: the FAQ question "${q.name}" is in the structured data and not on the page`);
      const answer = q.acceptedAnswer.text;
      assert.ok(answer.length > 80, `${path}: the answer to "${q.name}" has no substance behind it`);
      assert.ok(
        text.includes(fold(answer)),
        `${path}: the answer to "${q.name}" is in the structured data and not on the page in the same words`,
      );
    }
  }
});

// ---------------------------------------------------------------------------
// gannet is IN DEVELOPMENT. The site says so, and says nothing that only a
// released tool could say.
// ---------------------------------------------------------------------------

test("the home page states the development status, in the words of site.ts", () => {
  const text = visibleText(html.get("/")).replace(/\s+/g, " ");
  // The page is checked against site.ts, so site.ts is pinned here too: a test
  // that read both from the same place would pass when the status was deleted
  // at the source. The words change only when the state they describe changes,
  // and then this line changes with them, on purpose.
  assert.equal(status.label, "In development", "site.ts: the status label changed");
  assert.ok(/\bnot released\b/i.test(status.text), `site.ts: the status text no longer says gannet is not released: "${status.text}"`);
  assert.ok(text.includes(status.label), `index: the status label "${status.label}" is not on the page`);
  assert.ok(text.includes(status.text), `index: the status text "${status.text}" is not on the page`);
});

// UNSHIPPED: phrases that only make sense for a released gannet. EACH ENTRY
// COMES OFF THIS LIST IN THE PR THAT SHIPS WHAT IT NAMES - the install line in
// the release PR, the command in the PR that adds the command line, and so on.
// Entries are matched against lowercased visible text. They all name gannet on
// purpose: the SearXNG guide legitimately shows docker commands for SearXNG.
const UNSHIPPED = [
  "install gannet",
  "run gannet",
  "gannet is available",
  "download gannet",
  "try gannet",
  'gannet research "',
  "gannet can now",
  "dotnet run --project src/gannet",
];

test("no unshipped capability is described as though it exists", () => {
  const surfaces = [...html, ["/404/", notFound], ["llms.txt", llms]];
  for (const [path, doc] of surfaces) {
    const text = flat(doc);
    for (const phrase of UNSHIPPED) {
      assert.equal(text.includes(phrase), false, `${path}: "${phrase}" only makes sense for a released gannet`);
    }
  }
});

test("terraken and Terraform appear nowhere except the footer's sibling list", () => {
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    // The footer's "Also from DBHQ" list is the one place the sibling may be
    // named. It is cut out whole, so the rest of the document is held to it.
    const rest = doc.replace(/<ul class="also-list"[\s\S]*?<\/ul>/, " ");
    assert.notEqual(rest.length, doc.length, `${path}: the "Also from DBHQ" list was not found, so this test checked nothing`);
    const hit = rest.match(/.{0,40}(terraken|terraform).{0,40}/i);
    assert.equal(hit, null, `${path}: a leftover from the sibling site: "${hit?.[0]}"`);
  }
});

// ---------------------------------------------------------------------------
// The guides
// ---------------------------------------------------------------------------

test("each guide keeps the phrase it was built around, in its title and its H1", () => {
  for (const { path, phrases } of guides) {
    const doc = html.get(path);
    const title = titleOf(doc).toLowerCase();
    assert.ok(phrases.some((p) => title.includes(p)), `${path}: the title lost "${phrases[0]}" - "${title}"`);
    const h1 = h1Of(doc);
    assert.ok(h1, `${path}: no H1`);
    const h1Text = visibleText(h1).toLowerCase();
    assert.ok(phrases.some((p) => h1Text.includes(p)), `${path}: the H1 lost "${phrases[0]}" - "${h1Text}"`);
  }
});

test("the guides are guides, not landing pages", () => {
  // gannet is named sparingly: a guide that turns into an advert stops ranking
  // and deserves to, so the tool's name is capped at a share of the article.
  for (const { path } of guides) {
    const prose = proseOf(html.get(path));
    assert.ok(prose, `${path}: no .prose article body`);
    const words = visibleText(prose).split(/\s+/).filter(Boolean);
    const mentions = words.filter((w) => /\bgannet\b/i.test(w)).length;
    assert.ok(words.length >= 1200, `${path}: only ${words.length} words in the article, which is not a complete answer to the query`);
    assert.ok(
      mentions / words.length < 0.01,
      `${path}: ${mentions} mentions of gannet in ${words.length} words - this is turning into a landing page`,
    );
  }
});

// A GUIDE HAS TO HAVE A STOPPING POINT: a section that says how to tell
// whether the answer is right, rather than only what to do.
test("every guide has a section on how to tell", () => {
  for (const { path } of guides) {
    const doc = html.get(path);
    const headings = [...doc.matchAll(/<h2[^>]*>([\s\S]*?)<\/h2>/g)].map((m) => visibleText(m[1]).trim());
    assert.ok(
      headings.some((h) => h.startsWith("How to tell")),
      `${path}: no h2 starting "How to tell", so the stopping point cannot be found by scanning`,
    );
  }
});

// CAPTURED OUTPUT IS NEVER TYPED. The SearXNG guide shows real output from a
// real instance, pulled in from samples/searxng/ at build time by a marker.
test("every sample marker in the SearXNG guide names a file that exists, and none reach the page", () => {
  const md = guideMarkdown("searxng-search-api");
  const names = [...md.matchAll(/<<sample:\s*([^>\s]+)\s*>>/g)].map((m) => m[1]);
  assert.ok(names.length > 0, "the SearXNG guide has no sample markers, so this test checked nothing");
  for (const name of names) {
    assert.ok(existsSync(new URL(name, SAMPLES)), `samples/searxng/${name} is named by a marker and does not exist`);
  }
  const doc = html.get("/searxng-search-api/");
  assert.equal(doc.includes("<<sample"), false, "a literal <<sample marker reached the page");
  assert.equal(doc.includes("&lt;&lt;sample"), false, "an escaped <<sample marker reached the page");
});

// ---------------------------------------------------------------------------
// Links
// ---------------------------------------------------------------------------

test("every internal link carries the canonical trailing slash", () => {
  const surfaces = [...html, ["/404/", notFound]];
  for (const [path, doc] of surfaces) {
    for (const m of doc.matchAll(/href="(\/[^"?]*)"/g)) {
      const href = m[1];
      // Real files are allowed to look like files, and a fragment attaches to
      // a path that has already been checked.
      if (/\.[a-z0-9]+$/i.test(href)) continue;
      const bare = href.split("#")[0];
      assert.ok(bare.endsWith("/"), `${path}: internal link without a trailing slash: ${href}`);
    }
  }
});

test("every internal link resolves to a page that exists, and every fragment to a real id", () => {
  const known = new Set(pages.map((p) => p.path));
  const idsByPath = new Map(
    pages.map((p) => [
      p.path,
      new Set([...html.get(p.path).matchAll(/\sid="([^"]+)"/g)].map((m) => m[1])),
    ]),
  );
  const surfaces = [...html, ["/404/", notFound]];
  for (const [path, doc] of surfaces) {
    for (const m of doc.matchAll(/href="(\/[^"?]*)"/g)) {
      const href = m[1];
      if (/\.[a-z0-9]+$/i.test(href)) continue;
      const [bare, frag] = href.split("#");
      assert.ok(known.has(bare), `${path}: link to a page that does not exist: ${href}`);
      if (frag) {
        assert.ok(idsByPath.get(bare).has(frag), `${path}: link to an anchor that does not exist: ${href}`);
      }
    }
    // Same-page anchors too: the contents list on each guide is only useful if
    // every row lands somewhere.
    if (known.has(path)) {
      for (const m of doc.matchAll(/href="#([^"]+)"/g)) {
        assert.ok(
          idsByPath.get(path).has(m[1]),
          `${path}: a link points at #${m[1]}, which is not on the page`,
        );
      }
    }
  }
});

// Astro strips the whitespace between a text node and an element that starts on
// the next source line, so a paragraph reading "download it from the <a>releases
// page</a>" ships as "from thereleases page". It is invisible in the source, it
// reads as a typo rather than a build artefact, and it is only ever noticed by
// someone looking at the rendered page. The fix is a literal `&#32;` at the end
// of the line that comes first (in .astro files) or a plain space (Markdown).
//
// IT COVERS EVERY INLINE ELEMENT, NOT JUST LINKS. <code> is the worst of them,
// because the chip's own padding leaves a gap that looks like a space and is
// not one: the words are joined in the accessible name, in a copied selection
// and in anything that reads the text rather than the pixels.
test("no inline element is glued to the word beside it", () => {
  const TAGS = "a|code|strong|em";
  const surfaces = [...html, ["/404/", notFound]];
  for (const [path, doc] of surfaces) {
    const body = doc.replace(/<script[\s\S]*?<\/script>/g, " ").replace(/<style[\s\S]*?<\/style>/g, " ");
    // The character class is wider than \w on purpose. The bug is the same
    // whether the text before the element ends in a letter or in punctuation.
    //
    // A semicolon is NOT in the class and must not be added to it: the fix for
    // this bug is a literal `&#32;`, which ends in one, so a class holding `;`
    // fails on every line that has already been fixed.
    for (const m of body.matchAll(new RegExp(`[\\w.,:!?]<(?:${TAGS})\\b[^>]*>`, "g"))) {
      assert.fail(`${path}: no space before an inline element: ...${body.slice(Math.max(0, m.index - 45), m.index + 12)}`);
    }
    for (const m of body.matchAll(new RegExp(`</(?:${TAGS})>[A-Za-z]`, "g"))) {
      assert.fail(`${path}: no space after an inline element: ...${body.slice(Math.max(0, m.index - 45), m.index + 12)}`);
    }
  }
});

test("no page is a dead end - every footer links every page", () => {
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    const foot = doc.match(/<footer[\s\S]*?<\/footer>/);
    assert.ok(foot, `${path}: no footer`);
    for (const p of pages) {
      assert.ok(foot[0].includes(`href="${p.path}"`), `${path}: the footer does not link ${p.path}`);
    }
  }
});

test("external links open in the same tab, and are annotated", () => {
  // Same-tab is the house decision: a link is navigation, and a new window is a
  // behaviour change the reader did not ask for. A same-tab top-level
  // navigation creates no opener relationship, so it needs no rel="noopener".
  for (const [path, doc] of [...html, ["/404/", notFound]]) {
    for (const m of doc.matchAll(/<a\s[^>]*>/g)) {
      assert.equal(/\starget=/.test(m[0]), false, `${path}: a link carries a target attribute: ${m[0].slice(0, 90)}`);
    }
  }
  assert.equal(/rel="noopener"/.test(allHtml), false, "a same-tab link carries rel=noopener, which does nothing");

  for (const [path, doc] of html) {
    const outbound = [...doc.matchAll(/<a\s[^>]*href="https?:\/\/[^"]*"[^>]*>([\s\S]*?)<\/a>/g)];
    assert.ok(outbound.length > 0, `${path}: no outbound links at all`);
    for (const m of outbound) {
      // An icon link has no text to annotate, so the glyph and the "(external
      // site)" span have nothing to attach to. Those carry their destination
      // in an aria-label instead. The exemption is deliberately narrow: no
      // text content AND an explicit label.
      const iconOnly = /<(?:img|svg)\b/.test(m[0]) && !/>[^<>]*[A-Za-z]/.test(m[1].replace(/<[^>]*>/g, "><"));
      if (iconOnly) {
        assert.ok(/aria-label="[^"]+"/.test(m[0]), `${path}: icon-only outbound link with no aria-label: ${m[0].slice(0, 80)}`);
        continue;
      }
      assert.ok(
        m[1].includes("8599") || m[1].includes("↗"),
        `${path}: outbound link without the north-east glyph: ${m[1].slice(0, 60)}`,
      );
      assert.ok(
        m[1].includes("(external site)"),
        `${path}: outbound link without the accessible annotation: ${m[1].slice(0, 60)}`,
      );
    }
  }
});

test("the sibling DBHQ sites are linked in every footer, and this site is not listed on itself", () => {
  for (const [path, doc] of html) {
    const m = doc.match(/<footer[\s\S]*?<\/footer>/);
    assert.ok(m, `${path}: no footer`);
    const foot = m[0];
    // Driven from site.also rather than a second copy of the list.
    for (const { href } of site.also) {
      assert.ok(foot.includes(href), `${path}: the footer does not link ${href}`);
    }
    assert.ok(site.also.length >= 3, "the sibling block has collapsed to almost nothing");
    assert.equal(foot.includes("gannet.dbhq.uk"), false, `${path}: the footer links this site to itself`);
  }
  // Every sibling says what it is for. "Another DBHQ site" is not a reason to
  // click, and a list of bare names is what this block degrades into.
  for (const a of site.also) {
    assert.ok(a.desc.length > 20, `the sibling ${a.label} has no reason to click attached`);
  }
});

// ---------------------------------------------------------------------------
// Sitemap, llms.txt and edge configuration
// ---------------------------------------------------------------------------

// lastmod comes from git history (src/lib/content-dates.mjs), so it is
// asserted wherever the page's source has a commit. A page nobody has
// committed has no history to date it from, which is the state of a new site
// before its first commit; from that commit on, every entry must carry one.
const hasHistory = (path) => {
  const clean = path.replace(/^\/|\/$/g, "");
  const files = clean === "" ? ["site/src/pages/index.astro"] : [`site/src/content/guides/${clean}.md`];
  try {
    return execFileSync("git", ["log", "-1", "--format=%H", "--", ...files], {
      cwd: new URL("../../", import.meta.url),
      encoding: "utf8",
    }).trim() !== "";
  } catch {
    return false;
  }
};

test("the sitemap lists every page, with a lastmod, and never the 404", () => {
  const xml = read("sitemap-0.xml");
  for (const p of pages) {
    assert.ok(xml.includes(`<loc>${HOST}${p.path}</loc>`), `sitemap: missing ${p.path}`);
  }
  assert.equal(xml.includes("/404"), false, "sitemap: lists the 404 page");
  const entries = xml.split("<url>").slice(1);
  assert.equal(entries.length, pages.length, `sitemap: ${entries.length} entries, expected ${pages.length}`);
  for (const e of entries) {
    const loc = e.match(/<loc>([^<]+)<\/loc>/)[1];
    const path = loc.replace(HOST, "");
    if (hasHistory(path)) {
      assert.match(e, /<lastmod>\d{4}-\d{2}-\d{2}/, `sitemap: ${loc} has no lastmod`);
    }
  }
  assert.ok(read("sitemap-index.xml").includes("sitemap-0.xml"));
});

test("robots.txt points at the sitemap index on this host", () => {
  assert.ok(read("robots.txt").includes(`Sitemap: ${HOST}/sitemap-index.xml`));
});

test("llms.txt names every page and links it on this host", () => {
  for (const p of pages) {
    assert.ok(llms.includes(`${HOST}${p.path}`), `llms.txt: missing ${p.path}`);
  }
  assert.ok(llms.startsWith("# gannet"), "llms.txt: no H1, or it is not the product name");
  assert.ok(llms.includes("DBHQ"), "llms.txt: does not name the publisher");
  assert.ok(llms.toLowerCase().includes("not released"), "llms.txt: does not say gannet is unreleased");
});

test("the edge policy is tight, and stays tight", () => {
  const headers = read("_headers");
  assert.ok(headers.includes("Strict-Transport-Security"));
  // Read the directive off the CSP line itself. Matching the whole file also
  // reads the comment block above it, which discusses the directives by name.
  const cspLine = headers.split("\n").find((l) => l.trim().startsWith("Content-Security-Policy:"));
  assert.ok(cspLine, "_headers: no Content-Security-Policy");
  const directives = new Map(
    cspLine
      .replace(/^\s*Content-Security-Policy:\s*/, "")
      .split(";")
      .map((d) => d.trim())
      .filter(Boolean)
      .map((d) => {
        const [name, ...values] = d.split(/\s+/);
        return [name, values];
      }),
  );
  // GOOGLE TAG MANAGER IS THE ONLY THIRD-PARTY ORIGIN THIS SITE ALLOWS. The
  // list is asserted exactly rather than loosely, because "one analytics
  // origin" is a decision and "whatever accumulated" is not. A second vendor
  // has to change this line, which is where somebody gets asked what it sets.
  const GTM = "https://www.googletagmanager.com";
  assert.deepEqual(directives.get("script-src"), ["'self'", GTM], "_headers: script-src has gained or lost an origin");
  assert.equal(
    directives.get("script-src").includes("'unsafe-inline'"),
    false,
    "_headers: script-src gained 'unsafe-inline' - analytics.js and consent.js exist to avoid exactly this",
  );
  assert.deepEqual(directives.get("default-src"), ["'self'"]);
  // connect-src carries the GA beacon endpoints. Losing one of them loses
  // measurement silently, so they are named rather than pattern-matched.
  assert.deepEqual(directives.get("connect-src"), [
    "'self'",
    GTM,
    "https://www.google-analytics.com",
    "https://*.google-analytics.com",
    "https://*.analytics.google.com",
  ]);
  assert.deepEqual(directives.get("frame-ancestors"), ["'none'"]);
  assert.deepEqual(directives.get("object-src"), ["'none'"]);
  assert.equal(
    /<script(?![^>]*\bsrc=)(?![^>]*type="application\/ld\+json")[^>]*>/.test(allHtml),
    false,
    "an inline script shipped, which the CSP will block at the edge",
  );
  for (const h of ["X-Content-Type-Options", "Referrer-Policy", "X-Frame-Options", "Permissions-Policy"]) {
    assert.ok(headers.includes(h), `_headers: no ${h}`);
  }
});

// ---------------------------------------------------------------------------
// Analytics: on by default, a notice, and a simple opt-out
//
// Since 30 Sep 2026 GA4 runs by default under the PECR statistical-purposes
// exception, the same as every *.dbhq.uk site. The exception holds only while
// four things stay true, and these tests hold each one separately: the
// measurement is analytics and nothing else (ad signals denied, Signals and ad
// personalisation off), a reader who opts out loads nothing and sends nothing
// more, the reader is told on a notice that does not block the page, and
// opting out is as easy as carrying on and one click from every page.
//
// Most of these run analytics.js itself, in a sandbox with a fake document,
// rather than reading its text. A regex over the source proves a phrase is
// there; it does not prove the phrase is reached. Running it proves what a
// browser would load. dbhq/docs/reference/analytics.md is the record, and
// dbhq.uk's web/apex/src/layouts/Base.astro is the reference implementation.
// ---------------------------------------------------------------------------

const MEASUREMENT_ID = "G-3H3NFGSX85";
const MODERN_CHROME =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

// A browser just big enough for analytics.js: a cookie jar that honours expiry,
// localStorage, navigator, location and a <head> that records what is appended.
function runAnalytics({
  hostname = "gannet.dbhq.uk",
  ua = MODERN_CHROME,
  webdriver = false,
  cookies = {},
  storage = {},
  // likelyBot's staleness line moves a month at a time, so the clock is pinned
  // or this suite would start calling Chrome 140 a bot in 2028.
  now = Date.UTC(2026, 8, 30),
} = {}) {
  const jar = new Map(Object.entries(cookies));
  const writes = [];
  const appended = [];
  const store = new Map(Object.entries(storage));
  const document = {
    get cookie() {
      return [...jar].map(([k, v]) => `${k}=${v}`).join("; ");
    },
    set cookie(raw) {
      writes.push(raw);
      const [pair, ...attrs] = raw.split(";").map((s) => s.trim());
      const eq = pair.indexOf("=");
      const name = pair.slice(0, eq);
      const expired = attrs.some((a) => /^expires=.*1970/i.test(a) || /^max-age=0$/i.test(a));
      if (expired) jar.delete(name);
      else jar.set(name, pair.slice(eq + 1));
    },
    createElement: (tag) => ({ tag }),
    head: { appendChild: (el) => appended.push(el) },
  };
  const window = {
    document,
    location: { hostname },
    navigator: { userAgent: ua, webdriver },
    localStorage: {
      getItem: (k) => (store.has(k) ? store.get(k) : null),
      setItem: (k, v) => store.set(k, String(v)),
      removeItem: (k) => store.delete(k),
    },
    Date: class extends Date {
      static now() {
        return now;
      }
    },
  };
  window.window = window;
  vm.runInNewContext(read("analytics.js"), window);
  // gtag pushes Arguments objects; turn each into a plain array to compare.
  const calls = () => window.dataLayer.map((a) => Array.from(a));
  return { window, jar, writes, appended, store, calls };
}

const tagLoaded = (run) =>
  run.appended.some((el) => el.tag === "script" && el.src === `https://www.googletagmanager.com/gtag/js?id=${MEASUREMENT_ID}`);

test("a new reader on the live host gets GA4, configured for analytics only", () => {
  const run = runAnalytics();
  assert.ok(tagLoaded(run), "the tag did not load for a new reader");
  assert.equal(run.appended.length, 1, "more than one script was injected");

  const calls = run.calls();
  const def = calls.find((c) => c[0] === "consent" && c[1] === "default");
  assert.ok(def, "no consent default was set before the tag");
  // Each signal pinned on its own: granting an ad signal is the change that
  // takes the site outside the exception, and it must fail by name.
  assert.equal(def[2].analytics_storage, "granted", "analytics_storage is not granted by default");
  for (const signal of ["ad_storage", "ad_user_data", "ad_personalization"]) {
    assert.equal(def[2][signal], "denied", `${signal} is not denied`);
  }

  const config = calls.find((c) => c[0] === "config");
  assert.ok(config, "no config call");
  assert.equal(config[1], MEASUREMENT_ID);
  assert.equal(config[2]?.allow_google_signals, false, "Google Signals is not off in the config");
  assert.equal(config[2]?.allow_ad_personalization_signals, false, "ad personalisation is not off in the config");

  // The default must precede the config, or the first hit goes out without it.
  assert.ok(calls.indexOf(def) < calls.indexOf(config), "consent default comes after config");
  // Nothing is written until the reader answers.
  assert.equal(run.writes.length, 0, "a cookie was written before the reader chose");
});

test("GA4 loads nothing off the live host, for a likely bot, or for a stale desktop browser", () => {
  // gannet-site.pages.dev serves the same bytes and is not the site.
  for (const hostname of ["gannet-site.pages.dev", "gannet.pages.dev", "localhost", "100.115.72.85"]) {
    assert.equal(tagLoaded(runAnalytics({ hostname })), false, `the tag loaded on ${hostname}`);
  }
  assert.equal(tagLoaded(runAnalytics({ webdriver: true })), false, "the tag loaded under navigator.webdriver");
  assert.equal(
    tagLoaded(runAnalytics({ ua: "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)" })),
    false,
    "the tag loaded for a crawler",
  );
  // Desktop Chrome about two years stale is how rotating scrapers present.
  const stale = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36";
  assert.equal(tagLoaded(runAnalytics({ ua: stale })), false, "the tag loaded for Chrome 118 on Windows");
  // ...and mobile is exempt, because phones keep old browsers for real.
  const oldAndroid = "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Mobile Safari/537.36";
  assert.ok(tagLoaded(runAnalytics({ ua: oldAndroid })), "an old mobile Chrome was treated as a bot");
});

test("likelyBot is the estate's function, word for word", () => {
  // Kept identical to ScentVerdict's svAnalytics.likelyBot and dbhq.uk's copy,
  // so one bot rule applies to one GA4 property. A local "improvement" here
  // would count the same visitor differently on different hosts. Compared with
  // whitespace collapsed, so formatting is free and logic is not.
  const CANONICAL = `function likelyBot() {
    try {
      if (navigator.webdriver) return true;
      var ua = navigator.userAgent || "";
      if (/bot|crawl|spider|headless/i.test(ua)) return true;
      if (/Android|Mobile|CrOS/.test(ua) || !/Windows NT 10\\.0|Macintosh|X11/.test(ua)) return false;
      var n = Math.max(0, Math.floor((Date.now() - Date.UTC(2025, 8, 2)) / 2592e6));
      var c = /Chrome\\/(\\d+)\\./.exec(ua);
      if (c) return +c[1] < 140 + n - 24;
      var f = /Firefox\\/(\\d+)\\./.exec(ua);
      if (f) return [115, 128, 140, 153].indexOf(+f[1]) < 0 && +f[1] < 142 + n - 24;
    } catch (e) {}
    return false;
  }`;
  const src = read("analytics.js");
  const m = src.match(/function likelyBot\(\) \{[\s\S]*?\n\}/);
  assert.ok(m, "analytics.js: no likelyBot function");
  const squash = (s) => s.replace(/\s+/g, " ").trim();
  assert.equal(squash(m[0]), squash(CANONICAL), "likelyBot has drifted from the estate's copy");
});

test("the tag is referenced once, inside the load gate, and nowhere else", () => {
  const src = read("analytics.js");
  const refs = src.match(/googletagmanager\.com\/gtag\/js/g) ?? [];
  assert.equal(refs.length, 1, "analytics.js: the tag URL appears more than once");
  const gate = src.match(/function load\(\) \{([\s\S]*?)\n\}/);
  assert.ok(gate, "analytics.js: no load() gate");
  assert.ok(gate[1].includes("googletagmanager.com/gtag/js"), "the tag is referenced outside load()");
  assert.match(gate[1], /if \(!PROD \|\| bot \|\| loaded\) return;/, "load() lost a guard");
  // Only the real host is measured.
  assert.match(src, /const PROD = location\.hostname === "gannet\.dbhq\.uk";/);
  // Nothing else on the site references it either.
  assert.equal(/googletagmanager\.com\/gtag/.test(read("consent.js")), false, "consent.js loads the tag itself");
  assert.equal(/googletagmanager\.com\/gtag/.test(allHtml), false, "a page loads the tag directly");
});

test("opting out stores off on .dbhq.uk, stops every further hit and removes _ga", () => {
  const run = runAnalytics({ cookies: { _ga: "GA1.1.1", [`_ga_3H3NFGSX85`]: "GS2.1.s1" } });
  assert.ok(tagLoaded(run));
  run.window.dbhqAnalytics.optOut();

  // The cookie, with every attribute pinned: Domain=dbhq.uk is what makes one
  // opt-out hold across every *.dbhq.uk site.
  const choice = run.writes.find((w) => w.startsWith("dbhq_analytics="));
  assert.ok(choice, "no choice cookie was written");
  const attrs = choice.split(";").map((s) => s.trim());
  assert.equal(attrs[0], "dbhq_analytics=off");
  for (const a of ["Max-Age=31536000", "Path=/", "SameSite=Lax", "Secure", "Domain=dbhq.uk"]) {
    assert.ok(attrs.includes(a), `the choice cookie lacks ${a}: ${choice}`);
  }
  assert.equal(run.window.dbhqAnalytics.choice(), "off");

  // Denied consent alone still sends cookieless pings, the page-leave hit
  // included. Google's per-ID disable flag is what stops them.
  assert.equal(run.window[`ga-disable-${MEASUREMENT_ID}`], true, "ga-disable was not set on opt out");
  const update = run.calls().find((c) => c[0] === "consent" && c[1] === "update");
  assert.equal(update?.[2]?.analytics_storage, "denied", "consent was not updated to denied");

  // _ga lives on .dbhq.uk, so it has to be expired there, not only on the host.
  assert.equal(run.jar.has("_ga"), false, "_ga survived opting out");
  assert.equal(run.jar.has("_ga_3H3NFGSX85"), false, "_ga_<id> survived opting out");
  assert.ok(
    run.writes.some((w) => w.startsWith("_ga=;") && /domain=\.dbhq\.uk/.test(w)),
    "_ga was not expired on .dbhq.uk",
  );
});

test("an opted-out reader loads nothing on the next page, and can turn it back on", () => {
  const run = runAnalytics({ cookies: { dbhq_analytics: "off" } });
  assert.equal(tagLoaded(run), false, "the tag loaded for a reader who opted out");
  assert.equal(run.calls().length, 0, "gtag was called for a reader who opted out");

  run.window.dbhqAnalytics.keepOn();
  assert.ok(tagLoaded(run), "Turn back on did not load the tag");
  assert.equal(run.window[`ga-disable-${MEASUREMENT_ID}`], false, "ga-disable was left set after turning back on");
  assert.ok(run.writes.some((w) => w.startsWith("dbhq_analytics=on;")), "turning back on did not store on");
});

test("a refusal left under the old opt-in prompt is honoured and carried over", () => {
  // The old prompt stored its answer in localStorage, per origin. "denied"
  // was a refusal and stays one: nothing loads, the cookie takes over and the
  // old key goes.
  const denied = runAnalytics({ storage: { "dbhq-consent": "denied" } });
  assert.equal(tagLoaded(denied), false, "an old refusal loaded the tag");
  assert.equal(denied.jar.get("dbhq_analytics"), "off", "an old refusal was not migrated to the cookie");
  assert.ok(denied.writes[0].includes("Domain=dbhq.uk"), "the migrated cookie is not on .dbhq.uk");
  assert.equal(denied.store.has("dbhq-consent"), false, "the old key was left behind");

  const granted = runAnalytics({ storage: { "dbhq-consent": "granted" } });
  assert.ok(tagLoaded(granted));
  assert.equal(granted.jar.get("dbhq_analytics"), "on");
  assert.equal(granted.store.has("dbhq-consent"), false);

  // The cookie wins over a leftover key, whichever way round they disagree.
  const both = runAnalytics({ cookies: { dbhq_analytics: "on" }, storage: { "dbhq-consent": "denied" } });
  assert.ok(tagLoaded(both), "a leftover old key overrode the estate cookie");
});

test("off the estate, the choice cookie is host-only", () => {
  const run = runAnalytics({ hostname: "gannet-site.pages.dev" });
  run.window.dbhqAnalytics.optOut();
  const choice = run.writes.find((w) => w.startsWith("dbhq_analytics="));
  assert.equal(/Domain=/i.test(choice), false, "a pages.dev preview tried to set a dbhq.uk cookie");
});

test("the tag reports to the estate's one data stream, not a stream of its own", () => {
  // The rule, and the reasoning, are in dbhq/docs/reference/analytics.md: one
  // property, one stream, split by Hostname at reporting time. A stream of this
  // site's own would fragment every journey from dbhq.uk into a fresh session
  // and strand this host outside GA4's Search Console reporting.
  const everything = [read("analytics.js"), read("consent.js"), allHtml].join("\n");
  const ids = [...everything.matchAll(/G-[A-Z0-9]{8,}/g)].map((m) => m[0]);
  assert.deepEqual([...new Set(ids)], [MEASUREMENT_ID], "wrong or extra measurement ID");
});

test("the notice ships on every page, does not block it, and opting out is as easy as OK", () => {
  const consent = read("consent.js");
  for (const [path, page] of html) {
    const notice = page.match(/<aside[^>]*data-analytics-notice[^>]*>[\s\S]*?<\/aside>/);
    assert.ok(notice, `${path}: no analytics notice`);
    // Ships hidden; consent.js shows it only when no choice is stored.
    assert.match(notice[0], /^<aside[^>]*\bhidden\b/, `${path}: the notice is visible before the script decides`);
    // Matched as whole attributes, so a renamed hook cannot pass as a prefix.
    assert.match(notice[0], /\sdata-analytics-on(?=[\s>=])/, `${path}: the notice has no OK`);
    assert.match(notice[0], /\sdata-analytics-off(?=[\s>=])/, `${path}: the notice has no Opt out`);
    assert.match(notice[0], />Opt out</, `${path}: the opt-out button does not say Opt out`);
    // NOT MODAL. There is nothing to agree to before reading.
    assert.equal(/<dialog\b/.test(page), false, `${path}: a <dialog> shipped`);
    assert.equal(/aria-modal/.test(page), false, `${path}: something claims to be modal`);
  }
  assert.equal(/\.showModal\s*\(/.test(consent), false, "consent.js opens the notice as a modal");
  const component = readFileSync(new URL("../src/components/AnalyticsNotice.astro", import.meta.url), "utf8");
  assert.equal(/::backdrop|inert\b/.test(component), false, "the notice dims or disables the page");

  // Equal weight: the same .btn sizing on both and nothing else that differs
  // but the site's normal primary/ghost pairing. A smaller, fainter or
  // link-styled Opt out is the dark pattern the exception rules out.
  const index = read("index.html");
  const classes = (attr) =>
    index
      .match(new RegExp(`<button[^>]*\\s${attr}(?=[\\s>=])[^>]*>`))[0]
      .match(/class="([^"]*)"/)[1]
      .split(/\s+/)
      .filter((c) => !/^btn-(primary|ghost)$/.test(c) && !c.startsWith("astro-"))
      .sort();
  assert.ok(classes("data-analytics-on").includes("btn"), "OK is not a .btn");
  assert.deepEqual(classes("data-analytics-off"), classes("data-analytics-on"), "Opt out is styled differently from OK");

  // consent.js reads window.dbhqAnalytics, which analytics.js defines, and
  // modules execute in document order. The other way round the notice never
  // shows and Opt out does nothing.
  assert.ok(
    index.indexOf("/analytics.js") > -1 && index.indexOf("/analytics.js") < index.indexOf("/consent.js"),
    "consent.js loads before analytics.js",
  );

  // Red on this site means exactly one thing - this change can destroy
  // something - and spending it on a cookie notice is how that stops being
  // true. Read the component source rather than the built page: the styles
  // are inlined into every page, so searching the HTML cannot tell whose rule
  // a colour came from.
  assert.equal(/--danger|#D92D20/i.test(component), false, "the notice uses the danger red");
});

test("Cookie settings is in the footer of every page, and only appears with JavaScript", () => {
  for (const [path, page] of [...html, ["/404/", read("404.html")]]) {
    const foot = page.match(/<footer[\s\S]*?<\/footer>/);
    assert.ok(foot, `${path}: no footer`);
    const btn = foot[0].match(/<button[^>]*data-analytics-settings[^>]*>([^<]*)<\/button>/);
    assert.ok(btn, `${path}: no Cookie settings control in the footer`);
    assert.equal(btn[1].trim(), "Cookie settings", `${path}: the control is not labelled Cookie settings`);
    assert.match(btn[0], /\bhidden\b/, `${path}: Cookie settings shows without JavaScript, where it does nothing`);
    // A reopening control needs a notice to reopen on the same page.
    assert.ok(page.includes("data-analytics-notice"), `${path}: Cookie settings with no notice to open`);
  }
  const consent = read("consent.js");
  assert.match(consent, /\[data-analytics-settings\]/, "consent.js never wires up Cookie settings");
  assert.match(consent, /b\.hidden = false/, "consent.js never reveals Cookie settings");
  assert.match(consent, /"Turn back on"/, "a reopened notice cannot turn analytics back on");
});

test("the reader is told what is counted, that there is no advertising, and where to read more", () => {
  const index = read("index.html");
  const notice = index.match(/<aside[^>]*data-analytics-notice[^>]*>[\s\S]*?<\/aside>/)[0];
  const text = notice.replace(/<[^>]*>/g, " ").replace(/\s+/g, " ");
  assert.match(text, /Google Analytics/, "the notice does not name what it uses");
  assert.match(text, /No advertising/, "the notice does not say there is no advertising");
  assert.ok(notice.includes('href="https://dbhq.uk/privacy/#analytics"'), "the notice does not link the analytics section of the policy");
  // DBHQ is one person, and the display name is lowercase.
  assert.equal(/\bwe\b/i.test(text), false, "the notice says we");
  assert.match(text, /\bgannet\b/);
});
