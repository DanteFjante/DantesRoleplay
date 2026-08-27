# D&D 2024 web UI Slice 5 receipt — stateless game-table actions

Status: **implemented; acceptance confirmation pending 2026-08-27**
Implementation: [Slice 5 implementation](DND2024-WEB-UI-SLICE-5-IMPLEMENTATION.md)
Plan: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 5 / E1
Ruleset alignment: **dnd2024-owned**

## Delivered boundary

The existing private `dnd2024-play` viewport now loads the accepted generic application action
module before its D&D workspace. Once an exact campaign space and entity are selected, its new
**Action table** panel composes two purpose-built game controls:

- `<dnd2024-dice-tray>` provides d4/d6/d8/d10/d12/d20 choices and a bounded -99..99 modifier
  stepper. It sends exactly `{ count: 1, sides, modifier }` to
  `mechanic.dnd2024.dice` and has no entity role, browser RNG, or roll history.
- `<dnd2024-action-panel>` provides raw ability-check and saving-throw mode tiles, six ability
  tiles, a 0..30 DC stepper, one explicitly labelled Advantage/Disadvantage source, and the save
  voluntary-failure choice. It sends the selected entity only as the explicit `subject` role to
`mechanic.dnd2024.check.ability` or `mechanic.dnd2024.saving-throw`.

The private home page now has a prominent **Enter D&D 2024 game table** link to
`/ui/dnd2024-play`. This is an authored D&D-page entry, separate from the shared generic navigation
component's registered-application control-center links; it does not introduce an application-page
registry or make generic infrastructure D&D-aware.

The user reported the missing live entry on 2026-08-27, explicitly authorizing the required page
synchronization. The prior live `home` revision 5 was read back before publication. The reviewed
Home page was then published as active revision 7 (the first equivalent upload completed before a
PowerShell reserved-variable assignment error; revision 7 is the final read-back target), and the
previously absent `dnd2024-play` page was published as active revision 1. Both `/` and
`/ui/dnd2024-play` subsequently returned HTTP 200. No application or game-state data changed.

After the user closed the local host and reported that the authored D&D files were not loaded, the
host was restarted on its local private address with the repository as the approved source root.
The live database was exported before the change. A dry run then registered exactly the reviewed
`dnd2024-core` source (`catalog/applications/dnd2024/**/*`, trusted, precedence 0), and a second
dry run activated that exact core-only source profile. The matching commits succeeded: the active
profile is activation revision 2, contains one source (`dnd2024-core`), and has 358 source winners.
Optional/legacy content remains unselected. This was a runtime synchronization only: no catalog
file, schema, mechanism, route, or game-state record was edited.

The earlier `dnd2024-main` state space remains bound to the generic-only activation fingerprint
from revision 1. Its D&D descriptor now returns the expected stale-binding response (HTTP 409), so
it is deliberately excluded from the game table. It was not silently upgraded or migrated because
it already contains mixed live state. The workspace now also filters entity candidates to those
whose exact state-space component summaries contain both `dnd2024.abilities` and
`dnd2024.character-level`; generic facts and other non-character records cannot appear in the
adventurer picker.

Every control delegates to the existing `<application-action-button>`: descriptor read, prepare,
server-built review, separate **Confirm and execute**, then server result. Changing a game control
rebuilds that disposable action surface and removes any prepared proposal. The generic result card
now renders only the bounded text narration returned in `actionResults`; it does not expose raw
mechanic data, source, effects, seed, total, modifier, or transaction details.

The D&D browser code contains no roll, ability-modifier, proficiency, DC-success, condition, or
Advantage/Disadvantage resolution logic. Catalog JavaScript remains the sole rule/outcome owner.
No catalog, C#, route, schema, migration, browser storage, or game state was changed. The bounded
runtime source registration and activation above are the only database changes.

## Rules/source alignment

The implemented mechanics are the currently accepted D&D owners
`mechanic.dnd2024.dice`, `mechanic.dnd2024.check.ability`, and
`mechanic.dnd2024.saving-throw`, under `source.dnd2024.srd-5.2.1`. Local SRD 5.2.1 printed pages
6–8 were reviewed for D20 tests, Ability Checks, Saving Throws, voluntary failure,
Advantage/Disadvantage, and proficiency. The implementation sends only the source inputs those
owners accept; it does not reproduce those rules in UI code.

Foundry dnd5e's `module/dice/d20-roll.mjs` at commit
`275bed0be4ccfa15e6b3347acccb8da8784726d9` was consulted only as a pinned engineering reference
for separating a selected roll mode from target/result handling. No Foundry code, formula, asset,
or behavior was copied.

## Evidence

- Both browser modules passed `node --check`.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~WebInterfaceTests"` passed: **88/88**. Its source/asset contract now checks
  the two confirmed D&D control identities, exact three mechanic IDs, circumstance and
  voluntary-failure inputs, generic confirmation boundary, result narration, no D&D-side POST,
  browser RNG, storage, control-route, MCP, raw HTML injection, and the direct home game-table
  entry.
- `roleplay validate catalog` passed: **144 records valid**. It reported 21 existing advisory
  near-duplicate warnings and touched no live data.
- `dotnet build DantesRoleplay.slnx --no-restore` passed: **0 warnings, 0 errors**.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-build` passed: **21/21**.
- `git diff --check` passed for the Slice 5 edits. The checkout still reports pre-existing
  line-ending warnings on unrelated in-progress files.
- The live web host read back Home revision 5 before mutation, then reports active `home` revision
  **7** and `dnd2024-play` revision **1**. Direct local GET smoke checks returned **200** and the
  expected Home link/D&D workspace markup for both pages.
- The restarted local host returns **200** for the Home page, D&D game page, and D&D workspace
  asset. The live asset contains both the compatible-state-space filter and the D&D-character
  component-summary filter. Its focused source/asset test suite passed **88/88** after those
  additions.
- The operator flow previewed and then committed source registration and activation successfully:
  source fingerprint `3E151072...A6432`, D&D core preview fingerprint
  `0DCCD8B1...6E0B6`, and activation fingerprint `9132DFD2...36BF2`.

The targeted `Dnd2024AbilityCheckTests` invocation did not report a test start, completion, or
assertion within 30 seconds on either normal or `--blame-hang` execution. Only the dotnet processes
spawned by those two attempts were stopped. That is a verification exception, not an observed test
failure or evidence of a Slice 5 rule regression; this slice does not edit the catalog mechanics
those tests exercise. A stable core test-host run remains required before an all-core-suite claim.

## Deliberate exclusions and acceptance gate

Named skills, condition-derived circumstances, attacks, damage, healing, HP/Temporary HP,
inventory/equipment mutation, encounter actions, rerolls/Heroic Inspiration, persistent dice
history, browser-wide accessibility work, automatic migration of the populated stale
`dnd2024-main` state space, and all broader game rules remain out of scope.

Per the repository agreement, Order 5/E1 is not marked accepted until the user confirms this
completed feature boundary. After confirmation, update the dependency plan and web roadmap to
accepted and stop; the next leaf is Order 6 character/inventory mutation controls.
