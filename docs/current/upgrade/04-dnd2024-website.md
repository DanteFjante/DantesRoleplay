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

Implementation is in progress in the detached `C:/repo/DantesRoleplay-dnd-web` checkout from `6023d31b`. The bounded refactor adds reusable panel state/error containment, consumer-level Lore/Faction/world-row projection that ignores unknown fields and isolates malformed records, keyboard and mobile DND-local navigation, and host consumption of the delivered shared `system-navigation` and `system-theme-toggle` components. The existing scoped resource owners and Campaign premise write client remain the API boundary because they already enforce cancellation, response ordering, idempotency, receipt validation, and authoritative refresh. Theme colors now alias the shared semantic tokens; no second preference engine was added. Next: finish focused mounted coverage, build/typecheck, prepare the controlled-data preview, commit the detached implementation, and record exact results here.
