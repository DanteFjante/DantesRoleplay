# Trail Game TG4 Slice 1 implementation — Northstar Passage fixture and presentation

Status: **awaiting confirmation**
Owner/roadmap: [Customizable trail-survival roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG4 starter scenario / TG4.1](TG4-STARTER-SCENARIO-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival content**
Outcome: Author one immutable mechanical scenario and its human-readable presentation/provenance.
Exclusions: New mechanics, TG3 schema changes, C#, public/browser/MCP surfaces, migrations, startup,
automatic seeding, live state, external code/assets, and TG4 balance acceptance.
Allowed files/areas: Trail scenario component catalog files, the new Northstar Passage scenario
catalog folder, TG4 tests, and TG4 plans/receipts.
Stop point: Validate fixture identity/schema/cross-references/hash/provenance, record the TG4.1
receipt, activate TG4.2, and stop before claiming balance or TG4 acceptance.

## Confirmed decisions

Blocked until [TG4 confirmation](TG4-STARTER-SCENARIO-CONFIRMATION.md) records approval. Once
confirmed, use exactly its IDs, separate presentation meaning, and content-hash convention.

## External implementation reference

No external implementation applies. All authored names, prose, values, and data are original.

## Prerequisite evidence

[TG3 hardening](TG3-SLICE-5-RECEIPT.md) proves the existing schema and mechanics accept immutable
scenario data and execute the complete loop atomically and deterministically.

## Runtime artifacts

- New component metadata/schema: `trail-survival.scenario-presentation`.
- New authored entity fixture: `scenario.northstar-passage`, carrying exact version-1 mechanical
  and presentation components.
- New provenance ledger for the content pack.
- Focused fixture validation tests. No runtime code artifact.

## Authoritative state and closed input

The fixture JSON is the authored source. Mechanical scenario data is validated by
`trail-survival.scenario`; prose/labels are validated by the presentation schema. Tests require
entity ID/scenario ID parity, exact hash recomputation, unique and resolvable IDs, connected route,
complete market weights for every resource delta, valid defaults, and presentation parity.

## Behavior, result, and typed effects

This slice adds data only. TG3 continues to derive every setup and turn effect. Presentation data
is never projected into mechanics and cannot alter eligibility, costs, random draws, or outcomes.

## Failure, replay, and rollback contract

Malformed schema, duplicate/dangling IDs, hash drift, presentation drift, missing provenance, or
non-original attribution fails validation before the fixture is test-materialized. No live state is
changed. Runtime replay/rollback remains owned and covered by TG3.

## Implementation sequence

1. Record confirmation and activate this document.
2. Add presentation component metadata/schema.
3. Add the mechanical/presentation entity fixture and provenance ledger.
4. Add closed validation tests and materialize the exact fixture in disposable state.
5. Run catalog/focused checks and record `TG4-SLICE-1-RECEIPT.md`.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Identity/hash | Entity/scenario/version/hash/rules-profile parity and deterministic recomputation pass. |
| Schema | Both components validate with exact registered application component versions. |
| Graph | Eight unique landmarks, eight valid legs, branch convergence, and start-to-finish reachability pass. |
| Economy/policy | Resources, market weights, defaults, paces, rations, services, and event deltas resolve. |
| Presentation | Every authored mechanical ID has one bounded human-readable entry; no extras exist. |
| Provenance | Original-content ledger exists and names no external code or asset dependency. |
| Compatibility | Existing TG3 focused tests and catalog activation remain green. |

## Verification commands

Focused TG4/TG3 tests, disposable `roleplay validate catalog`, JSON/link/whitespace/diff audit, and
an isolated build. Full-suite acceptance belongs to TG4.3.

## Completion receipt and exit gate

After the matrix passes, record `TG4-SLICE-1-RECEIPT.md`, mark TG4.1 verified, make TG4.2 the only
active leaf, and stop without claiming a balanced or complete starter scenario.
