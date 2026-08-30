# D&D 2024 web UI canonical-host slice receipt

Status: **accepted 2026-08-30**
Superseded: **2026-08-30 by `DND2024-WEB-UI-REACT-SERVER-BUNDLE-SLICE-RECEIPT.md`**
Ruleset alignment: dnd2024-compatible presentation

## Delivered boundary

- Replaced the `http://localhost:5173` iframe entry page with the reviewed server-owned game-table
  page and canonical `<dnd2024-workspace>`.
- The page now loads `system-workspace`, `application-workspace`, `dnd2024-workspace`, and
  `application-conversation` only from same-origin `/components/*` URLs.
- Published the exact reviewed page to the running page store as `dnd2024-play` revision 7.
- Stopped the obsolete prototype development listener on port 5173 after proving it was the Vinext
  process for this checkout.
- Reversed the focused regression test: iframe, port 5173, and `chatgpt.site` references are now
  forbidden, while the canonical element and component URLs are required.

## Evidence

- Canonical workspace JavaScript syntax check: passed.
- Focused `Dnd2024_play_page` web test: **1 passed, 0 failed**.
- Live page readback: HTTP 200, canonical workspace present, no iframe, no port 5173 reference, and
  no ChatGPT Site reference.
- All four same-origin component assets returned HTTP 200 from port 6217; the canonical workspace
  asset was 335,634 bytes.
- After stopping port 5173, the live page and canonical workspace asset continued returning HTTP
  200 and no listener remained on that port.

## Deliberate exclusions

No UI redesign, React feature migration, game-state write, catalog record, authorization policy,
route, permanent ID, D&D rule, or hosted-Site deployment changed in this slice.
