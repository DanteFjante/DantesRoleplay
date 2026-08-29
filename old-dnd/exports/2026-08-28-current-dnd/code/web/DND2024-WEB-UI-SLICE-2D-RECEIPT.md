# D&D 2024 web UI Slice 2D receipt — bounded nested inventory

Status: **accepted 2026-08-27**
Implementation: [Slice 2D implementation](DND2024-WEB-UI-SLICE-2D-IMPLEMENTATION.md)
Plan: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 2 / C3
Ruleset alignment: **dnd2024-compatible**

## Delivered boundary

`<dnd2024-workspace>` now presents the selected entity's declared inventory as a read-only,
depth-first containment tree. It reuses only the accepted private direct-containment route and
existing entity/component/activated-definition reads. The selected entity remains an unrendered
root; each child is shown underneath its exact parent with its existing item facts or non-item
content card.

The browser keeps fixed limits of four containment levels, 96 visible rows in total, and the first
24 direct rows per container. It neither sends nor follows a cursor. Page, depth, entry-budget,
duplicate-identity, malformed-row, and unavailable-branch cutoffs are visible in the relevant
branch. Malformed rows consume the visible-row budget, so bad input cannot exceed the stated cap.

No route, component identity, database state, catalog record, mechanic, procedure, schema, action
control, browser storage, calculation, or write path was added. D&D custody and all item behavior
remain owned by the existing containment, ECS, catalog, and future action-mechanic owners.

## Evidence

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
  passed.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~WebInterfaceTests"` passed: **88/88**.
  This includes the actual private `/components/{name}.js` handler check for
  `dnd2024-workspace` (HTTP 200 and JavaScript content type), and asserts the fixed limits, exact
  parent query, duplicate guard, nested presentation, no inventory cursor traversal, and no write
  verbs/control routes/browser storage in the component.
- `dotnet build DantesRoleplay.slnx --no-restore` passed: **0 warnings, 0 errors**.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-build` passed: **21/21**.
- `git diff --check` passed for this checkout (only unrelated line-ending warnings were reported).

The full core command, `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build`,
is not green because of an unrelated in-progress D&D catalog change: `Dnd2024AbilityCheckTests`
fails while materializing an active mechanic Markdown record with no active JavaScript sidecar.
The failure occurs in `ActivatedApplicationCatalogMaterializer.MechanicRecord` before the web
component is loaded. Slice 2D's focused surface and build evidence are green; the catalog owner
must resolve that independent failure before a repository-wide all-green claim can be made.

## Deliberate exclusions

Inventory move/create/transfer/equip/stack/use controls, quantity steppers, burden/capacity and
currency calculations, dice/check execution, encounter mutation, live activation, and browser
invalidation remain outside this slice. The next ready web work is Order 4's generic entity
picker/action button/form over the already accepted action seam.
