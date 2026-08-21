# World Feature 11 dependency plan — faction fronts and exclusive territory

Status: **Feature 11 verified**  
Last updated: 2026-08-20

## Target capability

A world-scoped faction has one named front pressing against one location. The front follows a
closed manual state machine, records the in-world minute at which its current pressure phase began,
and is linked explicitly to the responsible faction, contested location, and world root. It is
inspectable through normal entity/component/relationship reads and the existing action audit.

Feature 11 also establishes the first precise territorial rule: `game.core.world.faction.controls`
remains a nonexclusive general claim, while a new `territory-controls` relationship records one
exclusive current territorial controller for an active location. Advancing a front never transfers
territory. Resolution, transfer, multiple fronts, and autonomous advancement remain later work.

### Included

- One closed `game.core.world.faction.front` component on a front entity.
- A world-scope link for each faction, an exclusive territory-control relationship, and
  world/faction/location links for each front.
- One fixture territorial controller and one fixture front that contests a location.
- One deterministic manual front-advance mechanic, using current root-clock minute as derived
  state evidence.
- Fresh-import, scope, exclusivity, phase, stale-command, audit/event, no-change, and isolation
  coverage.

### Excluded

- Automatic faction simulation, clock subscriptions, schedules, random advancement, front
  resolution, transfer of territory, conquest, combat, diplomacy, reputation, recruitment,
  faction assets as entities, or player/quest/campaign decisions.
- Multiple simultaneous fixture fronts, contested-control arbitration, shared territory,
  alliances/opposition changes, map/route changes, conditions, notifications, player filtering,
  browser UI, or new MCP query kinds.
- A migration, game-specific C# helper/table, semantic front event, background worker, or
  replicated faction/territory state on a campaign, location, or actor.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Semantic confirmation, repository validation, focused/full acceptance, and persistent-import boundary. |
| Faction ownership | `procedure.game.core.world.faction`; [Feature 3 plan](../feature-03/WORLD-FEATURE-03-DEPENDENCY-PLAN.md) and receipts | Closed faction state, nonexclusive `controls` claim, faction relationship conventions, and the explicit W11 ownership of fronts/territory. |
| World time | `procedure.game.core.world.time`; [Feature 5 plan](../feature-05/WORLD-FEATURE-05-DEPENDENCY-PLAN.md) | Root-owned clock and the current in-world minute used to timestamp a manual phase transition. |
| Mechanics/action | `procedure.action.run`; `procedure.mechanic.write`; `procedure.mechanic.projection` | Explicit roles, relationship projections, derived action result, replay-safe input, one atomic component replacement, and audit. |
| Existing reactive proof | `procedure.event.react`; [Feature 6 plan](../feature-06/WORLD-FEATURE-06-DEPENDENCY-PLAN.md) | A verified reactive-world slice is a sequencing prerequisite, but W11's first transition remains manual and adds no subscription. |
| World structures | `procedure.world.model`; `procedure.world.change`; `procedure.game.core.world.location` | Components/relationships are authoritative; containment stays the only hierarchy and locations keep no faction-front fields. |
| Read consumers | [Feature 7 plan](../feature-07/WORLD-FEATURE-07-DEPENDENCY-PLAN.md) | A later revision can add front/territory to bounded trusted-GM recipes; Feature 11's baseline inspection uses existing entity reads. |

## Ownership and confirmation boundary

Revise `procedure.game.core.world.faction` rather than adding a parallel faction procedure. It
continues to own faction components and links, and now additionally governs faction root scope,
territorial control, fronts, and manual front advancement.

The Feature 3 `controls` relationship is retained as a broad nonexclusive claim. It is not
silently upgraded to legal/territorial ownership and remains valid for any world entity. Feature
11's `territory-controls` relationship is narrower: faction → active location, exactly `{}` data,
and only one faction may hold it for a given location in one scoped world.

The user confirmed the following permanent IDs, fixture location, and front direction on
2026-08-20:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.faction.front` | Closed current pressure state: lifecycle, summary, descriptive visibility, phase, and minute when the current phase began. |
| `game.core.world.faction.in-world` | Directed empty-data relationship from each faction to exactly one active world root. |
| `game.core.world.faction.territory-controls` | Directed empty-data relationship from a faction to an active location in its scoped world; one target location has at most one current faction controller. |
| `game.core.world.faction.front.in-world` | Directed empty-data relationship from a front to exactly one active world root. |
| `game.core.world.faction.front.for-faction` | Directed empty-data relationship from a front to exactly one active scoped faction. |
| `game.core.world.faction.front.contests` | Directed empty-data relationship from a front to exactly one active location it pressures. |
| `mechanic.game.core.world.faction.front.advance` | Active manual action that advances a matching active front one pressure phase and records the current root-clock minute. |
| `front.feature-11.observatory-claim` | The reviewed fixture front belonging to the Lantern Compact and contesting the observatory. |

The initial territory fixture adds one `territory-controls` link from the Lantern Compact to the
market. Its Feature 3 `controls` link remains a broader supporting claim. The front contests the
observatory but does not create a territory link, change the market controller, or imply that the
observatory has transferred ownership.

## Closed front and action contracts

~~~text
game.core.world.faction.front
{
  status: "active" | "resolved" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  phase: "quiet" | "rising" | "pressing",
  phaseStartedMinute: integer, 0–1,000,000,000 inclusive
}
~~~

The component is closed. Missing, `null`, arrays/scalars, unknown keys, invalid/untrimmed text,
unknown lifecycle/visibility/phase, fractional/negative/overflow minute, or a mismatched
phase-time record rejects. It contains no faction/location/world ID, target list, territory state,
agenda copy, cause, player choice, campaign/quest field, timer, event ID, audit ID, or predicted
outcome; relationships and the action audit carry those meanings.

A front has exactly one of each front relationship, all with `{}` data. Its world root must match
the linked faction's one `faction.in-world` root. The contested location must be an active location
in that reviewed world topology. A front cannot link to itself, use an unscoped/inactive faction,
link to a faction/location from a different scoped world, or duplicate/reverse its declared links.

A `territory-controls` link has active faction and location endpoints, exact `{}` data, and the
faction's root scope matches the location's reviewed root topology. It is stored only faction →
location. A second territory-control link to the same active location, from any faction, is an
invalid conflict convention. This initial slice does not resolve the conflict by deleting, replacing,
or choosing a winner.

### Manual advance action

The mechanic declares exactly four roles:

| Role | Required projection | Purpose |
| --- | --- | --- |
| `front` | `game.core.world.faction.front` with relationships | Current phase plus all world/faction/location front links. |
| `faction` | `game.core.world.faction` with relationships | Active scoped owner and its root-scope evidence. |
| `location` | `game.core.world.location` | The exact contested active location. |
| `world` | `game.core.world.root` and `game.core.world.clock` | Matching active root and derived current minute. |

Input is exactly:

~~~text
{ "expectedPhase": "quiet" | "rising" }
~~~

The caller may supply a stale expected state but never the next phase, current minute, effect,
cause, target, territory result, or decision. The mechanic validates every closed component and
relationship/scope convention, then requires active front/faction/world/location, matching role
bindings, and `input.expectedPhase === front.phase`.

It deterministically chooses `quiet → rising` or `rising → pressing` and returns exactly one
complete `component.set` for the front, preserving status/summary/visibility and setting
`phaseStartedMinute` to the projected root-clock `currentMinute`. A `pressing`, resolved,
archived, inactive, corrupt, mismatched, or stale front rejects with zero effects.

No advance changes faction agenda, faction control claim, territory control, location topology,
clock, routes, conditions, map anchors, knowledge, campaign, quest, or actor data. Success uses
the existing `world.component.replaced` structural event and the action audit; Feature 11 adds no
semantic event or subscription.

## Dependency order and slices

~~~text
World Feature 11: manual faction pressure and exclusive territory
├─ W3 faction components, links, agenda fixture                       [verified]
├─ W5 root clock and minute semantics                                 [must be verified]
├─ W6 one accepted-event reactive-world proof                         [must be verified]
├─ generic actions/effects/audit/structural events                    [implemented]
├─ confirmed front/territory vocabulary and fixture prose             [implemented]
│  └─ Slice 1: faction scope, territory/front state, links, fixture  [verified]
└─ verified front/territory foundation                                [parent: Slice 1]
   └─ Slice 2: manual expected-phase advance and action coverage      [verified]

Automation, resolution, a second fixture front, and player-facing projection [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Scope, territory, and front foundation | W5/W6 are verified; all IDs and fixture front/territory decisions are confirmed. | **Verified:** fresh import proves one scoped faction, one exclusive territory controller, and one well-formed front; invalid scope/control conflicts are rejected. See the [Slice 1 receipt](WORLD-FEATURE-11-SLICE-1-RECEIPT.md). |
| 2 | Manual front advance | Slice 1 is verified. | **Verified:** a current expected phase advances exactly once, records the root minute, emits one structural replacement/audit, and stale/terminal/invalid calls change nothing. See the [implementation receipt](WORLD-FEATURE-11-IMPLEMENTATION-RECEIPT.md). |

## Slice 1 — scope, territory, and front foundation

| Artifact | Change |
| --- | --- |
| Component definition/schema | Add `game.core.world.faction.front` with the exact closed state above. |
| Governing procedure | Revise `procedure.game.core.world.faction` for faction root scope, exclusive territorial control, front authoring/correction, and the no-transfer boundary. |
| Fixture relationships | Add the Lantern Compact's root-scope and market territory-control links; add the observatory front entity, its component, and root/faction/location relationships. |
| Focused test | Add `CatalogWorldFeature11Tests` or the nearest world catalog owner for fresh-import/readback, closed state, scope, and disposable territory conflict cases. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Fresh import | The existing faction has exactly one root scope and one market territory controller; exactly one active observatory front has quiet phase at the confirmed clock minute and exact scope links. |
| Closed data | Invalid front lifecycle/text/visibility/phase/minute JSON rejects. |
| Scope | Missing/duplicate/reversed/self/nonempty/cross-world faction/front links reject. |
| Territory exclusivity | A second faction's `territory-controls` link to the market rejects; a nonexclusive Feature 3 `controls` claim remains valid and unchanged. |
| Endpoint eligibility | Inactive/non-faction/non-location/root/actor/front endpoints reject according to their link convention. |
| Isolation | Existing faction agenda/motives, locations/containment/adjacency, clock, traveller, routes, conditions, knowledge, map anchors, campaign, and quest state remain unchanged. |
| Repository | Focused tests and `roleplay validate catalog` pass; no persistent import occurs. |

## Slice 2 — deterministic manual front advance

Add the mechanic `.md`/`.js` pair and revise the faction procedure's action clause. Its match phrases
cover `advance the observatory front` and `increase Lantern Compact pressure`, but it must not
outrank Feature 3's agenda mechanic for an agenda action lacking front/location/world roles.

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Quiet advance | Valid `{"expectedPhase":"quiet"}` at root minute `m` yields one front replacement with `rising` and `phaseStartedMinute: m`. |
| Rising advance | Valid `{"expectedPhase":"rising"}` yields `pressing` with the current root minute. |
| Stale replay | Repeating the same expected-phase input after success rejects; the front byte representation, clock, territory, and location stay unchanged. |
| Terminal/lifecycle | Pressing, resolved, archived, inactive, missing, or malformed front state rejects with zero effects. |
| Role/scope mismatch | Wrong faction/location/world, missing/extra role, invalid root clock, bad front link, or faction root-scope mismatch rejects with no change. |
| Closed input | Missing/extra/`null`/non-object/unknown expected phase values reject; caller cannot supply a next phase/minute/effect/cause. |
| Determinism | On fresh identical state, roles/input/seed produce the same one replacement and output. |
| Evidence/rollback | Success has one action audit and one `world.component.replaced` event for the front. Any invalid proposed effect rolls back the front and all success evidence. |
| Feature isolation | No action creates/changes territory or general control claims, agenda, clock, routes, conditions, map data, knowledge, campaign, quest, notifications, or subscriptions. |
| Repository acceptance | Focused action tests, `roleplay validate catalog`, full suite, and `git diff --check` pass. Run a protocol walk only if the public MCP/dependency registration surface changes. |

## Completion boundary

Feature 11 is complete when the one scoped fixture front and exclusive territorial-control
convention import cleanly, its manual expected-phase action is auditable and replay-safe, and all
scope/conflict/terminal paths leave no partial state. Stop before resolving fronts, transferring
territory, scheduling faction behavior, adding a rival fixture faction/front, or exposing it to a
player audience.
