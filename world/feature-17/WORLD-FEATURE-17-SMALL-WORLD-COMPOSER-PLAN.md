# World Feature 17 dependency plan — compose one closed small world

Status: **Slice 1 verified; W17 is complete.**
Last updated: 2026-08-21

## Execution rule

This is the World-owned dependency for Campaign Feature 10 (C10), not a C10 implementation.
It follows `AGENTS.md`, `procedure.world.change`, and the planning/one-slice rule in
`TERRA-FEATURE-PLANNING-GUIDE.md`. The catalog remains authoritative for World vocabulary.
The eventual composer is an internal, effect-free capability: it may read and dry-run a staged
bundle, but it never applies effects, opens or commits a transaction, records an audit, emits an
event, exposes an MCP operation, or creates campaign state.

The [R3 ratification record](../../campaign/feature-10/CAMPAIGN-FEATURE-10-R3-CROSS-ROOT-RATIFICATION.md)
approves the child contract, namespace source, outer coordinator, fingerprint, and failure/audit
policy. No permanent catalog ID, C10 fixture, component, procedure, public operation, or runtime
source was created by this planning pass.

## Target capability

Given one closed, hand-authored C10 small-world blueprint and a coordinator-supplied namespace,
the World child can deterministically validate the complete World graph and return only ordered
World effects plus typed review evidence, without writing state.

### Included

- One root, one region, three locations, two canonical adjacency links, one faction, two actors
  with motives, one fact, one rumour, one secret, and three clues.
- Existing W1 topology, W3 faction/motive, and W4 knowledge vocabulary only, including the active
  knowledge-classification companion contract.
- Closed authored text/data slots, fixed local keys, deterministic proposed entity IDs, collision
  validation, and a staged zero-write dry-run.
- Typed identity, local-key, count, visibility, and problem evidence that an eventual C10 preview
  can combine with the campaign child's evidence.

### Excluded

- A C10 preview/create route, campaign validation/effects, a public command, operation/audit,
  transaction ownership, events, notifications, fingerprints, durable reservations, or a fixture.
- Generated content, alternate world shapes, arbitrary graph edges, arbitrary local keys,
  containment alternatives, quests, characters, time, travel, player authorisation, maps, or
  player-facing rendering.
- Any new World component, relationship kind, procedure, mechanic, schema, migration, or catalog
  record.

## Inventory and ownership result

The 2026-08-21 repository search covered `small world`, `small-world`, `WorldComposer`,
`compose world`, `blueprint`, `local key`, `creationKey`, and `new-world` across World, DataAccess,
tests, catalog, campaign, and planning files. It found no World small-world composer, no W17 plan,
and no C10 World fixture owner. W16 is the last assigned World feature. The search also found the
generic `IStagedWorldComposer`/`StagedWorldComposer`; it is infrastructure, not a World-content
owner, and validates an immutable effect overlay without applying it.

| Existing owner | Reused responsibility | Not reused for |
| --- | --- | --- |
| W1 / `procedure.game.core.world.location` | Root/location data, containment slots, canonical adjacency | Campaign or new-world coordination |
| W3 / `procedure.game.core.world.faction` | Faction/motive data, membership, control links | Campaign references or agenda action |
| W4 / `procedure.game.core.world.knowledge` | Knowledge data, classification, scope/about/support links, visibility meanings | Audience authorisation or clue revelation |
| `IStagedWorldComposer` | Read-only effect dry-run and virtual `IWorldStore` | Content validation, World identifiers, outer transaction, or audit |
| C2 `CampaignBlueprintValidator` | Existing-world campaign validation | A new staged world or nested C10 creation |

World owns this child because every generated entity/component/link is World state. C10 owns no
World representation. The future outer coordinator alone owns the single transaction, audit,
event/notification routing, preview fingerprint, and final cross-root result.

## Fixed graph and closed authored blueprint

The input has exactly the following slots; their local keys are internal constants, never
caller-selected strings. A caller supplies only the closed descriptive fields required by the
existing component contracts and each entity's nonempty display name.

| Canonical rank | Local key | Required World state |
| ---: | --- | --- |
| 1 | `world` | Active `game.core.world.root` |
| 2 | `region` | Active `game.core.world.location`, kind `region`, contained by `world` in `region` |
| 3 | `location.gate` | Active `game.core.world.location`, contained by `region` in `location` |
| 4 | `location.market` | Active `game.core.world.location`, contained by `region` in `location` |
| 5 | `location.observatory` | Active `game.core.world.location`, contained by `region` in `location` |
| 6 | `faction` | Active `game.core.world.faction` |
| 7 | `actor.one` | Active `game.core.world.motive` |
| 8 | `actor.two` | Active `game.core.world.motive` |
| 9 | `knowledge.fact` | Active `game.core.world.fact` plus classification |
| 10 | `knowledge.rumour` | Unconfirmed `game.core.world.rumour` plus classification |
| 11 | `knowledge.secret` | Active GM-only `game.core.world.secret` plus classification |
| 12 | `knowledge.clue.one` | Unrevealed GM-only `game.core.world.clue` plus classification |
| 13 | `knowledge.clue.two` | Unrevealed GM-only `game.core.world.clue` plus classification |
| 14 | `knowledge.clue.three` | Unrevealed GM-only `game.core.world.clue` plus classification |

The request is a closed typed record with one nested authored record for each row above. It has no
dictionary, extension data, raw JSON, permanent entity ID, local-key, effect, relationship, SQL,
script, generated-content, transaction, audit, event, or fingerprint field. The author supplies
the W1/W3/W4 closed data exactly as those current contracts require. Knowledge records additionally
supply one closed `subjectKind` and `sensitivity` classification. The composer injects all status,
visibility, kind, link endpoint, and relationship-data constants listed below; an author cannot
override them.

The only permitted graph links are fixed as follows. All relationships have data `{}`.

1. Containment: `region -> world` in slot `region`; each location, in gate/market/observatory
   order, `-> region` in slot `location`.
2. Adjacency: gate -> market, then market -> observatory, using W1's canonical
   `game.core.world.location.connected-to` kind.
3. Faction: faction -> actor.one, faction -> actor.two using `faction.member`; faction -> market
   using `faction.controls`.
4. Knowledge: each knowledge record links `in-world -> world`, then `about` as fact -> market,
   rumour -> observatory, secret -> actor.two, clue.one -> market, clue.two -> observatory, and
   clue.three -> actor.two. Clue support links are clue.one -> fact, clue.two -> secret, and
   clue.three -> secret.

R3 freezes injected component state for the C10 reference policy: root, region, gate, and market
are `public`; observatory, faction, and both motives are `party`; fact is `active/public`; rumour
is `unconfirmed/party`; secret is `active/gm`; and every clue is `unrevealed/gm`. The author still
supplies all non-derived W1/W3/W4 descriptive fields and knowledge classifications.

Consequently a valid result has exactly 14 entity creates, 20 component additions (14 primary plus
six classification companions), four containment moves, and 20 relationship creates. It contains
no W5 clock, routes, baselines, actor knowledge states, interactions, acquisitions, validity,
campaign, quest, or character records.

## Deterministic identities, ordering, and effects

R3 supplies one validated immutable namespace `N = "world.c10." + campaignIdSuffix` to the child,
where `campaignIdSuffix` is the exact valid substring after C1's `campaign.` prefix. The child
never derives it from free prose and the caller never submits entity IDs. For each fixed local key
`K`, the permanent entity ID is exactly `N + "." + K`.

Local keys are unique by construction and are returned in the canonical rank above. The child
rejects a null, empty, malformed, non-canonical, or colliding namespace; an existing entity at any
derived ID; a duplicate derived ID; or a staged dry-run collision. It does not reserve an ID.

For an otherwise valid request, the effect order is exact:

1. Fourteen `entity.create` effects in canonical local-key rank.
2. Twenty `component.add` effects in that same rank: root/location/faction/motive/knowledge primary
   component first, then the classification companion immediately after each knowledge primary.
3. Four `containment.move` effects in the containment order above.
4. Two adjacency `relationship.create` effects.
5. Three faction `relationship.create` effects.
6. Fifteen knowledge `relationship.create` effects, record-by-record in canonical knowledge order:
   `in-world`, `about`, then (for clues) `supports`.

Component JSON is emitted from typed fields, never passed through by the caller. Its member order is
the order in the governing component contract: root/location `status,summary,visibility`; faction
`status,summary,visibility,goals,methods,assets,agenda`; motive `status,summary,visibility`;
knowledge primary `status,summary,provenance,visibility`; classification
`subjectKind,sensitivity`. Arrays preserve caller order only after validation of W3's distinct,
trimmed collection rules. Empty relationship data serializes as the exact object `{}`.

## Validation and child result contract

Validation is all-or-nothing and accumulates stable ordered problems in canonical local-key then
field order. It reads current World state and calls only `IStagedWorldComposer.StartAsync` followed
by `AppendAsync` to dry-run the complete proposed bundle. It applies nothing. A child result is:

```text
WorldSmallWorldCompositionResult
  status: valid | invalid
  worldRootId: derived ID | null
  localKeyMap: ordered { localKey, entityId, name }[14] | []
  counts: { entities: 14, components: 20, containment: 4, relationships: 20 } | null
  visibilityReview: ordered { localKey, visibility, audience: party|gm }[] | []
  effects: ordered World-only Effect[] | []
  problems: ordered { code, path, reason }[]
```

`visibilityReview` contains the root/location/faction/motive/knowledge visibility after the
injected W-contract constants are applied. It is descriptive evidence only; it does not authorize
any reader. The C10 preview may expose the mapping/counts/review/problems but does not accept a
caller-provided effect list.

| Invalid condition | Stable code | Result rule |
| --- | --- | --- |
| Missing/null request, namespace, nested record, collection, or required text | `WORLD_BLUEPRINT_REQUIRED` | No effects or mapping. |
| Empty/whitespace/untrimmed/overlong text; invalid W1/W3/W4 enum, collection, or classification | `WORLD_BLUEPRINT_INVALID` | Identify the slot/path; no effects. |
| Unknown/extra, raw, derived, permanent-ID, local-key, or graph-control input | `WORLD_BLUEPRINT_CLOSED` | Reject before effect construction. |
| Namespace malformed; derived IDs duplicate or are already taken | `WORLD_ID_CONFLICT` | No effects, no reservation. |
| Fixed scope/endpoint/visibility/status/adjacency/faction/knowledge convention cannot be met | `WORLD_GRAPH_INVALID` | No effects. |
| Definitions missing/inactive or staged dry-run/guard rejects the bundle | `WORLD_EFFECTS_INVALID` | Return dry-run problems; write nothing. |
| Cancellation/exception | propagate cancellation or fail closed as `WORLD_COMPOSITION_FAILED` | No partial result or write. |

Malformed persisted input discovered by the staged dry-run is never normalised or repaired. It is
reported as invalid. Repeating the same request/namespace against identical state returns bytewise
equivalent ordered effects, mapping, counts, and problems; after another writer claims an ID, the
same call deterministically returns `WORLD_ID_CONFLICT`.

## Dependency graph and slices

```text
W17: effect-free closed small-world composer
├─ W1 topology vocabulary and conventions                         [implemented]
├─ W3 faction/motive vocabulary and conventions                   [implemented]
├─ W4 knowledge/classification conventions                        [implemented]
├─ generic staged effect overlay                                  [implemented]
├─ R3 namespace/coordinator/fingerprint/failure ratification      [ratified]
└─ Slice 1 World child composer                                   [next implementation leaf]
   ├─ typed closed request/result and deterministic effect builder [one coherent leaf]
   ├─ staged dry-run/collision validation                          [same leaf]
   └─ focused zero-write/determinism coverage                      [same leaf]
```

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Effect-free small-world child composer | R3 ratifies namespace source, child result visibility, effect representation, and outer ownership. | **Verified:** the fixed graph returns the stated ordered World effects and review evidence with zero writes. See the [Slice 1 receipt](WORLD-FEATURE-17-SLICE-1-RECEIPT.md). |

## Slice 1 handoff boundary

The companion handoff is **Active** under R3. Its allowed production files are limited to one World
contract file, one DataAccess implementation file, one focused test file, DI registration only if
the ratified outer contract needs it, and the W17 receipt. It must not revise catalog artifacts or
any campaign/C10 public surface.

The implementation uses the existing staged overlay as follows: construct the derived 14-ID
boundary; call `StartAsync` for the world-root create; append the remaining ordered effects; return
the staged plan's effects and virtual World read model only on success. Any invalid request returns
an empty effect list and must leave entities, components, containment, relationships, events,
notifications, operations, and catalog bytes unchanged.

Focused coverage must prove valid counts/order/mapping; every closed-input case; all collision
cases; malformed stored/definition state; graph scope/visibility violations; repeated equivalence;
and before/after byte comparisons of every durable World and evidence table. Run the focused tests,
the full suite at feature acceptance, and `git diff --check`; run catalog validation only if an
unexpected approved catalog edit is introduced (which this slice does not require). Stop after the
W17 receipt—do not implement C10 preview or campaign composition.

## Plan-quality audit

| Check | Result |
| --- | --- |
| One capability and explicit non-goals | Yes — target and boundary sections. |
| Existing ownership searched | Yes — inventory result and no W17/composer owner found. |
| Every dependency classified | Yes — W1/W3/W4/staged overlay implemented; R3 ratified the only semantic parent. |
| Lowest implementation slice named | Yes — Slice 1, effect-free World child only. |
| Inputs, IDs, ordering, effects, and failures closed | Yes — fixed slots/keys, namespace formula, exact counts/order, problem table. |
| Proportionate positive/negative/determinism/zero-write evidence | Yes — Slice 1 handoff boundary. |
| Planning stops before runtime | Yes — this document and its handoff create no runtime artifact. |

## Plan-change rule

Stop and return to R3 if it selects a different namespace formula, exposes a different child
effect/result representation, needs a different fixed graph, permits alternate authored graph
edges, or assigns transaction/audit ownership to a child. Any new World component, relationship
kind, procedure, schema, public operation, fixture, or cross-world sharing model is a separate
World dependency rather than an amendment silently folded into W17.
