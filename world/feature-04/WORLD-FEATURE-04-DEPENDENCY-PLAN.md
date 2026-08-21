# World Feature 4 dependency plan — knowledge, rumours, secrets, and clues

Status: **Feature 4 verified**  
Last updated: 2026-08-20

## Target capability

A fresh trusted GM can inspect one world-scoped public fact, one rumour, one GM-only secret, and
three planted clues. Each record has closed provenance and descriptive visibility, points to a
world entity, and is linked to its world root. A governed action reveals one clue or confirms one
rumour without changing the secret it may support.

This is durable world knowledge, not a campaign summary, quest state, narrative transcript, or an
authorization system.

### Included

- Four closed `game.core.world` knowledge components and two relationship conventions.
- A world-root scope relationship for every knowledge entity, with no copied `worldId` field.
- One fact, one rumour, one secret, and three clues in the existing world fixture.
- Deterministic clue-reveal and rumour-confirm actions, using existing structural events/audit.
- Fresh-import, fixture-convention, relationship-projection, replay, no-change, and readback
  coverage.

### Excluded

- Player-safe read filtering, authentication, authorization, per-player discovery, or a new query
  kind. Visibility remains descriptive metadata for trusted GM use.
- Campaign/quest/objective/chapter state, character beliefs, dialogue, lore generation, story
  prose, semantic events, subscriptions, notifications, clocks, maps, or automatic revelation.
- Turning a rumour into a copied fact, modifying a secret during reveal, a generic source graph, or
  a kernel/C# knowledge type.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Feature workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Repository authoring, confirmation boundaries, catalog validation, and acceptance evidence. |
| Generic world model | `procedure.world.model`; `procedure.world.change`; `procedure.world.naming` | Thin entities, components and directed object-data relationships, permanent IDs, atomic effects. |
| Mechanics and action | `procedure.action.run`; `procedure.mechanic.write`; `procedure.mechanic.projection` | Declared state, frozen relationship records, atomic proposed effects, action/replay audit. |
| Existing event ledger | `world.component.replaced`; `procedure.event.react` | `component.set` already creates structural evidence. No semantic knowledge event is justified yet. |
| World ownership | [World/lore Slice 4](../../WORLD_AND_LORE_PLAN.md); [Story-first ownership map](../../STORY_FIRST_ROADMAP.md) | Knowledge belongs to world/lore; campaign, quest, and storytelling only consume it. |
| Consumer limits | [Campaign plan](../../CAMPAIGN_CREATION_PLAN.md); [Quest plan](../../QUEST_IMPLEMENTATION_PLAN.md); [storytelling procedure](../../storytelling.md) | Campaign does not copy knowledge; quests may later link to clues; existing shorthand `clue`/`motive` is not an ID contract. |
| Relationship projection | `ProjectionResolver` and `ProjectionResolverTests` | An opted-in role gets frozen incoming/outgoing relationship records only; no endpoint state or graph traversal is implicit. |

No catalog component, mechanic, procedure, or fixture owned the proposed fact, rumour,
secret, clue, or knowledge relationship identifiers.

## Ownership and confirmed vocabulary

These permanent IDs and semantics were confirmed by the user on 2026-08-20 and are now Slice 1
catalog artifacts. Further changes are a new semantic boundary.

| Artifact | Meaning |
| --- | --- |
| `game.core.world.fact` | An asserted world fact. Its component type, not an unreviewed `certainty` string, states that it is asserted. |
| `game.core.world.rumour` | An attributed claim whose resolution stays explicit: unconfirmed, confirmed, or disproved. Confirmation never silently creates or overwrites a fact. |
| `game.core.world.secret` | An authoritative hidden truth. Its visibility is always `gm`; a clue may support it without changing it. |
| `game.core.world.clue` | Discoverable evidence. Its reveal state says whether the clue itself may be presented; it never contains a target ID or changes the target. |
| `game.core.world.knowledge.in-world` | Directed empty-data relationship from any knowledge record to exactly one world-root entity. This is the knowledge scope convention; it replaces no containment or component field. |
| `game.core.world.knowledge.about` | Directed empty-data relationship from a fact, rumour, secret, or clue to exactly one entity it concerns. |
| `game.core.world.clue.supports` | Directed empty-data relationship from a clue to exactly one fact or secret it evidences. |
| `procedure.game.core.world.knowledge` | Governs recording/correction of the four component types, root scope, target/support links, and trusted-GM visibility semantics. |
| `mechanic.game.core.world.clue.reveal` | Advances one scoped clue from unrevealed to revealed. |
| `mechanic.game.core.world.rumour.confirm` | Advances one scoped rumour from unconfirmed to confirmed. |

### Confirmed closed component data

```text
All four:
  summary: trimmed text, 1–1,000 Unicode scalar values
  provenance: trimmed text, 1–500 Unicode scalar values

fact:
  status: "active" | "archived"
  visibility: "public" | "party" | "gm"

rumour:
  status: "unconfirmed" | "confirmed" | "disproved" | "archived"
  visibility: "public" | "party" | "gm"

secret:
  status: "active" | "archived"
  visibility: exactly "gm"

clue:
  status: "unrevealed" | "revealed"
  visibility: "gm" when unrevealed; exactly "party" when revealed
```

`provenance` is a concise authored source/evidence description, not an entity ID, a confidence
score, a citation resolver, or a transcript. Entity references use the relationships above. Missing,
`null`, empty strings, untrimmed strings, unknown fields, arrays, and non-object component data are
invalid fixture/contract inputs.

## Relationship and visibility policy

Every knowledge entity has exactly one `knowledge.in-world` edge, exactly one `knowledge.about`
edge, and `{}` relationship data. Every clue has exactly one `clue.supports` edge whose target is a
fact or secret in the same scoped world. `supports` does not assert that a clue proves a secret;
it records the authored evidentiary connection for later GM reasoning.

These are feature conventions. Generic relationships are directed and only collapse identical
directed triples, so the procedure and focused catalog fixture validator must reject wrong
orientation, self links, wrong component endpoint, missing/duplicate scope or target, cross-world
links, nonempty data, and reverse duplicates where the convention is one-way.

Visibility is descriptive until an authorized audience projection exists. The only guaranteed
Feature 4 reader is a trusted GM, who can inspect every record and visibility label. The feature
must not claim that a direct query hides secrets from a party member. Future campaign/web projection
work may filter a frozen read model using these labels only after it has real caller authorization.

## Recursive dependency analysis

```text
World Feature 4: durable knowledge and one reveal/confirm path
├─ Feature 1 root/location fixture                                  [verified]
├─ generic components, relationships, effects                       [implemented]
├─ action/replay and opted-in relationship projection               [implemented]
├─ structural component-replacement event/audit                     [implemented]
├─ Feature 3 faction/motive fixture and vocabulary                  [verified]
├─ confirmed knowledge IDs, states, links, and fixture identities   [implemented: Slice 1]
│  └─ contracts, schemas, scoped records, relationships             [implemented]
└─ clue reveal and rumour confirmation                              [implemented: Slice 2]
   ├─ `clue.reveal` state transition                                [implemented]
   ├─ `rumour.confirm` state transition                             [implemented]
   └─ action/replay/event/readback coverage                         [implemented]

Audience authorization, campaign/quest consumers, prose, semantic events, automation [excluded]
```

Feature 4 does not begin implementation while Feature 3 is unverified: its first fixture targets
the confirmed faction/location/recurring-actor world graph and must not invent substitute actors.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Scoped knowledge foundation | World Feature 3 is verified and the permanent vocabulary, component shapes, relationship directions, and visibility policy above are confirmed. | **Verified:** fresh import contains exactly one fact, rumour, secret, and three clues with canonical scope/target/support links; contract and fixture checks pass. See the [Slice 1 receipt](WORLD-FEATURE-04-SLICE-1-RECEIPT.md). |
| 2 | Manual reveal and confirmation | Slice 1 is verified. | **Verified:** one clue reveals and one rumour confirms through the action runner; secret data is byte-identical, and rejection/replay changes nothing. See the [Slice 2 receipt](WORLD-FEATURE-04-SLICE-2-RECEIPT.md). |

## Slice 1 — scoped knowledge foundation

### Fixture and artifacts

Add the four definition/schema pairs, `procedure.game.core.world.knowledge`, six named knowledge
entities, and their links to the existing World Feature 1 root plus confirmed Feature 3 actors,
faction, and locations. The exact fixture IDs, display names, summaries, provenance text, targets,
and clue-support targets are confirmed together with the permanent vocabulary.

The fixture has one active public fact, one unconfirmed public or party rumour, one active GM
secret, and three unrevealed GM clues. Each clue supports the secret or the fact; the three clues
may use different locations/actors as their `about` targets. There is no containment for knowledge
records, no copied root ID, no embedded target/support IDs, and no campaign/quest component.

The normal authoring path is a read-first, one-transaction direct-effects list: entities,
component additions, then relationships. That records authored setting state. It does not simulate
a discovery or resolve uncertainty.

### Slice 1 acceptance matrix

| Test class | Input/setup | Exact expected result |
| --- | --- | --- |
| Fresh fixture | Disposable catalog import | Root-scoped fact, rumour, secret, and three clues read back with exact data, one scope link, one target link, and one support link per clue. |
| Closed state | Invalid fields, enums, visibility/status combinations, empty/untrimmed text, non-object data | Schema/fixture validation names the failure; valid component JSON is untouched. |
| Link convention | Missing/duplicate/reversed/self/cross-world/wrong-endpoint/nonempty links | Focused fixture validator rejects the convention; valid records use exact directions and `{}`. |
| Truth separation | Secret plus supporting clue/fact/rumour fixture | The secret component stays a separate GM-only record; no component embeds or copies its summary. |
| Isolation | Existing Feature 1 and verified Feature 3 records | Their component bytes, containment, and relationships do not change except confirmed new knowledge links. |
| Trusted read | GM reads all six knowledge entities | Readback includes visibility/provenance; no test asserts access control or party secrecy. |
| Repository | Focused tests, `roleplay validate catalog`, full suite at slice acceptance, `git diff --check` | All pass without a persistent import. |

### Slice 1 exit gate

**Verified.** The approved vocabulary, exact fixture graph, world scope, truth separation, and
validation evidence agree. See the [Slice 1 receipt](WORLD-FEATURE-04-SLICE-1-RECEIPT.md). Stop
before action mechanics.

## Slice 2 — reveal one clue and confirm one rumour

Add the `.md`/`.js` pairs and extend `procedure.game.core.world.knowledge` with two narrow action
paths.

`mechanic.game.core.world.clue.reveal` declares required `clue` and `world` roles. The clue has
`game.core.world.clue` plus `includeRelationships: true`; the world has
`game.core.world.root`. Input is exactly `{}`. The frozen clue relationships must include exactly
one canonical `knowledge.in-world` edge to the supplied world. The rule accepts only
`unrevealed`/`gm`, proposes one complete `component.set` changing only the clue's status and
visibility to `revealed`/`party`, and reports the prior/new state. It neither reads nor changes its
support target.

`mechanic.game.core.world.rumour.confirm` uses the analogous `rumour` and `world` roles. It
accepts only `unconfirmed`, verifies the canonical scope edge from frozen relationships, proposes
one complete `component.set` changing only status to `confirmed`, and preserves the summary,
provenance, and visibility byte-for-byte. Disproval is intentionally later work.

Both actions make no random call and accept no derived caller value. Success creates the ordinary
action/audit evidence plus one `world.component.replaced`; no custom event or subscription is
added. Revealing a clue does not reveal, rewrite, copy, or change the visibility of its supported
secret.

### Slice 2 acceptance matrix

| Test class | Input/setup | Exact expected result/state assertion |
| --- | --- | --- |
| Clue happy path | Fresh unrevealed scoped clue, correct world role, `{}` | One component replacement changes exactly `unrevealed/gm → revealed/party`; secret/support/target rows remain byte-identical. |
| Rumour happy path | Fresh unconfirmed scoped rumour, correct world role, `{}` | One component replacement changes only rumour status to `confirmed`. |
| Closed calls | Missing/extra/wrong roles; non-object or nonempty input | Action rejects with zero effects and no changed component/relationship bytes. |
| Corrupt scope/state | Missing/reversed/duplicate/nonempty/cross-world scope edge; invalid component state | Deterministic rejection; caller-supplied world role does not override stored evidence. |
| Replay/determinism | Two fresh imports and repeat after success | Fresh outputs/effects match; repeat rejects and preserves the accepted state. |
| Truth and scope isolation | Accepted clue and rumour actions | No secret/fact/other clue/faction/location/motive/root record changes. |
| Audit/event | Success and rejection | Success records one action result and one existing structural replacement event; rejection emits no replacement. |
| Repository | Focused action tests, catalog validation, full suite, diff check | All pass; protocol walk only if the public MCP surface changes. |

### Slice 2 exit gate

**Verified.** Both actions run through the real action path on fresh imported state. Their state
transitions, relationship validation, replay behavior, structural event/audit evidence, and
no-change cases are proved; catalog validation and the full suite pass. See the
[Slice 2 receipt](WORLD-FEATURE-04-SLICE-2-RECEIPT.md). Stop before party-facing projection, quest
integration, semantic events, or automatic discovery.

## Required confirmation and plan-change rule

Confirm the proposed permanent IDs, root-scope convention, closed states, exact relationship
directions, fixture identity/content, and descriptive (not enforced) visibility semantics before
implementation. Revise rather than widen this plan if World Feature 3 changes its entity
vocabulary, source must become a first-class entity relation, one clue must support multiple
targets, a reader needs actual authorization, a confirmation needs external proof logic, or a
consumer needs an automatic event. Those each require their own owner and dependency plan.
