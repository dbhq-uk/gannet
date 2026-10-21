// Structured data (schema.org JSON-LD) for gannet.dbhq.uk.
//
// The shape is terraken's (dbhq-uk/terraken/site/src/lib/schema.ts), and its
// two rules hold here:
//
// ONE NODE PER PAGE. Where a page is an article, the page node IS the
// TechArticle - `pageProps` merges the article's properties onto it - rather
// than a WebPage carrying a second article beside it.
//
// THE PUBLISHER IS dbhq.uk's OWN ORGANIZATION @id, so every DBHQ property
// describes one organisation rather than several that look alike.
//
//   /                               WebPage + SoftwareSourceCode
//   /<guide>/                       TechArticle + FAQPage
//
// SoftwareSourceCode, not SoftwareApplication: gannet is not released, so
// there is no application to download or run, only source being built. The
// home page describes a repository, and the structured data says the same.

import { REPO, site, type Faq } from "./site";

const BASE = site.url;
const DBHQ = "https://dbhq.uk";

export const ORG_ID = `${DBHQ}/#organization`;
export const WEBSITE_ID = `${BASE}/#website`;
export const CODE_ID = `${BASE}/#source`;

const SITE_NAME = "gannet";
const SITE_DESCRIPTION =
  "gannet is an open-source deep research agent being built in C#: it plans the research, searches the web, weighs each source and cites every claim to a page it read.";

function organizationNode() {
  return {
    "@type": "Organization",
    "@id": ORG_ID,
    name: "DBHQ",
    legalName: "DBHQ Consulting Ltd",
    url: `${DBHQ}/`,
    logo: { "@type": "ImageObject", url: `${DBHQ}/icon-512.png`, width: 512, height: 512 },
    email: "dan@dbhq.uk",
    sameAs: ["https://www.linkedin.com/company/dbhq-uk", "https://github.com/dbhq-uk"],
  };
}

function webSiteNode() {
  return {
    "@type": "WebSite",
    "@id": WEBSITE_ID,
    url: `${BASE}/`,
    name: SITE_NAME,
    description: SITE_DESCRIPTION,
    publisher: { "@id": ORG_ID },
    inLanguage: "en-GB",
  };
}

/** The repository, on the home page and nowhere else. */
export function sourceCodeNode() {
  return {
    "@type": "SoftwareSourceCode",
    "@id": CODE_ID,
    name: "gannet",
    description: SITE_DESCRIPTION,
    codeRepository: REPO,
    programmingLanguage: "C#",
    runtimePlatform: ".NET 10",
    license: "https://opensource.org/licenses/MIT",
    creativeWorkStatus: "In development",
    author: { "@id": ORG_ID },
    isBasedOn: "https://github.com/tarun7r/deep-research-agent",
  };
}

export function faqNode(id: string, faqs: readonly Faq[]) {
  return {
    "@type": "FAQPage",
    "@id": id,
    mainEntity: faqs.map((f) => ({
      "@type": "Question",
      name: f.q,
      acceptedAnswer: { "@type": "Answer", text: f.a },
    })),
  };
}

/** Properties merged onto a guide's page node, making it the TechArticle. */
export function guideArticle(headline: string, about: readonly string[], dateModified: string) {
  return {
    headline,
    about: about.map((name) => ({ "@type": "Thing", name })),
    author: { "@id": ORG_ID },
    publisher: { "@id": ORG_ID },
    dateModified,
    mentions: { "@id": CODE_ID },
  };
}

interface PageInput {
  canonical: string;
  title: string;
  description: string;
  image: string;
  pageType?: string;
  mainEntityId?: string;
  pageProps?: Record<string, unknown>;
  crumb?: string;
}

function webPageNode(p: PageInput) {
  const node: Record<string, unknown> = {
    "@type": p.pageType ?? "WebPage",
    "@id": p.canonical,
    url: p.canonical,
    name: p.title,
    description: p.description,
    isPartOf: { "@id": WEBSITE_ID },
    primaryImageOfPage: { "@type": "ImageObject", url: p.image },
    inLanguage: "en-GB",
    breadcrumb: { "@id": `${p.canonical}#breadcrumb` },
    ...(p.pageProps ?? {}),
  };
  if (p.mainEntityId) node.mainEntity = { "@id": p.mainEntityId };
  return node;
}

// The site is flat: every page is Home, or Home > <page>.
function breadcrumbNode(p: PageInput) {
  const items: Record<string, unknown>[] = [
    { "@type": "ListItem", position: 1, name: "gannet", item: `${BASE}/` },
  ];
  if (p.crumb) items.push({ "@type": "ListItem", position: 2, name: p.crumb, item: p.canonical });
  return { "@type": "BreadcrumbList", "@id": `${p.canonical}#breadcrumb`, itemListElement: items };
}

export function pageGraph(p: PageInput, extra: object[] = []) {
  return {
    "@context": "https://schema.org",
    "@graph": [organizationNode(), webSiteNode(), webPageNode(p), breadcrumbNode(p), ...extra],
  };
}
