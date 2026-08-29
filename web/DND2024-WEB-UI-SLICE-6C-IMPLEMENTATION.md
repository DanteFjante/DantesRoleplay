# D&D 2024 web UI Slice 6C implementation — ordinary inventory actions

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 6 / E4 final player-facing inventory sub-slice
Ruleset alignment: **dnd2024-compatible**
Source ID and locators: `source.dnd2024.srd-5.2.1`; exact immutable item-definition source
references already presented on each card. This UI adds no item rule, capacity formula, or
activity effect.
Outcome: finish Order 6 with contextual ordinary whole-item transfer, fungible-stack
consume/split/merge, and immutable descriptor-authored item-use controls. All actions remain
review-first and refresh exact authoritative inventory after execution.
Exclusions: explicitly administrative `item-instance.move`, `item-instance.create-and-place`,
`item-stack.create-and-place`, record/correction mechanics, arbitrary item/loot generation,
unpublished activities, commerce, burden/capacity calculations, automatic merge, partial transfer,
catalog/server/schema/route changes, migration, and live database activation.
Allowed files/areas: existing D&D workspace browser asset and focused web tests; this active
implementation document, dependency plan, roadmap, and completion receipt.
Stop point: supported ordinary inventory transitions available in the current catalog have
purpose-built card controls; Order 6 is complete without exposing administrative helpers.

Model assignment: `gpt-5.6-sol`, high reasoning, as assigned to Order 6 because transfer, stack
conservation/entity deletion and creation, descriptor grants, replay, and rollback cross several
typed-effect transaction boundaries.

## Confirmed decisions

The user's instruction to finish Slice 6 accepts Slice 6B and authorizes the remaining ordinary
inventory controls. Existing item cards and the confirmed D&D inventory responsibility are reused;
no new custom-element or route ID is introduced.

Administrative create/place and move mechanics are intentionally not game buttons: their catalog
descriptions say they bypass ordinary admission or bootstrap state, and their governing procedure
directs normal custody changes to `mechanic.dnd2024.item.transfer`. They remain available to
reviewed maintenance/bootstrap workflows outside this player interface.

## D&D 5e 2024 alignment

| Concern | Accepted owner | Browser consequence |
| --- | --- | --- |
| Whole-item custody | `mechanic.dnd2024.item.transfer` | Select exact source/destination/slot; browser does not calculate capacity, kind admission, weight, or cycle eligibility. |
| Stack consumption | `mechanic.dnd2024.item-stack.consume` | Positive stepper is bounded by displayed count for ergonomics; mechanic owns decrement or zero-as-entity-deletion. |
| Stack split | `mechanic.dnd2024.item-stack.split` | User supplies a smaller positive count/name; browser generates one disposable valid runtime entity ID; mechanic owns conservation and atomic creation. |
| Stack merge | `mechanic.dnd2024.item-stack.merge` | Offer only visible same-container/same-definition stack candidates; mechanic revalidates compatibility and atomically deletes the source. |
| Item activity | `mechanic.dnd2024.item-activity.use` | Offer only immutable published descriptors and send chosen activity plus generated grant ID; definition owns cost, grant definition/name/slot, and effects. |

## External implementation reference

No external implementation reference is required. This slice does not translate a D&D formula or
adopt external behavior; it projects accepted local state/mechanic contracts and delegates every
outcome to their catalog JavaScript owners.

## Prerequisite evidence

- [Slices 2B–2D](DND2024-WEB-UI-SLICE-2D-RECEIPT.md) accept exact bounded nested custody and item
  cards; Slice 2C accepts activated immutable item facts/provenance.
- [Slices 3–4](DND2024-WEB-UI-SLICE-4-RECEIPT.md) accept application actions, explicit confirmation,
  current revision revalidation, replay, rollback, and generic controls.
- [Slice 6B](DND2024-WEB-UI-SLICE-6B-RECEIPT.md) accepts contextual inventory-card actions and
  exact item/holder role binding.
- The transfer, stack consume/split/merge, and item-activity mechanics/procedures were inspected in
  full. Their exact roles, closed inputs, typed effects, conservation/admission rules, and failure
  behavior remain unchanged.

## Runtime artifacts

- Extend activated definition hydration with optional `dnd2024.item-activity` data already present
  in the public catalog record; validate it before displaying any action.
- Add a transfer palette to valid unequipped items. Destinations are bounded to campaign actors and
  visible inventory containers with recorded capacity; source/current destination/self/descendant
  candidates are omitted as presentation hygiene while the server remains authoritative.
- Add stack palettes only for valid fungible stacks with a known empty child set: Consume count,
  Split count/name/new runtime ID, and explicit same-container Merge target.
- Add item-use choices only for valid published consume-and-grant descriptors with sufficient
  displayed quantity; browser supplies a generated grant entity ID and no descriptor-owned field.
- Generalize the existing inventory action mount helper to exact caller-supplied role maps while
  retaining the equip/unequip behavior.
- Add focused source-contract tests. Add no server/catalog/schema/route/storage/database artifact.

## Authoritative state and closed input

SQLite/application ECS remains authoritative. Browser source input is limited to:

- transfer: roles `{item,source,destination}` and `{slot}`;
- consume: roles `{item,definition}` and `{count}`;
- split: roles `{source,definition}` and `{count,itemId,name}`;
- merge: roles `{source,target,definition}` and `{}`; and
- use: roles `{item,definition,grantDefinition}` and `{activityId,grantItemId}`.

Generated IDs are disposable lower-case `item.web.*` runtime entity identities produced once per
visible prepared creation action and regenerated after an execution receipt. The browser never
sends quantity results, definition/activity bodies, mass/capacity totals, custody revisions, grant
name/slot, effects, authorization, proposal fingerprints, or confirmation truth.

## Behavior, result, and typed effects

- Transfer uses a destination selector and short editable slot field. Equipped items must first use
  Slice 6B Unequip. Server admission/cycle/capacity errors remain review/action failures.
- Consume and Split use tactile bounded steppers. Consuming the displayed full count visibly warns
  that the server action may remove the stack. Split requires a conventional labelled name field.
- Merge always names an explicit visible target and warns that the current source stack is removed.
- Use shows the immutable activity's fixed consume count and grant name but sends only activity ID
  plus generated grant ID.
- All controls use prepare/review/separate-confirm/execute. On completion, local action state and
  generated IDs are discarded and the selected inventory is reread. No optimistic mutation exists.

## Failure, replay, and rollback contract

Changed custody, equipment, quantity, contents, target, definition/activity activation, capacity,
generated-ID conflict, insufficient quantity, invalid slot/name, stale proposal, replay conflict,
transaction failure, and network failure remain server/generic action errors. Changing any local
selection replaces the corresponding action control and discards prepared evidence. Atomic effects
and rollback remain owned by the action transaction; browser code never conserves or edits count.

## Implementation sequence

1. Add source assertions for all exact ordinary mechanic IDs, role/input shapes, bounded candidate
   gates, generated-ID lifecycle, receipt refresh, and absence of administrative helpers/effects.
2. Extend definition hydration, inventory-card game palettes, and generalized action mounting.
3. Run syntax, focused web and existing transfer/stack/activity owner tests, build, local-AI/full
   regression where stable, live asset/browser smoke, diff checks, and write the completion receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Transfer | Whole unequipped item sends exact item/source/destination/slot; admission stays server-owned. |
| Consume | Positive count reaches only consume; full count is explicit and deletion remains mechanic-owned. |
| Split | Smaller count, user-visible name, and fresh runtime ID reach only split; browser computes no resulting count. |
| Merge | Explicit visible compatible target reaches only merge; source deletion remains mechanic-owned. |
| Activity | Exact immutable descriptor selection sends only activity ID and fresh grant ID. |
| Fail closed | Equipped, corrupt, unknown-contents, invalid-definition/activity, no-destination/target, stale-binding cases expose no unsafe action. |
| Authority | No administrative helper, effect construction, capacity/weight arithmetic, direct write, optimistic mutation, or browser persistence exists. |
| Compatibility | Nested reads, equipment, Vitals, stateless actions, generic seam, and existing owner tests remain green. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- Focused `WebInterfaceTests`.
- Existing D&D transfer/capacity, stack lifecycle, and item-activity owner tests.
- `dotnet build DantesRoleplay.slnx --no-restore` and Local-AI/full core regression where stable.
- Live HTTP/browser read-back and `git diff --check`.

No catalog validation or MCP protocol walk is required because this slice changes neither catalog
records nor MCP operations/dependency registration.

## Completion receipt and exit gate

The [Slice 6C receipt](DND2024-WEB-UI-SLICE-6C-RECEIPT.md) records the completed implementation
and verification. Mark Order 6 accepted only after user confirmation. Order 7 encounter/combat
controls is next; administrative inventory bootstrap/correction remains outside the game UI.
