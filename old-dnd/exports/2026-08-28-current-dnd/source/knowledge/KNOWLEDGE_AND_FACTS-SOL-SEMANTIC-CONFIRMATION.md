# Knowledge and facts — Sol semantic confirmation packet

Status: **Approved by the user and implemented as Slice 1 on 2026-08-21**  
Date: 2026-08-21

## Recommended first-generation decisions

1. Keep `game.core.world.secret` as a first-class compatibility component. Do not migrate existing
   facts, rumours, secrets, or clues. A new classification companion describes sensitivity without
   replacing the existing components or changing their current bytes.
2. Support world, region, faction, and actor scopes first. A region is an entity with
   `game.core.world.location.kind = region`; faction membership uses the existing
   `game.core.world.faction.member` relationship. Add party/culture/profession/religion/institution/
   language scopes only when their own authoritative entity and membership owners exist.
3. Initial world, region, and faction baselines use **current-scope** semantics. They create no actor
   rows. Knowledge learned from an interaction becomes an explicit durable actor state and therefore
   survives leaving a region or faction. Taught-on-entry automation is deferred.
4. Retain the seven explicit actor states: `known`, `familiar`, `suspected`, `believed`, `doubted`,
   `disbelieved`, and `unknown`. Only one current state may exist for an actor/knowledge pair.
   `unknown` is the explicit exception that overrides common, regional, or faction knowledge.
5. Slice 1 records no invented world clock. The ordinary event ledger supplies operation identity
   and real timestamp for state changes. Slice 2 may add an acquisition record with a source event;
   authored world-time validity waits for an authoritative world-clock owner.

## Proposed permanent vocabulary

Approve these names and meanings together:

| ID | Shape | Closed meaning |
| --- | --- | --- |
| `game.core.world.knowledge.classification` | companion component on an existing fact/rumour/secret/clue entity | `{subjectKind, sensitivity}` where subject kind is `state`, `event`, `identity`, `relationship`, `location`, `capability`, `rule`, `quantity`, `intention`, or `negative`; sensitivity is `open`, `discreet`, `confidential`, or `secret`. |
| `game.core.world.knowledge.baseline` | relationship from world/region/faction to knowledge | Exact data `{\"inheritance\":\"current-scope\"}`. An applicable baseline resolves to `known`; it is descriptive in-world dissemination, never authorization. |
| `game.core.world.knowledge.state` | relationship from actor to knowledge | Exact data `{\"state\":...}` using the seven-state set above. It is the single current explicit actor override. |

Reserved for later slices, not approved for implementation by this packet:

- `game.core.world.knowledge.acquisition` and its source links;
- `game.core.world.knowledge.contradicts`;
- `game.core.world.knowledge.supersedes`;
- `game.core.world.knowledge.depends-on`.

## Effective-state rule

For one actor and one knowledge entity:

1. If one current explicit `game.core.world.knowledge.state` exists, return it, including
   `unknown`. Reject duplicate current states rather than choosing by row order.
2. Otherwise, if any applicable current faction or containing-region baseline exists, return
   `known`.
3. Otherwise, if a world baseline exists, return `known`.
4. Otherwise return derived `unknown` without storing a row.

An acquisition later updates the single explicit state; it is audit/provenance, not a second
competing precedence layer. Descriptive component `visibility` and `sensitivity` never participate in
authorization. Slice 1 exposes only a trusted-GM reader.

## Existing Feature 4 compatibility map

| Existing record | First-generation representation |
| --- | --- |
| `fact.feature-04.toll-ledger` | Existing fact unchanged; classification `state/open`; world baseline means common knowledge. |
| `rumour.feature-04.observatory-signal` | Existing rumour and confirmation states unchanged; classification `event/open`; a knower may explicitly believe, doubt, or disbelieve it independently of confirmation. |
| `secret.feature-04.oren-correspondence` | Existing secret unchanged; classification `relationship/secret`; no baseline, so it is unknown unless an explicit actor state is recorded. |
| `clue.feature-04.ledger-seal` | Existing clue/reveal behavior and support link unchanged; classification `identity/confidential`; revealed party visibility does not automatically teach every actor. |
| `clue.feature-04.oren-letter` | Existing clue unchanged; classification `relationship/secret`; a later reveal/interaction may create actor knowledge without revealing the supported secret. |
| `clue.feature-04.observatory-lantern` | Existing clue unchanged; classification `state/confidential`; evidence remains distinct from the truth it supports. |

The global-exception fixture adds an actor `unknown` state over a world baseline. Regional and faction
fixtures add one baseline each and prove outsiders remain derived `unknown`. The interaction fixture
is reserved for Slice 2 because no authoritative interaction owner exists yet. Historical and
superseded fixtures are reserved for Slice 3 and must not overload Slice 1 state.

## Terra Slice 1 boundary after approval

Slice 1 added the approved classification definition, baseline/state governed write paths, a
trusted-GM effective-state reader, fixtures, and focused tests. Existing Feature 4 component payloads
and reveal/confirm behavior remain unchanged. It stopped before acquisitions, time/supersession, FTS,
vector orchestration, player authorization, MCP exposure, or migrations.

Acceptance is the Slice 1 exit in `KNOWLEDGE_AND_FACTS_PLAN.md`, plus catalog validation. Any need to
rename an approved ID, widen a schema, reinterpret existing visibility, add a migration, or expose a
public surface returns to a confirmation boundary.
