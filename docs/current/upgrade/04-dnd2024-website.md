# DND2024 website components and resilient data

Owner: root agent workflow_publication_finish (the earlier cleanup preparation is complete). Worktree: C:/repo/DantesRoleplay-dnd-web, DETACHED from 6023d31b.

## Outcome

Improve the existing DND2024 website with clearer navigation and design, reusable components, API-bound controls, and component-local handling of incomplete JSON. This is implementation work. The broader game/application upgrade is planned separately in workstream 05.

## Ownership and shared boundaries

Keep React, Redux, Vite, and existing resource/scoping/pagination owners. Own src/system/web-interface/dnd2024/src/components, presentation/data helpers as needed, styles, server-host wiring, and focused tests. This includes TopBar, MainNavigation, and DndInformationHub. The shared website worker owns DantesRoleplay.Web/BrowserComponents; do not duplicate or concurrently edit that code.

Consume the shared /components/system-theme.js owner and semantic variables. Alias DND colors to those tokens and remove fixed dark-only backgrounds where necessary. Do not create another preference engine or storage key. Coordinate application-level navigation with shared system-navigation and keep DND view navigation clearly local.

## Component and API rules

- Accept unknown additional display fields. Check required field types at the component that consumes them.
- Missing optional fields use a meaningful local fallback. Missing required fields produce a local error or unavailable state with retry when possible; unaffected siblings and rows keep working.
- Reuse/reset error boundaries for new scope and data. Do not hide every error or label failed data as success.
- Keep transport provenance, authorization, audience, state freshness, and typed mutation validation strict.
- Compose existing clients for reusable reads, actions, buttons, and forms. Retain cancellation, response ordering, scope fences, idempotency, actual receipts, uncertain-write recovery, and authorized refresh after commit.
- Keep DND rules in catalog JavaScript. Do not add game-specific C# behavior or a replacement backend.

Inspect and reduce coherent chunks of DndInformationHub glue, reuse ViewErrorBoundary and resource owners, and address the reported Lore/Factions problems. Apply the pattern to Current, Campaign, World, Character, and Item page boundaries without rewriting all game rules.

## Acceptance

Focused mounted tests must show extra JSON accepted, one missing/malformed section isolated, siblings retained, stale scope results cancelled, and actual client action envelopes preserved without duplicate submission. Check theme and keyboard/mobile navigation. Run affected tests, typecheck, and frontend build. Provide a disposable real-component preview for visual QA and state any controlled-data limits.

## Current checkpoint

Delivered as detached commit `220018aa25cc92c20bf99dbac70841e3c64fa070` from baseline `6023d31b`; the code checkout is clean. The bounded refactor adds reusable panel state/error containment, consumer-level Lore/Faction/world-row projection that ignores unknown fields and isolates malformed records, keyboard and mobile DND-local navigation, and host consumption of the shared `system-navigation` and `system-theme-toggle` components from website-shell commit `ff3c486d`. The existing scoped resource owners and Campaign premise write client remain the API boundary because they already enforce cancellation, response ordering, idempotency, receipt validation, and authoritative refresh. Theme colors alias the shared semantic tokens; no second preference engine was added. Initial CUA review found remaining dark-only structural surfaces; sidebar, perspective, character hero/overview, inventory, and item panels were then converted to shared surface/text/muted/accent tokens and rebuilt. Verification: new resilience/theme/navigation mounted checks 5/5; existing Lore/Factions 6/6; display/web-state 30/30; connected projection 46/46; full mounted website states 79/79 including scope cancellation and one-write/receipt/reread behavior; focused inventory/portrait plus resilience 10/10; TypeScript check passed; production Vite build passed within its initial JavaScript budget. Final CUA checks confirmed coherent light and dark layouts; the controlled preview accessibility audit reported 0 violations and 32 passes in each theme. Disposable preview: `http://127.0.0.1:4178/.tmp/preview.html` while task session 41245 remains running, using real React components and item client with controlled data only. It has no authenticated application catalog, live activation, game database, or external provider. Integration order: land shared website-shell `ff3c486d` before DND commit `220018aa`.

Real-data rehearsal follow-up is delivered as detached commit `d7b797a6d9ba24f4814f0acb4d73898badbc40a6` on top of `220018aa`. The DND header now places the shared navigation in its own full-width row and relies on its single owned theme control. A shared website normalizes a stale Player preference to the full DM table view and removes the obsolete perspective switch only when the host supplies both the explicit `X-Website-Access: shared` transport evidence and the game-master audience binding; client preference alone cannot promote authority. World, Lore, and Faction structural colors now use semantic theme surfaces and text tokens. The existing Faction continuation remains user-controlled: the first 25 of 35 records are described as displayed from the source total, `Load more factions` carries the source-bound cursor, and the control disappears when the remaining page completes. Rehearsal evidence also established that the Lore partial notice reflects incomplete optional field coverage while all 237 readable records render. Verification: game-server context 91/91; presentation resilience/header 6/6; targeted mounted Faction continuation 1/1; TypeScript check and production build passed. Clean-commit bundle: `C:/repo/DantesRoleplay-dnd-web/.tmp/dnd-rehearsal-fix/dnd2024-play-d7b797a6.zip`, SHA-256 `F59A6D93B7AB3DA6593F2A3EAC41F8100E653F9719E405AE9EC40B44A2C0402D`, 63 root-valid entries. It still requires the authenticated host and shared BrowserComponents at deployment; no live database or page revision was changed by this workstream.
