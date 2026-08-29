# D&D 2024 basic character creation MVP plan

Status: **accepted**
Ruleset alignment: **dnd2024-owned**
Source: `source.dnd2024.srd-5.2.1`, *Character Creation > Create Your Character* and
*Step 3: Ability Scores* (PDF pp. 19-21), *Character Origins* (PDF pp. 82-86), and
*Classes > Fighter* (PDF pp. 47-48)
Owner: [D&D 2024 application roadmap](ROADMAP.md)
Full-resolution work: [character-creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)

## Outcome

Create and attach one valid **basic-playable** level-1 character without waiting for every selected
trait, feat, class feature, rest benefit, spell, or equipment rule to be executable.

The accepted first path was intentionally fixed and small:

- user-provided name;
- any accepted species and legal Size choice;
- Soldier background;
- Fighter level 1;
- Standard Array with legal Soldier ability increases; and
- core state already supported by the current system: abilities, Size, Speed, level, starting Hit
  Points, baseline Armor Class, and supported proficiencies.

The accepted [CC-MVP-C1 expansion](DND2024-CHARACTER-CREATION-ALL-CLASS-IMPLEMENTATION.md) now keeps
Soldier and the same request/transaction boundary but permits any of the twelve SRD level-1
classes. Every class has a source-bound creation profile and progression/feature identities;
unsupported mechanics remain pending instead of being partially approximated.

Unimplemented benefits are listed as pending and grant no approximate behavior. The result is never
called source-complete.

## Reuse boundary

The slice reuses the accepted CC1 and CC2A-CC2H2 resolvers, existing component schemas, Fighter
progression identity, Campaign C15 participation seam, staged world composer, typed effects, and
generic application transaction. It adds no replacement rules engine, wizard state, or second
campaign-state authority.

Human Skillful and the recommended Versatile/Skilled path may be applied through their accepted
resolvers. Other traits and features remain pending. The caller cannot supply calculated HP, AC,
Speed, proficiency bonus, effects, or a fabricated pending list.

## Single implementation slice: CC-MVP

**Effort:** 5-8 EP total.

**Model:** `gpt-5.6-sol`, high reasoning, for implementation and self-review.

**Do not subdivide** unless implementation discovers a real owner/schema conflict.

Deliver one end-to-end vertical slice that:

1. accepts the closed name/species/Size/ability-allocation request while fixing Soldier and Fighter;
2. resolves definitions and derives only already-supported core state with pure catalog JavaScript;
3. creates a small immutable `basic-playable` creation record containing selected definition IDs,
   applied component IDs, and unresolved entitlement IDs/reasons;
4. stages the actor, core components, creation record, and campaign participation through existing
   typed effects; and
5. commits them in one generic transaction, then proves readback, replay, and rollback.

Candidate permanent component ID: `dnd2024.character-creation-record`. The record must not duplicate
ability scores, HP, AC, Speed, inventory, traits, or campaign state owned by other components.

## Acceptance

One fresh-client test must prove that a legal request creates and attaches exactly one readable
character that can use the currently accepted basic check/Initiative/movement/weapon/HP path.

Negative tests must prove that:

- unknown, inactive, stale-source, or incompatible definitions create nothing;
- illegal ability, species, or Size choices create nothing;
- unresolved entitlements produce no effects;
- any staged failure leaves no partial actor or campaign attachment; and
- replay of the same operation returns the original result without creating another actor.

Run focused tests during implementation, then the full suite and `roleplay validate catalog` for
acceptance. Run the protocol walk only if the implementation changes protocol registration.

## Deliberately postponed

- executable non-Human species traits;
- full background grants, equipment packages, languages, and tools;
- Fighter and Origin-feat behavior not already accepted;
- rest completion, Resourceful, spellcasting, advancement, multiclassing, and full inventory; and
- conversion from `basic-playable` to `source-complete`.

Later feature mechanics can read authoritative selections plus the pending list and resolve one
entitlement at a time. Character creation does not need to be rebuilt.

## Confirmation gate - satisfied

The user's implementation instruction confirmed the permanent `dnd2024.character-creation-record`
ID and the meaning of `basic-playable`: an actor may exist with explicitly pending entitlements,
and pending entries grant no rules behavior. The focused replay, rollback, source-drift, and stored
state assertions provide the equivalent completion confirmation for the accepted boundary.

## Planning receipt

- Delivered initially by [the accepted implementation](DND2024-CHARACTER-CREATION-MVP-IMPLEMENTATION.md)
  and [completion receipt](evidence/DND2024-CHARACTER-CREATION-MVP-RECEIPT.md), then widened by the
  [all-class implementation](DND2024-CHARACTER-CREATION-ALL-CLASS-IMPLEMENTATION.md) and its
  [completion receipt](evidence/DND2024-CHARACTER-CREATION-ALL-CLASS-RECEIPT.md).
- The basic-playable vertical slice is complete; full source resolution remains optional follow-up
  work rather than a prerequisite.
