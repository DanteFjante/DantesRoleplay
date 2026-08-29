# D&D code adoption Slice 4A implementation — neutral conformance scenario contract

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 4A
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this slice does not encode or calculate a game rule.
Outcome: Define a closed, portable scenario document that can carry declared context, closed input,
deterministic seed material, selected output pointers, and source provenance for conformance checks.
Exclusions: No game mechanic, catalog activation, state migration, donor runtime, archive activation,
or production-host change.
Allowed files/areas: `ruleset/dnd2024/adoption/conformance/**`, Slice 4 evidence, and the two
adoption planning status lines.
Stop point: A schema-valid neutral fixture and explicit rejection cases exist; conversion and
comparison are reserved for 4B and 4C.

## Confirmed decisions

- The existing approved adoption boundary permits reusable tooling before a rule-bearing cohort.
- Scenario state is supplied as opaque JSON. The contract neither interprets IDs nor derives values.
- A source result is only implementation evidence; its provenance is required but it never becomes
  canonical game-state authority.

## External implementation reference

No Foundry review applies: this contract is not D&D-owned behavior and contains no rules logic.

## Prerequisite evidence

- Slice 3C proves that a declared, immutable operation view can be passed safely to catalog
  JavaScript: `adoption/evidence/DND-CODE-ADOPTION-SLICE-3C-RECEIPT.md`.
- The parent dependency plan marks the three Slice 4 leaves as ready after Slice 3.

## Runtime artifacts

- New confirmed, file-scoped contract: `adoption/conformance/contracts/conformance-scenario.schema.json`.
- New examples: a valid generic scenario and a negative-case manifest.
- No permanent runtime ID, public endpoint, catalog record, or schema meaning change is introduced.

## Authoritative state and closed input

`context`, `input`, and `seed` are opaque test-vector values. Each case declares the exact output
JSON Pointers that may be compared. Callers cannot insert executable code, expected outcomes, or
undeclared source values into a normalized scenario.

## Behavior, result, and typed effects

The contract accepts only a finite nonempty case list, unique case IDs, and ordered output pointer
names. It does not execute a case, calculate a result, mutate state, or emit effects.

## Failure, replay, and rollback contract

Malformed documents, unknown properties, duplicate IDs, malformed JSON Pointers, empty source
identity, and empty comparison fields are invalid. There is no transaction or mutable state.

## Implementation sequence

1. Add the closed Draft 2020-12 schema and valid/invalid examples.
2. Compile the schema and validate both positive and negative cases.
3. Record a receipt; leave conversion and comparison to subsequent leaves.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Valid opaque scenario | accepted |
| Extra top-level property | rejected |
| Duplicate case ID | rejected |
| Empty output-pointer list | rejected |
| Malformed pointer | rejected |

## Verification commands

`pwsh -File ruleset/dnd2024/adoption/conformance/tools/Test-ConformanceTooling.ps1 -Stage 4A`

## Completion receipt and exit gate

Write `adoption/conformance/evidence/DND-CODE-ADOPTION-SLICE-4A-RECEIPT.md` after the contract
tests pass. Stop before any source conversion or native/donor comparison.
