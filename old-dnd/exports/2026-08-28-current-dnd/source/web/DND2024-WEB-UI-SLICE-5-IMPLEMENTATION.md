# D&D 2024 web UI Slice 5 implementation — dice, ability-check, and saving-throw controls

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 5 / E1 stateless D&D actions
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Playing the Game > D20 Tests > Ability
Checks > Ability Modifier/Difficulty Class` (PDF p. 6), `Playing the Game > D20 Tests > Saving
Throws` (PDF p. 7), and `Playing the Game > Advantage/Disadvantage; Proficiency` (PDF p. 8).
Outcome: add purpose-built game-table controls for the accepted seeded dice, raw fixed-DC ability
check, and fixed-DC saving-throw mechanics. The D&D character picker admits only entities in the
selected D&D state space that expose both the D&D ability-score and character-level component
summaries; world facts, generic entities, and entities from other application state spaces are not
shown. It also admits a campaign space only when the accepted D&D dice mechanic descriptor is
available for that exact state-space binding, so a stale pre-activation binding cannot masquerade as
a playable D&D campaign. Each control uses the selected workspace entity as its explicit subject
where required, prepares through the generic review-first seam, and renders only the returned safe
result narration.
Exclusions: D&D mechanics/procedures/schemas, C# routes/services, skill-check UI, automatic
condition-derived circumstances, attacks/damage/healing/HP, character or inventory mutation,
encounter action, rerolls/Heroic Inspiration, persistent dice history, raw JSON input, source
file changes, browser storage, and player/GM authorization changes. The user's 2026-08-27 report that
Home could not reach the D&D page explicitly authorizes publishing the reviewed Home/D&D page
revisions as the bounded synchronization step for this slice. Their later report that the created
D&D files were not loaded authorizes the existing operator-only registration and activation of the
reviewed `dnd2024-core` source, after its required dry runs; it does not authorize automatic
state-space migration.
Allowed files/areas: existing D&D and generic browser assets, the D&D game-table page script,
the authored home page entry link, focused web tests, this document/receipt, the D&D web dependency
plan, and web roadmap.
Stop point: current `dnd2024-play` selection shows a dice tray and raw check/save panel that can
prepare, review, explicitly confirm, and display one accepted server result. All D&D calculations,
seeded rolling, validation, and outcomes remain catalog-mechanic owned.

Model assignment: `gpt-5.6-terra`, high reasoning, as assigned to Order 5 after the generic
authority/action controls were accepted.

## Confirmed decisions

The component inventory already confirms `<dnd2024-dice-tray>` and `<dnd2024-action-panel>`.
The user's 2026-08-27 request to continue with Slice 5 confirms binding the accepted stateless
mechanics to those existing confirmed IDs. The selected workspace entity is passed as the single
explicit `subject` role; no new picker, mechanic, route, role, or visibility policy is introduced.

The panel intentionally supports **raw** ability checks only. A named skill would require browser
ownership of the skill-to-ability mapping and proficiency presentation, so it remains outside this
slice. Advantage/Disadvantage can be entered only as one explicit user-labelled circumstance; the
browser never derives it from Conditions or any other state. The source's typical DC band is
presented as a bounded 0–30 GM control; unusual DCs remain a future authored control decision.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Browser consequence |
| --- | --- | --- | --- |
| D20 Test | roll 1d20, add relevant ability and applicable modifiers, compare total to target | `mechanic.dnd2024.check.ability` and `mechanic.dnd2024.saving-throw` | UI sends only ability, DC, and optional explicit circumstance; it never computes modifiers or success. |
| Advantage/Disadvantage | roll two d20s and take high/low; mixed sources cancel | same catalog mechanics | One optional mode/source control serializes a single accepted circumstance; no condition inference or local roll occurs. |
| Ability checks | ability modifier names the check; DC is GM-supplied | `mechanic.dnd2024.check.ability` | Six labelled ability tiles and a DC stepper invoke the raw check only. |
| Saving throws | named ability save; proficiency applies when known; a creature may choose to fail | `mechanic.dnd2024.saving-throw` | Six labelled ability tiles, DC stepper, and voluntary-failure toggle send closed source input; mechanics own proficiency and zero-roll result. |
| Seeded dice | bounded dice and modifier roll through the mechanic random source | `mechanic.dnd2024.dice` | Die-face buttons and modifier steppers request a roll; the browser has no RNG or persistent tray history. |

## External implementation reference

Foundry dnd5e `module/dice/d20-roll.mjs` at commit
`275bed0be4ccfa15e6b3347acccb8da8784726d9` (blob
`33d1551d5ed8fcc1aaac6a28d1238101d71b2035`) was reviewed as engineering reference only. Its
separate d20 formula parts, target, and explicit normal/Advantage/Disadvantage mode support the
same UI separation. No Foundry code, assets, formula, critical/fumble behavior, or dependency is
copied; the current catalog mechanics and exact SRD locators remain authoritative.

## Prerequisite evidence

- The local SRD 5.2.1 PDF was reviewed at printed pp. 6–8. It states D20 Test modifier/target
  comparison, saving-throw voluntary failure, Advantage/Disadvantage handling, and proficiency.
- `mechanic.dnd2024.dice`, `mechanic.dnd2024.check.ability`, and
  `mechanic.dnd2024.saving-throw`, with their governing procedures, are active accepted catalog
  owners with empty effects/events/notifications.
- [Slice 4 receipt](DND2024-WEB-UI-SLICE-4-RECEIPT.md) accepts the generic entity/action/form
  module and exact review/confirmation execution boundary.

## Runtime artifacts

- Revise the existing `<dnd2024-workspace>` module to define the two confirmed action controls and
  show them only once the workspace has exact application/state/entity scope. Its entity selector
  reads bounded entity/component-summary pages from that exact state space and presents an entity
  only when its summaries include `dnd2024.abilities` and `dnd2024.character-level`; it uses no
  name, ID, directory, campaign-label, or source-path inference.
- Before presenting a state space, the module performs the existing read-only D&D dice-descriptor
  lookup against that exact binding. A stale or otherwise unsupported binding is excluded rather
  than selected for controls that could not execute.
- Load the accepted generic action-control module before the D&D workspace module on
  `dnd2024-play`.
- Add one authored, direct `/ui/dnd2024-play` entry link on the private home page. The shared
  `<system-navigation>` continues to expose its generic registered-application control-center
  links; this page-specific game destination is not an application-page registry or a new generic
  routing contract.
- Extend the generic result card only to show server-returned safe action narration; it does not
  expose effects or mechanic source.
- Add no catalog/C#/route/storage/migration/public-surface artifact.

## Authoritative state and closed input

The D&D panel fixes exact mechanic IDs in D&D presentation code and sends only these closed inputs:

- dice: `{ count: 1, sides: 4|6|8|10|12|20, modifier: -99..99 }`;
- raw ability check: `{ ability, dc: 0..30 }`, optionally one
  `{ kind: advantage|disadvantage, source }` circumstance; and
- saving throw: the same ability/DC/circumstance shape, or `{ ability, dc, voluntaryFailure: true }`.

The server owns mechanic identity/version/fingerprint, selected entity truth, component state,
ability modifiers, proficiency, source validation, DC outcome, seeded randomness, roll mode,
results, effects, confirmation, replay, transaction, and receipts. The browser cannot send score,
modifier, Proficiency Bonus, roll, total, success, effects, revisions, seed, or rule-derived
condition state.

## Behavior, result, and typed effects

- The dice tray uses tactile d4/d6/d8/d10/d12/d20 choices and a bounded plus/minus modifier. Its
  single action has no role binding.
- The action panel gives one raw-check/save mode choice, six ability tiles, a bounded DC stepper,
  one explicit Advantage/Disadvantage source field, and the saving-throw voluntary-failure toggle.
- Setting voluntary failure clears/blocks the circumstance control; mode/source changes rebuild
  disposable input and discard any prepared proposal.
- The selected workspace entity is the only submitted `subject`; changing workspace scope replaces
  controls and discards all preparation state.
- The presentation filter is a D&D UI eligibility boundary, not a generic server query contract:
  it reads existing state-space-scoped records, does not add a route or persist a classification,
  and does not make any rules decision. A component-read failure excludes only that candidate and
  leaves the current state unchanged. Likewise, a state-space descriptor read that is stale, denied,
  or unavailable excludes only that campaign space and never upgrades/migrates it.
- Each interaction is delegated to `<application-action-button>`, so it must prepare, display the
  exact returned review card, and receive a separate confirmation before execution. Returned server
  narration is rendered as text; no roll is calculated or interpreted locally.
- All three underlying mechanics are effect-free. The UI neither assumes that fact for authority
  nor creates a local transaction or typed effect.

## Failure, replay, and rollback contract

Missing scope/subject, unavailable action descriptor, incomplete/invalid local source input,
blank Advantage/Disadvantage source, invalid prepared evidence, stale or denied execution, and
network failure appear as local control errors. Input/scope changes remove any review card before
execution. The generic seam owns idempotency/replay/conflict behavior. No failed, stale, or
rejected request mutates browser or game state; catalog/action owners retain rollback authority.

## Implementation sequence

1. Add focused source assertions for the exact three mechanic IDs, closed UI input, explicit review
   boundary, no browser RNG/derived values, and safe narration display.
2. Add game-table controls and the accepted generic module dependency without changing catalog or
   server behavior, then add the direct private home entry for the confirmed D&D page.
3. Run syntax, focused web/D&D tests, build, catalog validation, full core/local-AI suites, and
   record the receipt/update Order 5 once.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Dice | Face and modifier controls prepare the exact seeded dice mechanic with no browser RNG/history. |
| Raw check | Explicit selected subject/ability/DC and optional labelled circumstance reach only the raw ability-check mechanic. |
| Saving throw | Explicit subject/ability/DC, one labelled circumstance, or voluntary failure reach only the save mechanic. |
| Rule authority | No browser modifier, proficiency, d20, success, condition, or outcome calculation exists. |
| Confirmation | Every action uses the accepted prepare/review/explicit-execute flow; input/scope change clears prepared evidence. |
| Result | Only safe server narration is shown; effects and mechanic source remain absent. |
| Compatibility | Existing read-only workspace, generic action seam, catalog owner tests, and route/remote policies remain unchanged. |
| Home entry | Home exposes a direct D&D game-table link while generic registered-application links still retain their control-center scope. |
| Character scope | The D&D picker shows only exact-state-space entities carrying both required D&D component summaries; facts and generic/world entities stay out of the game table. |
| State-space compatibility | Only a state space bound to an activation that exposes the accepted D&D dice descriptor is selectable; stale bindings are not silently upgraded or shown as playable. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/application-workspace.js`
- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- Focused `WebInterfaceTests` and existing D&D ability-check tests.
- `roleplay validate catalog` (no intended catalog change; proves current catalog remains valid).
- `dotnet build DantesRoleplay.slnx --no-restore`, full core/local-AI suites, and `git diff --check`.

The MCP protocol walk is not required: no MCP operation or dependency registration changes.

## Completion receipt and exit gate

Write `DND2024-WEB-UI-SLICE-5-RECEIPT.md`, mark Order 5/E1 accepted after stated evidence, and
stop. Attacks, damage, healing, HP/equipment/inventory mutation, encounter controls, named skill
UI, condition-derived circumstances, rerolls, and broader accessibility work stay in later slices.
