# Sol review — Slice 5 staged content transformation boundary

Status: **approved by Sol after corrective review**
Review owner: `gpt-5.6-sol` high
Scope: development-time manifest and staging tooling only; no catalog record, runtime behavior, or
D&D rule is being submitted for activation.

## What was built

- A closed source/content manifest with exact source SHA-256, source key/revision/path, selected
  record/payload pointers, candidate target kind/ID/path, target payload schema, license facts,
  source verification state, and ruleset alignment.
- A deterministic transformer that validates the pinned source bytes and payload schema, then emits
  a staged candidate envelope preserving source, license, ruleset, and mapping provenance.
- A dry-run report plus whole-batch rejection for blocked licensing, duplicate candidate targets,
  stale hashes, and existing target paths. Staging can write only beneath an explicit staging root.

## Evidence to inspect

- [5A contract](../contracts/content-transform-manifest.schema.json)
- [candidate envelope](../contracts/staged-content-candidate.schema.json)
- [transformer](../tools/Invoke-ContentTransformation.ps1)
- [focused harness](../tools/Test-ContentTransformation.ps1)
- [neutral fixture](../fixtures/content-transform-manifest.valid.json)
- [adoption policy](../../ADOPTION-POLICY.md)

## Decisions requested from Sol

1. Confirm that Slice 5’s permitted staging dispositions exactly match the development policy:
   reviewed first-party recovery, MIT software with preserved notices, and independently verified
   CC BY SRD content. All other classification/disposition pairs are blocked before staging.
2. Confirm the manifest is deliberately *candidate-only*: candidate IDs/paths are not catalog IDs or
   source activation. Collision checks use an operator-supplied target root and do not replace the
   later owner/canonical-ID review.
3. Confirm the D&D-owned gate is sufficient for the next use: it requires
   `source.dnd2024.srd-5.2.1`, an exact locator, and `officialVerified: true`. A later rule-bearing
   slice must still perform Foundry review and its own owner/effect/transaction acceptance.
4. Review the explicit non-goal: the transformer is pointer selection plus target-schema validation;
   it must not become a rule translator, content author, schema rewriter, or catalog importer.

## Expected review outcome

- **Approve** the generic staging boundary, or identify an exact manifest field/constraint that must
  change before any real D&D content cohort is prepared.
- Do **not** approve or activate the fixture itself; it is neutral test data only.

## Review decision

Sol approved the corrected boundary after independently rerunning the focused harness. The accepted
evidence covers normalized-path collision rejection; root and descendant reparse-point rejection;
source, target-schema, mapping, and executing-tool hashes; D&D-owned commit/lock/source-review and
official-verification contract gates; and candidate-only staging without catalog activation.

## Known limits intentionally left for later slices

- Mapping/dependency reverse impact belongs to Slice 6.
- A real D&D cohort needs a per-item owner/SRD/Foundry review and a confirmed import boundary.
- The transformer does not write `catalog/`, SQLite, source overlays, or runtime components.
