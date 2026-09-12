# Shared website navigation, themes, and components

Owner task: 01a0917b-34d0-7cc1-bbbf-9689abd0b2e9. Worktree: C:/repo/DantesRoleplay-website-shell, DETACHED. Approved baseline: prepared merge 6023d31b (same platform code as 9395cd8e).

## Outcome

Provide a coherent responsive shell for many website applications, with usable navigation, reusable API components, and light/dark/system themes. Supporting applications without published pages stay out of user navigation; admin discovery remains available. No application IDs, including game.core, are hardcoded into this filtering.

## Approved contracts and ownership

Add /components/system-theme.js through the existing browser-asset route. It owns the single preference key dantes.system-theme.v1 with system/light/dark, applies data-system-theme to the HTML element, and follows the system color preference only in system mode. Provide a native-select system-theme-toggle and a small initializer/listener or documented DOM event that DND React can consume without duplicating preference logic.

Semantic tokens include --system-color-canvas, surface, surface-raised, text, muted, border, accent, accent-contrast, danger; also focus, radii, shadows, and fonts. Support blocked storage gracefully, accessible contrast/focus, and shadow-DOM inheritance.

Keep permissioned discovery and pagination unchanged. User navigation includes only normalized applications with isPublishable and at least one valid index or additional page. An application without an index but with another valid page remains usable through that page. Use an application switcher, selected-application page links, and a responsive drawer with keyboard/Escape/focus-return behavior. Do not render disabled non-website entries.

Own DantesRoleplay.Web/BrowserComponents/system-theme.js, system-workspace.js, system-publication.js, the home/application/control example shells, and relevant browser/asset tests. Do not edit DND React source. Do not change endpoints, authorization, catalog, or migrations.

Reuse system-progress, system-error, system-empty-state, system-data-view, system-action-button, and system-form. Preserve validated transport, caller scope, cancellation, idempotency, receipts, uncertain-write fencing, and fresh authorized readback. Extra display fields are tolerated and component failures stay local.

## Acceptance

Check many applications, no-index valid-page fallback, hidden/unusable applications, keyboard/mobile navigation, theme persistence/system change/storage denial, local read failures, and action receipt/recovery behavior. Run focused browser tests and furnish a disposable preview for visual inspection at desktop and mobile sizes. Do not describe fixtures as a live integration.

## Current checkpoint

Delivered as detached commit `ff3c486d` (`Add shared website navigation and themes`); the website-shell worktree is clean. The shared theme module, semantic tokens, native theme selector, published-page-only application switcher, selected-application links, responsive drawer, themed primitives, and themed example shells are implemented. The existing `system-progress.applicationCount` remains the discovered count; `navigableApplicationCount` reports the filtered user-navigation count. The exact theme exports and `system-theme-change` event contract were relayed to the coordinator and DND owner.

Verification: the three focused browser files pass 63/63; the three focused `WebInterfaceTests` pass 3/3; JavaScript module syntax checks, `start-platform-worktree.ps1 -ValidateOnly`, and `git diff --check` pass. A browser inspection covered desktop home in light and dark, the operator interface, application-local navigation, and a 390×844 drawer; it found and verified fixes for horizontal overflow and Escape/focus return. A disposable navigation/theme fixture is running at http://127.0.0.1:64907/ for coordinator inspection. It deliberately returns unavailable for unrelated API owners and is not live integration evidence. Next: coordinator integrates `ff3c486d`, repeats integration-level browser inspection, and accepts the shared contract alongside the DND consumer. Missing 01–05 dependent capabilities remain unavailable and were not simulated as passing.
