# D&D 2024 web UI legacy cleanup slice receipt

Status: **accepted 2026-08-30**
Ruleset alignment: dnd2024-compatible presentation

## Delivered boundary

- Retired the old owner-only Sites project from the former
  `dantes-roleplay-dnd2024-table` slug, moved it to a dated retired slug, and labeled it to direct
  the owner to `localhost:6217`. The available Sites API does not expose permanent project deletion.
- Removed `.openai/hosting.json`, the Next/Vinext app and API route, hosted runtime environment
  adapter, hosted build configuration, development environment files, and all hosted-only package
  dependencies.
- Removed the superseded `dnd2024-workspace.js` implementation and its recovery HTML. The generic
  application workspace and current reviewed map-image route remain.
- Removed hosted build caches/output, the old publication ZIP, TypeScript cache, and stale Site
  validation checkout.
- Removed 26 completed/superseded D&D web implementation documents under `web/` and 10 completed
  hosted/information-hub implementation documents under `prototype/dnd2024/planning/`. Durable
  receipts, the owning roadmap, active React plans, and the current dependency tree remain.
- Reduced the React package graph by 165 packages and updated its README, TypeScript configuration,
  and ignore rules for the server-only build.

## Verification evidence

- React tests: **165 passed, 0 failed**.
- Server React build: passed with one bundled JavaScript file, one stylesheet, and the reviewed map
  assets.
- Focused `WebInterfaceTests`: **89 passed, 0 failed**.
- Live `/ui/dnd2024-play`: HTTP 200 with the React root.
- Removed `/components/dnd2024-workspace.js`: HTTP 404.
- Current `/components/maps/thalos-world.png`: HTTP 200 with `image/png`.
- No listener remains on port 5173.
- Source scan found no Sites marker or hosted-only package. Remaining old-name strings are negative
  regression assertions that prove the retired paths stay absent.

## Deliberate exclusions

No live page revision, campaign/world record, map image, generic application component, catalog,
schema, mechanic, old-D&D archive, or durable receipt was deleted. The previously reported missing
People/Lore/History projection is a separate functional repair and was not changed by this cleanup.

## Recoverability

Deleted generated build/cache output is reproducible from the retained React source. Removed source
and documentation remain recoverable through version history; the retired Sites versions remain
owner-only recovery evidence.
