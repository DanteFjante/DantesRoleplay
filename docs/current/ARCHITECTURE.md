# Architecture

Last reviewed: 2026-09-01.

DantesRoleplay is a generic C# host for data-authored roleplaying games. The host knows how to execute and audit declared behavior; it must not know the rules or vocabulary of D&D or any other game.

## Sources of authority

| Concern | Authority |
| --- | --- |
| Generic execution, persistence, transactions, effects, retrieval, and protocol hosting | C# projects |
| Game rules, eligibility, calculations, and outcomes | Catalog JavaScript mechanics |
| Persistent state shape | Catalog component JSON Schemas |
| Authored development records | `catalog/` |
| A running game's campaigns, state, events, history, and MCP-authored records | SQLite |
| Finalized blob metadata and upload state | SQLite |
| Immutable runtime blob bytes | Content-addressed `blobs/` storage beside the database, or `BlobStorage:Root` |
| Contributor guidance | `docs/current/` |
| World-building working material, artwork, and maps | `docs/world/`, read only for the relevant world task |
| External rules sources | `docs/pdfs/`, used only as references while authoring catalog content |

Documentation, UI models, and plans are never runtime authority.

## Runtime flow

1. A client calls the MCP surface.
2. The host resolves the declared procedure and materializes only its declared context.
3. If rule behavior is needed, the host runs the selected catalog mechanic in the sandbox.
4. The mechanic returns a generic result and typed effects.
5. The C# host validates the envelope and effects, applies them transactionally, and records the operation.
6. Retrieval exposes authorized projections of the resulting state.

The host owns safety and consistency. The catalog owns meaning.

## The C# boundary

C# may:

- identify, store, version, and retrieve generic records;
- validate schemas, envelopes, capabilities, limits, and effect types;
- materialize declared inputs and execute JavaScript in a constrained sandbox;
- apply generic typed effects transactionally;
- audit operations and expose protocol-neutral results.

C# must not:

- special-case a ruleset, campaign, spell, class, species, condition, or event type;
- contain formulas or branching whose result can vary by game rules;
- infer gameplay semantics from a record ID;
- duplicate a catalog mechanic as a supposedly convenient host shortcut.

Generic security, resource, and transaction invariants remain host responsibilities.

## Project map

- `DantesRoleplay/` contains the domain model, ECS concepts, and generic kernel contracts.
- `DantesRoleplay.DataAccess/` contains SQLite persistence, retrieval, registrations, catalog access, and `Mechanics/JintMechanicEngine.cs`, the JavaScript sandbox implementation.
- `DantesRoleplay.MCPServer/` hosts the MCP endpoint and composes runtime dependencies.
- `DantesRoleplay.Tools/` implements catalog maintenance commands.
- `DantesRoleplay.Tests/` contains kernel, persistence, protocol, catalog, and feature tests.
- `DantesRoleplay.LocalAI/` owns provider-neutral AI requests, model discovery, structured-response
  validation, generated agent identity/capability prompts, direct in-process tool dispatch, and the
  Ollama provider. Its Codex provider uses a small client seam implemented by the host's app-server
  bridge.
- `DantesRoleplay.Web/` is a client of the runtime, not an authority for game state.
- `catalog/applications/dnd2024/` is the D&D 2024 catalog application. D&D-specific content belongs there or in shared catalog mechanics it explicitly uses.

## ECS lifecycle

- State spaces have an explicit generic scope. `runtime-state-space` contains ordinary live state;
  `application-publication` contains an application's discoverable published entities, and an
  application may own at most one such publication space.
- Every installed application receives one publication state space when the web surface is
  initialized. Web identity lives there as `system.web.page`; the landing entity also carries
  `system.web.index-page`. The separate web-content tables continue to own immutable HTML
  revisions and assets, referenced from the ECS page component rather than copied into it.
- System-owned pages such as home and the control center are not assigned to application
  publication spaces. Legacy content identities are never classified from their names. Migration
  reports every unlinked identity and applies only an explicit operator review; HTML revisions,
  activation pointers, assets, and their hashes remain owned and unchanged in the web-content
  tables.
- Web and AI clients discover navigation through `/api/web/applications`, not by scanning ECS.
  The publication API orders usable pages deterministically, excludes hidden and disabled entities,
  identifies the single index-page marker, and binds list cursors to application resolution
  fingerprints. Protected control routes expose hidden, disabled, missing-content, and malformed
  evidence without accepting extension-installation preset identities.
- The shared `system-navigation` web component uses only that publication model and its supplied
  URLs. Applications without a usable landing page remain visible with an explicit disabled state;
  secondary pages are keyboard-accessible menus, while home and control center remain a separate
  system-owned group. Direct `/ui/{slug}` requests are resolved through ECS publication state on
  every load, including browser refresh, and fail explicitly for hidden, disabled, missing-content,
  ambiguous, or otherwise invalid publication state.
- Browser components use the provider-neutral `system-client` module for bounded same-origin JSON
  requests, structured errors, publication cursors and resolution fingerprints, transient-read
  retries, and request cancellation. `application-navigation`, `application-page-host`, and the
  shared progress, error, empty-state, and structured-data views consume public application IDs and
  page slugs only. Published HTML is never inserted into another document: the page host resolves
  metadata and navigates to the server-owned `/ui/{slug}` document boundary.
- `outer-ai` and `inner-ai` are two presentations of the same `ai-workspace` contract. They discover
  providers and models from the host, show reasoning controls only when the selected model reports
  support, and submit messages, structured requests, tasks, plans, recipes, schedules, and continued
  subtasks through `/api/control/ai`. The browser may select an application and a separate runtime
  state space, but it cannot supply system prompts, tools, capabilities, extension presets, or
  extension-installation preset identities.
- The web AI gateway resolves the effective application fingerprint before execution, binds it to
  the durable conversation and direct-tool invocation, and rejects results if that resolution
  changes while work is running. Provider responses, structured data, and tool arguments are
  schema-validated before execution or display. Agent identity comes from the registered
  `web.outer` or `web.inner` profile, while the system prompt's capability section is generated from
  the exact direct tools authorized for that invocation. Tool activity and permitted reasoning
  summaries are stored as bounded conversation evidence; raw hidden reasoning is not exposed.
- Application play conversations use a separate durable continuity record keyed by verified
  principal, application, runtime state space, and caller-supplied session context. Every player
  turn and player-visible assistant reply is appended verbatim and ordered in SQLite; cache expiry
  or a different client does not create another conversation for the same binding. The play API
  exposes the current bounded window and cursor-based earlier transcript without turning transcript
  text into ECS authority.
- Play-facing outer and narration providers return a closed, schema-validated situation update and
  a bounded set of durable truths alongside the exact visible reply. The reply, situation change,
  and truth assertions are committed together. Active and completed situations preserve their
  participants, optional authorized entity references, location, and revision. Truths retain the
  exact assistant message and situation that established them, are deduplicated within the play
  session, and are supplied back to later turns as trusted narrative continuity. They do not replace
  verified mechanic effects or other authoritative ECS state.
- D&D's Current view prefers the exact `game.core.campaign.current-scene` projection. When that
  authoritative component is absent, it may show the active durable play situation as an explicitly
  recorded (non-ECS) continuity view, including up to twelve recent verbatim exchanges. The Current
  tab mounts the generic play-conversation element with the selected campaign as its session context
  and refreshes after recorded turns. Unknown or unauthorized location identities are never promoted
  into the location projection.
- Component-schema root annotations declare semantic roles and generic role constraints. The host
  enforces cardinality, required-role, and composite JSON-pointer uniqueness constraints for the
  state-space scope without knowing what a page or other domain role means.
- Mechanic role requirements distinguish required component snapshots from optional snapshots and
  required referenced-definition facets from optional facets. The resolver materializes only those
  declarations; an absent optional value remains absent rather than making the role unavailable.
- Mechanics that add components to an entity created in the same effect bundle declare those write
  identities in `effectComponentIds`. Writes to an existing entity still require a required or
  optional role snapshot so stale-state validation cannot be bypassed.
- Mutations reserve the SQLite writer before reading constraint state, then change components and
  validate the complete enabled-entity result in the same transaction. Effect batches validate
  once at their final state. Disabling excludes an entity; enabling revalidates every constraint.
- Component schemas are edited by appending immutable versions. A disabled component type keeps
  its exact version history available for existing references but is excluded from latest-type and
  component discovery, and cannot receive another version until re-enabled.
- Entity deletion is a reversible disable operation. Ordinary entity, component, containment, and
  relationship discovery excludes disabled identities; administrative lifecycle reads include
  them and report reference counts.
- Component-type IDs may be corrected atomically with their live ECS components; the old identity
  is retired when it has runtime components. Immutable projection and trigger definitions are not
  silently rewritten and are reported as blockers. Unused entity IDs may also be corrected, while
  entity components, edges, and trigger references remain explicit blockers.
- Permanent deletion requires the identity to be disabled and free of references. Local AI agents
  receive the same lifecycle service as direct in-process tools, but every lifecycle write requires
  trusted host confirmation.

## Catalog namespaces

- Namespace identity and metadata are stored in SQLite and exported as authored namespace files.
  The dotted namespace becomes the record's directory path; the final ID segment is its filename.
- A non-empty registry is enforced at the database save boundary for legacy catalog records and
  modern component types. Unknown, disabled, unreviewed, and wrong-kind namespace assignments
  cannot be saved through an alternate store. Catalog import is the explicit migration boundary
  that may preserve an already-authored record while reporting its unreviewed namespace.
- Namespace descriptions and aliases support discovery. Disabled namespaces are excluded from
  ordinary namespace and feature search while remaining administratively inspectable.
- Extension precedence is compiled during preview and activation from registered extension
  dependencies, conflicts, namespace contributions, and explicit ordering. Normal search resolves
  the effective application automatically; only operator diagnostics may include shadowed records.
  Exact qualified-ID inspection remains exact.

## General invariants

- Stable record IDs are contracts. Add or change them deliberately.
- Component schemas own stored state shape; mechanics do not invent undeclared state.
- Procedures declare the context and capabilities mechanics may use.
- Effects are validated before mutation and applied within the owning transaction.
- Live database records are exported and reviewed before corresponding authored files are changed.
- Web features read and write through supported runtime operations; they do not become a second game-state store.
- AI providers never call the MCP server to reach host functionality. They receive explicitly
  allowed `IAiTool` definitions, and the AI service validates arguments before dispatching those
  tools in process. The system-agent service aggregates context-bound tool sources for registered
  system capabilities, durable system tasks and plans, interaction recipes, scheduling, and every
  fine-grained operation published by the MCP host. Local models are not given the transport
  multiplexers `orient`, `query`, or `commit`; each underlying kind is a separately described tool.
- Verified multi-step recipes may recursively invoke the selected local provider once per bounded,
  dependency-ordered task. Each child receives prerequisite results and the same direct authorized
  tool surface, except the recipe-run tool itself. Recipe templates retain the shared 16-step graph
  limit, so recursive work cannot expand without bound.
- One-time and recurring local-AI tasks use the durable trigger scheduler. Fired tasks are audited
  and run without write-approval gates, so unattended work may read or prepare an inert plan but
  cannot confirm its own mutation.
- Read calls execute directly; writes retain capability preflight or trusted tool confirmation and
  idempotent execution. A model cannot confirm its own write. Secret capabilities require an
  explicit host opt-in.
