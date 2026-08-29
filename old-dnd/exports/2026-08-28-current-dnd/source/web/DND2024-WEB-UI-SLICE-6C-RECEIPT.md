# D&D 2024 web UI Slice 6C receipt — ordinary inventory actions

Status: **accepted 2026-08-27**
Completed: **2026-08-27**
Implementation: [Slice 6C](DND2024-WEB-UI-SLICE-6C-IMPLEMENTATION.md)
Parent: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 6 / E4
Ruleset alignment: **dnd2024-compatible**
Model assignment: `gpt-5.6-sol`, high reasoning

## Delivered boundary

- Added contextual **Transfer** controls to eligible unequipped inventory cards. The palette names
  an exact campaign actor or visible capacity-bearing inventory container and a short placement
  label, then invokes only `mechanic.dnd2024.item.transfer` with exact item/source/destination roles.
- Added tactile **Consume** and **Split** quantity steppers to valid fungible-stack cards. Consume
  visibly warns when the selected quantity can remove the stack; Split collects a conventional
  name and one disposable runtime item identity without calculating either resulting count.
- Added explicit **Merge** choices for visible same-container/same-definition stack targets and a
  visible warning that the current source stack can be removed.
- Added optional immutable descriptor-authored item activities. A valid published activity shows
  its fixed consume/grant summary and invokes only `mechanic.dnd2024.item-activity.use` with the
  descriptor ID and a fresh disposable grant-item identity.
- Generalized the existing inventory action mount so each game control supplies its exact role map
  while retaining the established prepare, review, separate confirmation, execute, and receipt UI.
- Successful receipts clear local palette/generated-ID state and reread the selected actor and
  bounded inventory from authoritative application state. The browser performs no optimistic move,
  count, creation, deletion, equipment, or activity mutation.

Together with accepted Slice 6A healing/Temporary HP and Slice 6B equip/unequip, this completes the
implemented player-facing boundary of Order 6.

## Authority and rules boundary

Transfer admission, containment cycles, capacity, equipped-item rejection, stack conservation,
entity creation/deletion, immutable activity cost/grant data, transaction atomicity, replay, and
rollback remain owned by the accepted catalog mechanics and generic application action seam. The
browser only narrows visible candidates and tactile stepper ranges from exact displayed state; the
server revalidates every request.

Administrative `item-instance.move`, item-instance/stack create-and-place, record/correction,
arbitrary loot generation, automatic merge, partial transfer, burden arithmetic, and direct state
writes are deliberately absent. Those mechanics bypass ordinary admission or bootstrap state and
belong to reviewed maintenance/bootstrap tooling rather than the player game table.

## Verification

- Browser syntax check — passed.
- Focused `WebInterfaceTests` — **89 passed, 0 failed**. The source contract covers all five exact
  ordinary mechanic IDs, role/input construction, bounded gates and stepper behavior, generated-ID
  lifecycle, receipt refresh, and the absence of administrative helpers, direct writes, effects,
  browser persistence, and direct API POSTs.
- Existing D&D transfer/capacity, equipment/custody, stack lifecycle, and descriptor-authored item
  activity owner tests — **4 passed, 0 failed** on a freshly rebuilt catalog snapshot.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with **0 warnings, 0 errors**.
- Local-AI regression suite — **21 passed, 0 failed**.
- The full core suite was retried from the completed build but produced no completion, failure, or
  further progress for 90 seconds. Only that stalled test process was interrupted; focused web and
  owner tests completed independently as recorded above.
- The restarted local host returns HTTP 200 for `/ui/dnd2024-play` and the served D&D asset. The
  asset contains Transfer, Consume, Split, Merge, and descriptor Use; it excludes administrative
  Move and direct API POST behavior.
- Browser read-back reaches the registered Brackenford campaign and Orban. That legacy campaign
  remains intentionally action-locked because its rules binding predates the active action set; it
  must be explicitly migrated before the new controls can appear against that live character.
- Focused `git diff --check` reports no whitespace error; only checkout line-ending notices remain.

## Deliberate exclusions and acceptance gate

No catalog, schema, mechanic, procedure, server route, database migration, campaign migration, or
live database content was changed. Concurrent character-creation work remains untouched.

The user's 2026-08-27 instruction to continue with Slice 7 accepts Slice 6C and therefore Order 6.
Order 7 encounter/combat controls is the next implementation leaf.
