# D&D 2024 web UI Slice 6A implementation — healing and Temporary HP controls

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 6 / E2 first bounded mutation sub-slice
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing >
Healing` (PDF p. 17), `Playing the Game > Damage and Healing > Temporary Hit Points` (PDF p. 18),
and `Playing the Game > Damage and Healing > Hit Points`.
Outcome: add tactile healing and Temporary HP controls to the selected character's Vitals panel.
Visible minus/plus steppers choose a positive amount; explicit Keep/Replace choices appear when a
Temporary HP buffer already exists; Expire is available only for a present buffer. Every action is
prepared and reviewed through the accepted generic action control before a separate confirmation,
and the workspace reloads authoritative state only after a successful receipt.
Exclusions: direct HP record/correction, damage, damage types, mitigation, attacks, zero-HP
consequences, death state, maximum-HP changes, conditions, sources/durations of healing or
Temporary HP, inventory/equipment mutation, catalog mechanics/procedures/schemas, C# routes,
state-space migration, and live database activation.
Allowed files/areas: the existing D&D workspace browser asset and focused web tests; this active
implementation document, dependency plan, roadmap, and completion receipt.
Stop point: a current playable D&D character can prepare, review, confirm, and read back healing,
Temporary HP grant/choice, and expiry from game-style controls. No browser rule calculation or
direct game-state write exists.

Model assignment: `gpt-5.6-sol`, high reasoning, as assigned to Order 6 because the surface exposes
transactional character mutations and must preserve the existing prepare/replay/rollback boundary.

## Confirmed decisions

The accepted component inventory already assigns HP, Temporary HP, and healing to the confirmed
`<dnd2024-vitals>` / `<dnd2024-action-panel>` presentation boundary, and the user has repeatedly
requested game-interface controls with minus/plus editing rather than a generic form. This slice
adds no permanent custom-element ID: it composes one internal Vitals action deck inside the
existing `<dnd2024-workspace>` and delegates writes to the already accepted
`<application-action-button>`.

`mechanic.dnd2024.hit-points.write` is deliberately not exposed. Its governing procedure says it
records or corrects a complete HP pair and explicitly is neither damage nor healing. Likewise,
damage remains coupled to the accepted damage/weapon composition owners and belongs to a later
combat slice rather than a browser-created HP decrement.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Browser consequence |
| --- | --- | --- | --- |
| Healing | A positive amount restores current HP only up to the existing maximum and does not restore Temporary HP. | `mechanic.dnd2024.healing.apply` / `procedure.mechanic.dnd2024.healing` | UI sends only one positive integer amount for the selected subject and never computes the applied or excess amount. |
| Temporary HP | Temporary HP is a separate positive buffer; a new grant does not add to an existing buffer, so the recipient chooses which buffer to retain. | `mechanic.dnd2024.temporary-hit-points.write` / its governing procedure | UI sends a first grant, or an explicit `keep|replace` choice when a buffer exists; it does not compare amounts or auto-pick. |
| Expiry | Absence is the only zero representation and a present buffer may be explicitly removed. | same owner | UI offers Expire only when current authoritative Temporary HP exists. |
| HP correction | Complete `record|correct` pairs are data correction, not ordinary healing/damage. | `mechanic.dnd2024.hit-points.write` | No direct current/max HP editor or damage shortcut appears in this slice. |

## External implementation reference

Foundry dnd5e `module/documents/actor/actor.mjs` on the `6.0.x` branch at commit
`a7aa584f7afb1a2e714391b94209eb72e04f1941` (retrieved content SHA-256
`834bb4b1dde60c8770f567f5748522c45b7d23a2fc4e668d6c50b36f2773952c`) was reviewed as an
engineering reference. Its actor owner keeps HP/Temporary HP mutation out of the sheet widget and
centralizes application behavior. No Foundry code, automatic larger-buffer rule, damage logic,
assets, or dependency is copied; the accepted local mechanics and exact SRD locators remain
authoritative.

## Prerequisite evidence

- [Slice 3 receipt](DND2024-WEB-UI-SLICE-3-RECEIPT.md) accepts the exact
  prepare/explicit-confirm/execute, stale authority, replay, transaction, and receipt owner.
- [Slice 4 receipt](DND2024-WEB-UI-SLICE-4-RECEIPT.md) accepts
  `<application-action-button>` and its closed role/input properties.
- [Slice 5 receipt](DND2024-WEB-UI-SLICE-5-RECEIPT.md) accepts the D&D game-control composition
  pattern; [Slice 5A](DND2024-WEB-UI-SLICE-5A-RECEIPT.md) preserves stale campaign readability
  while action controls stay locked.
- The three named catalog procedures were inspected. Healing accepts exactly `{amount}`;
  Temporary HP accepts exactly first `{mode:'grant',amount}`, existing
  `{mode:'grant',amount,onExisting:'keep|replace'}`, or `{mode:'expire'}`.

## Runtime artifacts

- Extend the current Vitals panel with one purpose-built action deck only when an exact playable
  state space, selected actor, valid HP component, and current action binding are available.
- Add a bounded positive-amount stepper for healing and another for Temporary HP. The controls
  display the current server-read HP/Temporary HP values, but never derive an action result.
- Mount accepted generic action buttons with fixed mechanic IDs, `{subject: selectedEntityId}`,
  and the exact closed input described above.
- Listen only for the generic composed `application-receipt` completion event. After a completed
  execution response, discard the local controls and reread the selected entity's authoritative
  state; the reread, not the event, determines whether state changed.
- Add focused source-contract tests. Add no route, server action, mechanic, schema, ID, storage,
  database synchronization, or public access change.

## Authoritative state and closed input

SQLite/application ECS remains authoritative for HP and Temporary HP. The browser reads current
components for display and submits only:

- healing: `{amount: positiveInteger}`;
- first Temporary HP grant: `{mode:'grant', amount: positiveInteger}`;
- existing-buffer choice: `{mode:'grant', amount: positiveInteger, onExisting:'keep'|'replace'}`;
  or
- expiry of a present buffer: `{mode:'expire'}`.

The browser never sends current/maximum HP, prior/resulting Temporary HP, applied/lost healing,
source references, effects, revisions, fingerprints, authorization, or confirmation truth.

## Behavior, result, and typed effects

- Healing uses a visible amount counter with minus/plus buttons bounded to 1..999 and a Prepare
  healing action. The 999 browser limit is interaction ergonomics, not a game-rule maximum.
- Temporary HP uses a separate 1..999 counter. With no current buffer, Prepare grant sends no
  `onExisting`. With a current buffer, Keep current and Replace with incoming are explicit
  mutually exclusive game buttons; neither is inferred from amount size.
- Expire appears only for a present valid buffer and prepares its own exact transition.
- The generic control renders the server proposal, requires separate confirmation, then renders
  returned narration/receipt. The mechanic owns `component.set`, `component.add`, or
  `component.remove`; browser code never constructs typed effects.
- A completed `application-receipt` schedules an exact selected-entity reload. A failed network
  prepare/execute emits no completion event; an unsuccessful returned action is still followed by
  an authoritative reread and cannot create an optimistic local mutation.

## Failure, replay, and rollback contract

Missing/stale scope, missing/invalid HP, malformed Temporary HP, unavailable mechanic descriptor,
invalid role/input, stale proposal, rejected confirmation, replay conflict, transaction failure,
and network failure remain visible through the accepted generic action control. Changing an amount
or choice replaces the control and discards prepared evidence. The server revalidates component
revisions and owns idempotency/rollback; the UI performs no optimistic HP mutation. Stale legacy
campaigns remain readable but receive no mutation deck.

## Implementation sequence

1. Add focused assertions for the exact two mechanic IDs, closed input variants, selected-subject
   binding, stepper/choice semantics, receipt-triggered refresh, and absence of direct writes or HP
   calculations.
2. Extend the existing Vitals rendering and D&D action styling without adding a new element ID or
   changing generic/server/catalog owners.
3. Run browser syntax, focused web tests, solution build, full relevant suites, and diff checks;
   record one completion receipt and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Healing | Positive amount and selected subject reach only `mechanic.dnd2024.healing.apply`; applied/lost values come from the server result. |
| First Temporary HP | Absent buffer sends grant+amount without `onExisting`. |
| Existing Temporary HP | Keep/Replace is explicit, no amount comparison or automatic choice exists, and Expire is separately reviewable. |
| Confirmation | Every mutation uses prepare, review, and separate confirm/execute through the generic action owner. |
| Refresh | A completed execution receipt triggers an authoritative selected-entity reread; no optimistic local state is committed. |
| Stale/invalid | Stale bindings and invalid component state expose no mutation deck and perform no write. |
| Authority | No browser D&D formula, effect, direct POST, source reference, or raw HP correction exists. |
| Compatibility | Existing campaign selection, read-only panels, dice/check/save controls, and generic action contracts remain green. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~WebInterfaceTests" --no-restore`
- `dotnet build DantesRoleplay.slnx --no-restore`
- Relevant full core/local-AI suites and `git diff --check` at acceptance.

No catalog validation or MCP protocol walk is required because this slice changes neither catalog
records nor MCP operations/dependency registration.

## Completion receipt and exit gate

Write `DND2024-WEB-UI-SLICE-6A-RECEIPT.md`, update Order 6/E2 to show only this bounded sub-slice
implemented, and stop for acceptance. Damage/mitigation/attack composition, raw HP correction,
inventory/equipment mutation, encounter controls, accessibility completion, migration, and live
activation remain later slices.
