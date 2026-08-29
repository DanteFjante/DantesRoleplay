# D&D 2024 web UI Slice 7A receipt — recorded encounter turns and resources

Status: **accepted 2026-08-27**
Completed: **2026-08-27**
Implementation: [Slice 7A](DND2024-WEB-UI-SLICE-7A-IMPLEMENTATION.md)
Parent: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 7A / E3
Ruleset alignment: **dnd2024-compatible**
Model assignment: `gpt-5.6-sol`, xhigh reasoning

## Delivered boundary

- Added a top-level **Encounter** selector beside Campaign, campaign space, and actor. Candidates
  come only from current application entities with a valid source-fixed immutable Initiative
  snapshot, an exact matching containment roster, and participant IDs entirely inside the selected
  campaign actor set. Names and IDs are never used to infer encounter identity.
- Defined the already-confirmed `<dnd2024-encounter-tracker>` component. It presents ordered
  participant cards, a strong `aria-current` current-turn treatment, exact status/round, and
  contextual reviewed **Start turns**, **Next turn**, and separately warned **End encounter** actions.
- Defined the already-confirmed `<dnd2024-turn-budget>` component. Action, Bonus Action, Reaction,
  Interaction, and Movement render as tactile resource tokens. Selecting an eligible token reveals
  one reviewed spend action; Movement uses a bounded 5-foot stepper.
- Ordinary resources and movement are selectable only for the displayed active participant. A
  ready Reaction remains selectable for any displayed roster participant while the server still
  revalidates membership, lifecycle, Conditions, and availability.
- Lifecycle buttons send only `{encounter}` plus `{}` to their exact start/advance/end mechanics.
  Resource buttons send only `{subject,encounter}` plus `{resource}` or
  `{resource:'movement',feet}` to `mechanic.dnd2024.turn-budget.spend`.
- Completion receipts clear disposable control selection and reread the selected actor and
  encounter. No round, index, active participant, resource, Speed, Exhaustion, or component is
  changed optimistically.
- Reused the campaign discovery component-summary cache during encounter discovery. This preserves
  the private read-rate boundary instead of issuing a second state-space-wide component scan.
- Added a non-sensitive status error code attribute for exact browser diagnostics; normal status
  presentation remains unchanged.

## Authority and rules boundary

The browser displays the stored snapshot order unchanged. Start/advance/end mechanics own turn and
round transitions plus atomic restoration of only the newly active participant. The turn-budget
spender owns active/off-turn eligibility, Condition prohibitions, availability, movement admission,
typed effects, replay, and rollback.

The UI performs no Initiative roll/sort/tie decision, roster mutation, turn restoration, Speed or
Exhaustion calculation, Condition ruling, movement positioning, effect construction, or direct
state write. Foundry dnd5e was reviewed for current-turn emphasis and combatant-scoped refresh
behavior; no Foundry code, data model, group-Initiative behavior, or templates were copied.

## Verification

- Browser syntax check — passed.
- Focused `WebInterfaceTests` — **89 passed, 0 failed**. The source contract checks exact encounter
  discovery/roster/campaign gates, summary-cache reuse, both confirmed custom elements, all four
  lifecycle/spend mechanic bindings, exact closed roles/inputs, movement stepper, receipt refresh,
  and absence of Initiative formation, attack/damage, direct writes, effects, browser persistence,
  direct POSTs, control routes, and MCP routes.
- Existing encounter owner tests — **3 passed, 0 failed**:
  `Fresh_host_encounter_composes_initiative_and_transacts_the_turn_lifecycle`,
  `Turn_lifecycle_restores_only_the_new_participant_and_applies_exhaustion_reduction`, and
  `Turn_budget_spender_enforces_active_turn_off_turn_reaction_and_condition_prohibitions`.
  The final combined focused run reports **92 passed, 0 failed**.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with **0 warnings, 0 errors**.
- Local-AI regression suite — **21 passed, 0 failed**.
- The full core suite was retried from the completed build but produced no completion, failure, or
  further progress for 90 seconds. Only that stalled test process was interrupted; the focused web
  and encounter-owner tests completed independently as recorded above.
- The restarted local host returns HTTP 200 for `/ui/dnd2024-play` and its D&D asset. The served
  asset contains the encounter selector, tracker, budget control, lifecycle, and spender; it
  excludes Initiative-order formation, weapon attack/damage, and direct POST behavior.
- Live browser read-back loads Brackenford and Orban with no browser errors or rate-limit failure.
  It correctly shows a disabled **No recorded encounters** selector and the legacy-binding action
  lock because that campaign currently has neither a recorded encounter nor a current action
  binding.
- Focused `git diff --check` reports no whitespace error; only checkout line-ending notices remain.

## Deliberate exclusions and acceptance gate

No catalog, component schema, mechanic, procedure, server route, migration, campaign registration,
encounter creation, roster edit, live database content, Initiative formation/tie resolution,
weapon attack/damage, tactical movement, Condition control, victory, reset, or restart was added.
Concurrent character-creation work remains untouched.

The user's 2026-08-27 instruction to continue accepts this delivered Slice 7A boundary. Slice 7B
must first close the pre-order encounter identity and actual-tie preflight authority gap; Slice 7C
then owns weapon attack/damage controls.
