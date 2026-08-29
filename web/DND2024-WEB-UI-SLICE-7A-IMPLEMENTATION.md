# D&D 2024 web UI Slice 7A implementation — recorded encounter turns and resources

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 7A / E3 first encounter sub-slice
Ruleset alignment: **dnd2024-compatible**
Source ID and locators: `source.dnd2024.srd-5.2.1`; `Playing the Game > Combat > The Order of
Combat`, its `Initiative` subsection, and `Playing the Game > Actions; Bonus Actions; Reactions;
Interacting with Objects; Combat > Your Turn`.
Outcome: select an exact recorded order-bearing encounter whose participants belong to the selected
campaign, present its ordered combatants/current turn as a game tracker, start/advance/end turns,
and spend the selected participant's current turn-budget resources through reviewed controls.
Exclusions: initial encounter creation/registration, roster mutation, Initiative rolling/order
formation/tie resolution, attacks, damage, target/weapon choice, movement position/routes, Conditions,
victory/reset/restart, catalog/server/schema/route/storage/database changes, and live migration.
Allowed files/areas: existing D&D workspace browser asset and focused web tests; this document,
dependency plan, roadmap, and completion receipt.
Stop point: recorded encounter lifecycle and resource spending work through exact current mechanics;
Order 7B owns safe initial order formation and Order 7C owns weapon attack/damage.

Model assignment: `gpt-5.6-sol`, xhigh reasoning, as assigned to Order 7 because encounter
composition, active/off-turn eligibility, current-revision revalidation, and two-entity writes are
cross-owner concurrency boundaries.

## Confirmed decisions

The user's 2026-08-27 request to continue with Slice 7 accepts Order 6 and activates the next
encounter leaf. Order 0 already confirmed permanent `<dnd2024-encounter-tracker>` and
`<dnd2024-turn-budget>` identities. This slice implements those confirmed elements without adding
a route, schema, mechanic, component, relationship kind, or other permanent identity.

Order 7 is split because initial order formation and weapon damage have different authority gaps and
root transactions. Before an Initiative snapshot, the catalog has no encounter marker/registration
owner; moreover, `encounter-initiative-order` accepts only actual-tie decisions in the same composed
request that rolls the counts. The browser cannot safely identify a pre-order encounter or predict
that child result. Slice 7A therefore acts only on exact already-recorded Initiative snapshots and
does not invent an encounter heuristic or tie protocol.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Initiative order | A stored descending combat order determines turns. | `dnd2024.encounter-initiative-order` plus `mechanic.dnd2024.encounter-initiative-order` | Display the immutable snapshot; do not sort, reroll, or replace it in browser code. |
| Turn lifecycle | Start at round 1/index 0; advance one participant and wrap rounds; end preserves final position. | `mechanic.dnd2024.encounter-turn.start|advance|end` | Offer only the action matching exact stored lifecycle state and submit `{}`. |
| Turn restoration | The newly active participant regains its declared budget and movement from authoritative Speed/Exhaustion. | start/advance composition over `turn-budget.read` and `speed.read` | Browser refreshes receipts; it never resets tokens or calculates movement. |
| Resource spending | Action, Bonus Action, interaction and movement require active turn; Reaction can be spent by any encounter participant. Conditions can prohibit use. | `mechanic.dnd2024.turn-budget.spend` | Bind exact subject/encounter roles; present only current plausible choices while server revalidates membership, active turn, Conditions, and availability. |

## External implementation reference

Reviewed Foundry dnd5e 6.0.x `module/applications/combat/combat-tracker.mjs`,
`module/documents/combat.mjs`, and `module/documents/combatant.mjs`. Useful presentation/data-flow
evidence: the tracker visibly marks the current combatant; combat start/turn changes recover only
the relevant combatant's uses; initiative remains combatant-bound rather than actor-persistent.
This slice adopts the strong current-turn treatment and receipt-driven refresh only. It does not
adopt Foundry's group-Initiative setting, defeated-combatant skipping, recovery vocabulary, data
model, templates, or code. Foundry is MIT-licensed engineering reference; no source is copied.

## Prerequisite evidence

- [Slice 2A receipt](DND2024-WEB-UI-SLICE-2A-RECEIPT.md) accepts read-only Initiative/turn cards and
  explicit absent/unavailable state.
- [Slice 4 receipt](DND2024-WEB-UI-SLICE-4-RECEIPT.md) accepts exact role/input action controls,
  review, separate confirmation, receipts, replay, and stale-state handling.
- [Slice 6C receipt](DND2024-WEB-UI-SLICE-6C-RECEIPT.md) accepts contextual game controls and
  authoritative reread after writes.
- Current encounter lifecycle and turn-budget procedures, mechanics, schemas, and focused owner
  tests were read in full. No catalog meaning changes in this slice.

## Runtime artifacts

- Add a top-level encounter selector populated only from current application entities with a valid
  immutable Initiative snapshot whose participant IDs all belong to the selected campaign actor set.
- Load the selected encounter entity plus its current Initiative and optional turn-state revisions
  independently from the selected actor.
- Define the already-confirmed `<dnd2024-encounter-tracker>` game component. It renders ordered
  participant cards, an emphatic current-turn marker, round/status, and one contextual reviewed
  Start/Next/End control.
- Define the already-confirmed `<dnd2024-turn-budget>` game component. Available resource tokens are
  tactile controls; movement uses a 5-foot bounded stepper. The action button receives only exact
  subject/encounter roles and closed resource input.
- Refresh both encounter and selected actor after a completion receipt. Add focused source-contract
  tests. Add no backend or catalog artifact.

## Authoritative state and closed input

Application ECS remains authoritative. Browser action source input is limited to:

- lifecycle: role `{encounter}` and exactly `{}`; and
- resource spend: roles `{subject,encounter}` with `{resource}` for Boolean resources or
  `{resource:'movement',feet}` for a positive 5-foot multiple.

The browser never supplies Initiative counts/order, roster, active participant, round, index,
restoration values, Speed, Exhaustion, Condition permissions, resulting budget, effects, revisions,
authorization, proposal fingerprints, or confirmation truth.

## Behavior, result, and typed effects

- Encounter discovery fails closed on missing/corrupt snapshots, unknown participants, cross-campaign
  participants, and over-bound lists. No candidate is inferred from ID or name.
- Missing turn state exposes Start. Active state exposes Next Turn and a separately warned End
  Encounter action. Ended state is historical/read-only.
- The active ordered participant is highlighted with `aria-current`. Participant labels come only
  from the selected campaign actor set and fall back to exact IDs.
- Ready Action, Bonus Action, Reaction, and Interaction values display as tactile tokens. Only the
  active selected participant gets ordinary spend controls; any selected roster participant may
  prepare a ready Reaction. Movement presents 5-foot increments up to displayed remaining movement.
- Every action uses prepare/review/separate-confirm/execute. Completion rereads authoritative actor
  and encounter state; no token, turn, round, or component changes optimistically.

## Failure, replay, and rollback contract

Wrong encounter/participant scope, stale roster/order/turn/budget, ended or missing lifecycle,
unavailable resource, off-turn ordinary spend, Condition prohibition, invalid movement, corrupt
Speed/Exhaustion, stale proposal, replay conflict, injected transaction failure, authorization, and
network errors remain generic action failures with no browser mutation. Selection changes discard
prepared controls. Server transactions own all lifecycle/budget typed effects and rollback.

## Implementation sequence

1. Add focused assertions for the two confirmed custom elements, encounter selector/discovery gates,
   exact lifecycle/spend mechanic IDs and inputs, active/off-turn presentation gates, receipt refresh,
   and absence of Initiative/attack/damage/admin/direct-write behavior.
2. Add exact encounter loading, game tracker, lifecycle controls, and turn-resource controls to the
   existing browser asset.
3. Run syntax, focused web and existing lifecycle/spender owner tests, build, supporting/full
   regression where stable, live HTTP/browser smoke, diff checks, and write the completion receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Discovery | Only valid order-bearing entities fully scoped to selected campaign actors appear. |
| Start | Snapshot without turn state submits `{encounter}` and `{}` only. |
| Advance | Active snapshot submits exact encounter and refreshes changed turn/participant budget. |
| End | Active snapshot offers a warned separate End action; ended snapshot offers no mutation. |
| Spend | Exact selected subject/encounter roles and only closed resource input reach the mechanic. |
| Off turn | Ordinary resources/movement stay unavailable; a ready Reaction remains possible for a roster member. |
| Fail closed | Missing/corrupt/cross-campaign/order drift/unknown budget values expose no unsafe control. |
| Authority | No Initiative sorting/roll, resource reset, Speed/Exhaustion math, Condition rule, effect construction, direct write, or persistence exists in the browser. |
| Compatibility | Campaign/actor selection, read panels, stateless actions, Vitals, and inventory controls remain green. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- Focused `WebInterfaceTests`.
- Existing `Fresh_host_encounter_composes_initiative_and_transacts_the_turn_lifecycle`,
  `Turn_lifecycle_restores_only_the_new_participant_and_applies_exhaustion_reduction`, and
  `Turn_budget_spender_enforces_active_turn_off_turn_reaction_and_condition_prohibitions` tests.
- `dotnet build DantesRoleplay.slnx --no-restore` plus Local-AI/full core regression where stable.
- Live HTTP/browser read-back and focused `git diff --check`.

No catalog validation or MCP protocol walk is required because this slice changes neither catalog
records nor MCP operations/dependency registration.

## Completion receipt and exit gate

The [Slice 7A receipt](DND2024-WEB-UI-SLICE-7A-RECEIPT.md) records completed implementation,
verification, and the user's 2026-08-27 continuation as acceptance. Do not implement initial
Initiative order or weapon attack/damage in this sub-slice.
