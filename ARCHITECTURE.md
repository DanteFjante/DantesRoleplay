# DantesRoleplay architecture

Status: **Authoritative code/data boundary**
Last reviewed: 2026-08-21

## Premise

DantesRoleplay is a small generic engine whose game is authored as data. C# provides storage,
transactions, sandboxing, retrieval, audit, and protocol transport. Catalog records provide the
world model, procedures, schemas, fixtures, and JavaScript mechanics. A new game rule should not
require recompiling the host.

The shortest placement test is:

> Is this required to safely host every game, or is it behavior that a ruleset/campaign may change?

The first belongs in C#. The second belongs in the catalog, normally as JavaScript plus declared
data contracts.

## Ownership map

| Concern | Authoritative owner | Never authoritative |
| --- | --- | --- |
| Generic persistence, versioning, transactions, sandbox, audit, retrieval, MCP transport | C# kernel | UI or chat context |
| Rule calculations, eligibility, rule branching, results | Catalog JavaScript mechanics | C# game-specific helpers |
| State shape and validation | Catalog component JSON Schemas | Repeated prose schemas |
| Authored worlds and source content | Catalog JSON entities/components/relationships | Hard-coded C# fixtures |
| Capability instructions and governance | Catalog procedure contracts | Roadmaps or old handoffs |
| Development catalog | Repository `catalog/` | Generated bootstrap copies |
| Running campaign/world/history | SQLite | Conversation memory or repository plans |
| Plans and implementation order | One subsystem roadmap plus one active feature plan | `STATUS.md` or receipts |
| Completed evidence | Tests and receipts/confirmations/validations | A completed plan's narrative |

Catalog procedure and mechanic Markdown files are individual runtime records. Their number is not a
reason to concatenate them: lookup by stable ID/category is part of the runtime design.

## Request flow

```text
MCP client
  -> orient | query | commit
  -> closed kind/payload validation
  -> procedure and catalog lookup
  -> mechanic selection by intent or explicit backend-owned binding
  -> C# materializes only declared context
  -> sandboxed pure JavaScript returns a structured result and typed effects
  -> C# validates the full effect batch
  -> guards, effects, events, reactions, notifications, and audit share one transaction
  -> committed state is read back from SQLite
```

The model may propose intent or catalog changes through governed paths. It does not receive SQL,
unrestricted filesystem access, arbitrary CLR access, caller-defined effects, or authority to turn
prose into committed truth.

## Kernel/game boundary

### C# may

- persist and version generic records;
- validate closed transport envelopes and catalog-declared schemas;
- materialize a mechanic's declared component/entity/event context;
- run JavaScript with time, statement, recursion, output, and effect limits;
- validate/apply the fixed generic effect vocabulary;
- own root transactions, replay protection, audit, and deterministic seed plumbing;
- provide generic catalog search, projections, orchestration seams, and protocol handlers.

### C# may not

- calculate Armor Class, damage, movement cost, class grants, quest outcomes, or other game rules;
- branch on `dnd2024.*`, campaign-specific, quest-specific, item-specific, or world-specific IDs;
- duplicate a mechanic in a resolver/service because C# is more convenient;
- accept caller-supplied values that should be derived from authoritative state;
- add a public MCP kind for a single game feature when an existing generic kind can carry it.

A C# coordinator may sequence generic, declared child capabilities when transaction or dependency
ownership requires it. The declarations and rule meaning remain catalog-owned; the coordinator must
not reproduce their rule logic.

## Governing invariants

1. **SQLite wins at runtime.** Conversation history is narrative context, never authoritative state.
2. **Catalog files win during development.** Import/export is an explicit synchronization boundary.
3. **Stable identity, append-only meaning.** Revised contracts and mechanics append versions; they
   are not silently overwritten.
4. **Mechanics are pure.** JavaScript receives declared input/context and returns data. It has no
   ambient store, network, filesystem, clock, randomness, or CLR authority.
5. **Context is declared.** C# may fetch only what the mechanic declaration asks for, before entering
   the synchronous sandbox.
6. **Inputs are closed.** Reject unknown fields and derive values from authoritative state whenever
   possible.
7. **Effects are typed and generic.** Validate the complete ordered list against a simulated state,
   then apply all or none.
8. **One root transaction.** Effects, guards, event ledger, reactions, notifications, success audit,
   and owned child actions commit or roll back together.
9. **Failures are useful and unchanged.** A rejection identifies the failing boundary and a callable
   recovery; no partial state or success evidence survives.
10. **Randomness is deterministic.** The root owns the seed and stable child derivation; replay is
    byte-for-byte meaningful.
11. **Reads are bounded projections.** Public recipes expose owner-approved views, not raw hidden
    state or accidental authorization.
12. **Three MCP verbs.** Extend `orient`, `query`, or `commit` by a reviewed closed kind; do not add a
    fourth tool for ordinary features.
13. **No UI authority.** Server-rendered or enhanced clients display and request; backend owners
    validate and commit.
14. **Audit is evidence, not state.** Operations explain what happened but never replace current
    owner records.

## Stack and projects

| Layer | Technology / project | Responsibility |
| --- | --- | --- |
| Domain | `DantesRoleplay` | Generic records, contracts, interfaces, value objects |
| Persistence | `DantesRoleplay.DataAccess` | EF Core/SQLite stores, coordinators, transactions |
| Rule sandbox | `DantesRoleplay.RuleAccess` | Jint execution and limits |
| Protocol host | `DantesRoleplay.MCPServer` | ASP.NET Core MCP surface and dependency registration |
| Tools | `DantesRoleplay.Tools` | Catalog validation/import/export and developer operations |
| Verification | `DantesRoleplay.Tests` | Unit, integration, catalog, replay, rollback, protocol tests |

The domain project stays free of infrastructure dependencies. SQLite is appropriate while one local
process owns the database; a different database is a deployment decision, not a game-feature fix.

## Runtime capabilities

- Versioned procedure contracts with governance and retrieval.
- Dynamic entity/component/relationship state validated by catalog schemas.
- Versioned JavaScript mechanics with declared requirements and deterministic execution.
- Typed effects with simulated validation and atomic application.
- Structural and declared events, pre-commit guards, deterministic reactions, and notifications.
- Operation history connecting reads, writes, procedures, mechanics, events, and failures.
- File-first catalog validation plus explicit import/export against a persistent database.
- Bounded world, campaign, quest, session, character, item, and ruleset projections built on the
  same generic kernel.

Feature status and remaining work do not belong here. See [STATUS.md](STATUS.md), the
[story-first roadmap](STORY_FIRST_ROADMAP.md), and the relevant subsystem roadmap.

## Verification contract

- Focused tests while iterating.
- `roleplay validate catalog` after catalog changes; it imports into a fresh migrated database.
- Full suite at feature acceptance.
- Protocol walk only after MCP surface or dependency-registration changes.
- Persistent catalog import only for explicit integration play or release.

A test must prove the same boundary it replaces: happy-path tests cannot stand in for rollback,
replay, authorization, or no-change evidence.

## Document ownership

- [AGENTS.md](AGENTS.md): short repository workflow and placement rules.
- This file: durable architecture only.
- [STATUS.md](STATUS.md): current cross-system summary and links.
- [KNOWN_ISSUES.md](KNOWN_ISSUES.md): current reproducible blockers only.
- One top-level subsystem roadmap: scope and ordered capabilities.
- One active feature plan: prospective slice details and stop gates.
- Receipts/confirmations/validations/ratifications: immutable evidence.
- Catalog contracts: runtime instructions and data definitions.

When a feature completes, preserve its receipt and authoritative catalog/code/tests. Remove or reduce
the completed plan instead of turning architecture, status, and roadmaps into duplicate histories.
