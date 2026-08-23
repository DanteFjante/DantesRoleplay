# Platform enabling features roadmap

Status: **E6 Slices 1–2 and E10 Slices 1–3A are accepted. E10 Slice 3A has an implemented
decision proposal; E7–E9 remain planned implementation features, not informal exceptions.**
Last updated: 2026-08-21

## Purpose

This roadmap turns cross-domain engine blockers into ordinary reviewed features. Each has one
owner, a dependency plan, independent slices, focused tests, a repository acceptance gate, and a
stop rule. A D&D, character, campaign, quest, session, or world feature may depend on an enabling
feature but may not implement a private substitute.

## Scope boundary

These are generic platform capabilities. They contain no D&D rule, game component, campaign
content, player/NPC state, source rule, or feature-specific action. Each consumer retains its own
mechanics, state, effects, source attribution, and player-facing intent.

## Active architecture refactor delivery

The cross-component delivery to separate system capabilities physically, extract local AI behind a
ruleset-neutral file/glob ingestion boundary, and evict compiled game rules is tracked by the
[system modularization dependency plan](modularization/SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md).
Architecture/composition ratchets, twelve generic capability moves, seven game-consumer quarantine
areas, and the standalone local-AI scanner/providers are verified through Slice 23. Compiled-rule
eviction, the three remaining scaffolded platform areas, generic derived indexing, and final
independence proof remain active work.

The semantic redesign that turns those physical components into an application-neutral kernel is
owned by the [generic application kernel dependency plan](application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md).
It defines `system.*` administration, registered applications and path/glob sources, deterministic
directory overlays, versioned application-owned component schemas, state-space isolation, and an
ECS capable of storing any bounded schema-valid JSON value. It is planning-only; its datatype,
migration, authorization, and public kinds require confirmation.

The server-controlled workflow for `system.*`/application-scoped intent planning, database-registered
directory overlays, typed receipts, trusted hybrid feature discovery, explicit execution, and
versioned recipe learning is specified by the
[Interaction orchestration dependency plan](interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md).
It consumes the generic application kernel's effective manifests and is planning-only; it does not
authorize its proposed component, migrations, public kinds, or recipe-promotion policy until the
named gates are confirmed.

## Dependency graph

~~~text
Platform enabling capabilities
├─ E1 events/subscriptions/chains                                     [verified]
├─ E2 phrase-first intent selection                                   [verified]
├─ E3 hierarchical catalog navigation                                 [separately planned]
├─ E4 local intent routing                                            [planned; depends on E2/E3]
├─ E5 exact numeric sandbox fidelity                                  [planned]
├─ E6 typed dependent mechanic composition                            [Slices 1–2 accepted]
│  ├─ F20 derived path-cost → budget spend
│  ├─ F32 cast admission/effect consequences
│  ├─ F34 Hide/surprise → condition/Initiative
│  └─ F38 social context → ability check
├─ E7 atomic staged composition and virtual projections               [planned; depends on E6]
│  ├─ Character CH5 creation
│  └─ F35 monster bootstrap
├─ E8 dynamic event role binding and bounded indexed fan-out          [planned; depends on E1]
│  ├─ F33 active-rest clock/interruption lifecycle
│  ├─ F17/F18 dynamic event consumers
│  └─ F32 timed active effects
├─ E9 trusted principal context and authorization hook                [planned; external identity decision]
   ├─ F38 GM social adjudication
   ├─ campaign trusted-host actions
   └─ Character CH14 player control
└─ E10 durable system feedback                                       [Slices 1–3A accepted]
   ├─ append-only LLM reports and local human triage/export
   └─ local retention staging; remote access gated by E9
~~~

## Delivery order

| Order | Enabling feature | First implementation slice | Primary consumers |
| ---: | --- | --- | --- |
| E6 | Typed dependent mechanic composition | **Slices 1–2 accepted** — [receipts](e6/); one named consumer adoption is next | F20, F32, F34, F38 |
| E7 | Atomic staged composition | Internal virtual-effect/projection overlay proof | CH5, F35 |
| E8 | Dynamic event role binding and bounded fan-out | **Slice 1 accepted** — exact event-payload role binding; Slice 2 bounded selector awaits confirmation | F17, F18, F33, F32 |
| E9 | Trusted principal context and authorization hook | Intentionally deferred for the prototype; resume after identity-provider and policy-boundary confirmation | F38, campaigns, CH14 |
| E10 | [Durable system feedback](e10/E10-DEPENDENCY-PLAN.md) | **Slices 1–3A accepted**; remote slices remain gated by E9 | Integration testing and developer review |

E6 precedes E7 because staged roots need the same reviewed data-flow vocabulary. E8 is independent
of E6/E7 and may be scheduled after its own current contracts are re-read. E9 is a security and
product-identity boundary; it is not made “ready” by a catalog-only implementation. E10 is
independent of game-feature delivery and may proceed after its own semantic/public boundary is
confirmed; remote access to its reports remains blocked by E9 and deployment policy.

The prototype may use its completed local E10 feedback loop without E9. Future identity and remote
feedback work is intentionally deferred and summarized in
[E10 future development](e10/E10-FUTURE-DEVELOPMENT.md).

## Shared implementation constraints

- Every E6–E10 implementation is a `procedure.system.modify` boundary: re-read the governing
  kernel/public-surface contracts, preserve the three-tool budget, and add focused DataAccess and
  integration tests before enabling a consumer.
- Declarations are closed, versioned, statically validated, acyclic where relevant, and surfaced
  in capability/contract discovery. Runtime must reject unknown paths, role names, ambiguous
  sources, cycles, excessive fan-out, and undeclared output/effect dependencies before a change.
- An enabling feature may expose typed platform evidence and proposed effects, never game
  vocabulary or a generic code/evaluation escape hatch.
- Each consumer migrates only after its own plan is amended with concrete bindings, replay,
  rollback, and routing assertions. Passing the E feature's fixture never silently enables every
  existing mechanic.

## Consumer migration matrix

| Consumer | Required enabling feature | Explicitly not supplied |
| --- | --- | --- |
| Feature 20 movement | E6 derived path-cost binding | Map, placement, terrain, or a movement rule |
| Features 32/34/38 | E6 trusted child-result input | Spell, visibility, social, D20, or action rules |
| CH5 and Feature 35 | E7 virtual actor construction | Character/monster source selection or component semantics |
| Features 17/18/32/33 | E8 dynamic event routing | A scheduler, automatic time advance, or free event queries |
| Feature 38 / Campaign / CH14 | E9 trusted principal/authorization hook | Login, identity provider, RBAC language, or player policy itself |
| Integration testing | E10 append-only feedback report | Automatic bug diagnosis, source edits, issue-tracker delivery, or remediation |

## Plan-change rule

Split a feature again if it needs unrestricted JSON transformation, dynamic code evaluation,
arbitrary graph/database querying, a new public tool kind, background scheduling, or a
feature-specific authorization model. Those are separate semantic boundaries, not implementation
details of E6–E10.
