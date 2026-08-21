# Subsystem implementation handoff

Status: Template — no active assignment

This file is a small execution envelope for one already-approved feature slice. The active feature
implementation document owns behavior, artifacts, acceptance, and D&D 5e 2024 alignment. Do not
copy those contracts here and do not implement from this unfilled template.

## Required reads

1. `AGENTS.md`
2. `docs/IMPLEMENTATION_DOCUMENT_READING.md`
3. the one active feature implementation document named below
4. its selected dependency-tree path and named owners
5. the relevant ruleset adapter, if any

Author or repair the source documents through `docs/DEPENDENCY_TREE_AUTHORING.md` and
`docs/FEATURE_IMPLEMENTATION_AUTHORING.md`; an implementation model must not guess missing fields.

## Assignment envelope

- Assignment ID: REQUIRED
- Status: `draft` or `active`
- Active feature document: REQUIRED
- Dependency tree and ready leaf: REQUIRED
- Subsystem/owner: REQUIRED
- Ruleset alignment: `dnd2024-owned`, `dnd2024-compatible`, or `ruleset-neutral`
- Source ID and exact locator: REQUIRED for `dnd2024-owned`; otherwise not applicable
- Model profile: high-planning, standard-implementation, or small-mechanical
- Allowed files/artifact IDs: REQUIRED
- Explicit exclusions: REQUIRED
- Required verification and receipt location: REQUIRED
- Stop point: REQUIRED

`active` is valid only when the named feature document is active, all semantic gates are confirmed,
the dependency leaf is ready, and no field above is missing.

## Model-fit gate

Use a small-mechanical model only when ownership, artifact IDs, input/state semantics, expected
results, cleanup, and escalation are all closed. Use a stronger planning model when a migration,
schema meaning, public surface, permanent ID, new effect/event kind, house rule, or cross-owner
decision remains open.

## Execution contract

1. Produce the pre-edit reading receipt required by the reading protocol.
2. Verify the named prerequisites against catalog/code/tests and stop on a mismatch.
3. Change only the allowed slice; preserve unrelated dirty work.
4. Follow the feature document's transaction, failure, replay, rollback, and restoration contract.
5. Run its focused checks, catalog validation when applicable, and full suite at acceptance.
6. Read back authored artifacts when the governing procedure requires it.
7. Write the named concise receipt and stop; do not start a sibling or later slice.

## Escalation

Stop without broadening scope if an owner/version differs, another artifact owns the behavior, an
unapproved semantic boundary appears, catalog and live state conflict, recovery is unsafe, an
allowed file overlaps unrelated work, or acceptance semantics are ambiguous. Report the concrete
evidence and the smallest decision needed.

Completion means the active feature document's acceptance matrix passes, required state/fixtures
are restored, evidence is recorded, and no out-of-scope work was started.
