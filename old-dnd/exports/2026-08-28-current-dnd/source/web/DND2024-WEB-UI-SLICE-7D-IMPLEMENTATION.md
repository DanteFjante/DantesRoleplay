# D&D 2024 web UI Slice 7D implementation — player knowledge notebook

Status: **accepted as one combined 7D2–7D3 product batch (2026-08-27)**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 7D / F2
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. This is a generic campaign-knowledge authorization and
presentation boundary, not a D&D rule.
Outcome: show a useful notebook of facts, clues, rumours, and secrets actually available to the
configured player perspective, without requiring the player to transcribe learned information.
Exclusions: direct fact/entity browsing, visibility-as-authorization, request-selected actor/role,
cross-campaign results, GM-secret exposure, compiling retained `old-dnd` code as a shortcut,
model-authored truth, maps, images, tactical movement, and unreviewed bulk knowledge grants.
Allowed files/areas: `src/system/knowledge/`, the generic MCP commit dispatcher needed for the
explicit reviewed synchronization boundary, current-host dependency registration, the D&D browser
workspace and its web adapter/tests, the active D&D metadata activation, live database backup and
reviewed state synchronization, and the owning plan/roadmap/status/receipt documents.
Stop point: do not infer or write knowledge from visibility, entity presence, filenames, IDs, or
prose. Only the exact reviewed manifest below may be synchronized in this batch.

## Confirmed decisions

- The user's instruction to continue accepts Slice 7C and confirms the desired audience-safe
  player-knowledge outcome.
- The confirmation does not authorize treating descriptive `public`/`party`/`gm` visibility as
  identity, granting the selected browser actor control, exposing trusted-GM ECS records, or
  reviving recovery sources as current runtime dependencies.
- No placeholder tab is useful: the page must either show authorized knowledge or keep the surface
  absent with a recorded blocker.
- The user's 2026-08-27 request to make larger batches confirms combining the previously ordered
  7D2 and 7D3 leaves into one end-to-end product increment, including the required private route,
  generic private synchronization command, live activation/synchronization, and browser acceptance.
- The new permanent private commit kind is `system.knowledge-state.sync`. It accepts campaign plus
  an exact reviewed list only; ambient policy selects the actor. The new private player read is
  `GET /api/applications/{applicationId}/campaigns/{campaignId}/knowledge`; it accepts no actor,
  role, world, sensitivity, hidden flag, or canonical knowledge ID.

## Governing owners

- `procedure.game.core.world.knowledge` requires player-safe filtering through the separate bounded
  `knowledge-answer` query and a host-supplied audience policy. The request may not contain a
  principal, actor, role, world, visibility override, exact knowledge ID, or include-hidden flag.
- `KNOWLEDGE_AND_FACTS_PLAN.md` keeps truth, epistemic state, sensitivity, and authorization
  separate. It explicitly blocks production/player exposure until a real authenticated audience
  policy exists.
- Knowledge Slice 6's retained contracts require authorization before any campaign/world read,
  actor participation as defense in depth, effective actor knowledge state, allowed-ID filtering
  before ranking, safe result projection, and a second policy/state check after inference.

## Accepted prerequisites and current evidence

1. [Slice 7D1](DND2024-WEB-UI-SLICE-7D1-RECEIPT.md) has accepted the provider-neutral authorized
   core, fixed loopback-only Orban actor seat, catalog-owned exact D&D binding, current-host
   registration, and independent campaign participation verification. No caller surface exists.
2. The current modular knowledge core is registered in the host but intentionally has no caller
   surface. This batch adds a safe notebook projection and a private, reviewed synchronization
   boundary; it does not restore retained `old-dnd/` runtime dependencies.
3. The running `dnd2024-main` state contains 166 knowledge records, including trusted-GM secrets,
   but zero `dnd2024.game.core.world.knowledge.baseline` or
   `dnd2024.game.core.world.knowledge.state` relationships. Therefore no actor-safe notebook can be
   derived without inventing what the character knows.
4. The generic information store contains zero records and is not a substitute for canonical world
   knowledge or its actor epistemic state.
5. Development configuration names the exact Orban actor seat. The new metadata is not yet in live
   D&D activation revision 2; activation and reviewed epistemic state are one explicit
   synchronization boundary rather than an implicit startup write.

## Reviewed initial-knowledge manifest

The following existing knowledge entities are explicitly approved as actor state `known` for the
fixed Orban seat in `campaign.thalorien.brackenford`. This is an authored campaign-state decision,
not an inference from each record's sensitivity or presence.

| Existing knowledge entity | Reviewed player-facing topic |
| --- | --- |
| `fact.thalorien.brackenford` | Brackenford, its ordinary services, and frontier role |
| `fact.thalorien.greenmantle` | The Greenmantle and Valeros's guarded western frontier |
| `fact.thalorien.frontier-watch` | Common frontier watch and patrol practices |
| `fact.thalorien.settlement-hospitality` | The Hearthside hospitality custom |
| `fact.thalorien.present-dangers` | Ordinary dangers during the long peace |
| `fact.thalorien.wilderness-danger` | The danger of wilderness beyond settled routes |
| `fact.thalorien.continent.thalos` | Thalos as the known continent |
| `fact.thalorien.seven-kingdoms` | Thalorien's seven-kingdom structure |
| `fact.thalorien.seven-kingdom-names` | The names of the seven kingdoms |
| `fact.thalorien.peace-as-value` | Peace as a central cultural value |
| `fact.thalorien.peace-generations` | The long peace as normal lived experience |

No baseline link is approved in this batch. No rumour, clue, or secret is granted. In particular,
`secret.thalorien.brackenford-goblin-migration` and
`secret.thalorien.brackenford-waystone-cellar` remain unknown and must not appear in a response,
count, category, empty-state distinction, log detail, or browser payload.

## Smallest safe prerequisite tree

| Slice | Boundary | Model | Effort | Estimate |
| --- | --- | --- | --- | --- |
| 7D0 | **Accepted:** adopt the provider-neutral contracts, canonical projection, effective-state resolver, allowlisted lexical retrieval, and safe answer coordinator into current modular owners. | `gpt-5.6-sol` | xhigh | 8–13 EP |
| 7D1 | **Accepted:** fixed loopback-only Orban audience, catalog-owned binding, active-document verification, exact participation, and current-host registration. | `gpt-5.6-sol` | xhigh | 4–6 EP |
| 7D2 | **Accepted in this combined batch:** atomically synchronize the exact reviewed actor manifest through the generic knowledge owner and activate the reviewed D&D binding metadata. | `gpt-5.6-sol` | high | 4–7 EP |
| 7D3 | **Accepted in this combined batch:** add the narrow private web adapter and game-styled notebook/search view over the safe result shape, omitting canonical IDs, sensitivities, hidden counts, and secret classification. | `gpt-5.6-terra` | high | 4–6 EP |

The two leaves share one player-visible outcome and one live synchronization boundary. Their
independent invariants remain separately tested inside this combined accepted batch.

## Acceptance matrix after prerequisites

| Case | Required outcome |
| --- | --- |
| Actor with explicit/baseline knowledge | Only safe statement/rumour/evidence text and stance appears. |
| Familiar | Topic recognition appears without proposition text. |
| Unknown/hidden/no match | One indistinguishable bounded unknown state; no count or ID leak. |
| GM seat | Canonical campaign-world scope only, never cross-campaign. |
| Missing/revoked/wrong-campaign audience | Denial occurs before campaign or knowledge reads. |
| Policy/state change | Result is discarded, retried once, then fails generically if still stale. |
| Browser tampering | Actor, role, world, visibility, IDs, and hidden flags cannot be supplied. |
| Read isolation | The notebook performs no game, knowledge, event, notification, or index-authority write. |
| Reviewed synchronization | Preview and commit use one atomic effect batch, exact campaign/actor participation, canonical world membership, optimistic revisions, private-operator authorization, durable audit, and replay identity. |
| Live acceptance | Database backup exists; active metadata is read back; eleven exact actor-state relationships are read back; restart and browser smoke show the notebook. |

## Exit gate

Accept only when the generic synchronization owner, safe notebook reader, private route, and
game-styled tab pass focused and full tests; catalog validation passes; the live database backup,
metadata activation, eleven-edge synchronization and exact readback are recorded; and the running
page is exercised in the in-app browser without exposing canonical IDs or excluded secrets.

Accepted evidence is recorded in the
[combined 7D2–7D3 completion receipt](DND2024-WEB-UI-SLICE-7D2-7D3-RECEIPT.md).
