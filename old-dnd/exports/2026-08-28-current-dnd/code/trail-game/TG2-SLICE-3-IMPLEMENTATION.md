# Trail Game TG2 Slice 3 implementation — decision state and domain acceptance

Status: **accepted 2026-08-25**; [receipt](TG2-SLICE-3-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG2 run domain / TG2.3](TG2-RUN-DOMAIN-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival contracts**
Outcome: Add policy, pending-choice, and outcome component contracts and close TG2 with a complete
disposable component registration/ECS round trip and isolation acceptance.
Exclusions: Playable fixtures, mechanics, seed/randomness, actions, calculations, legal transition
logic, scenario content, UI, migration, public surface, startup, or live state.
Allowed files/areas: `catalog/applications/trail-survival/components/decision/`, focused TG1/TG2
tests, and TG2 plan/receipt/roadmap statuses.
Stop point: Stop after final TG2 acceptance and receipt; do not begin TG3 mechanics or IDs.

## Confirmed decisions

- IDs and meanings are confirmed in [TG2 run-domain confirmation](TG2-RUN-DOMAIN-CONFIRMATION.md).
- Absence of `pending-choice` means no unresolved choice; absence of `outcome` means non-terminal.
- The disposable ECS test is structural evidence, not an authored scenario or legal gameplay
  transition. It may store each schema-valid witness on an appropriately named test entity.
- Full-suite acceptance can serve as completed-feature confirmation because focused tests assert the
  complete bounded TG2 invariant.

## External implementation reference

No external implementation applies. These are original ruleset-neutral state contracts.

## Prerequisite evidence

- [TG2 Slice 1 receipt](TG2-SLICE-1-RECEIPT.md) proves run-spine contracts.
- [TG2 Slice 2 receipt](TG2-SLICE-2-RECEIPT.md) proves party/inventory contracts.
- [TG1 receipt](TG1-SLICE-3-RECEIPT.md) proves activation and state-space isolation.

## Runtime artifacts

- `trail-survival.policy` metadata/schema
- `trail-survival.pending-choice` metadata/schema
- `trail-survival.outcome` metadata/schema
- No production C# or database artifact

## Authoritative state and closed input

Policy stores authored pace/ration IDs only. Pending choice stores one authored event ID, bounded
offered choice IDs, and opening turn. Outcome stores terminal kind, authored cause ID, and reached
turn. Prompt text, eligibility, probabilities, effects, summaries, and available actions are never
canonical inputs.

## Behavior, result, and typed effects

This slice defines no behavior or game effect. Generic component registration and ECS persistence
derive versions/hashes, validate exact schema references, and own transactions/revisions.

## Failure, replay, and rollback contract

Malformed/extra/invalid-enum/boundary values reject with no component write. Identical type
registration replays. Wrong-application component writes and cross-state-space access reject.
Injected game-effect rollback is not applicable because TG2 contains no mechanic/effect batch.

## Implementation sequence

1. Add three metadata/schema pairs and extend contract validation to all eleven types.
2. Add a disposable state-space test that registers all types, writes and reads every structural
   witness, rejects an invalid value without mutation, and proves application isolation.
3. Run focused tests, disposable catalog validation, full shared/local-AI suites, isolated
   warning-free build, link/whitespace/diff checks, and record TG2 acceptance.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Final schemas | Policy, pending-choice, and outcome closed valid/invalid cases pass. |
| Complete catalog | Exactly eleven confirmed component IDs parse, compile, register, and replay. |
| ECS round trip | Every type writes through exact version/hash and reads back unchanged. |
| No-change | Invalid value creates no component. |
| Isolation | Wrong-application type/state-space use rejects; `dnd2024` discovery remains empty. |
| TG1 compatibility | Source activation and navigable procedures remain valid after expansion. |
| Surface/live | No public/startup/migration/normal-database change. |
| Compatibility | Catalog validation, full shared/local-AI suites, and build pass. |

## Verification commands

- Focused TG1/TG2 tests using isolated build output.
- Disposable `roleplay validate catalog`.
- Full shared and standalone local-AI suites using isolated build output as needed.
- Warning-free isolated solution build.
- Markdown link, owned-file whitespace, and scoped diff checks.

## Completion receipt and exit gate

Record `TG2-SLICE-3-RECEIPT.md`, collapse the completed TG2 plan, advance the roadmap to TG3 next
but inactive, and stop before any mechanic/action/seed/schema-fixture work.
