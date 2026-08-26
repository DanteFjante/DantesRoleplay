# Application source profiles Slice 0 implementation — exact source-subset activation

Status: **accepted 2026-08-26**  
Owner/roadmap: [Application source profiles dependency plan](APPLICATION-SOURCE-PROFILES-DEPENDENCY-PLAN.md)  
Dependency tree/leaf: Slice 0  
Ruleset alignment: `ruleset-neutral`  
Source: not applicable  
Outcome: preview and activate an exact registered-source set before state-space creation.  
Exclusions: source registration mutation, trust changes, content semantics, automatic extension
discovery, campaign migration, and live-state materialization.  
Allowed areas: application preview/activation contracts and services; query/commit and system
capability adapters; focused tests; component metadata, roadmap, and receipt.  
Stop point: two source profiles produce separate deterministic activation fingerprints and exact
state-space bindings, while legacy null selection retains all-source behavior.

## Confirmed decisions

The user confirmed the public and semantic changes on 2026-08-26 under the core-versus-extension
rule. No new database column or migration is required because activation history already persists
the exact included source manifest.

## Authoritative state and closed input

Registered source IDs remain `source-registry` authority. Preview accepts `sourceIds` as null or a
bounded list. Null means all registered sources; an array means exactly its unique registered IDs.
The service sorts IDs ordinally before hashing. Unknown, duplicate, blank, or over-limit selections
fail without preview, activation, or state-space changes.

## Behavior and failures

Only selected sources contribute scanned documents, scan problems, source summaries, overlay
winners/shadows, and fingerprints. Activation recomputes the same selected preview, includes the
canonical list in request evidence, and retains the exact source manifest. Existing dry-run, stale
preview, replay, audit rollback, trust, and overlay rules remain unchanged.

State-space creation continues to require the exact current activation fingerprint. Once application
state exists, switching profiles continues to return `MIGRATION_REQUIRED`.

## Acceptance matrix

| Case | Expected |
| --- | --- |
| Compatibility | null selection includes all registered sources |
| Core-only | explicit core ID excludes extension documents and problems |
| Core plus extension | both sources participate and fingerprint differs from core-only |
| Determinism | reordered valid IDs produce the same canonical preview |
| Invalid | unknown, blank, duplicate, or excessive IDs fail with no activation |
| Replay | canonical selection is part of activation request identity |
| Binding | separately created state spaces retain their exact activation fingerprints |
| Mutation boundary | non-empty state space cannot switch profiles without migration |

## Verification

- focused application preview, activation, state-space, system-capability, and MCP protocol tests;
- solution build;
- full repository suite; and
- catalog validation because D&D policy documents change.

## Receipt and exit

Evidence is recorded in
[the Slice 0 receipt](receipts/APPLICATION-SOURCE-PROFILES-SLICE-0-RECEIPT.md). The implementation
stops before packaging an actual optional D&D content source.
