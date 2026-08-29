# D&D 2024 web UI Slice 2B implementation — direct carried inventory

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 2 partial delivery, C3 inventory foundation
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**; this slice presents accepted runtime identity, quantity,
equipment-state, and containment records without implementing or changing a D&D rule.
Outcome: add one bounded application-scoped direct-containment read and render the selected entity's
direct carried contents as game-styled inventory cards in `<dnd2024-workspace>`.
Exclusions: recursive/nested inventory traversal, immutable catalog item descriptions and statistics,
burden/capacity/currency calculations, item mutation, quantity steppers, equip/use/move controls,
action descriptors, rule execution, catalog changes, migrations, MCP changes, and live activation.
Allowed files/areas: the generic state-space edge read contract/store, web structure explorer,
application web endpoint and remote-path policy, the existing D&D browser asset, focused edge/web
tests, this document/receipt, the D&D web dependency plan, and the web roadmap.
Stop point: at most one bounded page of direct contents beneath the selected entity renders from
exact containment and component state. Deeper contents and static item facts remain explicitly
unavailable; no control prepares or commits a write.

## Confirmed decisions

The user's 2026-08-27 instruction to continue follows the accepted Slice 2A receipt, whose stated
next boundary is a safe containment/catalog foundation. It confirms this smaller application read
surface and partial inventory UI. The new permanent route is:

`GET /api/applications/{applicationId}/state-spaces/{stateSpaceId}/containments`

The route requires a bounded `containerEntityId`, supports the existing opaque `cursor` and
`limit` query pattern, and remains behind the existing private read security/rate-limit boundary.
It returns direct containment records only. It cannot list another application's state space,
request recursive traversal, or write an edge.

The previously confirmed `<dnd2024-inventory>` responsibility is composed inside the existing
`<dnd2024-workspace>` in this slice; no additional custom-element ID is created. The current public
catalog contains actions/procedures/queries but not immutable item entity records, so this slice
does not label runtime items with invented catalog facts.

## D&D 5e 2024 alignment

| Concern | Accepted owner | Slice consequence |
| --- | --- | --- |
| Item identity | `dnd2024.item-instance` | Display the exact stored `definitionId`; do not infer a definition from the entity name. |
| Stack quantity | `dnd2024.item-quantity` | Display a positive stored count only when the component is valid; do not assume one for missing/corrupt state. |
| Equipment state | `dnd2024.equipment-state` | Display only `held`, `worn`, or `unequipped` from stored state. |
| Custody | `IStateSpaceEdgeStore` containment | A direct item appears only when an exact containment row names the selected entity as its container. Component presence or names never imply custody. |
| Static item facts | explicitly published application catalog | Do not show kind, mass, capacity, weapon/armor facts, or source text until immutable content records have a public projection. |
| Nested contents | accepted inventory mechanic supports bounded materialization, but no browser action seam exists | Mark this slice as direct contents only; do not imitate mechanic traversal or silently omit that boundary. |

## External implementation reference

No Foundry dnd5e review is required because this slice introduces no D&D calculation, eligibility,
transition, or rule interpretation. No external code or asset is adopted.

## Prerequisite evidence

- [Slice 1 receipt](DND2024-WEB-UI-SLICE-1-RECEIPT.md) accepts the exact private application-state
  route pattern, opaque pagination, security boundary, and browser asset host.
- [Slice 2A receipt](DND2024-WEB-UI-SLICE-2A-RECEIPT.md) accepts the current game viewport and records
  containment as custody authority while excluding inferred inventory.
- `IStateSpaceEdgeStore` owns containment rows, revisions, state-space isolation, and acyclicity.
- The accepted item-instance, item-quantity, and equipment-state schemas close the values that the
  browser may display.

## Runtime artifacts

- Add a ruleset-neutral paged direct-containment read to `IStateSpaceEdgeStore` and its SQLite
  implementation. Preserve the existing full-list method used by current mechanics.
- Extend `ControlStructureExplorer` with an application-bound direct-containment projection and
  opaque scope-bound cursor.
- Add the one exact private GET route and extend remote-path matching for that exact shape only.
- Extend the existing D&D module with item-instance, item-quantity, and equipment-state hydration
  for the first 24 direct contents and a game-card inventory panel.
- Add no database schema, catalog record, D&D C# literal, action route, or write verb.

## Authoritative state and closed input

The route path supplies exact application and state-space IDs. `containerEntityId` is required and
bounded to 200 non-control characters; `limit` is 1 through 100; `cursor` is the existing opaque,
scope-bound web cursor. The server verifies application registration, state-space ownership, and
the current container entity before reading edges. Callers cannot supply containment rows,
revisions, component values, catalog facts, depth, derived quantities, or authorization truth.

The browser requests `limit=24`, reads at most that page, and clearly reports when more direct
contents exist. For each returned contained entity it reads only accepted application entity and
component routes and the three inventory component IDs. The page stores only disposable render
state.

## Behavior, result, and typed effects

- Direct containments are ordered by exact contained entity ID and return containment revision,
  slot, and timestamps unchanged.
- A valid item-instance becomes an inventory card with current entity name/ID, exact definition ID,
  direct slot, optional positive quantity, optional valid equipment-state badge, and containment
  revision.
- A direct contained entity without a valid item-instance appears in a separate “Other contents”
  group so custody evidence is not erased or misrepresented as an item.
- Missing quantity means “Individual” only for presentation; it does not synthesize a quantity
  component. Invalid quantity/equipment/item JSON is visibly unavailable.
- Empty direct containment is distinct from unavailable containment. A next cursor produces an
  explicit “more direct contents exist” notice rather than an unbounded browser walk.
- No typed effect, transaction, calculation, or state transition exists.

## Failure, replay, and rollback contract

Malformed IDs/limits/cursors fail without a write. Unknown application/state space/container and
wrong-application state spaces fail closed. A cursor copied between containers, state spaces, or
page sizes is stale. Missing/deleted child entities or failed component reads isolate the affected
inventory card/panel and do not erase valid character panels. Repeated reads are side-effect free;
replay and rollback are not applicable.

## Implementation sequence

1. Add and test the bounded direct-containment store read.
2. Add and test the application-bound projection, exact GET route, security/rate-limit metadata,
   and remote-path closure.
3. Extend the D&D workspace hydration and render direct inventory cards with explicit boundaries.
4. Run focused/full verification, record a Slice 2B receipt, update Order 2 status once, and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive custody | Only direct rows for the selected container return, in stable order, with exact revisions/slots. |
| Pagination | Limit and cursor page direct contents without loading all state-space edges; cursor scope is bound. |
| Scope isolation | Unknown/wrong-application state spaces and unknown containers fail closed. |
| Item presentation | Valid item identity, quantity, equipment state, entity identity, slot, and revision render as cards. |
| Unclassified contents | Direct non-item entities remain visible and are not mislabeled. |
| Partial failure | Invalid/missing components or child reads stay local to inventory; other game panels remain usable. |
| Honest boundary | The panel says direct contents, exposes truncation, and shows no inferred catalog facts or recursive result. |
| No write authority | Route inventory and browser source contain no new POST/PUT/DELETE/action/control/MCP path. |
| Compatibility | Existing edge mechanics, application routes, game viewport, and full suites remain green. |

## Verification commands

- Focused state-space edge tests for filtering, paging, cursor validation, and no-change reads.
- Focused `WebInterfaceTests` for exact route inventory, application isolation, security/rate limit,
  remote-path closure, browser inventory vocabulary/presentation, and no-write assertions.
- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- `dotnet build DantesRoleplay.slnx --no-restore`
- Full core and local-AI test suites.
- `git diff --check` plus trailing-whitespace checks over Slice 2B files.

Catalog validation and the MCP protocol walk are not required because this slice changes no catalog
artifact, active source, MCP surface, or dependency registration.

## Completion receipt and exit gate

[The Slice 2B receipt](DND2024-WEB-UI-SLICE-2B-RECEIPT.md) records passing evidence and marks the
direct-inventory portion of Order 2 accepted. The implementation stops here. Activated immutable
item browsing, nested inventory, catalog facts, +/- quantity controls, item actions, action
execution, page association, and live activation remain outside this slice.
