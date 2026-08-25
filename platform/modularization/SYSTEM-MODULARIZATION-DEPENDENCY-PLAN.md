# System modularization dependency plan

Status: **Accepted; modular system, standalone local AI, unlinked game adapters, and final independence verified**
Ruleset alignment: **ruleset-neutral**  
Source: **not applicable**  
Downstream semantic redesign: [Generic application kernel](../application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md)

## Outcome and non-goals

Refactor the current modular monolith so that every system capability has one physical component
directory, one explicit contract boundary, one registration entry point, and tests beside that
boundary. The system kernel must build and run without a game pack. Game- or ruleset-specific
meaning remains in catalog data and JavaScript; temporary C# compatibility code is isolated outside
the system tree and removed as its catalog replacement lands.

The local-AI integration is the first hard extraction. It becomes a standalone, ruleset-neutral
component that can receive a concrete file, a directory, or a path containing glob wildcards,
enumerate the matching files deterministically, and expose generic documents to embedding,
indexing, and schema-bound completion services. It has no reference to world, campaign, character,
quest, mechanic, procedure, or D&D types. Consumers translate their own records into and out of the
generic document/task contracts.

This plan does not:

- change the three public MCP verbs or add a public operation kind;
- migrate the live database, catalog, or game state;
- preserve game rules in a better-organized C# directory;
- turn the modular monolith into microservices;
- require local AI for correctness or allow it to execute tools, SQL, shell commands, or writes;
- perform a big-bang namespace, project, or database rewrite; or
- authorize implementation beyond the lowest ready leaf.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Kernel/game placement rule | [`ARCHITECTURE.md`](../../ARCHITECTURE.md) | verified | C# is generic hosting infrastructure; variable rules and outcomes belong to catalog JavaScript. |
| Cross-domain platform capability ownership | [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md) | verified | Platform features must contain no D&D, campaign, character, quest, session, or world meaning. |
| Current local-model safety model | [`LOCAL_INTENT_ROUTING_PLAN.md`](../../LOCAL_INTENT_ROUTING_PLAN.md) | verified | Ollama is optional, schema-bound, no-tools, bounded, and outside the JavaScript sandbox. |
| Current local-model implementation | [Knowledge Slice 5C receipt](../../knowledge/KNOWLEDGE_AND_FACTS-SLICE-5C-RECEIPT.md) | verified but misplaced | Embedding/completion providers and bounded orchestration exist, but registration and task classes are knowledge/game-facing. |
| Project dependency direction | current `.csproj` files and [`DantesRoleplay.slnx`](../../DantesRoleplay.slnx) | conflicting | Domain, persistence, protocol, game workflows, and local AI are grouped by technical layer rather than capability. |
| Composition root | [`DataAccessServiceCollectionExtensions.cs`](../../DantesRoleplay.DataAccess/DataAccessServiceCollectionExtensions.cs) | conflicting | One data-access registration method owns dozens of unrelated generic and game-facing services. |
| Local AI provider contracts | `src/system/local-ai/DantesRoleplay.LocalAI/Contracts` | verified | Provider-neutral contracts are standalone; game-specific vector contracts remain with the Knowledge adapter. |
| Ollama adapters | `src/system/local-ai/DantesRoleplay.LocalAI/Providers` | verified | Adapters and options are standalone and own no consumer task identifiers. |
| Generic filesystem/glob ingestion | `src/system/local-ai/DantesRoleplay.LocalAI/Documents` | verified foundation | Literal files, recursive directories, `*`, `?`, `**`, stable deduplication, hashes, binary/text results, roots, and byte/count limits are covered. |
| Game-specific C# rule logic | character resolvers and campaign bootstrap code under `DantesRoleplay*` | conflicting | C# contains permanent `dnd2024.*` and `source.dnd2024.*` identifiers and rule validation/branching. |
| Game-facing workflows and protocol adapters | Campaign, Character, Quest, Story, Knowledge, Travel classes and MCP tool helpers | conflicting | These are compiled into the same assemblies and composition root as the generic kernel. |

## Target component rule

A component is a capability directory, not merely a namespace. Each component owns its contracts,
runtime implementation, optional persistence adapter, host registration, and focused tests. A
component may contain more than one project where an assembly boundary provides useful enforcement,
but unrelated components never share a catch-all `DataAccess`, `World`, `Retrieval`, or `Tools`
directory.

Recommended target shape:

```text
src/
  system/
    building-blocks/
    catalog/
    state/
    procedures/
    mechanics/
    actions/
    effects-and-transactions/
    events-and-notifications/
    operations-and-audit/
    deterministic-retrieval/
    local-ai/
      contracts/
      files/
      ollama/
      index/
      hosting/
      tests/
    snapshots/
    feedback/
    sqlite-hosting/
    mcp-protocol/
    catalog-tools/
  applications/
    dantes-roleplay-host/
  game-adapters/
    dantes-roleplay/
      campaign/
      character/
      knowledge/
      quest/
      story/
      travel/
catalog/
```

The names are proposed layout labels, not permanent runtime IDs. Confirm them before the first
move. Existing game-specific C# may pass briefly through `game-adapters/dantes-roleplay/` to make
the dependency direction honest, but that directory is quarantine, not a new authority for rules.
Rules, eligibility, derived values, and outcomes still move to catalog schemas/data/JavaScript.

Allowed dependency direction:

```text
application host
  -> game adapters (optional)
  -> system component hosting entry points

game adapters
  -> public contracts of system components

system components
  -> building blocks and explicitly named component contracts only

local-ai
  -> its own contracts and general-purpose runtime libraries only
  -X-> game adapters, catalog semantics, main game database, or MCP tools
```

No component reaches into another component's persistence classes. Cross-component calls use a
small public port or a declared message/result contract. Each component exposes one idempotent
registration method; the application host composes them explicitly.

## Local-AI component contract

### Input discovery

The component accepts one or more path specifications. Each specification behaves as follows:

- A concrete file selects that file.
- A concrete directory means every regular file below it recursively, equivalent to `**/*`.
- A path containing wildcard segments resolves the non-wildcard prefix as its scan root and the
  remainder as a glob. `*`, `?`, `**`, and character ranges use one documented, cross-platform
  grammar.
- Relative paths resolve against an explicit caller-supplied base directory, never the process's
  accidental working directory.
- Matches are normalized to absolute paths, de-duplicated, and returned in stable ordinal order.
- Symbolic links/reparse points are not followed by default. A caller must opt in, and traversal
  must remain under an allowed root.
- No file type is silently treated as game content. Registered readers may decode text formats;
  unsupported, binary, inaccessible, oversized, or changed-during-read files produce bounded
  per-item outcomes rather than fabricated documents.
- Count, individual-size, total-byte, depth, and timeout limits are mandatory configuration.

The output is a generic source manifest and generic documents with source key, absolute/relative
path, media type, content hash, byte length, observed modification time, and decoded text/chunks.
It contains no game ID, entity type, audience, ruleset scope, or authorization decision.

### Model and index seams

- `ITextEmbeddingProvider` and the schema-bound completion provider move into local-AI contracts.
- Ollama options move under `LocalAI:*`; the adapter keeps loopback, identity, timeout, concurrency,
  prompt/output, schema, and deterministic-failure checks.
- Game-named allowed task classes leave provider defaults. The host explicitly registers opaque
  task definitions; prompts, schemas, and semantic validation remain with the consumer adapter.
- The index stores generic source/document/chunk keys plus provider identity and content hash. It
  has no `WorldId`, `KnowledgeId`, procedure ID, mechanic ID, or foreign key into game tables.
- Prefer a component-owned SQLite file for the local-AI derived index. It is disposable and can be
  rebuilt from sources; the game database remains authoritative for game state.
- The component returns candidates or validated JSON only. A consumer owns interpretation,
  authorization, citations, action construction, and every write boundary.
- Disabled/unavailable AI returns explicit status so each consumer can retain its deterministic
  fallback.

### Consumer adapters

The current knowledge-answer, knowledge-read, action-route, and story-plan flows do not move into
local AI. They remain consumer adapters until their game meaning is catalog-owned or otherwise
removed. Each adapter supplies bounded documents/tasks, invokes the neutral component, verifies
the result against its own current authoritative records, and performs no write based only on model
output.

## Dependency tree

```text
Ruleset-neutral modular system                                         [accepted]
├─ A. Architectural boundary and migration map                         [verified]
│  ├─ Current source/project/registration inventory                    [verified]
│  ├─ Target component directory convention                            [verified]
│  └─ Dependency and forbidden-vocabulary ratchet tests                [verified]
├─ B. Standalone local-AI component                                    [verified foundation]
│  ├─ Provider-neutral completion/embedding contracts                  [verified]
│  ├─ File/directory/glob scanner                                      [verified]
│  ├─ Generic document model                                           [verified]
│  ├─ Ollama completion/embedding adapters                             [verified]
│  ├─ Generic derived index                                            [verified downstream; disposable interaction index]
│  └─ Knowledge/routing/story/information consumer adapters            [verified]
├─ C. Per-component composition                                        [verified]
│  ├─ One registration entry point per component                       [verified]
│  ├─ Central DataAccess registration reduced to compatibility shim    [verified]
│  └─ Host explicitly selects components and game adapters             [verified; Slice 24]
├─ D. Generic kernel decomposition                                     [verified; depends on A/C]
│  ├─ Catalog, state, procedures, mechanics, actions                    [verified; moved]
│  ├─ Effects/transactions, events/notifications, operations/audit     [verified; moved]
│  ├─ Snapshots and feedback                                           [verified; moved]
│  └─ Deterministic retrieval, SQLite hosting, and MCP protocol         [verified]
├─ E. Game-code eviction                                               [accepted compile boundary; Slice 24]
│  ├─ Hard-coded game implementations excluded from generic compilation [accepted]
│  ├─ Game workflows retained only as uncompiled compatibility sources  [accepted user-directed exception]
│  ├─ Game-specific protocol dispatch removed from generic host         [accepted]
│  └─ Generic production paths guarded against game vocabulary/deps     [verified]
└─ F. Final independence proof                                         [accepted; Slice 12H]
   ├─ Build/test system without game adapter or ruleset catalog         [verified]
   ├─ Build/test application host without compiled game adapter         [verified]
   ├─ Verify local AI with arbitrary non-game directories               [verified]
   └─ Retain uncompiled legacy files without generic references         [accepted user-directed exception]
```

## Conflicts and decisions

1. **Ruleset-specific C# is an architecture violation, not just a folder problem.** Moving the
   character resolvers into a `dnd2024` C# directory would make dependency direction clearer but
   would not satisfy the authoritative boundary. Their calculations and eligibility must be
   re-authored through existing catalog mechanics or new confirmed catalog slices.
2. **The current local AI is provider-neutral at its lowest seam but game-coupled in composition.**
   The Ollama HTTP mechanics can be extracted. `KnowledgeVector*`, knowledge background jobs,
   route selection, prompts, schemas, and semantic validators remain outside the component.
3. **One shared DbContext is a migration hazard.** Do not split every table or migration first.
   Initially move repositories/configuration behind component ports while retaining the database
   and migration history. Decide database ownership per component only after code boundaries pass.
   Local AI is the exception: its derived index should be separate because it must be disposable
   and independent of game state.
4. **Namespace moves are public-surface changes inside the solution.** Add short-lived forwarding
   facades where they keep slices reviewable; do not retain duplicate implementations.
5. **The dirty worktree contains concurrent information work.** Refactor slices must inventory and
   preserve those files, avoid broad mechanical rewrites, and rebase their component placement on
   the then-current tree before moving anything.
6. **Directory isolation needs enforcement.** Project references plus architecture tests must fail
   when a system component references a game-adapter assembly, imports a game namespace, embeds a
   known ruleset ID, or registers another component's concrete implementation.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | Architecture inventory and dependency ratchet | Current tree | Record every production file, service registration, project reference, database owner, and known game-specific literal; tests allow the legacy set but fail on growth. No behavior or namespace change. |
| 2 | Confirm component convention and scaffold empty directories | Leaf 1 | Confirm the target labels and allowed dependency graph; add component-local readmes/ownership and build checks, with no source move. |
| 3 | Extract local-AI contracts and Ollama adapters | Leaf 2 | Local-AI projects have no reference to any DantesRoleplay game/domain assembly; existing provider tests pass through compatibility registrations. |
| 4 | Implement generic file/glob ingestion and derived index | Leaf 3 | Concrete file, directory, `*`, `?`, and `**` cases; ordering, duplicates, symlink escape, access failure, binary/unsupported files, limits, cancellation, hash/change detection, and rebuild tests pass against non-game fixtures. |
| 5 | Convert existing AI flows to consumer adapters | Leaf 4 | Knowledge, route, and story tests retain bounded prompts, semantic validation, authorization, stale-read checks, and deterministic fallback; local AI contains none of their vocabulary. |
| 6 | Split composition root by system component | Leaf 2 | Each component owns registration; the compatibility root delegates only; host enable/disable tests prove optional modules are absent when not selected. |
| 7 | Move generic capabilities component by component | Leaves 1, 2, 6 | For each move, focused behavior/replay/rollback tests pass, public compatibility is deliberate, and no sibling is bundled merely because it shares DataAccess. |
| 8 | Evict compiled game features | Leaves 1, 6, 7 | **Accepted by Slice 24:** generic projects no longer compile the retained game-adapter trees; files remain on disk by explicit user direction. |
| 9 | Recompose hosts and remove shims | Leaves 3-8 | **Accepted by Slice 24 and Slice 12H:** the generic host builds/tests without a game pack and the full suite/catalog/protocol evidence passes. |

Do not implement leaves 7 or 8 as one change. Each component move or one coherent game-rule
eviction gets its own active feature document, named owner, acceptance matrix, and receipt.

## Lowest ready leaf

Slices 1–23 verified the architecture ratchets, component convention/composition, generic
capability moves, quarantine placement, and standalone local-AI foundation. [Slice 24](SYSTEM-MODULARIZATION-SLICE-24-RECEIPT.md)
removed the retained game-adapter trees from generic compilation and host composition without
deleting them. [Interaction-orchestration Slice 12H](../interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-12H-RECEIPT.md)
then passed the full generic-build and local-AI independence matrix. No modularization leaf remains.

Application registration, source overlays, versioned component schemas, state spaces, and
application-scoped ECS semantics belong to the downstream application-kernel plan. Do not add them
to a physical-move or compiled-rule-eviction slice in this plan.

## Confirmation gates

Confirmation is required before:

- adopting the target component directory labels and assembly boundaries;
- fixing the path/glob grammar, allowed-root policy, file limits, or local-AI index location;
- changing namespaces, public interfaces, configuration keys, or service-registration behavior;
- moving or splitting EF ownership, creating migrations, or changing database files;
- replacing any game-specific C# behavior with a catalog mechanic or changing its semantics;
- adding/removing public MCP kinds or changing the three-verb contract;
- deleting legacy source directories or compatibility shims; and
- declaring the refactor accepted after the full independence proof. **Confirmed by the user's
  2026-08-25 instruction to finish the kernel upgrade.**

## Acceptance matrix for the completed refactor

- **Positive:** each component registers and performs its capability when explicitly selected.
- **Negative:** the system-only host cannot resolve game adapters or game-specific services.
- **Boundary:** architecture tests reject forbidden project references, namespaces, literals, and
  concrete cross-component registrations.
- **Local AI:** arbitrary temporary non-game trees exercise exact files, recursive directories,
  glob patterns, mixed formats, limits, cancellation, and deterministic incremental re-scan.
- **Fallback:** disabled/missing/wrong/slow Ollama and unavailable index preserve deterministic
  consumer behavior and make no state change.
- **Replay/rollback:** existing action/effect/event transaction tests remain byte-stable across
  component moves; local-AI derived data never joins a game transaction.
- **Compatibility:** the DantesRoleplay application composes the same public MCP surface while the
  generic system test host composes no game pack.
- **Catalog authority:** catalog validation passes after each later catalog slice; no rule remains
  duplicated in C# at final acceptance.
- **Repository:** solution build, full test suite, catalog validation, relevant protocol walk, and
  `git diff --check` pass against the same worktree.

## Planning receipt

- Runtime artifacts created: none.
- Catalog records, permanent IDs, migrations, and public kinds created: none.
- Proposed implementation order: boundary ratchet, local AI, component composition, generic moves,
  game-code eviction, final recomposition.
- Deliberate stop: this document and its roadmap link only.
