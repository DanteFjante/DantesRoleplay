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

Run the test, typecheck, and server-build gates independently, then generate the Slice 0 report:

```powershell
node --test
node node_modules/typescript/bin/tsc --noEmit
node node_modules/vite/bin/vite.js build --config vite.server.config.ts
node scripts/collect-baseline.mjs --browser-name "Google Chrome" --browser-version "<version>" --output baseline/slice-0-baseline.json
```

The collector fingerprints the commit and dirty worktree, repeats the three gates, hashes the
built bundle, and probes the configured HTTP and HTTPS listeners. If neither listener is running,
the report records live revision, live bundle, and browser sampling as blocked instead of deriving
them from build timings.

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
