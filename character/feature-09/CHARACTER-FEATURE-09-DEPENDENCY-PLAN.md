# Character Feature 9 dependency plan — one governed class-level advancement

Status: **Planned; no level-up implementation begins until the campaign advancement decision, class/HP resolver, and played CH6/CH7 evidence gates are accepted.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH3–CH7, and the ruleset D&D 2024 roadmap. It writes no runtime artifact.

CH9 is one character advancement transaction, not a correction, rest, respec, or a new generic level writer. Campaign progression decides whether advancement is available; the D&D ruleset owns class/HP/feature semantics; CH9 verifies the approved decision, resolves immutable source declarations, and coordinates their owners atomically.

## Target capability

A played, active, single-class level-one character with a valid campaign-owned advancement authorization can validate and advance to the next supported non-spellcasting class level. The same versioned class definition supplies the exact level-two declaration and closed choices. One root action updates class and total level, applies every newly earned grant exactly once through its true owner, records an immutable advancement receipt, and leaves the character usable. Any failure leaves neither a partial level nor a consumed authorization/grant.

The first fixture is **level 1 to level 2 of one approved non-spellcasting class only**. It is a content fixture under reusable single-class advancement contracts, not an assertion that every class, arbitrary number of levels, or a particular class's feature is generally supported.

### Included

- Campaign-owned authorization/eligibility lookup and one-time consumption as an external, explicit prerequisite; CH9 does not choose an XP, milestone, quest, or GM policy.
- One immutable, source-cited level-two declaration attached to the existing CH4 class content definition, with closed feature/grant/choice references.
- One guarded transition from a consistent class level 1 / total level 1 state to class level 2 / total level 2 for the actor's sole CH4 membership.
- A dedicated, source-cited class-level HP transition and any proved level-two feature owner calls; CH9 never accepts final HP, AC, proficiency, or feature state from its caller.
- Reuse of CH3 generic grant declarations, choice sets, and grant receipts for per-grant deduplication, plus one CH9 advancement receipt for the progression decision and completed transition.
- Existing action transport, audit, event, rollback, inspect, and CH7 deterministic evidence conventions.

### Excluded

- XP accounting, milestone selection, reward issuance, campaign/party policy, automatic advancement, player authorization, or a second campaign relationship. The missing campaign advancement owner is a blocker, not a free-text `reason` field.
- Level 3+, repeated advancement, downgrade, correction/respec, class replacement, subclass, multiclassing, feat/ASI, spellcasting, rest/Hit Dice recovery, arbitrary rolls, or any previously unsupported source option.
- Reusing the administrative `mechanic.dnd2024.character-level.record` as a level-up engine; it explicitly does not own class advancement or HP calculation.
- Caller-supplied next level, class ID, total level, HP/max HP, hit die result, AC, derived bonuses, grant targets, raw component payloads, raw effects, campaign ID, or authorization outcome.
- A parallel inventory, feature, class, source, audit, or authorization record; new level-two item grants remain blocked unless the existing CH5/Items transaction owner can create them atomically.

## Ownership and overlap result

| Concern | Authoritative owner and CH9 rule |
| --- | --- |
| Campaign scope and eligibility | A new **campaign advancement authorization/consumption contract**, planned and accepted by the Campaign owner before CH9. It proves a specific active character may move from exactly level 1 to 2 and is consumed only in the same root transaction. CH9 neither stores XP nor invents policy. |
| Class identity and per-class level | CH4 `dnd2024.character.class-membership`. Its field range is 1–20, but CH4 creates only 1; CH9 is the only writer for its supported 1→2 transition. CH12 alone may introduce plurality. |
| Total character level | Existing `dnd2024.character-level` component remains authoritative. CH9 requires a new protected advancement transition below its existing procedure rather than invoking the administrative record/correction mechanism with caller data. |
| Class level-two rules, hit die, HP, feature behavior | Ruleset D&D 2024 class-and-level/HP owners (roadmap feature 27) and any named feature owner. The current HP writer merely validates a final pair and cannot be used to calculate advancement. |
| Equipment and AC | Items/containment and ruleset armor/equipment derivation owner. A level-two declaration that changes either blocks until those owners and atomic composition are real; CH9 cannot calculate or write either. |
| Grant choices and duplicate suppression | CH3 `dnd2024.character.grant-declarations`, choice sets, and `dnd2024.character.grant-receipts`, unique by `(sourceDefinitionId, grantKey)`. They record each resolved grant, not the advancement decision itself. |
| Advancement provenance | CH9 `dnd2024.character.advancement-receipts`, immutable actor-side record of the successful 1→2 transition and campaign authorization reference. It does not duplicate grant receipts, source prose, final derived values, or the root operation ID. |
| Transaction/audit/event | Existing ActionRunner action transaction and operation history. The root operation ID stays in audit/history; the CH9 receipt must not copy it. |
| Discovery, inspection, guide/UI | CH6 action/query surface and CH8 consumer boundary. CH9 defines no new MCP kind or browser route; a later guide/UI only calls the same validate/advance command. |

The campaign authorization is deliberately a cross-subsystem leaf rather than a character component. If the Campaign owner chooses XP, milestone, quest, session, or GM approval semantics, it exposes one closed authorization projection and atomic consume call; CH9 consumes no raw policy data. If both campaign authorization and character effects cannot compose under one ActionRunner root, stop for the generic composition decision—do not nest roots or consume authorization before character effects commit.

## Source and fixture boundary

Before implementation, CH0/CH7 must ratify the exact D&D SRD 5.2.1 source identity and locator for the already supported class's level-two entry, including every feature, choice, HP instruction, and equipment consequence it activates. Record the source edition, document/section locator, stable class content-definition ID/version, and feature owner mapping in the implementation receipt—not in actor state.

The reusable declaration form may represent level-keyed non-spellcasting grants and choices for a single class membership. The sole initially accepted source record is the ratified class's level 2 declaration. A class with a different level-two choice shape is added only through CH7 evidence and a CH9 amendment; spell slots, subclass selection, feat/ASI, Hit Dice recovery, or a second class have named successors CH10, CH12, CH11, ruleset rests feature 33, and CH12 respectively.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Character advancement contract | `procedure.character.advance`, governing closed validate/advance input, dependencies, canonical result, receipt, and recovery. |
| Root advancement mechanic | `mechanic.dnd2024.character.advance`, selected through existing `commit(kind: "action")`; it coordinates one existing actor only and owns no source formula. |
| Class declaration extension | `dnd2024.character.class-level-declarations`, attached only to CH4 immutable class content definitions. It contains canonical level keys and references to CH3 grant/choice declarations and named feature/HP resolvers; it contains no prose, arbitrary rules expression, mutable actor state, or copied total level. |
| Class-level resolver | `mechanic.dnd2024.class.advance.resolve`, under `procedure.mechanic.dnd2024.class`; a zero-effect resolver that reads the actor's membership and exact immutable declaration, producing owner-bound level-two grants. |
| Total-level transition | `mechanic.dnd2024.character-level.advance`, under the existing character-level procedure. It accepts parent-bound current state and proves exactly the next level; callers never supply a replacement level. |
| Advancement receipt | `dnd2024.character.advancement-receipts`, actor component owned by CH9. It records only successful completed progression records and never replaces CH3 grant receipts. |

All names, parent scopes, schema meanings, the campaign authorization contract, and the atomic consume/composition behavior require semantic confirmation under `procedure.system.modify` before authoring. In particular, do not create a new total-level mechanism if the owning ruleset contract instead approves an extension to an existing one; the requirement is a non-administrative, parent-bound transition, not this provisional spelling.

## Closed advancement contract

`procedure.character.advance` accepts a schema-bound object:

~~~text
{
  operation: "validate" | "advance",
  characterId: canonical existing character entity ID,
  expectedFromLevel: 1
}
~~~

`characterId` identifies an existing actor; campaign scope is resolved through its campaign-owned attachment, never supplied. `expectedFromLevel` is a stale-intent guard, not a way to select any level. Omitted, null, empty, non-object, unknown field, malformed ID, duplicate key, unsupported operation, or a value other than integer `1` fails before projection. `validate` performs the complete current-state, authorization, source, choice, grant, and owner-readiness resolution with zero durable character/campaign effects. `advance` repeats all resolution inside the root transaction and never consumes a cached validate result.

The resolver determines the one class definition and next level. The request deliberately has no class, target level, campaign, HP, grant, feature, choice, or authorisation field. If the ratified level-two declaration includes a closed player choice, introduce that one named choice field only after its CH3 choice-set ID/cardinality and feature owner have been confirmed; an absent required choice is `incomplete`, an invalid present choice is a named validation failure, and no default is invented.

The canonical success result contains only `characterId`, `fromLevel`, `toLevel`, sorted `sourceDefinitionIds`, sorted applied `grantKeys`, `advancementReceiptPresent`, and literal `nextAction`. It contains no campaign policy, authorization payload, source prose, raw effect bundle, final HP/AC, audit/event ID, or promise that an unsupported class action is usable. Failure returns a stable code, the failed dependency/field, and the recovery call; it reveals no campaign-hidden eligibility facts beyond whether the actor is not currently advanceable.

### Receipt and declaration data

The level-two immutable declaration is canonically ordered by numeric level and stable grant/choice key. For this first fixture it has exactly level `2`, references the CH3 generic declarations, and names each required ruleset resolver/feature owner. It must reject duplicate level entries, duplicate grant keys within a source definition, an entry below 1 or above 20, unsupported grant domain, stale/archived source, or a resolver not registered under its approved procedure.

Each successful CH9 receipt record is append-only and canonical. Its transition fields are reusable
for a future CH12 first level in an added class (`0→1`), but CH9 may write only its supported
single-class `1→2` values:

~~~text
{
  classDefinitionId: canonical immutable ID,
  fromClassLevel: non-negative integer 0–19,
  toClassLevel: integer 1–20 and exactly fromClassLevel + 1,
  fromTotalLevel: integer 1–19,
  toTotalLevel: integer 2–20 and exactly fromTotalLevel + 1,
  campaignAdvancementAuthorizationId: canonical authorization ID,
  appliedGrantKeys: sorted unique stable keys
}
~~~

It records neither final/current HP, AC, source prose, chosen option values (CH3 receipt owns them), an item instance, campaign ID, total-level formula, source locator copy, root-operation ID, a `status`, or a mutable authorization result. Its unique key is `(classDefinitionId, toClassLevel)`. CH9's first slice requires total/class `1→2`; later CH9 slices may use another total `N→N+1`, and only CH12 may write class `0→1` for an approved added class. A duplicate or inconsistent prior receipt is corrupt durable state and blocks rather than being repaired or overwritten. The campaign authorization owner retains its own lifecycle/evidence; CH9 only references the consumed authorization it has already verified in the same transaction.

## Resolution and transaction boundary

1. Resolve the actor through CH6/CH5 read conventions and its campaign-owned attachment. Require exactly one active, non-archived CH4 class membership, a consistent total/class level of 1, no existing advancement receipt for level 2, and source definitions that are immutable, correct kind, approved, and not archived.
2. Ask the campaign owner for one authorization bound to this actor and exact `1→2` transition. Missing, spent, expired, cross-campaign, stale, or malformed authorization fails unchanged. CH9 does not infer eligibility from time, activity, XP, an audit, or a request string.
3. Resolve the exact level-two class declaration. Resolve all closed choices and generic grants once, reject overlap/replay, then call the class/HP/feature/equipment owners in dry-run/planned mode. Every result is typed and owner-bound; no raw effect is accepted.
4. For `validate`, return the canonical result with zero structural writes and do not consume authorization. For `advance`, repeat steps 1–3 inside the one ActionRunner root transaction.
5. Apply only the already-planned effects in confirmed canonical order: campaign authorization consumption; class-level transition; total-level transition; HP and any real feature/item owner effects; CH3 grant receipt entries; CH9 advancement receipt last. The precise sequence must be confirmed with the Campaign and ruleset owners; if an owner needs another order, change the shared root plan before implementation.
6. Write the ordinary root audit/event evidence only after the effect bundle commits. Any child, guard, reaction, audit, cancellation, or timeout failure rolls back both the authorization consumption and every character effect. Failure audit follows existing separate failure-audit semantics only.

No nested `commit`, browser call, raw database write, child independent transaction, or consume-then-advance sequence is permitted. Because CH9 advances an existing actor, it does not require CH5's virtual-new-actor workaround; it still requires the same effect-composition and rollback guarantees for cross-owner calls.

## Dependency graph and slices

~~~text
Played CH6 level-one fixture + accepted CH7 evidence
├─ campaign advancement authorization + atomic consume contract                  [missing campaign leaf]
├─ CH4 membership/content and CH3 grants/receipts                               [character prerequisite]
├─ ruleset class/level + HP transition and level-two feature owners              [missing ruleset leaves]
├─ Items/armor/equipment owner, if the source declaration requires it            [conditional leaf]
└─ ActionRunner cross-owner composition / rollback proof                         [shared transaction gate]
   └─ Slice 1: declarative level-two content and pure validation
      └─ Slice 2: one atomic 1→2 advance fixture
         └─ CH10, CH11, CH12 and later CH9 source expansions
~~~

### Slice 1 — declaration and pure validation

**Prerequisites:** All missing leaves above are accepted in their owners; CH0/CH7 records the exact source locator; permanent IDs and campaign composition semantics are confirmed.

1. Add the confirmed class level-declaration and resolver contracts with one supported level-two source record and only proved grant/choice forms.
2. Add the closed `procedure.character.advance` validate path and canonical projections without any durable write or authorization consumption.
3. Exercise valid, incomplete, invalid, stale-source, archived-source, malformed/corrupt membership, total/class mismatch, missing/spent/cross-scope authorization, unsupported feature owner, duplicate grant, and existing receipt cases.
4. Run focused resolver/catalog tests and `roleplay validate catalog` after catalog work.

**Exit:** a known played level-one actor can obtain a source-cited, owner-bound, zero-effect 1→2 preview only when campaign authorization and every real owner are currently valid.

### Slice 2 — atomic advancement fixture

**Prerequisites:** Slice 1 accepted; all participating owner mechanisms support one root planned effect bundle; failure-injection points and audit/event semantics are confirmed.

1. Add the guarded class/total-level transitions, campaign authorization consume call, HP/feature/item calls, CH3 grant receipt writing, and CH9 receipt write inside one existing action transaction.
2. Assert canonical ordering only as agreed by the participating owners; do not emulate their formulas/state transitions in CH9.
3. Run the supported actor from level 1 to 2, inspect all authoritative projections, and perform its available ordinary action after advancement.
4. Inject failure before and after every child owner, receipt, event, and audit boundary; test duplicate/replay, cancellation, timeout, stale concurrent intent, authorization double-consume, and restored readback. Run focused tests, `roleplay validate catalog` where applicable, full suite at acceptance, and a protocol walk only if the existing action surface/dependency registration changes.

**Exit:** one authorized fixture advances exactly once from a consistent 1/1 state to 2/2 and remains playable; every failed/replayed/concurrent attempt leaves the authorization, class/total-level state, grants, items, receipt, and success event/audit unchanged.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Eligibility ownership | CH9 advances only with one valid campaign authorization for this actor and exact transition. It neither creates nor interprets XP/milestone policy, and validate never consumes it. |
| State consistency | Before and after success, the sole class membership and total-level component agree: 1/1 becomes 2/2. Any mismatch, second membership, missing membership, or attempted target/downgrade blocks unchanged. |
| Source/version integrity | The immutable class definition and level-two declaration resolve at their recorded versions. Archived, wrong-kind, stale, duplicate, or unowned declarations fail; later source edits never reinterpret an existing receipt. |
| Grant integrity | Every declared level-two grant resolves through its named owner once. CH3 receipts deduplicate source grants; a CH9 receipt records the successful progression, and neither substitutes for the other. |
| Derived-state boundary | HP, AC, proficiency, features, equipment, and any random rule result come only from approved owners. Caller-provided or CH9-calculated values fail. |
| Atomicity | Authorization consumption, level changes, grants, items, receipts, events, and success audit commit together or not at all. Failure/cancel/timeout never spends the authorization or leaves a partial level. |
| Replay/concurrency | Repeating the same completed request, reusing a consumed authorization, or acting with a stale expected level produces no second grant/receipt/level change. |
| Narrow fixture, reusable contract | The 1→2 non-spellcasting fixture proves generic single-class level declarations; it does not silently support higher levels, subclasses, multiclassing, spellcasting, ASIs, or a different choice family. |
| Readback/play | Inspection shows the immutable sources, 2/2 progression, grant receipts, and advancement receipt without copied source prose or audit IDs; the resulting actor can perform a real supported action. |

## Evidence and change control

The implementation receipt records confirmed IDs, campaign authorization owner and atomic-consume proof, exact SRD locator/version, approved declaration/feature owner map, canonical input/result fixtures, valid post-advance play result, all rollback/replay/concurrency cases, catalog validation, and full-suite result. It does not copy class rules, formulas, campaign policy, authorization data, raw effects, or source prose.

Amend CH9 before adding a second transition, level 3+, a new class source/choice form, subclass, feat/ASI, spellcasting, multiclassing, rest/Hit Dice recovery, XP/milestone policy, a new public transport, browser level-up flow, respec, or migration. Those belong respectively to a subsequent CH9 slice with a source/evidence gate, CH7 expansion, CH11, CH10, CH12, ruleset rest feature 33, the Campaign advancement owner, CH6/CH8 plus public-surface confirmation, or separate lifecycle/migration work.
