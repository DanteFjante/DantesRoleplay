# Application source profiles Slice 1 receipt — optional D&D extension packaging

Status: **accepted**  
Completed: 2026-08-26  
Accepted implementation: [Slice 1](../APPLICATION-SOURCE-PROFILES-SLICE-1-IMPLEMENTATION.md)

## Delivered

- Added the closed D&D extension-package schema and the inert
  `dnd2024-extension.legacy-equipment` package manifest outside the `dnd2024-core` catalog glob.
- Classified the package as compatibility content, required exactly `dnd2024-core`, and fixed
  `enabledByDefault` to false.
- Updated the existing D&D source-registry procedure with the separate directory, source ID, glob,
  precedence, dependency, and pre-campaign selection boundary.
- Proved with disposable registrations that core-only preview contains no extension file, while an
  explicit core-plus-extension profile contains the package manifest, has a different fingerprint,
  and remains deterministic when selected IDs are reordered.
- Added negative manifest checks for an enabled-by-default package, an unknown classification, a
  missing core dependency, and unknown fields.
- Kept the scaffold inert: it contains no component, entity, mechanic, procedure, query,
  JavaScript, rule interpretation, typed effect, or campaign state.

## Evidence

- Focused extension packaging checks: 3 passed, 0 failed.
- D&D 2024 core plus extension packaging checks: 83 passed, 0 failed.
- Full shared suite: 1,100 passed, 0 failed.
- Release solution build: 0 warnings, 0 errors.
- Catalog validation: 144 records valid with 21 existing near-duplicate advisories; no live data was
  touched.
- `git diff --check`: passed; only existing line-ending advisories were emitted.

## Deliberate exclusions and next gate

This slice does not add either quarantined legacy equipment record, a component schema, a rule,
automatic dependency expansion, source-registration persistence changes, live source rows, UI
selection, or campaign migration. The next coherent leaf is a separately source-reviewed optional
content-family slice, beginning with one schema-compatible legacy equipment candidate only if its
exact non-core meaning and provenance remain explicit.
