// Markdown plugins for the guides. No dependencies: each walks the tree by hand.
//
// remarkSamples   a paragraph that is exactly `<<sample: NN-name.txt>>` becomes a
//                 code block holding that file from samples/searxng/, read at
//                 build time. Captured output is never pasted into a page, so a
//                 sample cannot be tidied by hand after it was captured.
// rehypeExternal  every link off this host gets the estate's outbound glyph and a
//                 visually hidden "(external site)", and stays in the same tab.
// rehypeTables    every table is wrapped so it can scroll on a phone, and takes
//                 the site's table style.
// rehypeCode      every code block gets the label strip the home page uses.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const SAMPLES = fileURLToPath(new URL("../../samples/searxng/", import.meta.url));
const MARKER = /^<<sample:\s*([A-Za-z0-9._-]+)>>$/;

function textOf(node) {
  if (node.type === "text") return node.value;
  return (node.children ?? []).map(textOf).join("");
}

export function remarkSamples() {
  return (tree) => {
    const walk = (node) => {
      if (!node.children) return;
      node.children = node.children.map((child) => {
        if (child.type === "paragraph") {
          const m = textOf(child).trim().match(MARKER);
          if (m) {
            const name = m[1];
            const value = readFileSync(join(SAMPLES, name), "utf8").replace(/\s+$/, "");
            const lang = name.endsWith(".json") || value.trimStart().startsWith("{") ? "json" : "text";
            return { type: "code", lang, meta: `sample=${name}`, value };
          }
        }
        walk(child);
        return child;
      });
    };
    walk(tree);
  };
}

const h = (tagName, properties, children) => ({ type: "element", tagName, properties, children });
const t = (value) => ({ type: "text", value });

export function rehypeExternal() {
  return (tree) => {
    const walk = (node) => {
      for (const child of node.children ?? []) {
        if (child.type === "element" && child.tagName === "a") {
          const href = String(child.properties?.href ?? "");
          // A bare URL used as its own link text wraps mid-path into a wall of
          // blue on a phone. Show the host and a short path; the href is kept.
          if (/^https?:\/\//.test(href) && textOf(child).trim() === href) {
            const u = new URL(href);
            const path = (u.pathname + u.search).replace(/\/$/, "");
            const shown = u.host.replace(/^www\./, "") + (path.length > 32 ? path.slice(0, 30) + "…" : path);
            child.children = [t(shown)];
          }
          if (/^https?:\/\//.test(href) && !href.startsWith("https://gannet.dbhq.uk")) {
            child.children.push(
              h("span", { className: ["out-glyph"], ariaHidden: "true" }, [t("↗")]),
              h("span", { className: ["sr-only"] }, [t(" (external site)")]),
            );
          }
        }
        walk(child);
      }
    };
    walk(tree);
  };
}

export function rehypeTables() {
  return (tree) => {
    const walk = (node) => {
      if (!node.children) return;
      node.children = node.children.map((child) => {
        if (child.type === "element" && child.tagName === "table") {
          child.properties = { ...(child.properties ?? {}), className: ["tbl"] };
          // A comparison with more than four columns breaks out of the reading
          // measure on a desktop, and dates never wrap at their hyphens.
          let cols = 0;
          const mark = (n) => {
            for (const c of n.children ?? []) {
              if (c.type !== "element") continue;
              if (c.tagName === "tr" && !cols) cols = (c.children ?? []).filter((x) => x.tagName === "th" || x.tagName === "td").length;
              if (c.tagName === "td" && /^\s*\d{4}-\d{2}-\d{2}\s*$/.test(textOf(c))) c.properties = { ...(c.properties ?? {}), className: ["nowrap"] };
              mark(c);
            }
          };
          mark(child);
          return h("div", { className: cols > 4 ? ["tbl-wrap", "wide"] : ["tbl-wrap"] }, [child]);
        }
        walk(child);
        return child;
      });
    };
    walk(tree);
  };
}

export function rehypeCode() {
  return (tree) => {
    const walk = (node) => {
      if (!node.children) return;
      node.children = node.children.map((child) => {
        if (child.type === "element" && child.tagName === "pre") {
          const code = child.children?.find((c) => c.tagName === "code");
          const cls = (code?.properties?.className ?? []).find((c) => String(c).startsWith("language-"));
          const lang = cls ? String(cls).slice("language-".length) : "";
          const meta = String(code?.data?.meta ?? code?.properties?.dataMeta ?? "");
          const sample = meta.match(/sample=([^\s]+)/)?.[1];
          const label = sample ? `Captured: samples/searxng/${sample}` : lang && lang !== "text" ? lang : "";
          const strip = label ? [h("div", { className: ["code-label"] }, [h("span", {}, [t(label)])])] : [];
          return h("figure", { className: ["code"] }, [...strip, child]);
        }
        walk(child);
        return child;
      });
    };
    walk(tree);
  };
}
