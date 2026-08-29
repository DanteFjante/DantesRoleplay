# Trail Game dependency tree — customizable trail-survival browser application

Status: **TG0 through TG3 accepted; TG4 planning awaits content confirmation**
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Ruleset alignment: **ruleset-neutral**
Source: **not applicable; this is an original non-D&D application**
Application identity: **`trail-survival` / Trail Survival**

## Outcome and non-goals

### Root outcome

A player can open a local/private browser application, choose a versioned original scenario, create
and provision a party, make explicit daily decisions, travel through a landmark route, experience
deterministically resolved events, save/resume the exact run, and reach a governed victory or defeat.
An author can create a different data-only scenario without changing generic C# or copying the
bundled scenario.

Root acceptance is an automated browser-and-protocol walk against a disposable database that:

1. activates the application and creates a fresh isolated state space;
2. starts a run from a known scenario and seed;
3. buys supplies, selects travel policy, resolves travel/rest/event turns, and reaches a landmark;
4. stops and resumes from persisted canonical state;
5. completes both a victory fixture and a defeat fixture;
6. replays duplicate action requests without repeating effects;
7. installs a second data-only scenario and proves that the same mechanics and UI can play it; and
8. proves that no trail state or catalog record leaks into `dnd2024` or a system-only host.

### Non-goals for customizable v1

- A remake, port, or branded release of any commercial Oregon Trail game.
- Compatibility with original save files, maps, balance tables, prose, art, audio, or minigames.
- D&D rules, D&D character imports, tactical combat, spells, or D20 Test semantics.
- Multiplayer, competitive leaderboards, accounts, public internet hosting, or cloud saves.
- An AI-required game loop. AI may assist authors or narrate later, but never selects authoritative
  outcomes or supplies effects.
- A general-purpose game engine rewrite. New generic infrastructure is permitted only where a
  demonstrated application seam is missing.
- A visual no-code editor or untrusted script marketplace in the first release.

## Product model

The application has three deliberately separate kinds of customization:

1. **Scenario selection:** multiple immutable content packs may coexist in one effective application
   catalog. A run pins one scenario identity and version.
2. **Data-only authoring:** an author creates schema-valid routes, landmarks, resources, event
   definitions, markets, text, presentation tokens, and tuning without executable code.
3. **Trusted rules modification:** a host administrator may later register a reviewed source with
   sandboxed application JavaScript. This creates a new application revision and requires explicit
   activation/state-space compatibility handling.

Application source overlays are not a per-run scenario switch. They resolve the effective
application revision before catalog navigation or execution. Existing non-empty state spaces remain
pinned and may not silently inherit changed schema or mechanic meaning.

## Existing owners and evidence

| Concern | Owner | State | Evidence and consequence |
| --- | --- | --- | --- |
| Application registration, revisions, sources, overlays, activation | Generic application kernel | verified | [Completion receipt](../platform/application-kernel/receipts/APPLICATION-KERNEL-COMPLETION-RECEIPT.md). Reuse as-is; do not add trail branches to system code. |
| Application-scoped JSON ECS, schema versions, state-space isolation | Generic application kernel | verified | Accepted kernel state supports schema-approved JSON and exact revision/hash pinning. |
| Sandboxed mechanics, generic typed effects, transactions, replay, audit | Application execution and ECS effects | verified foundation | Existing `application-execution` owner evaluates exact application mechanics and commits through generic effect/state owners. Every trail rule remains application JavaScript. |
| Deterministic catalog navigation without AI | Catalog navigation | verified | Scenario and mechanic discovery can use effective application records and exact inspection. |
| Outer conversational planning/execution | Interaction orchestration and `<application-conversation>` | verified optional adapter | Useful as an optional narrative/control surface, but the deterministic game must not require an AI provider. |
| Trusted HTML/CSS/JavaScript pages, assets, JSON reads, SSE | Web interface | verified foundation | [Web roadmap](../web/WEB-INTERFACE-ROADMAP.md). Presentation can ship as a versioned page bundle without a frontend build system. |
| Direct button-driven application action HTTP adapter | no confirmed owner | missing cross-owner seam | Existing conversation routes execute confirmed plans; a deterministic exact-action browser route is not yet an accepted public contract. TG5 must resolve this without making web the rule owner. |
| Legacy world/topology/time/travel contracts | `dnd2024` application | verified reference only | [Ownership ratification](../platform/application-kernel/LEGACY-OWNERSHIP-RATIFICATION.md) assigns all legacy `game.core.*` records to `dnd2024`. Study their atomicity and derived-time behavior; do not reference them from the new application's live state. |
| Original trail application state and mechanics | new application | missing | Must be defined through confirmed application-owned IDs, schemas, procedures, data, and JavaScript. |
| Scenario content license/provenance | new application content pack | missing | Every bundled or borrowed asset/code fragment needs explicit provenance and license compatibility. |

## Authority and dependency direction

```text
browser page
  -> generic web transport/read model
  -> confirmed exact application-action adapter
  -> application-execution
  -> sandboxed Trail Game mechanic
  -> generic ECS effects + root transaction + replay/audit
  -> Trail Game application-scoped canonical state

scenario source(s)
  -> application registry/overlay resolution
  -> immutable effective application revision
  -> catalog navigation + component/mechanic contracts
  -> pinned state space and pinned run scenario
```

Forbidden dependency directions:

- system/web C# must not reference Trail Game IDs, resources, formulas, vocabulary, or outcomes;
- Trail Game must not read or write `dnd2024` state or rely on legacy `game.core.*` IDs;
- pages and callers must not supply random outcomes, derived costs, elapsed time, resource deltas,
  event eligibility, arrival, victory, defeat, or typed effects;
- scenario prose or UI projections must not become canonical state; and
- a source overlay must not mutate an existing non-empty state space silently.

## Dependency tree

```text
Customizable trail-survival browser application                           [planned]
├─ TG0 Product, identity, and legal contract                              [confirmed]
│  ├─ Original working/product identity                                  [confirmed]
│  ├─ First audience and distribution boundary                           [confirmed]
│  ├─ MVP versus customizable-v1 scope                                   [confirmed]
│  └─ Code/asset/content provenance policy                               [confirmed]
├─ TG1 Separately registered application package                          [accepted; depends TG0]
│  ├─ Permanent application ID and source placement                      [confirmed]
│  ├─ Minimal manifest/catalog navigation                                [verified kernel seam]
│  ├─ Preview, activation, and fresh isolated state space                [verified kernel seam]
│  └─ Zero-app/dnd2024/application isolation proof                       [verified]
├─ TG2 Canonical run domain                                               [accepted; depends TG1]
│  ├─ Scenario/version/content/rules-profile pin                         [verified]
│  ├─ Route selection, landmark/leg progress, and visited state          [verified]
│  ├─ Party, member health/state, conveyance, and capacity               [verified]
│  ├─ Resource inventory                                                 [verified]
│  ├─ Run phase, clock/turn, policy, pending choice, and outcome          [verified]
│  └─ Player-safe derived projections                                    [deferred to TG5/TG6]
├─ TG3 Deterministic simulation loop                                     [accepted; depends TG2]
│  ├─ Create run and validate scenario                                   [verified]
│  ├─ Setup market and purchases                                         [verified]
│  ├─ Select pace/rations/route policy                                   [verified]
│  ├─ Resolve travel, rest, and abstract forage/hunt turns               [verified]
│  ├─ Seeded event eligibility, draw, choice, and resolution             [verified]
│  ├─ Landmark arrival and next-leg transition                           [verified]
│  └─ Victory, defeat, replay, rollback, and no-change failures           [verified]
├─ TG4 Original starter scenario                                         [awaiting confirmation; depends TG2/TG3]
│  ├─ One route with 6–10 landmarks and bounded legs                     [missing]
│  ├─ Initial roles/loadouts, resources, shops, and tuning               [missing]
│  ├─ 20–30 events across health, weather, breakdown, trade, and choice  [missing]
│  └─ Original narrative/presentation and provenance ledger              [missing]
├─ TG5 Deterministic player control bridge                               [missing; depends TG1/TG3]
│  ├─ Exact action discovery/read contract                               [planned]
│  ├─ Explicit action submit with principal/state-space/idempotency      [missing public contract]
│  ├─ Result/receipt and safe error projection                           [planned]
│  └─ Auth, stale/replay, cross-app, and no-change evidence               [planned]
├─ TG6 Player web application                                            [planned; depends TG3/TG4/TG5]
│  ├─ New/resume and scenario/setup screens                              [planned]
│  ├─ Journey dashboard, party/resources, route, and policy controls     [planned]
│  ├─ Event/choice, market, landmark, victory, and defeat views          [planned]
│  ├─ Accessible keyboard/mobile-width behavior                          [planned]
│  └─ Refresh/SSE, optimistic-lock feedback, and recovery                [planned]
├─ TG7 Customization and author workflow                                 [planned; depends TG2–TG4]
│  ├─ Data-only content-pack contract and JSON Schemas                   [missing]
│  ├─ Static validator with actionable diagnostics                       [planned]
│  ├─ Minimal and complete example packs                                 [planned]
│  ├─ Pack coexistence, selection, version pinning, and upgrade policy   [missing decision]
│  └─ Trusted overlay/rules extension workflow                           [deferred from MVP]
└─ TG8 Release hardening                                                 [planned; depends TG4–TG7]
   ├─ Deterministic balance simulations and tuning evidence              [planned]
   ├─ Fresh install/import, save/resume, replay, rollback, compatibility [planned]
   ├─ Accessibility and browser acceptance                              [planned]
   ├─ Packaging, author/player docs, license notices                     [planned]
   └─ Full-suite, protocol, security, and isolation acceptance           [planned]
```

## Nested plan contracts

These are child plans inside the root plan. Their filenames are prospective and must not be created
until the parent entry gate is closed; this avoids multiple active documents claiming the same work.

### TG0 — Product contract plan

Outcome: confirm the first release as an original single-player trail-survival game, select a
non-infringing working identity, decide whether the first target is “first playable,” “MVP,” or
“customizable v1,” and accept the customization/security boundary.

Slices:

1. Product brief and vocabulary/IP review.
2. First-release capability matrix and non-goals.
3. Permanent application ID/source-placement confirmation.

Exit gate: the new application identity, product boundary, data-only customization promise, and
external-material policy are confirmed. No runtime code is part of TG0.

### TG1 — Application package plan

Accepted under the [TG1 dependency plan](TG1-APPLICATION-PACKAGE-DEPENDENCY-PLAN.md) and
[final receipt](TG1-SLICE-3-RECEIPT.md). The generic kernel hosts the independent one-record Trail
Survival catalog, exact activation, and empty state-space binding without Trail-specific production
C# or a `dnd2024` dependency. Zero-app, invalid-source, real-host protocol, replay, and two-app
isolation evidence all pass. No inert component was needed or created.

### TG2 — Run domain plan

Accepted under the [TG2 dependency plan](TG2-RUN-DOMAIN-DEPENDENCY-PLAN.md) and
[final receipt](TG2-SLICE-3-RECEIPT.md). The canonical state is split into eleven independently
versioned application components instead of one save-game blob, with exact stored-versus-derived
boundaries and cross-application rejection.

Confirmed semantic areas:

| Area | Canonical concern | Derived/read-only concern |
| --- | --- | --- |
| Scenario | selected scenario/rules version | title, description, content summary |
| Route | selected route, current landmark/leg, distance into leg, visited landmarks | route graph/content, preview, remaining distance |
| Run | lifecycle phase, current turn, outcome | seed cursor, available commands, progress percent |
| Party | membership and active/dead/departed status | survivor counts and warnings |
| Member | bounded health/condition state | status labels and risk summary |
| Conveyance | condition, capacity, current placement | load/capacity calculations |
| Resources | canonical quantities | consumption forecast and affordability |
| Policy | selected pace/rations/strategy IDs | expected costs and risk descriptions |
| Pending choice | exact unresolved event and offered choice IDs | safe player prompt and eligibility |

The authored route graph, markets, event/resource definitions, seed cursor, and player-safe read
projections were removed as false TG2 prerequisites: TG4 owns content and TG3 owns simulation state
needed by mechanics, while TG5/TG6 own player control/presentation. TG2's exit gate is met by closed
versioned schemas, explicit absence semantics, exact generic ECS ownership, invalid/no-change
evidence, and cross-application rejection.

### TG3 — Simulation-loop plan

Outcome: one exact command advances one root turn transaction. A command derives all costs,
probabilities, event eligibility, seeded random values, resource/health changes, distance/time,
arrival, and outcome from pinned catalog plus canonical state.

Recommended implementation order:

1. Create-run/setup transaction.
2. Purchase/sell transaction with capacity and affordability.
3. Policy selection transaction.
4. Rest turn.
5. Travel turn without random event.
6. Seeded event draw plus pending choice.
7. Event-choice resolution.
8. Landmark arrival and next-leg selection.
9. Victory/defeat terminal states.

Every mechanic needs positive, malformed, wrong-phase, stale, boundary, deterministic, replay,
rollback, and injected-failure evidence. A pending choice blocks incompatible commands. Terminal runs
reject further play without mutation.

Exit gate: the full loop plays headlessly from a known seed and produces byte-for-byte stable
results, exact audit, no partial effects, and no caller-authored outcomes.

### TG4 — Starter-scenario plan

Outcome: provide one original, deliberately compact scenario that exercises every MVP mechanic and
can be replaced by a second fixture pack.

Content budget for estimation, not confirmed canon:

- 6–10 landmarks and 7–12 directed legs;
- 3–5 setup roles or loadout presets;
- 6–10 resource/item kinds;
- 3–5 travel policies or policy combinations;
- 20–30 reusable events with bounded choices;
- 3–5 markets or service stops; and
- one victory and at least four defeat causes.

Exit gate: catalog validation succeeds from a fresh disposable database; every record has original
or licensed provenance; seeded runs reach all major event families and terminal outcomes.

### TG5 — Player-control bridge plan

Outcome: an ordinary trusted application page can discover allowed exact commands, submit one
explicit command, and receive a bounded result/receipt without AI and without bypassing application
execution, authorization, replay, or transaction ownership.

This is the highest-risk cross-owner seam. The child plan must first determine whether an existing
generic route can be safely adapted. If a new HTTP route or public contract is required, it needs
explicit confirmation. The web adapter must translate transport only; it cannot know Trail Game
IDs, select rules, construct effects, or write ECS state directly.

Exit gate: exact-action browser tests cover success, unauthorized, wrong app/state space, malformed,
stale, duplicate idempotency, mechanic failure, and injected rollback. Existing MCP and
conversation flows remain compatible.

### TG6 — Player web-application plan

Outcome: deliver one versioned HTML/CSS/JavaScript bundle using browser-native composition and the
generic control/read seams. Keep UI projections disposable and refreshable from canonical state.

Recommended slices:

1. New/resume plus setup and provisioning.
2. Main journey dashboard and direct commands.
3. Event, market, landmark, victory, and defeat views.
4. Accessibility, responsive layout, recovery, and browser acceptance.

Exit gate: the complete first-playable walk works with keyboard only, at narrow and desktop widths,
after browser refresh, and with AI/local-model providers disabled.

### TG7 — Customization plan

Outcome: an author can copy a minimal example, change only documented data, validate it locally,
register it through reviewed source/activation boundaries, and start a new run pinned to it.

The first delivery is data-only. Trusted JavaScript extension points are a later subplan because
they add security, compatibility, provenance, and semantic-versioning obligations.

Exit gate: two materially different scenario packs coexist; the same core mechanics and UI play
both; malformed/cyclic/out-of-bounds content gives actionable diagnostics; an overlay creates a new
application revision; existing runs remain pinned or fail with an explicit upgrade requirement.

### TG8 — Release-hardening plan

Outcome: close product acceptance after deterministic simulation, browser, isolation, provenance,
documentation, and recovery evidence.

Exit gate: focused tests, disposable catalog validation, full solution suite, protocol walk when
public contracts changed, fresh install, two-pack compatibility, save/resume, duplicate replay,
rollback, accessibility, and license checks all pass against one stable worktree.

## Size assessment

### Estimated slice count

| Workstream | Bounded slices | Relative risk |
| --- | ---: | --- |
| TG0 product contract | 1–2 | medium (identity/scope decisions) |
| TG1 application package | 2–3 | low–medium (kernel exists) |
| TG2 run domain | 4–6 | high (schema meaning and ownership) |
| TG3 simulation loop | 7–10 | high (atomic rules, deterministic randomness, balance) |
| TG4 starter scenario | 3–5 | medium (content volume and tuning) |
| TG5 player-control bridge | 2–4 | high (public/auth/cross-owner seam) |
| TG6 player UI | 4–6 | medium (state-driven UX and browser recovery) |
| TG7 customization | 4–7 | high (versioning, diagnostics, safe overlays) |
| TG8 hardening/release | 3–5 | medium–high (cross-system acceptance) |
| **Total customizable v1** | **30–48** | **medium-large application** |

### Calendar ranges

| Milestone | Solo engineer | Two engineers after TG2 contracts stabilize |
| --- | ---: | ---: |
| Seam proof | 2–4 weeks | 2–3 weeks |
| First playable | 5–8 weeks | 4–6 weeks |
| MVP | 10–16 weeks | 7–11 weeks |
| Customizable v1 | 18–30 weeks | 12–20 weeks |

The ranges include repository-grade planning, tests, validation, and receipts. They exclude a large
professional art/audio commission, localization, public hosting/security, app-store work,
multiplayer, a visual pack editor, and a broad scenario library. Content polish is likely to become
the schedule driver after the engine loop is stable.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 0 | TG0 product/identity confirmation | none | Product boundary, application identity, data-only v1 customization, and IP policy confirmed. |
| 1 | TG1 minimal independent application seam | TG0 | Tiny catalog previews/activates and isolated state-space round-trip passes. |
| 2 | TG2 run/scenario skeleton | TG1 | Minimal scenario, run, route, party, resource, and outcome state contracts accepted. |
| 3 | TG3 deterministic simulation loop | TG2 | [Accepted and hardened](TG3-SLICE-5-RECEIPT.md): full headless loop, audit, replay, rollback, limits, and no-change boundaries pass. |
| 4 | TG4 minimal scenario fixture | TG2/TG3 | One short route can reach victory and defeat headlessly. |
| 5 | TG5 exact browser action seam | TG1/TG3 | A button can safely invoke the exact action without AI. |
| 6 | TG6 first-playable UI | TG4/TG5 | New-to-result browser walk passes. |
| 7 | TG3/TG4 MVP breadth | first playable | Setup market, policies, event families, landmarks, save/resume accepted. |
| 8 | TG7 second pack and validator | MVP contracts stable | Data-only author workflow and version-pinning evidence pass. |
| 9 | TG8 release acceptance | TG0–TG7 | Customizable-v1 matrix passes and receipt closes the roadmap target. |

## Lowest ready leaf

TG3 is accepted under its [final hardening receipt](TG3-SLICE-5-RECEIPT.md). TG4 is the lowest
roadmap leaf. Its [dependency plan](TG4-STARTER-SCENARIO-DEPENDENCY-PLAN.md) is complete and its
[content contract](TG4-STARTER-SCENARIO-CONFIRMATION.md) awaits confirmation before permanent
scenario content is authored. TG0 decisions remain in the
[product-contract confirmation](TG0-PRODUCT-CONTRACT-CONFIRMATION.md).

## Conflicts and decisions

1. **Existing world reuse:** legacy `game.core.*` belongs to `dnd2024`. Decision: reuse patterns and
   acceptance evidence, not identifiers or live records. Any future shared-application extraction
   is a separate migration project and is not required for Trail Game.
2. **Direct UI commands:** the web host exposes generic reads and a confirmed conversational
   plan/execute flow, but no accepted deterministic button-to-exact-action contract was identified.
   TG5 must confirm the smallest generic seam before implementation.
3. **Scenario packs versus overlays:** packs coexist as catalog content selected and pinned by runs;
   overlays change the effective application revision. They must not be conflated.
4. **Randomness:** all outcomes use server-derived deterministic seeds/cursors. Browsers, models,
   and pack records may declare distributions but never supply a resolved roll.
5. **Save format:** canonical ECS state is the save. An export file, if later added, is a bounded
   projection with application/scenario/schema fingerprints, not a second authority.
6. **Customization safety:** data-only packs are in v1. Trusted JavaScript extensions require a
   separate confirmed security/versioning plan.
7. **Historical theme:** the bundled scenario should be original and may be fictional. A historically
   specific pack needs its own source/provenance review and content budget.

## Confirmation gates

Confirmation is required before runtime work for:

1. the permanent application ID, display name, and source placement;
2. the first milestone target: first playable, MVP, or customizable v1;
3. first-release theme: original historical-inspired or fictional trail;
4. schema meanings for run, scenario, route/progress, party/member, conveyance, resources, policy,
   pending choice, and outcome;
5. every permanent component/mechanic/procedure/event/relationship ID;
6. the seeded randomness and replay contract;
7. the exact-action browser HTTP/public contract and authorization behavior;
8. content-pack version pinning, coexistence, overlay, and state-space upgrade semantics;
9. any migration or compatibility behavior for non-empty state spaces;
10. any direct reuse of external code/assets and its notice/provenance obligations; and
11. completed first-playable, MVP, and customizable-v1 acceptance.

## Acceptance strategy

Each simulation leaf must provide:

- positive behavior from a fresh state space;
- malformed, missing, wrong-phase, wrong-scope, and unauthorized rejection with no state change;
- minimum/maximum/zero/capacity/health/resource/clock boundaries as relevant;
- deterministic same-seed and divergent-seed evidence;
- stale state and duplicate idempotency replay evidence;
- injected failure proving atomic rollback;
- catalog revision/scenario version compatibility evidence;
- cross-application and system-only-host isolation; and
- browser acceptance only after the headless mechanic is authoritative.

Feature acceptance runs focused tests while iterating, then the disposable catalog validator and
full suite against one stable worktree. Run the protocol walk only when a protocol/public dependency
registration changed. No plan should quote old pass counts as current acceptance.

## External implementation references and licensing

- [warnock/oregon-trail-game](https://github.com/warnock/oregon-trail-game) is MIT-licensed and may
  be reviewed for its small JavaScript caravan/day/event loop. Do not copy code until the exact
  files and retained notices are recorded in the relevant child plan/receipt.
- [Trail Typers](https://github.com/wfryer/trail-typers) is an MIT-licensed reference for a compact
  browser-local customization and optional peer-to-peer presentation, not a survival simulation
  dependency.
- [clintmoyer/oregon-trail](https://github.com/clintmoyer/oregon-trail) preserves an Unlicense BASIC
  version useful for historical design study, not a JavaScript dependency or source of branded
  assets.

The application will use an original title, presentation, prose, art, audio, scenario data, and
balance. Public repository visibility without an explicit compatible license is not permission to
copy. Each accepted direct reuse needs a provenance ledger entry and required notice.

## Planning receipt

- Runtime artifacts created through TG1: one descriptive application procedure under
  `catalog/applications/trail-survival/`; no playable state or rule artifact.
- Permanent TG1 identities created: `trail-survival`, `trail-survival-core`, and
  `procedure.trail-survival.about`.
- TG2 runtime artifacts: one governing procedure and eleven component metadata/schema pairs under
  `catalog/applications/trail-survival/`; no fixture, mechanic, migration, public route, or live
  database state.
- Existing accepted owners inspected: application kernel, legacy application ownership, web
  interface, application execution, and world travel evidence.
- TG0 runtime artifacts: none; its decisions are recorded in the confirmation.
- TG1 acceptance: [recorded](TG1-SLICE-3-RECEIPT.md).
- TG2 meanings and permanent IDs: [confirmed](TG2-RUN-DOMAIN-CONFIRMATION.md).
- TG2 acceptance: [recorded](TG2-SLICE-3-RECEIPT.md).
- TG3 confirmation: [recorded](TG3-SIMULATION-CONFIRMATION.md).
- TG3 acceptance and boundary hardening: [recorded](TG3-SLICE-5-RECEIPT.md).
- Active implementation: none; [TG4 Slice 1](TG4-SLICE-1-IMPLEMENTATION.md) awaits confirmation.
