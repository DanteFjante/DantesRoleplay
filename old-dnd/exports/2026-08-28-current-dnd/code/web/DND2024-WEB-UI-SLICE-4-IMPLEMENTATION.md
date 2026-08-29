# D&D 2024 web UI Slice 4 implementation — generic game-style action controls

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 4 / D4 generic entity picker, action button, and form
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**; this slice presents existing opaque application mechanics
without implementing or changing a D&D rule.
Outcome: deliver the confirmed `<application-entity-picker>`, `<application-action-button>`, and
`<application-form>` controls in the confirmed `/components/application-workspace.js` module. They
make an accepted application action a deliberate control-card interaction, not a raw JSON form.
Exclusions: routes/services/C# changes, action-seam changes, D&D mechanic IDs or inputs, D&D rule
logic, raw JSON editing, schema authoring, role inference, browser storage, auto-execution, live
page activation, and catalog/game-state changes.
Allowed files/areas: the confirmed browser asset, focused web tests, this document/receipt, the
D&D web dependency plan, and the web roadmap.
Stop point: an author can place one generic control for an exact opaque mechanic, select only its
declared role bindings from bounded current entities, prepare it, review server-built evidence, and
explicitly execute it. Where no input schema is authored, the form can send only `{}`.

Model assignment: `gpt-5.6-terra`, high reasoning, as assigned to Order 4 after the generic
authority and transaction boundary was accepted.

## Confirmed decisions

The component IDs and module route were confirmed with the first-release component inventory. The
user's 2026-08-27 “continue” request confirms this ready D4 leaf. No new permanent ID, route,
schema, D&D mechanic ID, or visibility policy is introduced.

Slice 3 has one verified private-operator policy; these controls do not claim separate player/GM
authorization. A `schemaStatus: not-authored` descriptor cannot grow a synthetic or raw JSON form:
the form visibly reports that no ordinary entry fields are published and may submit only `{}`.
Future D&D-specific controls own rule-shaped inputs.

## D&D 5e 2024 alignment

| Concern | Existing owner | Consequence |
| --- | --- | --- |
| Mechanic inputs, eligibility, outcomes, and effects | activated application catalog mechanics | Opaque descriptor data only; no D&D branch, calculation, or input inference in browser code. |
| Role requirements and entity truth | Slice 3 descriptor and accepted entity read route | Render only server-declared roles; selection is explicit. |
| Authorization, confirmation, replay, transaction, receipt | Slice 3 action seam and existing interaction/action owners | Prepare, show exact returned evidence, then explicitly execute. |

## External implementation reference

No Foundry dnd5e review applies: this ruleset-neutral browser composition adopts no external code
or rule behavior.

## Prerequisite evidence

- [Slice 3 receipt](DND2024-WEB-UI-SLICE-3-RECEIPT.md) accepts the exact descriptor, prepare, and
  execute routes plus server-owned confirmation, replay, rollback, and receipts.
- [Slice 2D receipt](DND2024-WEB-UI-SLICE-2D-RECEIPT.md) accepts the game-table presentation
  direction without making browser presentation rule authority.
- The private application entity route is application/state-scoped and paged; it is the picker's
  only entity-list owner.

## Runtime artifacts

- Add the confirmed `application-workspace.js` asset with only the three confirmed generic elements.
- `application-entity-picker` reads at most the first 100 entities from one declared application
  and state space, exposes an explicit selection, and shows a boundary instead of following a
  cursor.
- `application-action-button` accepts exact application/state/mechanic attributes and disposable
  `roleEntityIds`/`input` properties; it loads, prepares, reviews, and explicitly executes.
- `application-form` composes declared role pickers with the action flow. It renders only a
  conservative published object-schema subset; the current absent schema displays no editable JSON
  fallback and sends `{}` only.
- No C#, endpoint, storage, catalog/schema/mechanic/procedure/effect, migration, page activation,
  or D&D-specific browser artifact is added.

## Authoritative state and closed input

Attributes establish only application ID, state-space ID, and qualified mechanic ID. The server
owns descriptor truth, state/activation scope, role requirements, entities, contracts, input
validation, authorization, proposal construction, confirmation, operation identity, effects,
results, transactions, replay, and receipts.

Prepare receives only `{ idempotencyKey, roleEntityIds, input }`. Execute receives only the exact
returned `{ resolutionReceiptId, proposalFingerprint, proposal }` plus a distinct idempotency key.
Browser keys are disposable request tokens. The browser cannot send effects, revisions, seeds,
requirements, schema hashes, authorization, confirmation truth, receipt status, or JavaScript.

## Behavior, result, and typed effects

- Components validate bounded opaque IDs, abort obsolete requests, and preserve server order.
- Entity selection is explicit; no first entity, role, container, or relationship is inferred.
- Required roles block preparation until chosen; optional roles are omitted unless chosen.
- Fields arise only from `descriptor.input.schemaJson`, never from mechanic source or D&D labels.
  Unsupported/absent schema has no free-form JSON escape hatch; absent schema means `{}`.
- Prepare uses a fresh key and displays safe summary, evidence, fingerprint, and server-built
  proposal. It never executes automatically.
- Confirm uses a distinct key and submits precisely the returned receipt/fingerprint/proposal. It
  displays only returned safe outcome/receipt values and interprets no effects.
- Composed result/error/progress events contain safe opaque IDs/status only. Browser code creates
  no typed effect or transaction.

## Failure, replay, and rollback contract

Malformed scope/properties, unavailable descriptor/entity list, incomplete roles, malformed or
unsupported schema, denied request, failed preparation, stale/tampered/replayed evidence, and
network failure create a local error state. Scope/input/role changes discard prepared evidence.
The browser performs no local retry using the same key; durable replay/conflict and rollback remain
server-owned. Rejected/stale/failed requests do not mutate browser or game state.

## Implementation sequence

1. Add source/asset assertions for the three exact IDs, bounded entity reads, descriptor/prepare/
   execute flow, separate confirmation, and absent-schema behavior.
2. Add the browser module with no D&D vocabulary.
3. Run syntax, focused web tests, build, core/local-AI suites, asset-handler check, then write the
   receipt and update Order 4 once.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Picker | Exact app/state route, server order, explicit selection, and one-page/100-item boundary. |
| Roles/input | Required role gate; no role inference; fields only from an authored schema; absent schema is `{}` without raw editor. |
| Confirmation | Separate prepare/review and explicit execute with exact returned evidence. |
| Authority | No control/MCP/conversation route, server-owned value, effect, or browser persistence. |
| Stale/tamper/replay | Scope/input/role change clears preparation; server remains authority for execution evidence. |
| Compatibility | Existing D&D/read/action seams and private route boundary remain unchanged. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/application-workspace.js`
- Focused `WebInterfaceTests` for component asset, source authority, and route boundary.
- `dotnet build DantesRoleplay.slnx --no-restore`
- Full core and local-AI suites.
- `git diff --check` and trailing-whitespace checks over Slice 4 files.

Catalog validation and the MCP protocol walk are unnecessary: no catalog, MCP operation, or
dependency registration changes.

## Completion receipt and exit gate

The [Slice 4 receipt](DND2024-WEB-UI-SLICE-4-RECEIPT.md) records focused green evidence, the
passing build/local-AI suite, and the unrelated current core-suite catalog-materialization failure.
D4/Order 4 is accepted for this browser boundary. Dice/check/save, character/inventory +/-
mutation, encounter controls, live activation, and new D&D owners remain later slices.
