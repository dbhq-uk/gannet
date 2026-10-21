// Reads the questions out of a guide's Markdown, so the FAQPage structured data
// is built from the same words the page renders. tests/content.test.mjs checks
// each answer is on the built page, word for word.
//
// The section is the `## ` heading whose text starts "Frequently asked" or is
// "FAQ"; each `### ` inside it is a question and the paragraphs under it are
// the answer, with Markdown links, code and emphasis reduced to their text.

export function plain(md) {
  return md
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\*\*([^*]+)\*\*/g, "$1")
    .replace(/\*([^*]+)\*/g, "$1")
    .replace(/\s+/g, " ")
    .trim();
}

export function faqsFrom(raw) {
  const body = raw.replace(/^---[\s\S]*?\n---\n/, "");
  const start = body.search(/^## (Frequently asked|FAQ\b)/m);
  if (start < 0) return [];
  const rest = body.slice(start).split("\n").slice(1).join("\n");
  const end = rest.search(/^## /m);
  const section = end < 0 ? rest : rest.slice(0, end);
  return section
    .split(/^### /m)
    .slice(1)
    .map((chunk) => {
      const [q, ...a] = chunk.split("\n");
      return { q: plain(q), a: plain(a.join("\n")) };
    })
    .filter((f) => f.q && f.a);
}
