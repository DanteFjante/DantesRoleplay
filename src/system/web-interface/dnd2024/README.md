# D&D 2024 server-hosted React interface

This is the canonical React source for the local D&D table at `/ui/dnd2024-play`.

The browser is a read-only presentation of audience-filtered data from the DantesRoleplay server.
It does not own campaign, World, character, rules, map, or authorization state. Canonical D&D rules
and authored content live under `catalog/applications/dnd2024`; live game state lives in SQLite.

## Commands

- `npm test` runs the focused presentation and server-envelope tests.
- `npm run build:server` creates the page bundle in `server-dist/` for publication by the local
  DantesRoleplay server.

## Layout

- `src/components/` contains the componentized DM/Player interface.
- `src/data/` contains presentation-only types, filters, and asset routing.
- `src/server/` adapts already-authorized local server responses into the UI envelope.
- `server-host/` contains the page entry point.
- `public/` contains reviewed page-owned image assets.
- `test/` contains focused React data and envelope tests.
