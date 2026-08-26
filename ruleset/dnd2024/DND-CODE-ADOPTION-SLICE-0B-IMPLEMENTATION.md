# D&D code adoption Slice 0B implementation — adoption and provenance contract

Status: **accepted 2026-08-25**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption plan, Slice 0 / 0B](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable** — this slice imports or interprets no D&D rule/content.
Outcome: Establish the closed selective-transplant, licensing, attribution, provenance, coverage,
model-assignment, and prohibited-material contracts that every future recovery/import candidate must
satisfy before implementation or activation.
Exclusions: Populating the coverage matrix; classifying an archived feature; selecting/adapting donor
code; copying license/SRD prose into runtime content; legal advice; catalog/application source
registration; runtime IDs/dependencies; component/projection/effect schemas; public operations;
migrations; database access; and modification of `old-dnd/`.
Allowed files/areas: this document; `ruleset/dnd2024/adoption/ADOPTION-POLICY.md`; JSON Schemas and
positive/negative examples under `ruleset/dnd2024/adoption/contracts/`;
`ruleset/dnd2024/adoption/tools/Test-AdoptionContracts.ps1`; one receipt under
`ruleset/dnd2024/adoption/evidence/`; the dependency plan and roadmap status.
Stop point: Stop when policy and schemas require exact source bytes/commit/path/symbol/license/SRD
locator/owner/dependency/transformation/test/review evidence, reject prohibited candidates, validate
the positive examples, reject the negative examples, and change no runtime/catalog/database/archive
artifact.

## Confirmed decisions

- The user's 2026-08-25 instruction to implement Slice 0 confirms the selective-transplant policy,
  proposed donor pins, permanent development-tooling contract paths, and no automatic activation.
- Slice 0A accepted exact source fingerprints and a reproducible donor baseline. Test success is not
  license evidence and test failure is not permission to repair donor source in place.
- `old-dnd/` is first-party historical evidence but non-authoritative until an exact subset passes a
  later recovery slice.
- Schema IDs are file-local development-tooling URNs, not catalog/application/public/runtime IDs.
- This repository policy is an engineering gate based on source licenses/notices; it is not a legal
  opinion. Unknown or mixed scope blocks reuse until explicitly reviewed.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| D&D rule/content meaning | Not selected in this slice. | Future D&D-owned slice plus `source.dnd2024.srd-5.2.1` exact locator | A candidate cannot become implementation-ready without an exact official locator. |
| Donor behavior | Engineering evidence only. | Candidate provenance row | Donor tests/results never become rule authority. |
| State/effects | Not read or changed. | Application kernel/SQLite/catalog JavaScript | Contracts may describe future owners/effects but cannot register or execute them. |

## External implementation reference

- The pinned `dnd-srd-engine` LICENSE/NOTICE separate MIT engine code from a CC BY 4.0 starter pack
  and SRD reference submodule. The notice itself warns that some starter entries require independent
  SRD confirmation, so content is approved only per exact item and official locator.
- The pinned Foundry LICENSE covers software under MIT; its README identifies SRD 5.1/5.2 content as
  CC BY 4.0 and assets as separately licensed. Repository policy therefore keeps Foundry
  reference-only by default and prohibits assets.

No external behavior or source file is reused by this slice.

## Prerequisite evidence

- [Slice 0A receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-0A-RECEIPT.md) proves exact source,
  tree, license/notice hashes, build/test baseline, reference isolation, and safe cleanup.
- [Donor lock](adoption/donor-lock.json) owns exact URLs/commits/roles and forbids floating refs,
  automatic activation, and production dependency.

## Runtime artifacts

None. Development-only artifacts:

- policy Markdown with closed source roles, allowed/rejected dispositions, attribution, source
  precedence, review, and activation rules;
- provenance-ledger JSON Schema plus positive and prohibited-material negative examples; and
- coverage-matrix JSON Schema plus one minimal valid example for Slice 1 generation; and
- a development-only validator that parses/compiles both schemas, accepts positive examples, and
  requires every declared negative mutation to fail.

## Authoritative state and closed input

The exact donor lock/baseline owns source identity. Each future provenance row must name one target
candidate, exact source repository/commit/path/symbol/hash, license scope/disposition, transformation,
existing owner/dependencies, rule-alignment/source-locator state, tests, review state, and target
hash when generated. The coverage row must join active, archive, donor, SRD, Foundry, conflict,
disposition, model, dependency, and test evidence for one capability key.

Callers/models may not assert that a license is approved, a rule matches SRD, an owner is absent, a
schema is compatible, tests pass, or activation is safe without the required evidence fields and
separate repository confirmation gate.

## Behavior, result, and typed effects

- Closed enums distinguish first-party archive, MIT software, CC BY SRD content, reference-only,
  blocked mixed/unknown/non-SRD material, and rejected candidates.
- A reusable MIT-code row requires exact copyright/license preservation evidence.
- A reusable CC BY row requires exact official `source.dnd2024.srd-5.2.1` locator, attribution text,
  source/change indication, and content-only target classification.
- Foundry starts `reference-only`; changing that disposition requires per-symbol MIT scope and
  Foundry-independence review. Foundry assets remain prohibited.
- Generated output remains `candidate` until hashes, transformation notes, semantic review, tests,
  and explicit activation/acceptance gates pass.
- Any `unknown`, `mixed`, `non-srd`, `premium`, `asset`, floating-ref, missing-hash, missing-locator,
  or conflicting-owner condition is blocked rather than inferred.

Typed effects and transactions: none.

## Failure, replay, and rollback contract

Malformed/extra properties, missing exact pins/hashes, unbounded text/arrays, unsupported license or
model names, CC BY reuse without locator/attribution/change indication, executable code marked as
CC-BY content reuse, reference-only Foundry marked importable, or an accepted row with unresolved
conflicts must fail validation. Validation is deterministic and read-only. There is no rollback
because no runtime state is written.

## Implementation sequence

1. Write the policy from exact pinned license/notice evidence and repository architecture.
2. Add bounded Draft 2020-12 schemas for provenance and coverage rows.
3. Add minimal valid examples and deliberate invalid examples covering prohibited assets, unknown
   licenses, missing SRD locator, and premature acceptance.
4. Parse/check schemas and validate positive/negative examples with an independent JSON Schema
   implementation.
5. Check links, prohibited runtime paths, `old-dnd`/catalog non-mutation, and diff whitespace.
6. Write receipt, accept Slice 0B/parent Slice 0, and stop before Slice 1 inventory generation.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Exact provenance | Repository, commit, path, source hash, license scope, transformation, target, and review fields are closed/bounded. |
| SRD/CC BY | Content reuse requires official source ID, exact locator, attribution, and change indication. |
| MIT | Code reuse requires exact MIT scope and notice-preservation evidence. |
| Prohibited material | Foundry/mixed assets, premium/non-SRD content, unknown licenses, and floating refs fail. |
| Ownership | Active/archived/donor candidates and conflicts cannot silently create a duplicate owner. |
| Models | Only exact `gpt-5.6-luna`, `gpt-5.6-terra`, and `gpt-5.6-sol` assignments with bounded reasoning are valid. |
| Candidate lifecycle | Generated/recovered/imported output cannot be `accepted` with conflicts or missing reviews/tests. |
| Boundary | No catalog, runtime, database, public surface, application source, or archived file changes. |

## Verification commands

- Parse every new JSON/JSON Schema document.
- Draft 2020-12 schema self-check and positive/negative example validation.
- Verify exact lock/baseline hashes referenced by policy.
- Search changed paths for prohibited runtime/catalog/archive changes.
- Local Markdown link validation and `git diff --check` for Slice 0 files.

Full solution tests, catalog validation, and protocol walk are not applicable because this slice adds
only development policy/schema/example documents.

## Completion receipt and exit gate

Write `adoption/evidence/DND-CODE-ADOPTION-SLICE-0B-RECEIPT.md`, mark Slice 0B and parent Slice 0
accepted, and identify Slice 1A as the next planned leaf. Stop before generating the real coverage
matrix, classifying/importing any rule/content, registering a source/schema/projection, executing a
donor function, or changing live/runtime state.
