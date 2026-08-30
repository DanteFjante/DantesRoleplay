# Platform enabling features roadmap

Status: **E6 Slices 1–2, E8 Slices 1–2 plus downstream trigger Slices 0–10, E10 Slices 1–3A, application-kernel Slices 0–11J and interaction-orchestration Slices 12A–12H, and its legacy ownership ratification are accepted. E10 Slice
3A has an implemented decision proposal; E9 private-host Slice 1 is accepted while its general
multi-user and transport-parity work remains planned. E7 remains planned; E8 consumer adoption remains separately owned, not
informal exceptions.**
Last updated: 2026-08-25

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

The accepted ruleset-neutral catalog portability slice adds reconstructable setup and reviewed
existing-database upgrade commands. Its boundary and evidence are owned by the
[catalog setup/upgrade implementation](catalog-portability/CATALOG-SETUP-UPGRADE-IMPLEMENTATION.md).

The accepted cross-component delivery separating system capabilities physically, extracting local AI behind a
ruleset-neutral file/glob ingestion boundary, and unlinking compiled game adapters is tracked by the
[system modularization dependency plan](modularization/SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md).
Architecture/composition ratchets, generic capability moves, game-consumer quarantine, and the
standalone local-AI scanner/providers are verified through Slice 23. Slice 24 removes generic host
and build references to the retained game-adapter trees without deleting user files, and Slice 12H
closes the final generic-build/local-AI independence proof.

The semantic redesign that turns those physical components into an application-neutral kernel is
owned by the [generic application kernel dependency plan](application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md).
It defines `system.*` administration, registered applications and path/glob sources, deterministic
directory overlays, versioned application-owned component schemas, state-space isolation, and an
ECS capable of storing any bounded schema-valid JSON value. It also owns reusable application-scoped
derived projections: exact component-field and projection dependencies form a validated acyclic
graph, materialize through deduplicated bounded reads, expose reverse schema/mapping impact, and may
use only disposable revision-keyed caches. Canonical component state remains authoritative. E6 is
the dependent-execution foundation; E7 remains the separate owner only when a projection must see
uncommitted root-local virtual effects. The same plan owns described, deterministic,
cursor-paginated catalog traversal/search that remains complete without vectors or local AI. The
kernel's [Slice 0 semantic contract](application-kernel/APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md),
[Slice 1 read-only legacy inventory](application-kernel/APPLICATION-KERNEL-SLICE-1-IMPLEMENTATION.md),
and [Slice 2 pure contracts](application-kernel/APPLICATION-KERNEL-SLICE-2-IMPLEMENTATION.md), plus
[Slice 3 registry persistence](application-kernel/APPLICATION-KERNEL-SLICE-3-IMPLEMENTATION.md),
[Slice 4 source overlays](application-kernel/APPLICATION-KERNEL-SLICE-4-IMPLEMENTATION.md),
[Slice 5 component type/schema security](application-kernel/APPLICATION-KERNEL-SLICE-5-IMPLEMENTATION.md),
[Slice 6 application-scoped ECS state](application-kernel/APPLICATION-KERNEL-SLICE-6-IMPLEMENTATION.md),
[Slice 7 structural projections](application-kernel/APPLICATION-KERNEL-SLICE-7-IMPLEMENTATION.md),
and [Slice 8A atomic ECS effects](application-kernel/APPLICATION-KERNEL-SLICE-8-IMPLEMENTATION.md),
[Slice 9 deterministic catalog navigation](application-kernel/APPLICATION-KERNEL-SLICE-9-IMPLEMENTATION.md),
and [Slice 10 system protocol](application-kernel/APPLICATION-KERNEL-SLICE-10H-IMPLEMENTATION.md),
plus [Slice 11 complete legacy adoption and execution parity](application-kernel/APPLICATION-KERNEL-SLICE-11J-IMPLEMENTATION.md),
are accepted, as is the [legacy `dnd2024` ownership ratification](application-kernel/LEGACY-OWNERSHIP-RATIFICATION.md).
Slice 11 is accepted through 11J: explicit complete legacy-state adoption, generic graph
edges/effects, replay/rollback evidence, migration, authenticated protocol, application-ECS
projection parity, and deterministic sandbox invocation across all 14 ratified mechanics pass
without changing the normal live database. Dynamic application write protocol, event integration,
and model-directed execution remain assigned to interaction orchestration rather than the kernel.

The server-controlled workflow for `system.*`/application-scoped intent planning, database-registered
directory overlays, typed receipts, trusted hybrid feature discovery, explicit execution, and
versioned recipe learning is specified by the
[Interaction orchestration dependency plan](interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md).
It also owns the planned dual Codex product roles: a bounded inner Luna Low mechanic planner and a
player-facing outer Luna High guide that can delegate or submit its own verified proposal, plus a
reusable outer conversation component for application pages. It consumes the generic application
kernel's effective manifests. Slices 12A–12D accept the read handoff, threat model, new
`interaction-orchestration` owner, pure authority/proposal/result contracts, provider-isolation
requirements, explicit execution-consent reference, trusted/untrusted feature lanes, deterministic
exact/lexical retrieval, optional hybrid vector candidates, and the separately configured disposable
derived index, and append-only authorized/redacted interaction receipts in the authoritative main
database. [Slice 12E](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-12E-IMPLEMENTATION.md)
accepts the bounded local/remote planner, common current-authority verifier, safe receipt evidence,
and dedicated no-tools Luna Responses adapter; the existing operator Codex bridge is deliberately
not reused. [Slice 12F](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-12F-IMPLEMENTATION.md)
is accepted with the four `system.*` protocol kinds, exact application action owner,
two-phase consent/replay/partial-progress behavior, basic private-host authorization, ephemeral
application conversations, and reusable `<application-conversation>` surface. Its prerequisite
review rejects the legacy intent-matching `IActionRunner` as an application-state executor and
specifies the smallest ruleset-neutral exact-action correction. Slice 12 has eight subslices,
12A–12H, and all are accepted. See the
[Slice 12H receipt](interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-12H-RECEIPT.md).
Slice 12G adds explicit opt-in candidate learning, append-only recipe storage, private
review/promotion, and current-authority verified retrieval. Slice 12H closes the combined acceptance
and independence matrix, including the guarded removal of game-adapter compile wildcards from the
generic build.

A [Slice 13 adaptive extension](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md)
has accepted [13A](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-13A-IMPLEMENTATION.md): an
explicit local or remote outer provider with no automatic network fallback, and
[13B](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-13B-IMPLEMENTATION.md): correlated
inner-first resolution with one typed outer fallback. [13C](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-13C-IMPLEMENTATION.md) is accepted:
application-owned read-only query contracts
can return safe results or bind typed values into later actions with exact private receipts and replay.
The accepted [13D implementation](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-13D-IMPLEMENTATION.md)
adds bounded intent-level task agendas and process-local fresh-state work batches. The
Accepted [13E](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-13E-IMPLEMENTATION.md)
adds explicit value-free outer-fallback learning, deterministic host promotion, and safe later inner
route guidance without a migration or new transport operation. Final
[13F combined acceptance](interaction-orchestration/INTERACTION-ORCHESTRATION-SLICE-13F-IMPLEMENTATION.md)
is accepted with green repository evidence; see its
[receipt](interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-13F-RECEIPT.md).

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
├─ E8 dynamic event role binding and bounded indexed fan-out          [Slices 1–2 accepted; depends on E1]
│  ├─ F33 active-rest clock/interruption lifecycle
│  ├─ F17/F18 dynamic event consumers
│  ├─ F32 timed active effects
│  └─ durable scheduling/external triggers                            [downstream Slices 0–10 accepted]
├─ E9 trusted principal context and authorization hook                [private-host Slice 1 + MCP read parity accepted]
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
| E8 | [Dynamic event role binding and bounded fan-out](e8/E8-DEPENDENCY-PLAN.md) | **Slices 1–2 accepted** — exact event-payload role binding and bounded indexed fan-out; Slice 3 is consumer adoption. The separate [downstream trigger plan](e8/E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md) has **Slices 0–10 accepted** and is complete for notification-only triggers. | F17, F18, F33, F32; scheduled reminders and external sources downstream |
| E9 | Trusted principal context and authorization hook | **Private local/Tailscale web Slice 1 plus loopback MCP administrative reads and registry writes accepted**; other MCP writes, multi-user identity, persistent grants, and consumers remain planned | F38, campaigns, CH14 |
| E10 | [Durable system feedback](e10/E10-DEPENDENCY-PLAN.md) | **Slices 1–3A accepted**; remote slices remain gated by E9 | Integration testing and developer review |

E6 precedes E7 because staged roots need the same reviewed data-flow vocabulary. E8 is independent
of E6/E7; its generic routing Slices 1–2 are accepted and consumers adopt them under their own
plans. Durable schedules, conditions, feeds, and device observations have accepted semantic and
pure-contract, persistence, security-hardening, private-ingestion, durable-worker, and atomic
notification/status/recurrence/state-condition/observation-match/phone-identity/management Slices 0–10 under the separate downstream E8 trigger dependency tree;
that notification-only downstream plan is complete. E9 is a security and
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
| Scheduled reminders and external inputs | [E8 downstream trigger scheduling](e8/E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md) | Direct event insertion, automatic world-clock advancement, ambient action authority, or arbitrary uploaded code |
| Feature 38 / Campaign / CH14 | E9 trusted principal/authorization hook | Login, identity provider, RBAC language, or player policy itself |
| Integration testing | E10 append-only feedback report | Automatic bug diagnosis, source edits, issue-tracker delivery, or remediation |

## Plan-change rule

Split a feature again if it needs unrestricted JSON transformation, dynamic code evaluation,
arbitrary graph/database querying, a new public tool kind, background scheduling, or a
feature-specific authorization model. Those are separate semantic boundaries, not implementation
details of E6–E10. The planned scheduling/external-observation boundary is recorded in the
[E8 downstream trigger plan](e8/E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md).
