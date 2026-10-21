# gannet.dbhq.uk - design

The reference is **Superhuman's style as Refero records it**, "golden hour
editorial" (Dan, 21 Oct 2026:
[the reference](https://styles.refero.design/style/418b374a-be64-44f0-b17e-1d45308c7e62)):
warm parchment, charcoal type, one deep brand colour used sparingly, one
full-bleed dark band, generous space, no shadows, and photography carrying the
feeling. Put into gannet's colours.

## The mapping

| Reference | gannet | Token |
|---|---|---|
| Warm Parchment canvas | the same, the page's ground | `--bg #F2F0EB` |
| Ink Charcoal text, never pure black | the same | `--text #292827` |
| Midnight Wine: primary button, banner, footer | navy: primary button, status, footer | `--ink #102A4C` |
| Deep Lagoon: one full-bleed dark band | deep sea teal, once per page at most | `--sea #0C3B3C` |
| Royal Violet links | DBHQ blue links, nowhere else | `--blue #2B6BF3` |
| Super Sans VF at 460 for headlines | Geist at 460, self-hosted | `font-weight: 460` |
| 16px radius, hairline borders, no shadows | the same | `--r-md`, `--border` |
| Photography with product UI as glass over it | gannet photographs with the rules as glass over them | `.glass` |

## The rules

- **Headlines at 460, never bold.** Size and space make the hierarchy.
- **Navy is the one chromatic fill.** A page asks for one thing.
- **Gold is the mark's eye** and appears nowhere else.
- **The sea band appears once on a page at most.** It is the one tonal break.
- **No drop shadows.** Depth is the photograph and the glass.
- **Images are mood, never proof.** The photographs are generated and the
  footer says so. The glass cards carry gannet's rules as text, never a mock of
  output, because gannet has none yet. When it does, the site shows a real
  capture.
- **Guides read like articles**: 18px body at a 68-character measure, one
  column, the table of contents at the top rather than in a sidebar.
- **Code blocks are charcoal, not navy**, so a terminal never reads as another
  brand band.
- **The wordmark is the generated SVG** from `../assets/_gen/logo.py`, never set
  in a font.
- No em dash, no en dash, anywhere.
