# Web Interface Feature 2 dependency plan — operator control center

Status: **complete — Slices 0–15 accepted**  
Ruleset alignment: **ruleset-neutral**  
Source: **not applicable**

## Outcome and non-goals

Give the authenticated operator one browser-native control center that can:

- inspect server health and change a closed set of safe server settings;
- browse committed effects together with their operation/audit context;
- exchange messages with the configured local LLM and with a locally running Codex agent;
- browse application state spaces, entities, components, component schemas, and published contracts;
- edit, preview, publish, and roll back database-authored web pages from the web interface itself.

This feature does not make the web layer authoritative for game rules, expose arbitrary database
tables, edit catalog files as live game state, reveal or store provider secrets, automatically
approve Codex actions, expose Codex or MCP through Tailscale Serve, add a frontend build toolchain,
or make untrusted uploaded HTML safe. Existing pages remain trusted operator-authored code.

## Requested capability map

The control center is planned as one versioned page bundle composed from browser-native custom
elements. The public UI IDs below were confirmed with Slice 0; Slices 1–3 now implement the shell,
effect history, and ECS explorer while the remaining elements keep truthful unavailable states.

| Proposed component | Responsibility | Server owner used |
| --- | --- | --- |
| `<server-settings-panel>` | Effective/pending setting values, validation, restart status | `dantes-roleplay-host` configuration |
| `<effect-history-panel>` | Committed change timeline with before/after and audit links | events ledger and operation history |
| `<assistant-panel>` | Provider status, conversations, streamed output, approvals | local AI and proposed Codex bridge |
| `<ecs-explorer>` | State spaces, entities, components, schemas, published contracts | application registry, ECS, catalog navigation |
| `<site-editor>` | Page/revision list, isolated preview, publish, rollback, ZIP export | existing web page revision owner |

The proposed page ID is `control-center`, and the proposed HTTP family is `/api/control/*`. A
single route family keeps the privileged surface auditable and lets the existing Tailscale remote
boundary include or exclude it as one unit.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Web page revisions and active pointer | `DantesRoleplay.Web` / `IWebPageStore` | verified | Slices 1–5 receipts and append-only page/asset tables |
| Local and Tailscale operator identity | `WebAccessPolicy` | verified | Slice 5 receipt; local and exact-host/login paths passed |
| Dynamic current-world reads | `IWorldStore` through `DynamicDataReader` | verified | Existing `/api/data` routes and web tests |
| Committed structural change evidence | `IEventLedger` | verified | Immutable event rows include payload, sequence, entity IDs, correlation, and root operation ID |
| Operation/audit history | `IOperationLog` | verified | Bounded recent reads and existing `query(kind: "history")` |
| Effect transaction | `IEffectApplier` | verified | Effects validate and commit atomically; receipts become event payloads |
| Application identity and state-space binding | `IApplicationRegistry` / `IStateSpaceRegistry` | verified | Exact reads plus bounded, scoped discovery are covered by the Slice 3 receipt |
| Component contracts and values | `IApplicationComponentTypeRegistry` / `IEntityComponentStore` | verified | Exact versioned schemas/values plus bounded latest-type/live-entity discovery are covered by the Slice 3 receipt |
| Published contract navigation | `ICatalogNavigator` / `IPublicApplicationCatalogProvider` | partially verified | Bounded browse/search contracts exist; the host currently registers the empty provider |
| Local model completion | `ILocalStructuredCompletionProvider` / Ollama | partially verified | Provider and limits exist; current host composition does not register the provider from its configuration |
| Host configuration and lifetime | `dantes-roleplay-host` | verified owner, missing mutable seam | Application manifest owns configuration and process lifetime; current values are startup-only |
| Codex deep integration | Codex app-server | external seam verified, repository owner missing | Official app-server supports local stdio JSON-RPC, threads, streaming, auth, and approvals |
| Conversation history and provider-neutral messages | none | missing | No durable conversation/thread/message owner exists |
| Privileged web write authorization and CSRF boundary | existing web identity plus control policy | verified foundation | [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) verifies exact capabilities, route confinement, and JSON/Host/Origin rejection before handlers |

## Dependency tree

```text
Operator control center                                             [planned]
├─ Privileged control boundary                                 [verified]
│  ├─ Existing local/Tailscale identity                         [verified]
│  ├─ Closed control capability policy                         [verified]
│  ├─ Same-origin mutating-request checks                      [verified]
│  └─ Shared security headers, limits, and safe evidence        [verified]
├─ Shared browser-native shell                                 [verified]
│  ├─ Existing revision-scoped HTML/assets                     [verified]
│  ├─ Control-center page and custom-element IDs                [confirmed]
│  └─ Component loading/error/empty-state convention            [verified]
├─ Committed effect history                                  [verified]
│  ├─ Existing immutable event ledger                          [verified]
│  ├─ Existing operation history                                [verified]
│  ├─ Bounded event-detail and exact operation reads             [verified]
│  └─ Read-only correlated activity projection                   [verified]
├─ ECS and contract explorer                                    [verified]
│  ├─ Existing exact ECS/application reads                       [verified]
│  ├─ Bounded state-space/type/entity discovery                 [verified]
│  ├─ Explicit public-catalog provider boundary                 [verified]
│  │  └─ Production public catalog activation       [unavailable by design]
│  └─ Read-only structural/catalog projection                   [verified]
├─ In-browser site editing                                      [verified]
│  ├─ Existing append-only revisions and active pointer           [verified]
│  ├─ Page/revision/asset list and exact-revision reads           [verified]
│  ├─ Inactive draft append and optimistic activation             [verified]
│  ├─ Script-isolated preview                                     [verified]
│  └─ Rollback by reactivating an immutable revision              [verified]
├─ Safe server settings                                [partially verified]
│  ├─ Host-owned setting-definition registry                     [verified]
│  ├─ Redacted effective/pending setting reads                    [verified]
│  ├─ Versioned non-secret override persistence                    [missing]
│  └─ Atomic validate/audit/apply-or-stage transition              [planned]
└─ Assistant conversations                                        [planned]
   ├─ Durable provider-neutral conversation envelope                 [missing]
   ├─ Local LLM adapter                                             [planned]
   │  ├─ Existing bounded schema completion                       [verified]
   │  └─ Host registration and fixed advisory chat task             [missing]
   └─ Codex adapter                                                 [planned]
      ├─ Local app-server process/stdio JSON-RPC bridge                [missing]
      ├─ Thread/turn streaming and interruption                       [planned]
      ├─ Read-only default workspace policy                           [planned]
      └─ Explicit, expiring action approvals                          [planned]
```

## Cross-cutting decisions

### Authority

- Web handlers are adapters only. They call owner interfaces and never query kernel tables
  directly.
- Committed effects are reconstructed from accepted events, not from a new parallel `effect_log`.
  The root operation ID joins them to observable intent, result, procedures, mechanic version,
  seed, and guard evidence.
- The activity panel labels accepted events as committed effects. Rejected/dry-run proposals remain
  operation or guard evidence; persisting every rejected proposal is a separate schema decision.
- ECS values remain owned by their state space and exact component contract. The explorer never
  accepts caller-supplied schema hashes, derived versions, or game-rule interpretations.
- Catalog Markdown/JSON and the catalog navigator remain contract authority. The browser receives
  bounded published views, never raw filesystem paths or arbitrary file reads.
- Server setting definitions belong to the host. The web layer cannot invent keys or decide whether
  a value is live, restart-required, read-only, or sensitive.
- Conversation records capture user-visible messages, provider identity, timestamps, status, and
  external thread IDs. They never store hidden reasoning or chain-of-thought.

### Security

- `control.read`, `control.pages.write`, `control.settings.write`, `control.ai.message`, and
  `control.codex.approve` are proposed internal capabilities. Initially the existing local operator
  and exact allowed Tailscale login receive them; no account database is introduced.
- Every mutating request uses a non-simple JSON `PUT`/`POST`, requires the expected same-origin Host
  and Origin, checks the operator capability, and rejects stale revision tokens. Caller-supplied
  forwarded identity headers remain ignored.
- Because stored pages are already trusted same-origin operator code, every trusted page can invoke
  control APIs when the operator identity is present. Per-page isolation is not implied.
- API keys, Codex credentials, bearer tokens, environment variables, database paths, and access
  allowlists are never returned. A sensitive setting reports only `configured: true|false`.
- Database path, listen addresses, MCP route, Tailscale identity/host allowlist, secret locations,
  and the control capability policy are read-only in the first settings release to prevent web
  lockout or self-escalation.
- Codex starts read-only. Command execution, file changes, network access, permission grants, and
  MCP side effects require a visible, turn-scoped approval. The browser never emits
  `acceptForSession` in the first Codex action slice.

### Browser composition

- Use browser-native custom elements, ES modules, fetch, EventSource, textarea/code editing, and
  iframe preview. Keep the existing no-Node/no-SPA-build boundary.
- Each element owns its loading, empty, retry, unavailable, and forbidden states. One failing panel
  must not prevent the remaining panels from loading.
- Read APIs use bounded cursor/page contracts. Large JSON/schema/payload fields are fetched only
  for an exact selected record.

## State and transaction ownership

| Transition | Root owner | Commit/failure rule |
| --- | --- | --- |
| Activity/ECS/catalog read | Existing read owner | No write; malformed or stale cursor returns a stable 4xx result |
| Page draft append | Web page store | Append HTML/assets without moving the active pointer; one web transaction |
| Page publish/rollback | Web page store | Compare expected active revision, then move the pointer atomically; stale is 409 with no change |
| Setting override | Host setting store + operation log on shared kernel DbContext | Validate definition/value, append revision, audit, and apply-or-stage in one transaction |
| Local LLM turn | Conversation store around an external provider call | Persist user turn as pending; append result or terminal failure; idempotency key prevents duplicate provider calls |
| Codex turn | Conversation store plus app-server external boundary | Persist requested turn, stream external events, then reconcile terminal state; no claim of atomicity with Codex side effects |
| Codex approval | Conversation approval record plus app-server request | Record one expiring decision, dispatch once, and reconcile; stale/already-resolved requests return 409 |

The proposed host-setting and conversation records require main-database migrations. Exact table and
public contract names remain a confirmation gate. Page drafts and activation can use the current
page/revision tables without changing their meaning.

## Closed setting model

Each host-owned setting definition should declare:

- key, display name, description, JSON schema, default, and non-secret current source;
- sensitivity (`public-value` or `configured-only`);
- mutability (`read-only`, `live`, or `restart-required`);
- validation and optional bounded choice list;
- owning component and whether changing it can disrupt active requests.

The first editable allowlist should be deliberately small: local-completion enabled state, loopback
Ollama endpoint, model name, profile, output-token limit, timeout, and concurrency within the
existing `OllamaCompletionOptions.Validate` bounds. Values become live only after the local-AI
registration supports atomic refresh; otherwise they are staged for restart. No arbitrary key/value
editor is allowed.

## Assistant contract

### Common conversation envelope

- Browser input: conversation ID or create request, provider (`local` or `codex`), bounded message,
  expected conversation revision, and idempotency key.
- Server-derived fields: operator identity, timestamps, message IDs, provider/model identity,
  status, external thread/turn IDs, token/elapsed metadata, and approval state.
- Stable statuses: `pending`, `running`, `awaiting-approval`, `completed`, `failed`, `cancelled`.
- Conversation list/read is paged and scoped to the current operator. Deletion is excluded from the
  first release; archive may be added later as a reversible state.

### Local LLM

- Add one fixed server-owned advisory task class and response schema; the caller supplies only the
  message and optional IDs of already-authorized context records.
- The local provider receives bounded materialized context from the same read facades used by the
  control center. It receives no raw database access and has no write/effect tools.
- Provider unavailable, invalid schema output, timeout, and saturation become terminal visible
  messages without partial game or settings changes.
- This conversation/history layer is not the planner defined by the
  [interaction-orchestration dependency plan](../platform/interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md).
  Its messages and replies confer no planning, receipt, proposal, execution, or recipe authority;
  later orchestration must enter through its separately confirmed adapter and verifier.

### Codex

- Prefer `codex app-server` over pretending a Codex model is an interchangeable chat completion.
  Run it as a child process over its default local stdio JSONL transport; do not expose its
  experimental WebSocket listener remotely.
- Fix the working directory to the repository root and use the operator's existing Codex
  authentication/configuration. Do not copy ChatGPT credentials or API keys into the web database.
- Persist only user-visible input/output, thread/turn IDs, status, tool/command summaries, file
  change summaries, and approval decisions. Do not persist hidden reasoning.
- Stream normalized events to the browser with bounded queues. On process exit, mark active turns
  interrupted and allow explicit resume/retry rather than silently replaying a prompt.

Official OpenAI documentation describes app-server as the deep product-integration interface for
authentication, conversation history, approvals, and streamed agent events. It supports local
stdio JSON-RPC and generates version-matched schemas; implementation should pin/test one Codex
version and regenerate its protocol bundle when that dependency changes.

## Site editor contract

- List pages and immutable revisions with active/draft markers, timestamps, content hashes, and
  asset summaries.
- Read one exact revision and download/export its bundle. Do not expose storage paths.
- Saving creates an inactive revision using an expected latest-revision token. Publishing uses an
  expected active-revision token. These are separate actions.
- Preview exact revision HTML/assets under a sandboxed iframe response that denies same-origin
  control API access and external connections, even though published pages remain trusted.
- Rollback reactivates an existing immutable revision; it never edits or deletes history.
- Editing `control-center` itself is allowed only after its new revision passes preview and the UI
  clearly offers rollback. CLI upload remains the recovery path if the active editor is broken.

## Failure, replay, and rollback contract

- Unauthenticated, wrong-host, disallowed Tailscale, missing capability, or invalid Origin requests
  fail before owner invocation and make no change.
- All list sizes, search text, messages, JSON bodies, schemas, event payloads, ZIPs, concurrent
  streams, and provider calls have explicit ceilings. Existing web limits remain the lower bound.
- Unknown setting keys and sensitive/read-only writes return 400/403; invalid values return their
  definition-owned validation result; stale setting revisions return 409.
- Missing ECS/catalog records return 404. Invalid or stale cursors return stable restart guidance.
- Page preview never activates. Draft failure creates no pointer change; publish failure leaves the
  prior active revision intact.
- Conversation retry with the same idempotency key returns the original turn. A different key
  appends a new turn.
- Local provider failure has no world, settings, catalog, filesystem, or page effect.
- Codex process failure never implies that an external side effect rolled back. The bridge
  reconciles the final app-server item state and displays any completed command/file result.
- Expired, mismatched, duplicate, or already-resolved Codex approvals return 409 and are never sent
  twice.

## Ordered leaves

The model column selects the implementation agent, not a runtime model used by the finished server.
`Sol -> Terra` means Sol must close and record the named semantic gate before Terra receives an
active, bounded implementation document. Terra does not inherit unresolved design decisions.

| Order | State | Slice | Depends on | Implementation model | Exit gate |
| --- | --- | --- | --- | --- | --- |
| 0 | **accepted** | Control authorization and API conventions | Existing web identity | **Sol, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) records local/Tailscale, origin, capability, route, and no-handler evidence |
| 1 | **accepted** | Read-only control-center shell and health/status | Slice 0 and existing page bundles | **Terra, medium** | [Receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md) records bounded status, five independently rendered panels, and no-mutation evidence |
| 2 | **accepted** | Committed effect/activity history | Slice 0, event ledger, operation log | **Terra, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-2-RECEIPT.md) records bounded grouping/detail, operation context, and no-write evidence |
| 3 | **accepted** | ECS and contract explorer | Slice 0, new bounded discovery reads, effective catalog provider | **Sol, high -> Terra, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-3-RECEIPT.md) records scoped discovery, exact schema/value reads, public-catalog boundary, and no-write evidence |
| 4 | **accepted** | Site editor drafts, preview, publish, rollback | Slice 0 and page revisions | **Terra, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-4-RECEIPT.md) records draft isolation, exact preview/export, optimistic activation, rollback, recovery, and atomic-failure evidence |
| 5 | **accepted** | Setting definitions and redacted read view | Slice 0 and host configuration owner | **Sol, high -> Terra, medium** | [Receipt](WEB-CONTROL-CENTER-SLICE-5-RECEIPT.md) records the exact allowlist, validation, redaction, inactive-provider truth, and read-only UI evidence |
| 6 | **accepted** | Audited setting overrides | Slice 5 and confirmed migration | **Sol, xhigh** | [Receipt](WEB-CONTROL-CENTER-SLICE-6-RECEIPT.md) and [ratification](WEB-CONTROL-CENTER-SLICE-6-SOL-RATIFICATION.md) record live/staged transitions, concurrent stale/no-change, restart, rollback, and audit evidence |
| 7 | **accepted** | Provider-neutral conversation store and local LLM | Slice 0, local-AI registration, confirmed migration | **Sol, high -> Terra, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-7-RECEIPT.md) records status, success/failure/timeout/saturation, idempotency, bounds, recovery, and no-side-effect evidence |
| 8 | **accepted** | Read-only Codex app-server conversation | Slice 7 and confirmed Codex bridge owner | **Sol, xhigh** | [Receipt](WEB-CONTROL-CENTER-SLICE-8-RECEIPT.md) records the pinned process seam, start/resume/stream/cancel/restart behavior, read-only/no-network policy, and browser evidence |
| 9 | **accepted** | Explicit Codex approvals | Slice 8 | **Sol, xhigh** | [Receipt](WEB-CONTROL-CENTER-SLICE-9-RECEIPT.md) records command/file/network/permission prompts, accept/decline/cancel/expiry/duplicate/process-failure/reconciliation evidence |
| 10 | **accepted** | Persistent sidebar and application workspace | Slices 1–9 | **Terra, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-10-RECEIPT.md) records closed hash routes, stable panels, responsive/accessibility structure, and application deep-link evidence |
| 11 | **accepted** | Root control-center entry route | Slice 10 and existing web security | **Terra, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-11-RECEIPT.md) records the root read route, private-web admission, and unchanged MCP/direct-page boundaries |
| 12 | **accepted** | Reachable home and page navigation | Slices 4 and 11 | **Terra, high** | [Receipt](WEB-CONTROL-CENTER-SLICE-12-RECEIPT.md) records root home, control-center and direct page links, and live revision evidence |

Slices 2, 3, and 5 are read-only siblings after Slice 0 and may be reordered. Slice 4 is the first
web-content mutation. Slice 6 is the first host-configuration mutation. Slice 9 is the first point
where the web UI may authorize Codex side effects.

## Implementation-model routing

[Official OpenAI model guidance](https://developers.openai.com/api/docs/models) describes GPT-5.6
Sol as the flagship choice for complex reasoning and coding, and GPT-5.6 Terra as the balanced
intelligence/cost choice. This plan therefore uses Terra wherever the owner, input, output,
transaction, and failure behavior can be closed in advance. Sol is reserved for decisions whose
mistakes could cross an authority boundary or leave ambiguous durable state.

### Sol-required work

- **Slice 0:** define the privileged capability boundary, Host/Origin checks, local-versus-Tailscale
  identity mapping, stable denial behavior, and the reusable endpoint-filter convention. This is a
  security/public-surface decision and must not be inferred by Terra from existing read endpoints.
- **Slice 6:** own the override schema/migration, audit transaction, live-versus-restart transition,
  refresh failure, recovery, and concurrency semantics. Settings writes can affect process
  availability and durable host state.
- **Slice 8:** own the pinned app-server protocol seam, child-process lifecycle, JSONL framing,
  cancellation/restart reconciliation, bounded streaming, authentication reuse, and read-only
  sandbox policy. Do not ask Terra to discover an evolving external protocol while implementing it.
- **Slice 9:** own the approval state machine and the reconciliation of command, file, network,
  permission, and MCP side effects. This slice is the first web-authorized external side-effect
  boundary.

### Sol-to-Terra gates

- **Slice 3:** Sol names and confirms the bounded discovery contracts, cursor semantics, maximum
  sizes, application/state-space scoping, and effective public-catalog provider composition. Terra
  may then implement those exact contracts, HTTP projections, custom element, and tests.
- **Slice 5:** Sol freezes the host-owned setting-definition contract and the complete first-release
  allowlist, including schema, redaction, source, sensitivity, mutability, restart state, and
  disruption metadata. Terra may then implement the read registry, projection, panel, and tests;
  it must not add persistence or writes.
- **Slice 7:** Sol freezes the conversation schema/migration, uniqueness and idempotency rules,
  message/status transition table, operator scope, transaction boundaries around the external
  call, and fixed local-AI task/response schema. Terra may then implement the confirmed storage,
  Ollama adapter, endpoints, UI, and deterministic fake-provider tests.

Once Sol completes a gate, it must leave an implementation document with `active` status, exact
artifact names and shapes, granted confirmations, allowed files, verification commands, and an
explicit stop point. A prose chat handoff is not sufficient.

## Terra execution envelope

This envelope applies to Slices 1, 2, 3 after its Sol gate, 4, 5 after its Sol gate, and 7 after its
Sol gate.

### Required reading packet

Terra reads only:

1. repository `AGENTS.md` and `docs/IMPLEMENTATION_DOCUMENT_READING.md`;
2. this dependency plan and the web roadmap;
3. the one active slice implementation document;
4. the component manifests, contracts, tests, and prerequisite receipts explicitly listed in that
   document.

The active document must copy or link every exact route, request/response shape, public/internal
type name, bound, cursor rule, migration name, transaction owner, and expected error result needed
by the slice. It must identify whether each artifact is existing, revised, or confirmed new. Terra
must not search unrelated roadmaps or use an old plan as authority.

### Global Terra constraints

- Preserve the dirty worktree and existing user-authored pages. No unrelated cleanup or formatting.
- Use existing owners through interfaces; no direct kernel-table queries from web handlers.
- Do not invent permanent IDs, public methods, tables, migrations, capability names, setting keys,
  message statuses, or provider behaviors. Stop and return the unresolved item to Sol if the active
  document omitted one.
- Keep browser code build-free: custom elements, ES modules, fetch, EventSource, textarea/code
  editing, and sandboxed iframe preview only.
- Derive identity, authority, revision tokens, timestamps, hashes, model/provider identity, and
  owner scope on the server. Treat caller-supplied versions, identities, paths, and authorization
  claims as untrusted.
- Implement one root transaction owner and one coherent slice. Do not pull a later slice forward
  because its UI placeholder is visible.
- Run focused tests while iterating, then the active document's full-suite command and
  `git diff --check`. Write the receipt and stop; do not begin the next slice.

### Terra stop-and-escalate conditions

Terra must stop without guessing when implementation would require:

- a route, component tag, capability, table, migration, setting key, status, or public method not
  named in the active document;
- a change to schema meaning, authorization policy, transaction ownership, operator identity,
  secret handling, catalog authority, or effect/event authority;
- choosing between live and restart-required configuration behavior;
- exposing a raw filesystem/database path, secret value, hidden model reasoning, or caller-selected
  schema/version;
- making an external side effect atomic with a database transaction, silently retrying a provider
  request, or approving a Codex action;
- touching MCP registration/surface when the active document says no protocol walk is required; or
- a failing prerequisite receipt/test or an overlapping user change in an allowed file.

## Terra slice packets

These packets make each Terra assignment concrete, but do not activate a slice. The active
implementation document remains the authorization boundary and must freeze the exact artifact
names before work begins.

### Slice 1 packet — shell and status

- **Prerequisites:** accepted Slice 0 receipt; confirmed `control-center` page ID, five custom-element
  tags, `/api/control/*` family, capability mapping, and existing page-bundle/SSE behavior.
- **Use:** `src/system/web-interface/component.json`, `src/system/web-interface/README.md`,
  `src/system/web-interface/DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs`, the existing page
  bundle/store contracts, web security types, `src/system/web-interface/tests/WebInterfaceTests.cs`,
  and Slices 4–5 receipts.
- **Build:** one revision-scoped control-center bundle; a bounded read-only status projection fixed
  by the active document; five independently loadable elements; shared loading, empty, unavailable,
  forbidden, and retry presentation; navigation that continues working if any one panel fails.
- **Do not build:** effect/ECS/settings data, page editing, conversation persistence, model calls, or
  any mutating control endpoint. Panel bodies are truthful unavailable/coming-later states.
- **Acceptance:** local and exact Tailscale operator load the same revision; wrong identity is denied;
  each panel failure is isolated; all assets are revision-scoped; no Node/build dependency or
  mutating route appears.

### Slice 2 packet — committed effect history

- **Prerequisites:** Slice 0 receipt; confirmation that immutable accepted events are the only past-
  effect authority; the active document freezes filters, cursor/order, page/detail limits, grouping,
  redaction, event kinds, and operation-correlation response shapes.
- **Use:** `events-and-notifications` event-ledger contract/persistence/tests,
  `operations-and-audit` operation-log contract/persistence/tests, effect receipts, the Slice 1
  panel, and web endpoint/security test patterns.
- **Build:** bounded owner-level read methods only where named; a web projection that groups accepted
  events by root operation/correlation without manufacturing missing history; summary paging first
  and exact payload/before-after detail on selection; stable filters and empty states.
- **Do not build:** a second effect table, rejected-proposal persistence, event mutation, raw SQL in
  the endpoint, arbitrary JSON search, or world-state reconstruction beyond recorded evidence.
- **Acceptance:** deterministic newest/oldest ordering from the active contract; page boundaries and
  caps; unknown/stale cursor behavior; correlated and uncorrelated records; missing optional detail;
  redaction; wrong identity; and proof that all requests are read-only.

### Slice 3 Terra packet — ECS/contracts implementation

- **Prerequisite Sol handoff:** exact application/state-space/type/entity discovery signatures,
  cursor encoding/order, caps, schema/version selection rules, public-catalog composition, and
  unknown/stale behavior are confirmed and active.
- **Use:** `application-registry/domain/ApplicationContracts.cs`, ECS component and application-
  scoped contracts, catalog-navigation public provider/navigator, their persistence and tests, plus
  the Slice 1 explorer element.
- **Build:** the confirmed bounded registry/store implementations; dependency registration exactly
  as handed off; read-only HTTP summaries and exact detail; lazy schema/component/catalog loading;
  breadcrumbs that retain application and state-space scope.
- **Do not build:** generic table browsing, filesystem contract reads, component edits, caller-
  selected authoritative hashes/versions, game-rule interpretation, or a replacement catalog.
- **Acceptance:** empty/multiple applications and state spaces; unknown IDs; pagination/caps; schema
  version accuracy; entity/type scope isolation; empty versus unavailable public catalog; malformed
  cursor; wrong identity; and no-write evidence.

### Slice 4 packet — site editor

- **Prerequisites:** Slice 0 receipt; confirmed draft-versus-publish semantics; the active document
  names page/revision list, exact-revision read, inactive append, activation, preview, bundle-export,
  request/response, revision-token, size, and conflict contracts.
- **Use:** `IWebPageStore`, `WebPageStore`, page/bundle models and readers, existing web migrations,
  endpoints/security, `WebInterfaceTests.cs`, and Slices 1–5 receipts.
- **Build:** page and immutable-revision lists; exact revision HTML/asset reads; inactive draft save;
  isolated preview; expected-active-revision publish and rollback; editor UI with explicit save,
  preview, publish, rollback, and download actions.
- **Do not build:** revision deletion or mutation, auto-publish, raw storage paths, arbitrary server-
  filesystem editing, external preview connections, or schema migration unless the active document
  explicitly confirms one.
- **Acceptance:** draft never changes active content; stale save/publish is 409 with no pointer move;
  injected persistence failure rolls back; preview cannot call same-origin control APIs; rollback
  reactivates an immutable revision; editing `control-center` preserves a tested CLI recovery path.

### Slice 5 Terra packet — settings read view

- **Prerequisite Sol handoff:** exact setting-definition interface and allowlist, JSON schemas,
  defaults, sources, sensitivity, mutability, disruption/restart metadata, and redaction response
  are confirmed and active.
- **Use:** host `DantesRoleplay.MCPServer/ServerConfiguration.cs`,
  `DantesRoleplay.MCPServer/Program.cs`, appsettings, local-AI
  `OllamaCompletionOptions.Validate`, the relevant component manifests/tests, and the Slice 1
  settings element.
- **Build:** host-owned in-memory definitions; a redacted effective/pending read projection; stable
  source/mutability/restart labels; UI controls rendered disabled/read-only because writes belong to
  Slice 6.
- **Do not build:** overrides, migrations, restart triggers, live refresh, arbitrary configuration
  enumeration, secret retrieval, database/listen/MCP/Tailscale edits, or values absent from the
  confirmed allowlist.
- **Acceptance:** every returned key is registered; configured-only values never disclose content;
  defaults versus configured sources are exact; invalid host configuration still fails through the
  existing owner; unknown key/detail is 404; wrong identity and response bounds pass.

### Slice 7 Terra packet — conversation/local-LLM implementation

- **Prerequisite Sol handoff:** confirmed migration and entity names; operator scope; message and
  conversation revision rules; status transition table; idempotency uniqueness; retention/archive
  exclusion; transaction/failure algorithm; local provider registration; fixed advisory task and
  response schema; all body/token/time/concurrency bounds.
- **Use:** local-AI contracts/provider/options/tests, shared SQLite hosting/migration conventions,
  operation/audit conventions named by the active document, Slice 1 assistant element, and web
  endpoint/security patterns.
- **Build:** confirmed storage and migration; create/list/read/send endpoints; one provider call per
  idempotency key; pending-to-terminal reconciliation; bounded normalized stream or polling contract;
  local-provider status; assistant UI with visible success/failure/timeout/saturation states; fake-
  provider tests before any opt-in Ollama smoke test.
- **Do not build:** arbitrary system prompts, tools/effects, database access for the model, Codex
  app-server behavior, hidden reasoning storage, secret storage, automatic retry with a new call, or
  deletion.
- **Acceptance:** fresh migration; operator isolation; successful, invalid-output, unavailable,
  timeout, cancellation, and saturation paths; same-key replay without a second provider call;
  different-key new turn; process restart with pending reconciliation; all failure paths prove no
  world, catalog, settings, filesystem, or page change.

## Slice 13 packet — Codex CLI compatibility refresh (**Sol, xhigh**)

- **Prerequisite evidence:** Slice 8 and Slice 9 receipts; the locally installed standalone
  `codex-cli 0.149.1`; the version-matched official app-server schema generated by that CLI; and
  the existing process, protocol, policy, and deterministic fake-process tests.
- **Use:** `CodexBridgeOptions`, `CodexAppServerProcessFactory`, the checked-in app-server protocol
  descriptor, the MCP host composition/configuration, bridge tests, and the existing assistant
  status endpoint. Treat the installed executable path as local development configuration, never as
  browser-visible data.
- **Build:** pin the bridge to `0.149.1`; configure this development host to use the accessible
  standalone CLI; regenerate/review the matching app-server descriptor; and prove the bridge can
  start and initialize the local JSONL app-server without starting a model turn.
- **Do not build:** a version range or automatic future upgrade, a new provider, a browser setting
  for executable paths, a new approval mode, a relaxed sandbox/network policy, remote exposure, or
  an actual model turn as a configuration check.
- **Acceptance:** status reports the observed and pinned `0.149.1` versions; the deterministic
  protocol suite still rejects invalid/oversized/unapproved behavior; a live initialization-only
  smoke uses the configured CLI; unrelated assistant and web tests pass; no conversation, approval,
  setting override, page revision, MCP surface, or database schema is created.

## Slice 14 packet — site-editor draft preview repair

- **Prerequisite evidence:** Slice 4's immutable draft/preview owner and current control-center
  page bundle. The current Preview action selects an existing revision and therefore cannot display
  unsaved textarea content.
- **Build:** make the editor's preview-current-changes action append the same explicit inactive
  draft as Save, then open that exact returned revision in the existing isolated preview iframe.
- **Do not build:** `srcdoc`/same-origin preview, mutation of an existing revision, auto-publish,
  new routes, schema/migration changes, asset editing, or changes to trusted-page authority.
- **Acceptance:** changed textarea content is shown from a new inactive revision; the active pointer
  remains unchanged; the preview retains its existing sandbox and CSP; normal Save and exact
  historical preview continue to work.

## Slice 15 packet — Codex Luna host model selection

- **Prerequisite evidence:** Slice 13's pinned local CLI and direct initialization-only app-server
  probe. A no-turn ephemeral `thread/start` accepted exact model `gpt-5.6-luna` and returned that
  model/provider.
- **Build:** make `gpt-5.6-luna` the closed host-owned model for newly created Codex threads,
  report it in the existing status projection, and show it in the assistant panel.
- **Do not build:** browser-selected arbitrary model IDs, per-message model input, changing existing
  external threads, a provider/account entitlement claim, changes to approval/sandbox/network policy,
  model turns as verification, migrations, or new routes.
- **Acceptance:** a new-thread protocol frame includes only the configured exact Luna model; resumed
  threads preserve their existing model; status/panel report the configured model; live no-turn
  probe accepts it; and all current safety behavior remains unchanged.

## Lowest ready leaf

Slices 0–9 are accepted by their [Slice 0](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md),
[Slice 1](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md),
[Slice 2](WEB-CONTROL-CENTER-SLICE-2-RECEIPT.md), and
[Slice 3](WEB-CONTROL-CENTER-SLICE-3-RECEIPT.md) and
[Slice 4](WEB-CONTROL-CENTER-SLICE-4-RECEIPT.md) and
[Slice 5](WEB-CONTROL-CENTER-SLICE-5-RECEIPT.md), and
[Slice 6](WEB-CONTROL-CENTER-SLICE-6-RECEIPT.md), and
[Slice 7](WEB-CONTROL-CENTER-SLICE-7-RECEIPT.md), and
[Slice 8](WEB-CONTROL-CENTER-SLICE-8-RECEIPT.md), and
[Slice 9](WEB-CONTROL-CENTER-SLICE-9-RECEIPT.md), and
[Slice 10](WEB-CONTROL-CENTER-SLICE-10-RECEIPT.md), and
[Slice 11](WEB-CONTROL-CENTER-SLICE-11-RECEIPT.md), and
[Slice 12](WEB-CONTROL-CENTER-SLICE-12-RECEIPT.md), and
[Slice 13](WEB-CONTROL-CENTER-SLICE-13-RECEIPT.md), and
[Slice 14](WEB-CONTROL-CENTER-SLICE-14-RECEIPT.md), and
[Slice 15](WEB-CONTROL-CENTER-SLICE-15-RECEIPT.md) receipts. There is no remaining ordered leaf in
this feature plan. Embedded application websites and any broader authority still require a separate
confirmed plan.

## Confirmation gates

1. **Completed with Slice 0:** control-center page, route family, custom-element IDs, capability
   names, operator mapping, and same-origin mutation boundary.
2. **Completed with Slice 2:** committed events—not a second effect log—are the authoritative
   “past effects” view.
3. **Completed with Slice 3:** bounded ECS/application discovery and the existing explicit-public
   catalog provider are the read authority; production remains unavailable until separately activated.
4. **Completed with Slice 4:** inactive drafts, exact isolated preview/export, optimistic
   activation, immutable rollback, and direct-upload recovery use the existing page schema.
5. **Completed with Slice 6:** seven local-completion definitions use immutable, audited override
   revisions; writes only stage, reset and rollback append history, and validated heads apply at
   the next successful host startup without a web restart action.
6. **Completed with Slice 7:** durable operator-scoped conversations/messages, exact replay and
   recovery, and advisory local-LLM chat are separate from Codex coding threads and from the
   interaction-orchestration plan's intent/proposal/execution authority.
7. **Completed with Slice 8:** the new Codex bridge owns the pinned local stdio process seam and
   read-only/no-network first policy.
8. **Completed with Slice 9:** durable two-minute, turn-scoped approvals precede exactly one
   app-server response; accept, decline, cancel, expiry, duplicate, and failure paths remain within
   the pinned protocol and never create a session-wide grant.
9. **Completed with Slice 10:** preserve the control navigation while one selected panel or
   application structure occupies the main workspace; use only client-side routes and existing
   owners, without embedding application websites.
10. **Confirmed for Slice 11:** `GET /` is the control-center entry route under the existing
    private read boundary; `/ui/control-center/index.html` remains available and `/mcp` remains
    outside the web route family.
11. **Confirmed for Slice 12:** root is the active `home` page; it links to control center, and
    Site Editor exposes direct links for all existing pages without changing page ownership.
12. **Completed with Slice 13:** the installed standalone `codex-cli 0.149.1` is the exact reviewed
    app-server bridge pin and local host executable; safe assistant authority is unchanged.
13. **Completed with Slice 14:** changed-content preview explicitly saves an inactive draft and frames
    that exact revision; it never previews unsaved arbitrary HTML or moves the active pointer.
14. **Completed with Slice 15:** new Codex threads receive the confirmed closed Luna model; resumes
    preserve their existing model, and the model remains status-only to the browser.
15. Confirm each completed slice at feature acceptance; passing tests may replace manual confirmation
    only when they assert the same invariant.

## Verification strategy

- Focused owner tests for web security, event/audit projections, ECS/catalog discovery, page revision
  transitions, settings, conversations, local AI, and Codex protocol normalization.
- Fresh disposable SQLite migration/import tests for every new main-database record.
- Browser HTTP walks for local and Tailscale identities, Origin rejection, paging, self-edit preview,
  SSE reconnect, and rate limits.
- A fake app-server JSONL process for deterministic Codex start/stream/approval/crash tests; one
  opt-in live Codex smoke test after the fake protocol suite passes.
- Full solution build/tests at every slice acceptance. Run the MCP protocol walk only when shared
  dependency registration or the MCP surface changes.
- `roleplay validate catalog` only when a slice changes catalog records; this plan proposes none.
- `git diff --check` for every slice.

## Plan update receipt

- Slice 0 introduced the closed capability and control-request convention recorded in its receipt.
- Slice 1 delivered the page shell and bounded status read.
- Slice 2 delivered accepted-event history reads and the correlated activity projection, recorded in
  its receipt. It added no persistent table, migration, catalog record, or MCP surface.
- Slice 3 delivered bounded owner discovery, exact ECS/schema reads, and public-only catalog
  browse/search/detail through the control center. It added no write, migration, catalog activation,
  catalog record, or MCP surface.
- Slice 4 delivered inactive page drafts, isolated exact preview/export, optimistic activation,
  and immutable rollback without a migration.
- Slice 5 delivered the seven-setting host registry, redacted GET-only projection, and disabled
  settings panel. It added no override, migration, provider activation/call, restart, or MCP surface.
- Slice 6 delivered audited versioned startup-setting overrides; Slice 7 delivered durable local
  advisory conversations; Slice 8 delivered pinned read-only Codex conversations.
- Slice 9 delivered expiring, turn-scoped Codex approvals with durable decision-before-dispatch,
  exact one-request responses, process reconciliation, and no session-wide grants. Existing
  user-authored pages and unrelated worktree changes remain untouched.
- Slice 10 delivered the persistent responsive navigation shell, closed client routes, and
  application structure deep links without adding a server route, capability, record, migration,
  dependency, or embedded application website.
- Slice 11 delivered the root control-center entry route through the existing page store and read
  boundary. It added no page revision, migration, catalog record, MCP route, hosting, or deployment.
- Slice 12 made root the active home page, linked home to control center, and exposed direct live
  links in Site Editor. It reused existing page revisions and direct routes without a new schema,
  catalog record, generic navigation model, hosting, or deployment.
- Slice 13 configured the accessible local standalone Codex executable, refreshed the exact bridge
  pin to `0.149.1`, and verified the stdio initialization lifecycle. It added no conversation,
  model turn, approval, policy change, migration, MCP surface, page revision, or database record.
- Slice 14 repaired Site Editor's changed-content preview by explicitly appending an inactive draft
  before framing its exact isolated preview. It added no route, schema, authority, or asset-editing
  capability; the reviewed control-center bundle is live as revision 3.
- Slice 15 configured the exact `gpt-5.6-luna` model for new Codex threads, exposed that host-owned
  value in bounded status, and updated the live control-center bundle as revision 4. It added no
  browser model input, turn, account entitlement claim, approval/policy change, migration, or route.
