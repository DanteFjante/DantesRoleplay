# Trail Game TG2 Slice 2 implementation — party and inventory schemas

Status: **accepted 2026-08-25**; [receipt](TG2-SLICE-2-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG2 run domain / TG2.2](TG2-RUN-DOMAIN-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival contracts**
Outcome: Add closed party, member, conveyance, and resources component contracts with bounded
schema/registration evidence.
Exclusions: Policy/pending-choice/outcome schemas, fixtures, mechanics, health/capacity/resource
calculations, transitions, UI, migration, public surface, startup, or live state.
Allowed files/areas: `catalog/applications/trail-survival/components/party/`, the focused TG2 test,
and TG2 plan/receipt statuses.
Stop point: Stop after the four party/inventory component types compile/register/replay and their
representative valid and invalid values pass; do not begin TG2.3 in this slice.

## Confirmed decisions

- IDs and meanings are confirmed in [TG2 run-domain confirmation](TG2-RUN-DOMAIN-CONFIRMATION.md).
- Party membership and conveyance assignment use bounded entity-ID references in the party
  component; TG2 does not also create generic relationship edges.
- Resource quantity entries contain no derived weight, value, price, or consumption rate.
- Health, condition, status, capacity, and condition bounds are structural. TG3 owns legal
  combinations and all changes.

## External implementation reference

No external implementation applies. These are original ruleset-neutral state contracts.

## Prerequisite evidence

- [TG2 Slice 1 receipt](TG2-SLICE-1-RECEIPT.md) proves the authored/registry seam and run spine.
- Existing generic schema and ECS owners remain unchanged.

## Runtime artifacts

- `trail-survival.party` metadata/schema
- `trail-survival.member` metadata/schema
- `trail-survival.conveyance` metadata/schema
- `trail-survival.resources` metadata/schema

## Authoritative state and closed input

Only the confirmed fields are canonical. Callers never supply schema version/hash, ECS revision,
calculated load, remaining capacity, status summaries, resource value, or consumption forecast.

## Behavior, result, and typed effects

This slice defines no behavior or effect. It extends the immutable application component-type
catalog through existing generic registration and validation semantics.

## Failure, replay, and rollback contract

Malformed schema/value input rejects without type/state mutation. Identical registration replays
version 1. Cross-application discovery remains empty.

## Implementation sequence

1. Add four metadata/schema pairs.
2. Extend focused parse/compile/value/registration/isolation evidence.
3. Validate focused tests and catalog, write the receipt, and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Closed shape | Valid party/member/conveyance/resources examples pass. |
| Negative | Empty required IDs, duplicates, bad enums, negative/overflow values, and derived fields reject where schema-expressible. |
| Registry | All eight TG2 types remain Trail-owned version 1 with stable replay. |
| Isolation | `dnd2024` component discovery remains empty. |
| Live/surface | Disposable database only; no public/startup change. |

## Verification commands

- Focused TG1/TG2 tests using isolated build output.
- Disposable `roleplay validate catalog`.
- Owned-file whitespace/link checks.

## Completion receipt and exit gate

Record `TG2-SLICE-2-RECEIPT.md`, mark TG2.2 accepted, activate TG2.3 separately, and do not add
decision/outcome contracts before that boundary.
