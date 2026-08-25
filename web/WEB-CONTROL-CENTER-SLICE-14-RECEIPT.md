# Web control center Slice 14 receipt — changed-content preview repair

Status: **accepted**  
Date: **2026-08-24**  
Implementation: [Slice 14](WEB-CONTROL-CENTER-SLICE-14-IMPLEMENTATION.md)

## Delivered boundary

- Replaced the ambiguous Site Editor Preview action with two explicit choices: **Save & preview draft** for changed textarea content, and **Preview saved revision** for historical/existing content.
- Save & preview calls the existing append-only draft endpoint, uses the returned revision identity, then opens the already-isolated exact-revision iframe. It does not publish the draft.
- Exported the live control-center revision 2 before synchronization, imported the reviewed bundle as inactive revision 3, then explicitly activated revision 3 through the existing optimistic active-pointer endpoint.

## Evidence

- The prior live revision 2 did not contain the changed-content preview action; its exported content hash was `5E951376DF677331ABEE1C920DBEA7F9D72DCF2C85DCA958911AF00A2D9B47E0`.
- The focused web suite passed: 68 tests, including source coverage that confirms the returned draft revision is selected and previewed, normal saved-revision preview remains, and the iframe retains `sandbox="allow-scripts"`.
- The MCP host build passed with an isolated output path. A standard-output build was intentionally not used for acceptance because the running local host holds its served binaries open.
- Live page state now reports active/latest control-center revision 3. Both the exact stored revision and `/ui/control-center/index.html` contain **Save & preview draft**.
- The live exact preview returned HTTP 200 with `Cache-Control: no-store` and a CSP including `connect-src 'none'`; the iframe remains opaque and cannot use same-origin control APIs.

## Deliberate exclusions

No revision is mutated, no page is auto-published from preview, no `srcdoc` or arbitrary-unsaved-document preview exists, and no schema, route, asset, catalog, game-state, MCP, settings, assistant, or Codex behavior changed.
