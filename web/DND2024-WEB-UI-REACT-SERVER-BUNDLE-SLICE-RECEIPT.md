# D&D 2024 web UI React server-bundle slice receipt

Status: **accepted 2026-08-30**
Ruleset alignment: dnd2024-compatible presentation

## Delivered boundary

- Built the established React information hub as a bounded static page bundle and published it to
  the existing `dnd2024-play` page identity as revision 8.
- `/ui/dnd2024-play` now mounts React directly and loads its JavaScript, CSS, and 22 reviewed map
  files from `/ui/dnd2024-play/assets/` on the DantesRoleplay server.
- Added a same-origin browser bootstrap that uses the existing closed game-server reader and hub
  projector. World, Campaign, map, campaign selection, and DM/Player selection therefore remain
  backed by live server records rather than copied fixture data.
- Preserved the existing Vinext/Sites build and its `/api/hub` loader as a separate packaging path;
  the canonical server bundle does not depend on that runtime.
- Removed the development iframe allowance from the generic local content policy. The active page
  contains no iframe, ChatGPT Site URL, old `<dnd2024-workspace>` entry, or port 5173 reference.

## Verification evidence

- React website tests: **165 passed, 0 failed**.
- Existing production website build: passed, including `/` and `/api/hub`.
- Server-bundle Vite build: passed; emitted one root `index.html` plus 24 assets.
- Bundle archive: 25 entries and 8,030,027 compressed bytes, below the server's limits.
- Focused `WebInterfaceTests`: **89 passed, 0 failed**; the React entry and self-only frame policy
  are asserted.
- Live page: HTTP 200 with the React root; JavaScript and CSS returned their correct media types.
- Live bundle assets: **24/24 returned HTTP 200**, totaling 9,551,789 bytes.
- Live data adapter: selected World `Thalorien`, Campaign `The Waystone at Brackenford`, 25 live
  locations, 12 map-bearing locations, and the closed DM/Player perspective set.
- Runtime isolation: no listener on port 5173; the page source contains no iframe, hosted Site URL,
  old workspace element, or port 5173 text.
- Restarted server response carries `frame-src 'self'` and no development-frame exception.

## Full-suite note

The repository-wide .NET suite was started after the focused acceptance passed. It encountered
unrelated, already-present catalog worktree failures: the canonical-schema count expected 154 but
found 166, and several D&D tests referenced catalog component files that are currently absent from
the checkout. The run was stopped after those repeated failures. This slice changes no catalog,
schema, mechanic, or gameplay record; its focused server suite and full React suite are green.

## Deliberate exclusions

No UI redesign, game-state write, catalog/schema/mechanic change, new public route, hosted-Site
publication, reverse proxy, separate Node server, or cleanup/deletion was included.
