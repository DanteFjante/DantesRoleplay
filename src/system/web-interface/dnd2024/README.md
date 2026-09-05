# D&D 2024 server-hosted React interface

This is the canonical React source for the local D&D table at `/ui/dnd2024-play`.

The browser is a read-only presentation of audience-filtered data from the DantesRoleplay server.
It does not own campaign, World, character, rules, map, or authorization state. Canonical D&D rules
and authored content live under `catalog/applications/dnd2024`; live game state lives in SQLite.

## Commands

- `npm run typecheck` checks the maintained TypeScript and TSX source without emitting files.
- `npm test` runs the focused data/envelope tests and mounted browser-component tests.
- `npm run build:server` creates the page bundle in `server-dist/` for publication by the local
  DantesRoleplay server.
- `npm run release:manifest -- --output <path>` records the exact production files, source and
  tool fingerprints, and the no-wheel-zoom contract before publication.
- `npm run release:verify-live -- --manifest <path> --output <path>` compares every live byte,
  hash, asset reference, cache policy, and retired map signature with that manifest.
- `npm run verify` runs typecheck, both test groups, and the production server build as one gate.

## Reproducible baseline

The Slice 0 tools inspect current source and the exact live release separately. They never publish
a page, activate a catalog, restart the server, or edit game state. The historical
baseline/slice-0-baseline.json retains the original eight TypeScript diagnostics; it is not current
acceptance evidence.

Run from this package directory with an installed Chromium-family browser and Playwright:

```powershell
npm run baseline:browser -- --listener http://localhost:6217 --playwright-module "<absolute-path-to-playwright/index.mjs>" --browser-executable "<absolute-path-to-chrome.exe>" --output .tmp/website-slice-0/browser.json
npm run baseline:collect -- --listener http://localhost:6217 --browser-results .tmp/website-slice-0/browser.json --output .tmp/website-slice-0/baseline.json
```

Omit --playwright-module if Playwright is already resolvable in the local Node environment.
The sampler uses a separate headless browser and disposable contexts, never the user's profile or
open tab. Its default is 20 pairs: a fresh context with cleared HTTP cache, then a second navigation
through World, Character sheet, Map and Current View in that same context. The server stays warm;
private API reads still obey no-store. The target records browser version, viewport, CPU, OS, memory,
perspective and listener. A GM binding is measured in the normal initial Player-preview perspective.
Run without competing tests/builds for a comparable local performance baseline.

The collector runs node tests, mounted tests, typecheck, and production build as independent gates
and retains their exit status and diagnostics. It hashes the dirty worktree without changing it
(excluding its own evidence files), checks for changes during the gates, records raw/gzip source
bundle sizes, and verifies the active revision plus every published asset, including lazy chunks.
Source bundle measurements are never attributed to the unchanged live release.

Browser evidence must match the exact listener, machine, browser, audience/runtime fingerprint and
active page before and after sampling. Missing metrics, duplicate IDs, short runs and drift cannot
pass. Readiness timing summaries retain sample count, p50 and p95. Request summaries retain path,
parent interaction, response status, transferred payload size and browser/server cache evidence;
unknown lengths or cache results remain unknown. An unavailable character/map is recorded as a
known baseline failure with no invented ready latency. An absent combat board is not applicable,
not zero milliseconds. This can complete baseline collection without claiming that the website
itself is healthy.

The browser blocks writes (including background beacons), records blocked operation paths, and
stores no private payload bodies, query values, cookies, DOM text or console messages. No routing
interception disables the warm browser cache. The complete report is local ignored evidence; it
must not be published with the website. If no listener is available, collection records live checks
as blocked rather than deriving them from build time. Failed gates, invalid/missing browser
evidence, or worktree drift return a nonzero exit status while preserving the report.

Development builds expose a bounded `__DND2024_DEVELOPMENT_OBSERVABILITY__` snapshot containing
request path, duration, status, payload byte count, cache result, and parent interaction. It never
stores request or response bodies. Browser automation may supply 20 cold and 20 warm runs to the
collector with `--browser-results`; every emitted timing summary includes its sample count, p50,
p95, listener, and browser identity.

## View loading boundary

The page renders its navigation shell before private campaign data resolves. The World overview is
the default view; Party, Campaign, Current, Rules, Installed Content, and Map code load only when
opened. Private reads go through `ViewReadClient`, which cancels superseded work, retries one
transient failure, validates the response before use, and retains only fingerprinted last-good
values in process memory. Campaign and perspective preferences may remain in `localStorage`, but
private response payloads must not be written to browser storage.

The character page requires the registered `dnd2024.query.character-sheet-v2` read model. The
previously published page bundle remains the rollback path; the current source does not accept a
v1 character payload or infer missing labels, inventory hierarchy, or wallet values in the browser.

## Layout

- `src/components/` contains the componentized DM/Player interface.
- `src/data/` contains presentation-only types, filters, and asset routing.
- `src/server/` adapts already-authorized local server responses into the UI envelope.
- `server-host/` contains the page entry point.
- `public/` contains reviewed page-owned image assets.
- `test/` contains focused React data and envelope tests.
