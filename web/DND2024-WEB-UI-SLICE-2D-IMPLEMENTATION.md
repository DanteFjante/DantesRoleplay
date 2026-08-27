# D&D 2024 web UI Slice 2D implementation — bounded nested inventory

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 2 final remainder / C3 nested inventory
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**; this slice presents existing containment and item state
without implementing or changing D&D rules.
Outcome: complete Slice 2's read-only inventory surface by rendering an exact bounded containment
tree below the selected entity, preserving the accepted direct item cards and definition facts.
Exclusions: containment mutation, quantity steppers, equip/use/move controls, burden/capacity or
currency calculations, item rules, generic catalog browsing, new routes, schema/catalog changes,
migrations, MCP changes, action controls, browser storage, live invalidation, and page activation.
Allowed files/areas: the existing D&D browser asset and focused web tests; this implementation
document/receipt; the D&D dependency plan and web roadmap.
Stop point: the selected entity's declared contained contents render as a bounded, visibly nested,
read-only tree using only existing private application read routes. No new server behavior or
write-capable control is introduced.

Model assignment: `gpt-5.6-terra`, high reasoning, as assigned to Order 2's contained browser
composition after the generic authority boundary is frozen.

## Confirmed decisions

The user's 2026-08-27 request to “Finish slice 2” confirms the final C3 remainder. Existing direct
containment reads remain the only custody owner; no recursive route is added. The browser will use
the already accepted route once per visible container with these fixed presentation bounds:

- four containment levels beneath the selected entity;
- no more than 96 visible contained entities total; and
- no more than 24 direct children from each container's first opaque page.

Every page, depth, entry-budget, repeated-ID, malformed-row, or isolated child-read cutoff is
rendered as an explicit local inventory boundary. The browser never follows a cursor, infers a
container by item name/type, or treats a displayed tree as authority to move an item.

## D&D 5e 2024 alignment

| Concern | Existing owner | Slice consequence |
| --- | --- | --- |
| Custody hierarchy | `IStateSpaceEdgeStore` containment rows | Expand only exact rows whose current `containerEntityId` is the displayed parent. |
| Item identity/state | accepted item-instance, quantity, equipment-state, and activated definition owners | Reuse the existing card/details rendering unchanged at every visible depth. |
| Inventory limits and behavior | catalog mechanics and future action controls | Do not calculate capacity, stack, equip, transfer, or item effects in browser code. |

## External implementation reference

No Foundry dnd5e review is required: this is a generic read-only containment presentation with no
rule calculation, eligibility, transition, or item behavior. No external code or asset is adopted.

## Prerequisite evidence

- [Slice 2B receipt](DND2024-WEB-UI-SLICE-2B-RECEIPT.md) accepts the exact paged direct-containment
  route, application scope, cursor semantics, item hydration, and no-write card surface.
- [Slice 2C receipt](DND2024-WEB-UI-SLICE-2C-RECEIPT.md) accepts exact activated definition facts
  and per-card unavailable behavior.
- `IStateSpaceEdgeStore` is the authoritative containment/acyclicity owner. `ControlStructureExplorer`
  and the private application containment route already bind it to the requested application/state
  space and container.

## Runtime artifacts

- Revise only `<dnd2024-workspace>` inventory hydration/rendering to load a bounded tree by calling
  the existing direct-containment route for visible parent entities.
- Add no permanent route, custom element, storage key, API model, database artifact, catalog record,
  mechanic, procedure, schema, effect, transaction, or D&D-specific C# branch.

## Authoritative state and closed input

The selected application, state space, and entity remain established by existing workspace discovery.
For each branch the browser sends only that exact parent `containerEntityId` and fixed `limit=24` to
the existing route. It follows neither cursors nor user-supplied depth or limit. Every returned row
must name the requested parent and pass existing bounded row checks before entity/component/catalog
hydration.

The server alone owns application scope, containment row truth/revision, entity/component values,
catalog publication/definition facts, and private authorization. Browser state is disposable render
state only.

## Behavior, result, and typed effects

- The selected entity is the unrendered root; its first-level contents retain the existing “Carried
  items” presentation. Descendants render underneath their exact parent in an indented game-card
  tree, with a visible “Inside <parent>” label and depth marker.
- Each visible child reuses the exact existing item card or other-content card. A non-item entity may
  still expand if it has declared containment rows.
- The traversal is deterministic: each direct page remains server-ordered, children preserve that
  order, and recursion is depth-first. A set of seen entity IDs prevents a corrupt/repeated row from
  producing duplicate or cyclic browser presentation.
- A branch with a next cursor, depth four, exhausted 96-entry budget, or repeated ID stops locally
  and displays why. It does not fetch the next cursor or continue a hidden recursive walk.
- Child/entity/component/definition failures remain local to that card/branch. The selected actor's
  other game panels and already loaded sibling branches remain usable.
- No typed effect, transaction, calculation, or state transition exists.

## Failure, replay, and rollback contract

Unknown/wrong-scope parents are rejected by the existing server route. An unavailable root list
makes only the inventory panel unavailable. A malformed containment, child detail failure, duplicate
entity ID, depth limit, entry limit, or truncated direct page yields a visible local boundary without
writing state or mislabeling custody. Repeated reads are side-effect free; replay and rollback do not
apply.

## Implementation sequence

1. Add focused browser assertions for fixed bounds, exact parent checks, nested rendering, no
   cursor traversal, and no write/action/control path.
2. Revise the existing browser inventory loader to form the bounded tree and the renderer to show
   nested branches and cutoff states.
3. Run JavaScript syntax, focused web tests, build, core/local-AI suites, and the private component
   asset-route handler check; then write the receipt and mark Order 2 accepted.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Nested custody | Direct children and their declared descendants render beneath their exact parent in stable server order. |
| Bounds | Four depths, 96 total entries, and 24 children/page are fixed; depth, page, and total cutoffs are explicit. |
| Isolation | Each read remains current application/state/parent scoped through the accepted route. |
| Item facts | Existing exact definition facts and unavailable-state behavior remain present at nested depths. |
| Non-item containers | Non-item child entities remain visible and may expose their declared contents. |
| Partial failure | One child/branch failure does not erase loaded sibling cards or other workspace panels. |
| No authority widening | No route, write verb, effect, action control, browser storage, D&D calculation, or local containment inference is added. |
| Compatibility | Existing direct inventory, viewport, action seam, security, and full suites remain green. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- Focused `WebInterfaceTests` for nested-inventory source behavior, bounds, route reuse, and no-write
  surface.
- `dotnet build DantesRoleplay.slnx --no-restore`
- Full core and local-AI test suites.
- `git diff --check` plus trailing-whitespace checks over Slice 2D files.

Catalog validation and the MCP protocol walk are not required: this slice changes no catalog
artifact, MCP operation, or dependency registration.

## Completion receipt and exit gate

`DND2024-WEB-UI-SLICE-2D-RECEIPT.md` records the focused/browser evidence, the passing local-AI
suite, and the unrelated pre-existing core-suite catalog-materialization failure. Order 2/C3 is
accepted for this bounded web boundary; all action controls and mutations remain in later slices.
