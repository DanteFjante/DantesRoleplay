# D&D code-adoption Slice 12A implementation — fresh-host play, replay, and rollback

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), adoption acceptance lane  
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 12A  
Ruleset alignment: `ruleset-neutral` verification of accepted D&D 2024 behavior  
Source ID and locator: not applicable; this leaf adds no rule meaning and preserves the locators
owned by the exercised mechanics  
Outcome: prove a newly created SQLite host can activate the application, execute a representative
encounter/combat/healing flow, replay an action without a second mutation, reject invalid state
without mutation, and retain atomic rollback under an injected later-effect failure.  
Exclusions: new rules/content, runtime IDs, schema or host changes, public operations, live database
access, donor execution, and network access.  
Allowed files/areas: this document; `DantesRoleplay.Tests/Dnd2024AbilityCheckTests.cs`; the existing
Slice 6C impact/replay/rollback proof; Slice 12A receipt.  
Stop point: focused fresh-host and injected rollback evidence passes; no runtime artifact changes.

## Confirmed decisions

- The user's request authorizes this acceptance leaf; it introduces no confirmation-gated runtime
  identity or semantic change.
- The existing test harness remains the fresh-host owner: every test creates a new SQLite database,
  previews and activates the catalog source, and disposes it after the test.
- The existing action runner and operation history remain replay authority.
- The accepted Slice 6C proof remains rollback authority; this leaf composes its evidence instead
  of manufacturing a second effect-transaction implementation.

## Prerequisite evidence

- Slice 7D proves source preview/activation and ordinary evaluator/action execution on fresh SQLite.
- Slice 6C proves dependency impact, replay identity, stale-write failure, and rollback of an earlier
  effect when a later effect fails.
- Slice 11D and 11H prove accepted mitigation, Temporary HP, and healing behavior independently.

## Authoritative state and behavior

The acceptance test supplies only closed action input and role bindings. Ability scores, weapon
profile, Armor Class, Hit Points, mitigation, Temporary HP, encounter state, source identity,
component revisions, and operation history are materialized from the fresh host. The flow must:

1. activate the current D&D application on an empty database and add test fixtures;
2. order/start an encounter through the activated action surface;
3. grant Temporary HP, apply weapon damage through mitigation and absorption, then heal actual HP;
4. replay one committed action and observe `Replayed` with no extra revision;
5. submit corrupt authoritative state and observe failure with no related state change; and
6. run the existing injected-failure proof to show all-or-nothing typed-effect rollback.

No acceptance assertion may calculate an alternate D&D outcome in C#; it checks invariants and
committed catalog output.

## Failure, replay, and rollback contract

Malformed input, absent bindings, invalid activated dependencies, and corrupt authoritative state
fail closed. A replayed operation returns its recorded result and cannot apply another effect.
Injected failure in a later effect leaves every effect in that action unapplied. No failed path may
write game state, events, notifications, or a partial successful operation.

## Acceptance matrix

| Case | Required evidence |
| --- | --- |
| Fresh import | new SQLite database, source preview/activation, activated mechanics |
| Positive composition | encounter plus Temporary HP/damage/healing actions succeed |
| Deterministic/replay | identical operation ID is replayed; component revision is unchanged |
| Invalid state | corrupt state fails and preserves related components |
| Rollback | existing injected later-effect failure preserves both targeted components |
| Compatibility | no opt-in extension is required by the core flow |
| Surface | no MCP or public-operation change |

## Verification commands

- Run the focused Slice 12A fresh-host test.
- Run the existing Slice 6C impact/replay/rollback proof.
- Run all `Dnd2024AbilityCheckTests` before accepting the leaf.

## Completion receipt and exit gate

Results are recorded in `adoption/evidence/DND-CODE-ADOPTION-SLICE-12A-RECEIPT.md`. The leaf stops
before Slice 12B changes.
