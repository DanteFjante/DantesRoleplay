# D&D 2024 web runtime integration Slice 1 receipt — local prototype host

Superseded 2026-08-30: the canonical-host slice removed this iframe mount and restored the
server-owned `<dnd2024-workspace>`. This receipt remains as historical revision evidence only.

Completed: 2026-08-29

## Delivered boundary

- `/ui/dnd2024-play` revision **5** now mounts the actual React information-hub application from the local prototype runtime at `http://localhost:5173/`.
- The local prototype is configured through `.dev.vars` to use `http://localhost:6217` as its server-side game-data origin.
- The existing server reader now accepts display names containing spaces, so the actual campaign and actor records are usable.
- A presentation-only connected-to-hub adapter displays only live campaign, actor, party-goal, and player-safe knowledge fields. Maps, current-location state, people, history, factions, and rules remain visibly unavailable/empty when the database has no corresponding projection.
- The host CSP permits iframe content only from itself and this fixed local prototype origin; arbitrary remote framing remains disallowed.
- Removed the prototype fixture chapter label from the shared navigation and replaced it with live campaign labels.

## Evidence

- `npm test` in `prototype/dnd2024` passed: 122 tests.
- `npm run build` in `prototype/dnd2024` passed.
- Focused web interface tests passed: 89 tests.
- Browser smoke at `http://localhost:6217/ui/dnd2024-play` rendered the iframe’s actual React World/Campaign/Party/Current View/Rules navigation and live values for **The Waystone at Brackenford**, **Orban**, two recorded party goals, and eleven player-safe knowledge entries.

## Backup

- Pre-mount page-store backup: `DantesRoleplay.MCPServer/data/backups/dnd2024-prototype-mount-before-20260829T060027/`.

## Deliberate exclusions

No fixture world records, game-state mutation, C# game-rule logic, DM authorization, map projection, or new database schema was introduced.
