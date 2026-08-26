# Customizable trail-survival application roadmap

Status: **TG0 through TG3 accepted; TG4 starter scenario awaits content confirmation**
Last reviewed: 2026-08-25
Application identity: **`trail-survival` / Trail Survival**

## Outcome and ownership

Deliver an original, browser-playable trail-survival application in which a player creates a party,
equips a conveyance, chooses a route and travel policy, resolves deterministic day turns and events,
reaches landmarks, saves/resumes, and wins or loses under a selected scenario pack.

The application must be customizable without changing generic C#:

- ordinary scenario authors provide bounded data for routes, resources, events, markets, tuning,
  narrative, and presentation;
- trusted advanced authors may later provide sandboxed application JavaScript through reviewed
  application sources;
- the generic kernel owns registration, schema validation, application-scoped state, transactions,
  replay, audit, catalog navigation, and application execution; and
- the web interface owns transport and presentation, never simulation rules.

This is a new application. The legacy `game.core.*` world and travel records are owned by
`dnd2024`; they are implementation evidence and design reference, not cross-application runtime
dependencies.

## Delivery map

The governing plan is the
[Trail Game dependency plan](TRAIL-GAME-DEPENDENCY-PLAN.md). It contains the following nested
workstreams, each of which receives its own dependency/implementation document only when its entry
gate is closed:

| Plan | Capability | Current state |
| --- | --- | --- |
| TG0 — Product contract | Original identity, audience, first-release scope, customization tiers, and IP boundary | [confirmed](TG0-PRODUCT-CONTRACT-CONFIRMATION.md) |
| TG1 — Application package | Registered application source, deterministic catalog, activation, and fresh state space | [accepted](TG1-SLICE-3-RECEIPT.md) |
| TG2 — Run domain | Scenario, route, party, member, conveyance, resources, clock, progress, and outcome state | [accepted](TG2-SLICE-3-RECEIPT.md) |
| TG3 — Simulation loop | Setup, market, policy, travel/rest/forage turns, deterministic events, arrivals, victory, and defeat | [accepted and hardened](TG3-SLICE-5-RECEIPT.md) |
| TG4 — Starter scenario | One original end-to-end data pack with balanced route and narrative content | [awaiting content confirmation](TG4-STARTER-SCENARIO-CONFIRMATION.md) |
| TG5 — Player control bridge | Explicit non-AI browser reads and exact action submission with replay/authorization safety | missing cross-owner seam |
| TG6 — Player web application | Setup, journey, decision, event, inventory, status, save/resume, and result views | planned |
| TG7 — Customization | Content-pack schema, validator, examples, version pinning, overlays, and author workflow | planned |
| TG8 — Release hardening | Accessibility, deterministic balance runs, failure recovery, packaging, documentation, and acceptance | planned |

## Release targets and size

| Target | Included plans | Expected bounded slices | Solo engineering range |
| --- | --- | ---: | ---: |
| Seam proof | Narrow TG0–TG3 path through one no-choice travel action | 5–8 | 2–4 weeks |
| First playable | Narrow TG0–TG6 path: one party, one resource set, one route, travel/rest, deterministic event, save/resume, win/lose, minimal UI | 10–14 | 5–8 weeks |
| MVP | TG0–TG6 with setup economy, several event families, multiple landmarks, complete browser flow, and tests | 20–28 | 10–16 weeks |
| Customizable v1 | TG0–TG8 with safe content packs, documentation, validator, versioning, polish, and release evidence | 30–48 | 18–30 weeks |

These ranges assume one experienced engineer using the existing kernel and web foundations, modest
original art/audio, one bundled scenario, no multiplayer, and no visual pack editor. A polished
content library, commercial-grade art/audio, mobile packaging, or user-uploaded JavaScript is a
separate expansion and can materially increase the schedule.

## First-release boundary

The recommended first release is a deterministic single-player application with data-only scenario
customization. It deliberately excludes:

- `The Oregon Trail` name, logos, audiovisual assets, text, or other franchise presentation;
- historical-accuracy claims beyond separately sourced content;
- multiplayer, accounts, cloud synchronization, mobile-store packaging, and achievements;
- reflex/action hunting or river-crossing minigames;
- procedural AI as simulation authority or as a requirement to use the game;
- a visual scenario editor; and
- arbitrary untrusted JavaScript uploads.

## Next action rule

TG3 is accepted. TG4 planning is complete; confirm the exact Northstar Passage content contract
before authoring its permanent scenario/route/event IDs. Do not begin TG5 public/browser transport.
