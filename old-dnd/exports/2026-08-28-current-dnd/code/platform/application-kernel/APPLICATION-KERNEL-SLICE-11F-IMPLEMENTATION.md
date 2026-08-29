# Application kernel Slice 11F implementation — lossless legacy stats contract adoption

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / application-owned component contracts](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible legacy contract migration only**  
Source ID and locator: **not applicable** — no D&D rule or field meaning is introduced.  
Outcome: Supply the missing schema sidecar for legacy `stats`, preserve its exact object-only value
boundary without inferring fields, and prove `dnd2024.stats` registers with the other 32 contracts
on a fresh disposable host.  
Exclusions: Field-specific stats, ability-score or character rules; fixture/value migration;
mechanic/procedure rewriting; aliases; state-space creation; default-host registration;
projections; remote MCP; vectors; and AI orchestration.  
Allowed files/areas: New `catalog/components/stats.schema.json`; focused component-administration
and fresh-host protocol tests; this document/receipt and concise dependency/roadmap status links.  
Stop point: Stop after both authored fixture values validate, non-object roots fail, all 33 legacy
component contracts register, the stats definition and schema are both activated as source
documents in disposable evidence, and no state or mechanic is migrated.

## Confirmed decisions

- The accepted ownership ratification assigns unqualified legacy `stats` to `dnd2024` migration.
- The user continued after Slice 11E deliberately left `stats` as the next classified adoption
  owner, confirming this exact parity boundary.
- The accepted inventory and governing `procedure.world.model` prove the only enforced legacy
  value constraint was an object root. The two fixtures have incompatible field sets, while the
  two dependent mechanics dynamically select a caller-named numeric top-level field.
- The sidecar is therefore exactly `{ "type": "object" }`. It does not require, document, or
  prohibit any property and does not turn observed fixture fields into D&D rules.
- The runtime type is the already-confirmed application-qualified migration identity
  `dnd2024.stats`; no legacy alias or unqualified system identity is created.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Stats value shape | Legacy values are arbitrary objects. | `stats.json`, `procedure.world.model`, accepted inventory | Preserve object-only parity; infer no fields. |
| D&D mechanics | None adopted. | Existing catalog fixtures/mechanics | No SRD/Foundry rule review applies and no game formula enters C#. |

## External implementation reference

No Foundry review applies because this slice deliberately declines to model D&D character data or
mechanics. It only versions the repository's pre-existing object-root contract.

## Prerequisite evidence

- [Slice 11E receipt](receipts/APPLICATION-KERNEL-SLICE-11E-RECEIPT.md) proves all 32 sidecar-backed
  legacy contracts register and leaves only sidecar-less `stats` in this component-contract leaf.
- [Legacy ownership ratification](LEGACY-OWNERSHIP-RATIFICATION.md) assigns `stats` to `dnd2024`.
- [Slice 1 inventory](inventory/LEGACY-APPLICATION-KERNEL-INVENTORY.json) proves two object fixture
  values, no sidecar, and object-only legacy persistence.

## Runtime artifacts

- Add `stats.schema.json` with one object-root assertion.
- Extend disposable preflight and MCP registration evidence from 32 to 33 contracts.
- Widen the disposable `component-stats` source specification so both definition and sidecar are
  included in preview/activation evidence.
- Add no C# runtime behavior, migration, new MCP kind, state row, fixture rewrite, or mechanic.

## Authoritative state and closed input

The catalog definition, new sidecar, and existing fixtures are authored authority. SQLite becomes
runtime authority only inside the existing disposable registration proof. The caller still cannot
supply profile, hash, version, owner, or schema-validation success.

## Behavior, result, and typed effects

Any bounded JSON object validates, including both current fixtures; arrays, strings, numbers,
booleans, and null do not. Fresh-host registration derives `dnd2024.stats` version 1 through the
existing dry-run/commit transaction. Typed effects: none.

## Failure, replay, and rollback contract

Non-object values fail without mutation. Missing/rejected sidecar input creates no type. Existing
registration replay, authorization, dry-run, and transaction behavior remain unchanged. The
disposable proof ends with no state spaces, entities, components, or legacy state rows.

## Implementation sequence

1. Add exact fixture-valid/object-boundary tests.
2. Add the minimal sidecar.
3. Extend preflight, registration, source preview, activation, replay, and absence evidence.
4. Run focused/full/local-AI/catalog/build/diff checks; record receipt and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Fixture parity | Homer and Orban stats values validate without field normalization. |
| Root boundary | Empty/arbitrary objects pass; all non-object JSON roots fail. |
| Registration | 33 exact legacy contracts register once through dry-run/commit. |
| Source activation | `stats.json` and `stats.schema.json` are effective documents under the same stats source. |
| Isolation | `dnd2024.stats` is application-owned; no system or unqualified runtime type appears. |
| No state | State-space and legacy entity/component tables remain empty in fresh-host evidence. |

## Verification commands

- Focused schema/component-administration and fresh-host MCP tests.
- Catalog validation; full shared/local-AI suites; warning-free solution build; `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11F-RECEIPT.md`, mark this document
accepted, update Slice 11 status links, and stop before fixture/state or mechanic migration.
