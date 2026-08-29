# D&D 2024 web UI Slice 6B implementation — direct-item equip and unequip controls

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 6 / E4 first bounded inventory-mutation sub-slice
Ruleset alignment: **dnd2024-compatible**
Source ID and locators: `source.dnd2024.srd-5.2.1`; the selected item's accepted immutable
`dnd2024.item-definition.sourceRef`, including `Equipment > Weapons` or
`Equipment > Armor > Armor table (PDF p. 92)` as applicable. The UI adds no rule meaning.
Outcome: add contextual game buttons to exact directly carried, non-stack item cards. A currently
unequipped eligible item offers only its definition-declared Held/Worn modes; a held or worn item
offers Unequip. Each action uses the accepted review-first application seam and refreshes the exact
inventory after the returned execution receipt.
Exclusions: item create/move/transfer, destination selection, custody or slot changes, stack
create/record/split/merge/consume, quantity steppers, item activities, burden/capacity/currency
derivation, hand/slot counts, AC/attack changes, don/doff timing or action cost, catalog changes,
server routes, state migration, and live database activation.
Allowed files/areas: the existing D&D workspace browser asset and focused web tests; this active
implementation document, dependency plan, roadmap, and completion receipt.
Stop point: an exact eligible direct item can prepare, review, confirm, and read back equip or
unequip from its card. Nested items, stacks, invalid/unknown definitions, corrupt equipment state,
and stale campaign bindings expose no mutation control.

Model assignment: `gpt-5.6-sol`, high reasoning, as assigned to Order 6 because equipment state is
transactional and depends on exact definition references, direct custody, stale-state validation,
replay, and rollback.

## Confirmed decisions

The accepted component inventory assigns equipment slots and contextual equip controls to the
existing D&D inventory surface. The user has asked to continue the next Order 6 slice and prefers
game-interface controls over generic forms. No permanent custom-element ID is added: the controls
compose directly into existing item cards and delegate to `<application-action-button>`.

This slice intentionally stops at equip/unequip. Transfer requires explicit source/destination and
capacity ownership; stack changes can create/delete entities; item activity has descriptor-owned
consumption/grants. Those are separate transaction boundaries and will be designed as Slice 6C or
later rather than inferred from a card button.

## D&D 5e 2024 alignment

| Concern | Accepted owner | Browser consequence |
| --- | --- | --- |
| Eligibility | `dnd2024.item-definition.equipmentModes` plus `mechanic.dnd2024.item.equip` | Render only exact declared `held`/`worn` mode choices; never infer from item kind, name, weapon, or armor data. |
| Direct possession | containment plus equip/unequip role contract | Controls appear only at inventory depth 1 when the exact containment names the selected actor as container; server revalidates custody. |
| Non-stack item | absence of `dnd2024.item-quantity` plus equip owner | Stacks and corrupt quantity state receive no equipment controls; no browser conversion occurs. |
| Current equipment state | `dnd2024.equipment-state` | Missing/`unequipped` offers permitted equip modes; `held`/`worn` offers Unequip; corrupt state fails closed. |
| Separate consequences | procedure constraint | UI does not calculate AC, attack readiness, hands, slots, timing, or action-resource cost. |

## External implementation reference

No external implementation reference is required for this slice. The browser performs no D&D
calculation or new eligibility interpretation; it projects the accepted local definition modes,
containment, state, and mechanic contracts exactly. No external code or asset is adopted.

## Prerequisite evidence

- [Slice 2B receipt](DND2024-WEB-UI-SLICE-2B-RECEIPT.md) accepts exact direct custody cards and
  stored item-instance/quantity/equipment presentation; [Slice 2C](DND2024-WEB-UI-SLICE-2C-RECEIPT.md)
  accepts activated immutable item definitions and source provenance.
- [Slice 3](DND2024-WEB-UI-SLICE-3-RECEIPT.md) and [Slice 4](DND2024-WEB-UI-SLICE-4-RECEIPT.md)
  accept the prepare/confirm/execute/replay/rollback owner and generic application action button.
- `mechanic.dnd2024.item.equip` accepts exactly `{state:'held'|'worn'}` with `item` and `holder`
  roles, requires direct custody, rejects stacks/already-equipped items, resolves the immutable
  definition reference, and applies only a complete equipment-state add/set.
- `mechanic.dnd2024.item.unequip` accepts exactly `{}` with the same roles, requires direct
  custody plus current `held|worn`, and sets exactly `{state:'unequipped'}`.

## Runtime artifacts

- Extend direct inventory cards with a contextual equipment action area only when all browser-read
  eligibility evidence is valid and current action binding is available.
- For missing/unequipped state, show definition-declared mode buttons and mount one equip action for
  the selected mode. For held/worn state, mount one Unequip action and no equip mode chooser.
- Bind the exact contained entity as `item` and selected actor as `holder`; pass only the closed
  input owned by the chosen mechanic.
- Reuse the generic composed `application-receipt` event to reread the selected entity and complete
  inventory tree. Do not mutate the card badge optimistically.
- Add focused source-contract tests. Add no server/catalog/schema/route/storage/database artifact.

## Authoritative state and closed input

SQLite/application ECS remains authoritative. The browser uses exact current read projections and
submits only:

- equip: roles `{item: containedEntityId, holder: selectedEntityId}` and
  `{state:'held'|'worn'}` selected from the current definition's `equipmentModes`; or
- unequip: the same roles and `{}`.

The browser never submits definition identity/content, custody/slot/revision, current equipment
state, quantity, derived eligibility, effects, authorization, fingerprint, or confirmation truth.

## Behavior, result, and typed effects

- A direct separate item with one permitted mode shows one clear **Hold** or **Wear** choice. A
  two-mode definition shows both as mutually exclusive tactile buttons, then one Prepare action.
- A valid current `held` or `worn` item shows its badge plus one Prepare Unequip action.
- Nested items, stacks, invalid item instances, missing/unavailable definitions, definitions with
  invalid/no equipment modes, malformed custody, and corrupt equipment state remain readable where
  possible but expose no equipment action.
- Every action uses the generic review card and separate confirm/execute. Catalog mechanics own the
  `component.add|set` effect and server-side direct-custody/definition/current-state checks.
- A completed execution receipt reloads exact character and inventory state. A rejected/stale action
  leaves the current card unchanged until an authoritative read says otherwise.

## Failure, replay, and rollback contract

Missing/stale scope, changed custody, changed definition activation, changed/current equipment
state, stack appearance, invalid mode, wrong item/holder, stale proposal, replay conflict,
transaction failure, and network failure remain generic action errors. Rerendering or choosing a
different mode replaces the action control and discards prepared evidence. The server owns
idempotency and rollback; the browser never performs an optimistic badge or component change.

## Implementation sequence

1. Add focused assertions for exact mechanic IDs, role bindings, closed equip/unequip inputs,
   direct-depth/containment/quantity/definition/state gates, receipt refresh, and no direct write.
2. Add contextual game controls to existing item cards and style them without adding a custom
   element ID or changing server/catalog owners.
3. Run syntax, focused web tests, build, relevant catalog-owner tests where the concurrent catalog
   permits, local-AI regression, diff checks, and record one receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Equip | Exact direct non-stack item and selected holder reach only `mechanic.dnd2024.item.equip` with one definition-declared mode. |
| Unequip | Exact currently held/worn direct item reaches only `mechanic.dnd2024.item.unequip` with empty input. |
| Eligibility | Nested, stacked, corrupt, unavailable-definition, no-mode, wrong-custody, and stale-binding cases have no action control. |
| Confirmation | Every mutation uses prepare, review, separate confirm/execute, and returned receipt. |
| Refresh | Completed execution rereads authoritative inventory; no optimistic state mutation exists. |
| Rule authority | Browser calculates no equipment eligibility beyond exact accepted fields and no AC/attack/hand/slot/time/action consequence. |
| Compatibility | Existing nested read tree, item facts, Vitals controls, dice/check/save actions, and generic seam stay green. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~WebInterfaceTests" --no-restore`
- Focused existing D&D equipment/transfer owner test when the concurrent catalog fixture validates.
- `dotnet build DantesRoleplay.slnx --no-restore`
- Local-AI regression and `git diff --check` at completion.

No catalog validation or MCP protocol walk is required because this slice changes neither catalog
records nor MCP operations/dependency registration.

## Completion receipt and exit gate

Write `DND2024-WEB-UI-SLICE-6B-RECEIPT.md`, update Order 6/E4 to show only equip/unequip
implemented, and stop for acceptance. Transfer/move/destination selection, stack and quantity
mutations, item activity, damage/combat, encounter controls, migration, and live activation remain
later slices.
