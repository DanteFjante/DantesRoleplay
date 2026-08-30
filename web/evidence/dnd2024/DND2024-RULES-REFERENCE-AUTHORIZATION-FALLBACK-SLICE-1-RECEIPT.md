# DND2024 Rules reference authorization fallback Slice 1 receipt

Status: **accepted 2026-08-30**

Implementation:
`web/DND2024-RULES-REFERENCE-AUTHORIZATION-FALLBACK-SLICE-1-IMPLEMENTATION.md`

Published page: `dnd2024-play`, active revision **17**

## Delivered boundary

- A denied, unavailable, incomplete, or failed private campaign bootstrap now opens the public
  read-only Rules library instead of the full-page private-table stop screen.
- World, Campaign, Party, and Current View remain disabled in that fallback. The browser receives
  no substitute campaign envelope and no campaign authorization check is bypassed.
- A successfully authorized audience continues to open the full private table unchanged.
- Rules still refresh from the registered D&D catalog and retain the accepted source-built
  fallback, so new and revised registered references remain visible without a website allowlist.

No catalog record, D&D rule, schema, application activation, state-space binding, live campaign
record, authentication policy, typed effect, or transaction changed.

## Evidence

- Focused availability/Rules/state tests: **35 passed, 0 failed**.
- Complete D&D web suite: **135 passed, 0 failed**.
- Production server build: passed; emitted the React script, stylesheet, reviewed city maps, and
  bounded Rules catalog asset.
- Published immutable page revision 17 with five assets; page and active script returned HTTP 200,
  and the active script contained the Rules-only fallback marker.
- Live audience read returned `200`, bound to D&D 2024, the Brackenford campaign, the canonical
  `dnd2024-main` state space, and the game-master seat.
- Live in-app browser check opened the Rules tab, loaded **2,380 references**, and rendered the
  exact selected Acolyte source locator and catalog revision without console errors.
- Focused .NET web tests were attempted while the live host was serving the acceptance page; the
  build stopped before test execution because Windows correctly held the running host DLL open.
  No C# or server route changed in this slice, and the focused/full JavaScript suites plus live
  server readback cover the changed boundary.

## Rollback

Revision 16 remains immutable and can be reactivated. Source rollback removes the Rules-only shell,
the navigation availability option, the bootstrap selection helper, its tests, and the small style
additions. No database or campaign rollback is required.
