# D&D code-adoption Parent Slice 8 receipt — complete native recovery

Date: 2026-08-25  
Status: **accepted**  
Boundary: Every `recover-archive` D&D mechanic, component, and governing-procedure row in the
accepted Slice 1B coverage matrix

## Delivered

- Resolved all 51 classified mechanics across base Speed, turn budget/action economy, Conditions,
  character identity/origin, inventory transitions and readers, experience/progression, and seeded
  dice. D&D formulas and branching remain in catalog JavaScript.
- Resolved all 26 classified component rows as 25 current activated owners plus one explicit
  replacement: archived `dnd2024.source` is superseded by the immutable application-source registry
  and D&D source-governance procedure, with no duplicate campaign-state authority.
- Resolved all 39 classified governing procedures, including the Slice 7 weapon contracts and the
  play/ruleset/source-registry contracts recovered at closure.
- Fixed generic child-composition snapshot propagation so composed mechanics inherit exact observed
  component/containment revisions and reject conflicts before JavaScript execution. The C# kernel
  remains ruleset-neutral.

## Family evidence

- [8A — base Speed](DND-CODE-ADOPTION-SLICE-8A-RECEIPT.md)
- [8B — turn budget and action economy](DND-CODE-ADOPTION-SLICE-8B-RECEIPT.md)
- [8C — Conditions and D20 state effects](DND-CODE-ADOPTION-SLICE-8C-RECEIPT.md)
- [8D — character identity and origin state](DND-CODE-ADOPTION-SLICE-8D-RECEIPT.md)
- [8E — inventory canonical state and transitions](DND-CODE-ADOPTION-SLICE-8E-RECEIPT.md)
- [8F — inventory, currency, and carrying readers](DND-CODE-ADOPTION-SLICE-8F-RECEIPT.md)
- [8G — experience and class progression](DND-CODE-ADOPTION-SLICE-8G-RECEIPT.md)
- [8H — seeded dice primitive](DND-CODE-ADOPTION-SLICE-8H-RECEIPT.md)
- [8I — contract and disposition closure](DND-CODE-ADOPTION-SLICE-8I-RECEIPT.md)

## Parent acceptance

- Hash-pinned matrix comparison — passed: 51 mechanics, 26 component dispositions, 39 procedures,
  and zero unresolved rows.
- Full activated D&D suite — passed, 60/60; all 51 JavaScript mechanics pass syntax validation.
- Combined D&D/kernel/effect/application-seam suite — passed, 84/84.
- Solution build — passed with 0 warnings and 0 errors.
- Core catalog validation — passed, 144 records with 21 existing advisory warnings; no live data
  was touched.
- Full repository suite — passed, 1,062/1,062 plus 20/20 local-AI tests.
- `git diff --check` — passed with only existing line-ending notices.

## Deliberate exclusions

No archive deletion, live-state mutation, migration, public-operation expansion, donor runtime
dependency, static SRD content cohort, automatic character builder, full damage/dying automation,
or capability absent from the accepted matrix was added. Future work can extend these modular
owners through separately approved slices without making Parent 8's closure ambiguous.
