# Application kernel Slice 4 receipt — deterministic source overlays and candidate manifests

Status: **accepted**  
Completed: 2026-08-24

## Delivered

- Added a ruleset-neutral, immutable candidate-manifest model for generic scanned documents,
  winners, shadows, diagnostic problems, and deterministic fingerprints.
- Added a pure overlay resolver: highest trust is eligible first, then higher precedence wins;
  equal-trust/equal-precedence candidates conflict and produce no winner.
- Adapted registered allowed-root-relative sources to the existing local file/glob scanner. The
  adapter strips canonical paths, binary content, and scanner exception detail before exposing
  generic source metadata.
- Generic scanned files use only `file:<normalized-relative-path>` identity and remain
  non-executable. No catalog/application parser or application activation was introduced.

## Evidence

- Focused source-overlay tests: 3 passed, 0 failed.
- Local document-scanner suite: 19 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Full shared suite: 468 passed, 0 failed.
- `git diff --check` passed (line-ending notices only).

## Deliberate exclusions

- No migration, persistent candidate/active manifest, source-registration mutation, catalog import,
  declared-record parser, component/projection schema, vector/index update, AI prompt, activation,
  protocol endpoint, authorization behavior, state-space binding, or application-specific branch
  was added.
- Slice 5 owns versioned component type registration and bounded JSON Schema evaluation.
