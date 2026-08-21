# World Feature 3 dependency plan — factions and recurring motives

Status: **Feature 3 verified**  
Last updated: 2026-08-20

## Target capability

The catalog contains one world-owned faction, two recurring world actors with durable motives,
and explicit faction links. A governed action can advance the faction's one current agenda from
its initial state exactly once. The accepted change is atomic, replayable, and visible through the
existing action audit and structural component-replacement event.

### Included

- Closed faction and recurring-motive component contracts in the `game.core.world` namespace.
- Explicit, empty-data relationship conventions for faction membership, control, alliance, and
  opposition.
- A small world fixture: one faction, two actors carrying motives, one membership link, and one
  faction-to-location control link.
- One deterministic, manual agenda-advance mechanic and its governing procedure.
- Fresh-import, direct-authoring, action, replay, conflict-convention, and event/audit coverage.

### Excluded

- Faction reputation, recruitment, diplomacy resolution, territorial exclusivity, assets as
  entity-ID arrays, autonomous simulation, clocks, schedules, random advancement, or background
  jobs.
- Character creation, a generic NPC classifier, character sheets, campaign state, quest/objective
  state, knowledge/clues, player-safe visibility enforcement, notifications, maps, or UI.
- New C# game vocabulary, migration, MCP kind/tool, event type, subscription, or custom action
  runner.

This feature records what a faction and a recurring actor currently are. It does not decide why an
agenda should advance. World Feature 6 may react to a verified event, and W11 owns richer faction
front and territory state.

## Source and contract basis

Factions and motives are authored product state, not an SRD calculation. The current repository
contracts and plans are the authority:

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Feature workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Repository mode, catalog validation, focused/full tests, and semantic confirmation boundaries. |
| World model and change | `procedure.world.model`; `procedure.world.change`; `procedure.world.naming` | Components/relationships rather than kernel tables; permanent IDs; atomic direct effects. |
| Action and mechanics | `procedure.action.run`; `procedure.mechanic.write`; `procedure.mechanic.projection` | Declared frozen inputs, versioned JavaScript, one atomic returned effect, replay evidence. |
| Event runtime | `world.component.replaced` event type; `procedure.event.react` | `component.set` already creates structural evidence; a semantic agenda event is not yet warranted. |
| Verified topology | [Feature 1 plan](../feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md), [receipt](../feature-01/WORLD-FEATURE-01-RECEIPT.md), and `procedure.game.core.world.location` | Existing world/location IDs and containment; faction links must not copy topology. |
| World ownership | [World and lore plan](../../WORLD_AND_LORE_PLAN.md), Slice 3 | Faction agenda and recurring motives belong to world/lore, not campaign or quest. |
| Consumer boundaries | [Campaign plan](../../CAMPAIGN_CREATION_PLAN.md), [Quest plan](../../QUEST_IMPLEMENTATION_PLAN.md), and [Character plan](../../CHARACTER_CREATION_PLAN.md) | Consumers may reference world factions/actors; they do not own agenda, motive, or faction lifecycle. |

The current projection runtime already supports opted-in relationship records. This feature's
agenda rule does not need relationship data: it reads and replaces only its faction component.
Any later player-facing relationship writer that must detect cross-entity conflicts must declare
that projection and receives no endpoint components unless they are separately declared.

## Ownership and confirmed vocabulary

The following permanent IDs and meanings were confirmed by the user on 2026-08-20 and are now
catalog artifacts. Further changes to this vocabulary are a new semantic boundary.

| Artifact | Owner and meaning |
| --- | --- |
| `game.core.world.faction` | A faction entity's closed world state: lifecycle, concise public description, descriptive visibility, goals, methods, known asset descriptions, and one current agenda. It contains no member/location/entity-ID lists. |
| `game.core.world.motive` | A recurring world actor's closed durable motive state. It can attach to a named actor entity without inventing a kernel-level NPC type or a campaign-only motive record. |
| `game.core.world.faction.member` | Directed empty-data link from faction to member actor. It describes affiliation, not exclusive loyalty. |
| `game.core.world.faction.controls` | Directed empty-data link from faction to a controlled or claimed world entity. It is an asserted control relationship, not exclusive legal ownership. |
| `game.core.world.faction.allied-with` | Empty-data, non-self, undirected-by-convention link stored once in lexical entity-ID orientation. |
| `game.core.world.faction.opposed-to` | Empty-data, non-self, undirected-by-convention link stored once in lexical entity-ID orientation. |
| `procedure.game.core.world.faction` | Governs catalog/direct-effect recording and correction of the two components and the faction relationship conventions. |
| `mechanic.game.core.world.faction.agenda` | The one active manual agenda transition. Its category is `game.core.world.faction`; it proposes a complete `component.set`, never writes itself. |

### Confirmed closed component data

These fields were confirmed as a single semantic boundary before Slice 1 implementation:

```text
game.core.world.faction
{
  status: "draft" | "active" | "archived",
  summary: nonempty trimmed text (at most 1,000 Unicode scalar values),
  visibility: "public" | "party" | "gm",
  goals: 1–5 distinct trimmed texts, each 1–500 Unicode scalar values,
  methods: 1–5 distinct trimmed texts, each 1–500 Unicode scalar values,
  assets: 0–10 distinct trimmed descriptive texts, each 1–500 Unicode scalar values,
  agenda: { state: "ready" | "advanced", summary: trimmed text of 1–1,000 Unicode scalar values }
}

game.core.world.motive
{
  status: "draft" | "active" | "archived",
  summary: nonempty trimmed text (at most 1,000 Unicode scalar values),
  visibility: "public" | "party" | "gm"
}
```

`assets` holds descriptions, never entity IDs; relationships carry references. `ready` means the
stored agenda has not yet crossed this feature's single manual transition; `advanced` means it
has. There is no `resolved`, rollback, progress counter, frontier, or next agenda in this slice.
Those would change the state-machine meaning and belong to a later confirmed feature.

## Relationship conventions and conflict policy

All Feature 3 faction links have data exactly `{}`. Generic relationship storage remains directed
and only prevents an identical directed duplicate. Therefore the governing procedure and focused
fixture validator must state, test, and report these feature conventions; this plan does not claim
that bare generic direct effects universally enforce them.

| Kind | Direction/endpoints | Duplicate or conflict policy |
| --- | --- | --- |
| `faction.member` | faction → named actor | Reverse orientation, self-link, non-faction source, and nonempty data are invalid conventions. Multiple faction affiliations are permitted and are not silently interpreted as exclusive loyalty. |
| `faction.controls` | faction → named world entity | Reverse orientation, self-link, non-faction source, and nonempty data are invalid conventions. Multiple control claims are permitted and remain explicitly competing claims until W11 defines territorial exclusivity. |
| `faction.allied-with` | faction ↔ faction, lexical stored orientation | Reverse/duplicate/self/non-faction/nonempty links are invalid; `allied-with` and `opposed-to` may not coexist for one unordered pair. |
| `faction.opposed-to` | faction ↔ faction, lexical stored orientation | The same canonical and mutual-exclusion rules as `allied-with`. |

The initial fixture uses only `member` and `controls`; an alliance/rival fixture waits until there
are two factions. Disposable conflict cases still prove the convention and make the absence of
generic enforcement explicit.

## Recursive dependency analysis

```text
World Feature 3: faction state, motives, and one agenda transition
├─ Feature 1 world/location fixture and containment                   [verified]
├─ generic components, relationships, effects                         [implemented]
├─ action runner, replay, component replacement event                 [implemented]
├─ confirmed faction/motive vocabulary                                 [implemented: Slice 1]
│  └─ contracts, schemas, fixture, relationship conventions           [implemented]
└─ manual agenda advance                                               [implemented: Slice 2]
   ├─ faction-only declared projection                                [implemented]
   ├─ deterministic JavaScript mechanic and procedure                 [implemented]
   └─ action/replay/event/readback coverage                            [implemented]

Campaign, quest, knowledge, reactive advancement, fronts, territory, and player authorisation [excluded]
```

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Faction/motive state and links | User confirmation of the permanent IDs, closed shapes, relationship directions, and nonexclusive control/affiliation policy. | **Verified:** a fresh catalog import contains the components, procedure, fixture, and canonical links; focused convention and isolation coverage passes. See the [Slice 1 receipt](WORLD-FEATURE-03-SLICE-1-RECEIPT.md). |
| 2 | One manual agenda advance | Slice 1 is verified. | **Verified:** a single active faction advances `ready → advanced` through the action runner; rejection/replay preserves bytes, and success has action/audit plus one existing structural event. See the [Slice 2 receipt](WORLD-FEATURE-03-SLICE-2-RECEIPT.md). |

## Slice 1 — faction and recurring-motive foundation

### Runtime artifacts

| Artifact | Change |
| --- | --- |
| Component definitions and schemas | Add confirmed `game.core.world.faction` and `game.core.world.motive` definition/schema pairs. Both use closed JSON objects and point to the faction procedure. |
| Governing procedure | Add `procedure.game.core.world.faction`, covering full-record authoring, component ownership, relationship directions, `{}` data, and conflict policy. |
| Catalog fixture | Add one faction and two named recurring-actor entities, attach one faction component and two motive components, then add membership and location-control links to the verified Feature 1 topology. Exact fixture IDs/names are confirmed with the vocabulary. |
| Focused coverage | Add `CatalogWorldFeature3Tests` or the nearest current catalog-world test owner for fresh-import/readback and disposable invalid-fixture cases. |

The procedure's normal setup path is a read-first, one-transaction direct-effects list: new
entities, their component additions, then relationships. It is administrative world authoring,
not a player outcome. Component corrections are complete replacements; no partial motive or
agenda merge invents a state that was not reviewed.

### Slice 1 acceptance matrix

| Test class | Input/setup | Exact expected result |
| --- | --- | --- |
| Fresh fixture | Disposable catalog import | One faction with confirmed data; two actor entities each have exactly one motive; one canonical membership and one canonical location-control edge read back exactly. |
| Shape and scope | Invalid enum/text/array/nested agenda/unknown-field examples | Component schema/fixture validation rejects invalid data; no entity-ID lists, parent/location field, campaign state, quest state, or actor-classifier field appears. |
| Ownership | Readback plus cross-plan assertions | Faction and motives are world entities/components; campaign and quest fixture records are absent and no world state is copied onto a campaign record. |
| Link direction/data | Reversed, self, non-faction-source, nonempty-data, duplicate examples | Focused convention validation rejects them; valid links use exact kind, orientation, and `{}`. |
| Explicit conflicts | Multiple affiliation/control claims; alliance and opposition for one pair | The first two are accepted as nonexclusive claims; the same unordered allied/opposed pair is rejected as contradictory. |
| Isolation | Existing Feature 1 fixture after import | Root/location component bytes, containment, and adjacency records are unchanged. |
| Repository | Focused tests, `roleplay validate catalog`, full suite at slice acceptance, `git diff --check` | All checks pass without persistent import. |

### Slice 1 exit gate

**Verified.** The confirmed permanent vocabulary, closed schema behavior, fixture graph,
relationship policy, and regression evidence agree. See the
[Slice 1 receipt](WORLD-FEATURE-03-SLICE-1-RECEIPT.md). Stop before adding the agenda mechanic.

## Slice 2 — deterministic manual agenda advance

### Runtime artifacts and exact behavior

`mechanic.game.core.world.faction.agenda` and its governed procedure play path declare exactly one
required role, `faction`, carrying
`game.core.world.faction`; it does not request relationships, contents, or any actor component.
Input must be exactly `{}`—extra keys, non-object roots, or malformed JSON reject.

The mechanic parses the closed component. It accepts only an active faction whose agenda is
exactly `ready`, returns one complete `component.set` effect preserving every confirmed field
byte-for-byte except `agenda.state`, which becomes `advanced`, and returns a deterministic result
containing the faction ID and prior/new agenda states. It makes no random call and does not alter
membership, control links, motives, locations, visibility, goals, methods, assets, or agenda
summary.

The action audit and the existing `world.component.replaced` event are sufficient success
evidence. This slice adds no semantic `faction.agenda.advanced` event or subscription; World
Feature 6 can introduce one only if a consumer needs a semantic distinction that the structural
event cannot provide.

### Slice 2 acceptance matrix

| Test class | Input/setup | Exact expected result/state assertion |
| --- | --- | --- |
| Happy path | Fresh verified fixture; active ready faction | Intent routes to the agenda mechanic; exactly one `component.set` changes only `agenda.state` to `advanced`. |
| Closed call | Missing/extra role, wrong entity, `{}` versus extra/invalid input | Rejected before a state change; relevant component/relationships remain byte-identical. |
| Invalid state | Draft/archived faction; advanced, missing, malformed, or unknown agenda state | Deterministic rejection with zero effects. |
| Determinism/replay | Two fresh imports with same role/input/seed; repeat after success | Fresh outputs/effects match; second advance rejects and preserves the advanced state. |
| State isolation | Successful action | Motives, relationship rows, root/location records, goals, methods, assets, visibility, and agenda summary do not change. |
| Audit/event | Successful and rejected action | Success has one action/audit outcome and one existing `world.component.replaced`; rejection has no faction component replacement. |
| Repository | Focused action test, catalog validation, full suite, diff check | All pass; no protocol walk unless an MCP surface/dependency registration changes. |

### Slice 2 exit gate

**Verified.** Fresh-import action coverage proves exactly one accepted agenda transition,
deterministic replay, rejection immutability, action audit, and the existing structural event.
Catalog validation and the full suite pass. See the
[Slice 2 receipt](WORLD-FEATURE-03-SLICE-2-RECEIPT.md). The feature stops before reactive or
territorial work.

## Plan-change rule

The user has confirmed the permanent IDs, faction/motive shapes, `ready → advanced` as the only
initial transition, relationship direction, and deliberate nonexclusive affiliation/control
policy. Revise this plan before a future extension if later review needs exclusive control/loyalty, a
different agenda state machine or external trigger, or relationship conflict validation beyond
the frozen records it explicitly declares. Do not broaden this feature into campaign, quest,
character, knowledge, or reactive-world work.
