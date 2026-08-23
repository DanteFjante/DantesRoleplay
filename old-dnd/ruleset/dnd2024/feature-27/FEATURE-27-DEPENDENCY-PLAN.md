# Feature 27 dependency plan — class progression, hit dice, and class-feature entitlement

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slice 1 verified; Slices 2–5 remain blocked on CH4 class membership, the HP-policy leaf, named feature owners, C14, and CH9.**
Last updated: 2026-08-21

## Execution rule

This plan now records a completed repository implementation slice. It follows `AGENTS.md`,
`procedure.system.create-feature`, `procedure.system.modify`, the
[Terra planning guide](../TERRA-FEATURE-PLANNING-GUIDE.md), the existing character plans, and the
file-first catalog workflow. The Slice 1 receipt records its runtime catalog artifacts and
verification. A later implementation pass selects exactly one next verified slice, runs focused
tests and `roleplay validate catalog`, records a receipt, and stops. Persistent catalog import
remains an integration-play/release decision.

## Target capability

A D&D 2024 character workflow can resolve the source-backed consequences of gaining exactly one
class level—class progression, Hit Die/Hit Point gain, and newly entitled class features—without
copying class rules onto the actor, trusting caller-calculated results, or itself deciding whether
the character is permitted to advance.

### Included

- Immutable, versioned class-progression declarations attached to existing class-content entities.
- Effect-free resolution of one class's exact next level, its declared feature entitlements, and
  its source-backed Hit Die/fixed-HP facts.
- A narrow first declaration for the existing SRD Fighter content at class levels 1 and 2.
- A later, closed fixed-or-rolled Hit Point gain resolver, with Constitution modifier derived from
  the existing ability owner and with no caller-supplied total or modifier.
- A feature-owner map: Feature 27 grants entitlement; the responsible gameplay feature implements
  the feature's actual action, resource, rest recovery, or reaction behavior.
- Future structured expansion to subclasses, higher levels, other classes, and multiclassing only
  after their source declarations and downstream owners are accepted.

### Excluded

- Campaign XP/milestone policy, advancement authorization, authorization consumption, or the root
  character-level transaction (Campaign C14 and Character CH9).
- First-level character creation, class membership persistence, origin grants, equipment, AC,
  profile, or item creation (CH4/CH5 and their owners).
- A second total-level field, stored proficiency bonus, stored eligibility, actor-side class prose,
  caller-provided Hit Point totals/modifiers/die results, or raw effects.
- Rest recovery/spending of Hit Dice, Action Surge recharge, Second Wind use/recovery, Tactical
  Mind reaction timing, Fighting Style choices, Weapon Mastery behavior, feats/ASIs, spellcasting,
  subclasses, multiclassing, respec, or a new MCP tool/kind in the first delivery.

## Official source basis

The registered `source.dnd2024.srd-5.2.1` remains the source identity and attribution owner.
The official 2024 Basic Rules, *Creating a Character > Level Advancement*, say that a level gain
chooses a class, grants one additional Hit Die, and increases Hit Point maximum by the die roll plus
Constitution modifier (minimum 1), or by the table's fixed value plus Constitution modifier. The
same section assigns new class features to the gained class level and bases Proficiency Bonus on
total character level, not a class level. [Official source](https://www.dndbeyond.com/sources/dnd/br-2024/creating-a-character/).

For the initial Fighter declaration, *Character Classes > Fighter* gives d10 Hit Dice and fixed
HP gain of `6 + Constitution modifier`; Fighter level 2 entitles Action Surge (one use) and
Tactical Mind. It does not make either feature mechanically implemented merely by declaring it.
[Official source](https://www.dndbeyond.com/sources/dnd/br-2024/character-classes).

The source review immediately before any HP-writing slice must verify the current-HP consequence
of a maximum increase. The existing HP component owns `current` and `maximum`, but the cited
level-advancement text establishes the maximum gain; this plan will not invent an uncited current-
HP adjustment.

## Verified existing dependencies and overlap result

| Dependency | Evidence and boundary |
| --- | --- |
| Source identity/content provenance | `source.dnd2024.srd-5.2.1` and `procedure.mechanic.dnd2024.character-content-definition` exist. `content.dnd2024.class.fighter.v1` has immutable class identity and an SRD locator, but no progression rules. |
| Total character level | `dnd2024.character-level` is a 1–20 total-level fact and derives Proficiency Bonus. Its recorder expressly excludes class level, XP, and advancement. |
| Hit Point storage | `dnd2024.hit-points` stores a closed current/maximum pair. Its writer validates a final pair and explicitly does not calculate class advancement. |
| Ability facts | Feature 2's ability component/reader is the authoritative source for Constitution; Feature 27 must derive the modifier from it, never accept it as input. |
| Character class state | CH4 plans the sole initial `dnd2024.character.class-membership` and level-one declaration. It is not implemented, so Feature 27 cannot yet resolve an actor's class transition. |
| Level-up root | CH9 plans the only supported single-class `1→2` root. It needs a typed class/HP/feature result and must not duplicate this feature's formulas. |
| Campaign scope/authorization | C15 supplies campaign participation; C14 still owns policy and authorization; neither owns a class rule or Hit Point formula. |
| Turn/action budget and D20 checks | Features 11–13 provide action-budget and ability-check foundations, but they do not yet provide Action Surge or a post-failure Tactical Mind composition seam. |
| Rest/recovery | Feature 33 is planned. It is the required owner for short/long-rest recovery and spent-Hit-Die behavior; Feature 27 must not add a parallel rest reset. |
| Multiclassing | CH12 is planned and explicitly awaits Feature 27 eligibility/class-grant/HP resolution. It is a consumer, not a second class-progression owner. |

## Recursive dependency analysis

```text
Feature 27: source-backed class progression and class-level consequences       [blocked parent]
├─ SRD source registry + immutable class-content identity                       [implemented]
├─ Fighter 1–2 progression declaration + entitlement reader                    [missing Slice 1 leaf]
│  ├─ canonical progression schema/procedure                                    [missing Slice 1]
│  └─ versioned Fighter feature identities (declarative only)                   [missing Slice 1]
├─ actor class membership / exactly-one-class invariant                         [blocked: CH4]
├─ class-level transition resolver                                               [blocked: CH4 + Slice 1]
│  └─ total/class level consistency                                              [implemented total-level input; CH4 missing]
├─ Hit Point / Hit Die gain resolver                                             [blocked parent]
│  ├─ Constitution modifier reader                                               [implemented Feature 2]
│  ├─ current-HP consequence source decision                                     [missing source/owner leaf]
│  └─ fixed first path, then seeded roll path                                    [blocked after current policy]
├─ Fighter level-2 feature behavior                                              [blocked parent]
│  ├─ Action Surge extra-action and resource lifecycle                           [needs Feature 11/12 extension + Feature 33]
│  └─ Tactical Mind failed-check composition and Second Wind resource            [needs D20 post-failure + Feature 33]
├─ CH9 atomic 1→2 transaction and C14 authorization consume                     [blocked character/campaign parents]
├─ subclasses, level 3–20, ASI/feat, and other classes                           [excluded future source slices]
└─ multiclass prerequisites and plural class state                               [excluded: CH12 consumer]
```

## Dependency and ownership decisions

1. **Class rules are immutable content, not actor state.** A class-content entity owns its
   versioned progression declaration and source reference. An actor may later reference the class
   through CH4 membership, but never copies hit-die size, fixed HP value, feature list, class name,
   subclass table, or rule text.
2. **Class level and total level are different facts.** CH4/CH12 own persisted class membership;
   `dnd2024.character-level` remains the one total level. Feature 27 verifies their relationship
   for a requested transition and never replaces either with a derived duplicate.
3. **Entitlement is derived; feature state is not.** The progression reader returns canonical
   feature-definition IDs for the exact class level. It stores no `hasFeature` list. Action uses,
   turn changes, healing, reactions, rest recovery, and any feature-specific state belong to the
   feature/action/rest owner that implements that behavior.
4. **HP gain is derived, while HP remains HP-owner state.** Feature 27 resolves only a source-
   backed gain. The HP owner writes the complete pair inside CH9's root after the source-defined
   current-HP policy is confirmed. Neither CH9 nor the caller calculates a die result, Constitution
   modifier, fixed gain, or final pair.
5. **Proficiency Bonus stays total-level derived.** Class tables may display it, but Feature 27
   neither stores nor grants a separate bonus. A total-level transition causes existing consumers
   to derive the new value.
6. **Feature 27 is not a universal feature mechanics bucket.** It declares and resolves class
   entitlement. A feature without a named, tested behavior owner makes the class-level transition
   unsupported; a receipt cannot simulate Action Surge, Tactical Mind, or any other class feature.
7. **The initial source slice is Fighter 1–2 only.** This is a reusable declaration shape, not a
   claim that fighter level 2 is playable until every named owner and CH9/C14 are ready.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | Immutable Fighter 1–2 progression declaration and reader | This plan approved; source locator and permanent vocabulary confirmed | A class-content entity has one validated source-backed progression declaration; a zero-effect reader returns exact level-1/2 entitlement diagnostics and rejects unknown/archived/corrupt data. |
| 2 | Generic class-transition resolver | Slice 1 and CH4 membership accepted | Given consistent actor membership/total-level state, resolve only the exact next class level and source feature IDs; no actor mutation, HP write, or campaign decision. |
| 3 | Hit Point/Hit Die gain resolver | Slice 2; exact current-HP policy and Feature 2 Constitution projection confirmed | Fixed Fighter `1→2` HP gain is derived with minimum 1 and no caller-derived values; seeded roll branch is separately verified before enabled. |
| 4 | First supported level-2 feature behavior | Slices 2–3, Action Surge/Tactical Mind composition contracts, Feature 33 recovery ownership | A Fighter level-2 entitlement is mechanically usable only through named action/check/rest owners; unsupported feature paths block the CH9 fixture. |
| 5 | CH9/C14 one governed Fighter `1→2` | Slices 2–4, C14, CH9, CH4/CH5 playable-character evidence | One authorized active character advances atomically; every owner effect/receipt/authorization succeeds or rolls back together. |
| 6 | Source expansion | Slice 5 accepted, each new class/level owner map reviewed | Add one source-backed level, class, subclass, feat/ASI, or multiclass branch per amendment; do not infer support from the table shape. |

## Slice 1 — immutable Fighter 1–2 progression declaration and reader

### Implemented runtime artifacts

- `procedure.mechanic.dnd2024.class-progression`, category
  `ruleset.dnd2024.core.advancement.class-progression`, governing source declarations and the
  read-only progression mechanic.
- `dnd2024.class-progression`, a closed component attached only to a class-content entity already
  carrying `dnd2024.character.content-definition` with `kind: "class"`.
- `content.dnd2024.feature.fighter.action-surge.v1` and
  `content.dnd2024.feature.fighter.tactical-mind.v1`, immutable feature-content identities with
  source provenance only; they grant no actor state or playable behavior in this slice.
- `mechanic.dnd2024.class-progression.read`, an effect-free diagnostic/entitlement reader.

The exact entity IDs, component meaning, category, and reader ID are permanent. They are distinct
from CH4 membership, CH3 grant receipts, existing total-level/HP state, and any gameplay feature
mechanic.

### Governing contracts and source locator

Re-read `procedure.system.create-feature`, `procedure.mechanic.write`,
`procedure.mechanic.dnd2024.character-content-definition`,
`procedure.mechanic.dnd2024.character-level`, and the source registry immediately before writing.
The fixed source is `source.dnd2024.srd-5.2.1`; use `Character Classes > Fighter, PDF pages 47–48`
for the Fighter class entity and exact stable locators for the two feature identities after checking
the registered SRD PDF. Do not substitute a Player's Handbook-only locator or copy source prose.

### Data/input contract and required state

The closed progression declaration contains only:

- `hitDieSides`: one of `6`, `8`, `10`, or `12`;
- `fixedHitPointGainBeforeConstitution`: an integer from `1` through `12`;
- `levels`: canonical ascending entries, each with exactly `classLevel` (1–20), sorted unique
  `featureDefinitionIds`, and sorted unique `choiceSetDefinitionIds`;
- fixed `sourceRef`.

It contains no actor ID, class name/key/version, total level, XP, proficiency bonus, Constitution
modifier, final HP/current HP, die result, resource count, feature state, subclass, grant receipt,
campaign, or effect. Class identity/version/source status remain on the existing content-definition
component. For the first Fighter declaration, levels 1 and 2 are present only; level 2 references
the two new immutable feature identities in canonical ID order, while level 1 declaratively
references the three source identities for Fighting Style, Second Wind, and Weapon Mastery. All
five initial feature identities report unimplemented behavior until their own accepted owner map.

The reader accepts exactly `{ "classLevel": <integer 1–20> }` and a `class` role. It requires the
class content-definition and progression components. It reports content/progression validity,
requested class level, `supported`, hit-die/fixed-HP source facts, canonical feature/choice IDs,
and a closed `problem` code. Absent level entry is `unsupported-level`, not an inferred empty
feature list. It uses no randomness and returns zero effects.

### Resolution behavior

1. Reject missing/non-object/extra input, non-integer/out-of-range level, missing class role, or
   any role not carrying exactly one active `kind: class` source-backed content identity.
2. Parse and validate the complete closed progression: fixed provenance, allowed hit die/fixed HP
   pair, ascending unique levels, and canonical child IDs. The catalog fixture/import test—not the
   sandboxed reader—proves that each declared child ID names one active source-backed feature
   entity, because a mechanic cannot dynamically fetch entities it did not declare as roles.
3. Find the exact `classLevel`; do not interpolate, use the highest lower level, or default an
   omitted level to no features.
4. Return an effect-free structured declaration result. Level 2 says it entitles Action Surge and
   Tactical Mind, but marks both `behaviorStatus: "unimplemented"` until their individual owners
   are accepted. It must never portray an entitlement as an available action.

### Invariants, failure behavior, and non-goals

- One class-content entity has at most one complete progression declaration; new source revision
  means a successor content entity, not a mutation of a published definition.
- All rejected reads and all read results leave entity/component bytes and revision unchanged.
- The reader does not inspect an actor, write membership, calculate a Constitution modifier, add
  HP, grant a feature, spend a resource, grant an action, or compose with C14/CH9.
- Equivalent source state/input produces byte-identical data, narration aside; no random call is
  allowed in this slice.

### Slice 1 acceptance matrix

| Case | Assertion |
| --- | --- |
| Fighter source data | The existing Fighter class entity receives one valid d10/fixed-6 progression declaration and the two level-2 feature identities with fixed SRD provenance. |
| Exact levels | Reading level 1 returns exactly Fighting Style, Second Wind, and Weapon Mastery; reading level 2 returns exactly Action Surge then Tactical Mind. Every initial entitlement reports unimplemented behavior. |
| Unsupported level | Level 3, and a valid but absent level between/above declaration entries, return `unsupported-level`, no effects, and unchanged bytes. |
| Closed/provenance | Extra fields, duplicate/out-of-order levels or feature IDs, wrong class kind/status/source locator, or wrong hit die/fixed HP pair diagnose with zero effects. The catalog fixture test separately proves every declared feature ID exists, is active, and has fixed source provenance. |
| Differential | A level-1 and level-2 read differ only in their exact entitlement list/support result; neither changes actor or class content state. |
| Routing | “inspect fighter class level” and “read class progression” select this reader; no phrase overlaps administrative total-level/HP recording or future CH9 advancement. |
| Determinism/integrity | Repeated reads are structurally identical and effect-free; corrupt disposable state reports the named diagnostic and is restored/deleted. |

### Slice 1 exit gate

**Verified.** The source-backed class declaration and reader are catalog-valid, focused tests cover
the matrix, the catalog validates in a disposable database, and the receipt records the exact
source locator and evidence. Stop before actor membership, HP, Action Surge, Tactical Mind,
campaign authorization, or level-up work.

## Later slices: required contracts

### Slice 2 — generic class-transition resolver

Requires CH4's accepted one-class membership and Slice 1. Proposed reader input is exactly
`{ "fromClassLevel": <1–19> }`; it reads the actor's membership, total level, referenced class
content, and progression. It derives only `toClassLevel = from + 1`, verifies the actor's current
class level and total level are consistent under the first single-class contract, and returns the
source declaration for that exact next level. Caller-supplied class ID, target level, total level,
feature IDs, HP, Constitution, grant, campaign, authorization, or effects fail. It has zero
effects. The acceptance matrix must cover absent/duplicate/malformed/archived membership, class-
source drift, total/class mismatch, cap, feature ordering, replay, and no mutation.

### Slice 3 — Hit Point and Hit Die gain resolver

Requires Slice 2, the exact cited current-HP policy, and Feature 2's canonical Constitution
projection. It accepts exactly either fixed mode or a declared roll mode; it never accepts a
modifier, die sides, outcome, final HP, or target level. Fixed Fighter `1→2` derives
`max(1, 6 + Constitution modifier)`. The roll route must call `ctx.randomInt(1, 10)` exactly once,
then derive `max(1, roll + Constitution modifier)`, expose the reproducible roll result to CH9,
and be rejected until its result/receipt ownership is confirmed. It returns a typed delta, not an
HP component effect. Test Constitution transition bands, minimum-1 floor, fixed/roll differential,
one-roll count/seed replay, corrupt ability/HP state, and unchanged actor bytes.

### Slice 4 — named feature behavior owners

Requires the transition result, Feature 11/12 turn-budget extension, D20 result-composition
design, and Feature 33 rest/resource recovery. Action Surge must be an explicit once-per-rest
class resource and may grant one additional non-Magic Action only on the user's turn; it cannot be
a free replacement of normal Action budget or a second turn. Tactical Mind must run only after a
failed ability check, offer a Second Wind expenditure, roll d10 once, and preserve the expenditure
when the adjusted check still fails. The resource, check, and rest owners must agree a typed,
atomic composition seam before any feature mechanic is written. Each feature gets its own source
declaration and test matrix; Feature 27 merely gates the entitlement.

### Slice 5 — CH9/C14 Fighter 1→2 integration

Requires C14 exact authorization/consume, CH4 membership, CH5/CH6 playable actor evidence,
Slices 2–4, and an owner-agreed current-HP policy. CH9 remains the root: it consumes one exact
authorization, asks Feature 27 for the next-level/HP/entitlement results, delegates every state
write to its owner, increments total and class levels once, and writes CH3/CH9 receipts last. A
failure in any HP/feature/receipt/event/audit/guard/reaction path rolls back authorization and all
effects. It must prove valid play after advancement, replay/double-consume rejection, stale state,
cancellation, timeout, and no duplicate grants.

## Plan-quality audit

- One class-progression capability with campaign and transaction boundaries explicit: **yes**.
- Official source, registered identity, and exact initial Fighter source sections: **yes**.
- Existing total-level, HP, content, CH4/CH9/C14/C15, action/check/rest, and multiclass owners
  searched: **yes**.
- Every unresolved requirement is expanded to a leaf or blocked parent: **yes**.
- Persistent facts, derived results, transient roll context, and downstream feature effects have
  distinct owners: **yes**.
- Exactly one lowest implementation slice is named: **yes — Slice 1 declaration and reader**.
- Slice 1 has closed state/input, source validation, negative/boundary/routing/determinism and
  integrity assertions: **yes**.
- Slice 1 runtime catalog artifacts are implemented and verified in
  [the receipt](FEATURE-27-SLICE-1-RECEIPT.md); no persistent catalog import occurred: **yes**.

## Plan-change rule

Revise before implementation if CH4 chooses a different class-membership representation, the SRD
source locator/version changes, a feature needs a different action/check/rest composition seam, the
HP owner establishes a different source-supported current-HP rule, or CH12 needs a compatible
plural class representation. Do not work around any change with a second total level, actor-copied
class table, stored feature entitlement, caller-supplied HP/modifier/die result, generic class
feature effects endpoint, campaign policy field, or direct database write.
