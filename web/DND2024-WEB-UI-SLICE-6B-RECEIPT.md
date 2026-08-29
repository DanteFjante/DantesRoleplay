# D&D 2024 web UI Slice 6B receipt — direct-item equip and unequip controls

Status: **accepted 2026-08-27**
Completed: **2026-08-27**
Implementation: [Slice 6B](DND2024-WEB-UI-SLICE-6B-IMPLEMENTATION.md)
Parent: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 6 / E4
Ruleset alignment: **dnd2024-compatible**
Model assignment: `gpt-5.6-sol`, high reasoning

## Delivered boundary

- Added a contextual **Equipment** action area to existing inventory cards. It appears only for a
  depth-one item whose exact containment names the selected actor as container, whose item instance
  and activated definition are available, whose quantity component is absent, and whose optional
  equipment state is missing or one valid `held|worn|unequipped` value.
- Missing/unequipped eligible items show only their definition-declared modes as tactile **Hold** /
  **Wear** choices. Selecting a mode rebuilds the disposable reviewed action and sends exactly
  `{state:'held'|'worn'}` to `mechanic.dnd2024.item.equip`.
- A current held/worn item shows one **Prepare unequip** action with exactly `{}` for
  `mechanic.dnd2024.item.unequip`. It does not also show an equip chooser.
- Both actions bind the exact contained entity as `item` and selected actor as `holder`, then
  delegate descriptor lookup, prepare, proposal review, separate confirmation, execution, replay,
  transaction, and receipt behavior to the accepted generic action button.
- A completed execution receipt rereads the selected character and complete bounded inventory tree.
  No equipment badge, component, custody, or definition is mutated optimistically in the browser.
- Nested items, stacks, corrupt state, missing/unavailable definitions, invalid/no declared modes,
  malformed custody, and stale campaign bindings remain readable where possible but receive no
  equipment mutation control.

## Authority and rules boundary

The UI projects only accepted current containment, `dnd2024.item-instance`, optional
`dnd2024.item-quantity`, optional `dnd2024.equipment-state`, and immutable
`dnd2024.item-definition.equipmentModes`. It does not infer eligibility from names or item kinds.
The catalog mechanics revalidate direct possession, non-stack state, referenced definition,
permitted mode, and current state before constructing their complete equipment-state add/set.

No AC, attacks, hands, equipment slots, don/doff time, action resource, burden, custody, or transfer
rule is calculated or implied. No external implementation code or asset was needed or adopted.

## Verification

- Browser syntax check — passed.
- Focused `WebInterfaceTests` — **89 passed, 0 failed**. The asset contract checks the exact two
  mechanic IDs, direct-depth/custody/quantity/definition/state gates, role bindings, closed inputs,
  tactile mode labels, generic receipt refresh, and absence of transfer/move/stack/activity IDs,
  direct browser writes, effects, RNG/storage, control routes, MCP routes, and HTML injection.
- Existing D&D equipment/custody owner test
  `Equipment_and_transfer_require_definition_eligibility_direct_custody_and_unequipped_state` —
  **1 passed, 0 failed**. It proves equip succeeds, transfer while equipped fails, unequip succeeds,
  and transfer can then succeed under the existing owner.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with **0 warnings, 0 errors**.
- Local-AI regression suite — **21 passed, 0 failed**.
- Full core suite was started with the completed build but produced neither completion nor failure
  output for two minutes. Only that stalled test run was interrupted; no failing assertion was
  reported. Focused web and equipment-owner tests completed independently as recorded above.
- The restarted local host returned HTTP 200 for `/ui/dnd2024-play`. Its served D&D asset contains
  Equip and Unequip, excludes Transfer, and contains no direct POST. Browser read-back reached the
  registered Brackenford campaign and Orban while preserving the stale-binding action lock.
- Focused `git diff --check` found no whitespace error; only checkout line-ending notices remain.

## Deliberate exclusions and acceptance gate

No item create/move/transfer, destination picker, custody/slot change, stack create/split/merge/
consume, quantity stepper, item activity, burden/capacity/currency derivation, AC/attack consequence,
catalog/server/schema/route/storage change, migration, database synchronization, or live activation
was added. Concurrent character-creation work remains untouched.

The user's 2026-08-27 request to finish Slice 6 accepts this delivered feature boundary. The final
Order 6 inventory slice owns ordinary transfer, stack quantity, and descriptor-authored item use;
explicit administrative move/create helpers remain maintenance tooling rather than game controls.
