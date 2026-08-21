# Feature 29 dependency plan — attunement and SRD magic items

Status: **Slice 1 verified; immutable source-cited magic-item profiles are available. Slice 2 remains blocked on a reviewed Feature 23 physical-definition/instance bridge.**
Last updated: 2026-08-21

## Execution rule

Slice 1 was implemented as a separate immutable catalog definition, preserving Feature 23's
ordinary physical-item boundary. It created the closed `dnd2024.magic-item-profile` component,
its static governing procedure, and three versioned source entities; it created no instance,
attunement, action, event, subscription, charge ledger, effect, or campaign state. Its evidence is
recorded in `FEATURE-29-SLICE-1-RECEIPT.md`; later slices remain prospective.

## Target capability

The game can identify source-cited SRD magic items and, in later slices, track legal attunement and
resolve an item’s declared benefit only through its specific rule owners.

### Included

- Immutable magic-item identity/profile data: category, rarity, whether attunement is required,
  physical-use requirement, activation family, consumption/charge declaration, and source locator.
- Attunement eligibility, the three-item limit, duplicate-copy exclusion, begin/end lifecycle, and
  explicit diagnostic readout as later slices.
- Physical magic-item instances through Feature 23’s custody model and bounded, source-defined
  activation families as later slices.
- The SRD Magic Items A–Z catalog as iterative, source-reviewed content expansion.

### Excluded

- Artifacts, sentient items, curses, crafting, markets/prices, identification UI, loot generation,
  repair/destruction, or a general-purpose item scripting language.
- Automatic identification, GM adjudication of unusual anatomy/fit, mixed potions, and optional
  magic-item buying/selling policy.
- Replacing Item/Equipment, Armor Class, attack, damage, HP, condition, movement, spell, rest,
  character, or campaign owners.

## Official source basis

The source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the Coast
LLC, 2025-05-01, CC-BY-4.0): [Equipment > Magic Items, PDF pp. 101–102](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), [Gameplay Toolbox > Magic Items, PDF pp. 204–250](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), and [Magic Items A–Z, PDF pp. 209–250](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- An attuned creature gains an item’s magical properties; without the required attunement it has
  only nonmagical benefits unless its description says otherwise.
- Attunement uses a focused Short Rest while physically touching one item, succeeds only when that
  rest is not interrupted, is limited to three items, and forbids two copies of the same item.
- Attunement ends on lost prerequisites, an item being more than 100 feet away for 24 hours, death,
  another creature’s attunement, or a voluntary focused Short Rest except for a curse.
- Activation, consumption, charges, spells cast from items, and next-dawn recharge have distinct
  rules. An item’s individual description controls its effects and exceptions.

## Planning inventory and ownership result

| Inquiry | Evidence and decision |
| --- | --- |
| Physical identity/custody | Feature 23 owns immutable `dnd2024.item-definition`, campaign-local `dnd2024.item-instance`, containment, quantity, and equipment state. Magic profiles reference this seam; they never duplicate an owner or inventory list. |
| Current definition boundary | The Feature 23 item-definition contract explicitly excludes magical exceptions, attunement, actions, and scripting. A magic profile is a distinct static definition, not an accidental extension of ordinary item fields. |
| Equipment/physical use | Feature 23 establishes only held/worn eligibility, not anatomical slots or magic activation. Feature 24 owns Armor/Shield AC and Feature 25 weapon properties; magic bonuses must compose with their final readers rather than changing static ordinary profiles. |
| Rest/interrupt/time | Feature 33 owns Short Rest completion/recovery. It must expose a focused, physical-contact, interruption-aware attunement handoff; Feature 29 cannot declare a rest finished or a 24-hour/next-dawn clock. |
| Range/death/prerequisites | Feature 20 owns placement/distance, Feature 17 owns death, Feature 27 class membership, and Features 31–32 spellcasting. These are required to determine later attunement eligibility/endings. |
| HP, damage, and conditions | Features 15–17 own mitigation, Temporary HP/healing, and dying; Feature 13 owns conditions. A Potion of Healing or a defensive magic item must call those owners, not mutate their components directly. |
| Effects/duration/charges | No general durable source/target/expiry effect lifecycle is verified. Charges are instance state and require an exact next-dawn clock; timed/magic/spell effects remain blocked parents. |
| Existing magic owner | Searches of catalog, ruleset, character, and campaign contracts find no attunement component, magic-item profile, charge state, or item-effect resolver. Feature 23 reserves this scope for Feature 29. |

## Recursive dependency analysis

```text
Feature 29: attunement and SRD magic items
├─ SRD magic-item and attunement source                         [implemented source basis]
├─ immutable magic-item profile catalog                         [implemented: Slice 1]
├─ physical magic-definition/instance link                      [blocked: Feature 23 revision]
├─ attunement state and effect-free reader                      [blocked: physical link]
├─ focused Short-Rest attunement/end lifecycle                  [blocked: Feature 33 + 17 + 20]
├─ class/spellcaster attunement prerequisites                   [blocked: Features 27 + 31]
├─ held/worn/physical-use eligibility                           [blocked: Features 22–25]
├─ charge state and dawn recovery                               [blocked: clock/duration owner]
├─ consumable/instant HP or damage item effects                 [blocked: Features 9, 15–17]
├─ magic bonuses and passive properties                         [blocked: final AC/attack/save/sense readers]
├─ spells cast from items                                       [blocked: Features 18, 31–32]
└─ full source item behaviours                                  [blocked parent]
```

The catalog leaf is intentionally effect-free: an immutable statement that an item requires
attunement is not evidence that a creature is attuned, holding it, or entitled to use it.

## Dependency and ownership decisions

1. Magic identity is immutable catalog data. A proposed `dnd2024.magic-item-profile` attaches to
   a versioned magic-item content entity and describes only the source’s stable classification and
   declared interfaces. It is not an item instance, inventory entry, actor property, or script.
2. Physical possession remains one Feature 23 item instance linked by containment. Before an
   instance link is confirmed, profiles may not pretend to be objects that can be held, worn,
   consumed, attuned, or transferred.
3. A future `dnd2024.attunements` component belongs on the creature and records exact physical
   instance ids in canonical order. Missing means known no attunements only after a normal
   lifecycle/recorder exists; it cannot be used as an implicit default. It must not store item
   properties, distances, elapsed time, prerequisites, charges, or copied profile ids.
4. The three-item cap and same-copy prohibition are derived/validated from authoritative instance
   references and magic profiles on every begin operation. An attuned instance cannot be inferred
   merely from equipment state, custody, or a profile requiring attunement.
5. Item effects are typed, individual composition contracts, not arbitrary profile JavaScript or a
   generic “apply magic” endpoint. For example, Potion of Healing waits for Feature 16’s healing
   transition; spell items wait for Features 31–32; bonuses wait for the actual reader they modify.
6. Charges are current per-instance state. Their maximum/regain rule is immutable profile data;
   remaining charges, spending, and next-dawn recharge require a single lifecycle owner. They must
   never be copied onto a creature or reset during inventory transfer.
7. Curses, sentience, artifact destruction, and anatomy exceptions are deliberately deferred.
   They alter voluntary end, agency, physical fit, or world policy and cannot be smuggled into the
   basic attunement list.

## Confirmation boundary

| Decision | Required confirmation before implementation |
| --- | --- |
| Profile model | Exact profile component/procedure ids, fixed source shape, category/rarity/activation vocabularies, and immutable version policy. |
| Content/physical relation | Whether a magic profile shares a Feature 23 item-definition entity or references it, with exact source, mass, stack, and version rules. |
| Initial catalog | The first source items and page locators, especially whether Potion of Healing is the representative content fixture. |
| Attunement state | Actor component/reader/writer ids, instance reference validation, missing/empty semantics, canonical ordering, and stale-reference behaviour. |
| Short-Rest handoff | Feature 33 focused-rest, physical contact, interruption, voluntary-end, and transaction-composition contract. |
| Automatic endings | Feature 17 death, Feature 20 distance, 24-hour clock, prerequisite-loss, transfer, and another-creature ordering. |
| Effect families | A separate owner/typed input/result/effect route for healing, damage, AC/saves, conditions, movement, spells, charges, and durations. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Immutable magic-item profile catalog | **Verified 2026-08-21.** | Source-cited representative profiles read back deterministically with zero creature/gameplay effects. |
| 2 | Magic physical-definition and instance bridge | Slice 1 and Feature 23 revision accepted. | A physical instance resolves exactly one immutable magic profile without copied magic facts. |
| 3 | Attunement state and diagnostic reader | Slice 2 and reference semantics confirmed. | A closed list reports valid/missing/stale/over-capacity/duplicate diagnostics with zero grants. |
| 4 | Begin/voluntary-end through Short Rest | Slice 3 and Feature 33 focused-rest composition. | One eligible item attunes/ends atomically only at the correct completed rest boundary. |
| 5 | Automatic end and prerequisite families | Slice 4, Features 17/20/27/31, and clock lifecycle. | Death, distance/time, prerequisite loss, and competing attunement end in deterministic source order. |
| 6 | Charge ledger/recharge and consumable activity | Slices 2–5, inventory atomicity, clock, and exact effect family owner. | Each use spends once and either resolves or rolls back; recharge follows an owned time event. |
| 7 | One bounded item-effect family at a time | Its individual upstream owner is verified. | A named source item has its full effect without changing unrelated rules. |
| 8 | Source catalog expansion | All required family contracts are verified. | Each item adds only declarations and behaviours covered by existing accepted families. |

## Slice 1 — immutable magic-item profile catalog

### Runtime artifacts

- A confirmed `dnd2024.magic-item-profile` schema/component and governing static-definition
  procedure, or a reviewed extension of an existing content-definition contract if it is the
  unambiguous owner.
- Versioned source content identities for a small representative set, initially proposed as Potion
  of Healing, one non-attuned wearable, and one attunement-required wearable only after the exact
  physical-definition relation is confirmed.
- Focused catalog validation/tests. No item instance, actor component, player action, event,
  subscription, rest, charge, effect, or catalog import into a persistent game.

### Data contract and required state

The closed profile contains a stable item key/version, fixed source reference, magic category,
rarity, `requiresAttunement`, declared physical-use mode, declared activation family, consumable
flag, declared charge policy kind, and ordered effect-family keys. It contains no remaining
charges, selected bearer, attunement, item-instance id, custody/equipment state, AC/attack/save
bonus, spell id, dice, damage, healing amount, duration, target, command word, price, or code.

Rarity, category, use mode, activation family, and effect-family vocabulary are all closed. A
profile says only which later rule families it needs; it does not encode the family’s mechanics.
Every profile is attached to exactly one versioned content identity, with a matching key/version
and source locators into the SRD Magic Items sections. Missing or mismatched identity/source,
unknown vocabulary, duplicate/out-of-order keys, incompatible declaration, or extra field rejects.

### Recording behaviour, result, and effects

Catalog validation creates/reviews static source content only. The administrative read result
returns canonical identity, classification, and declared interface keys. It has no creature role,
randomness, player-facing intent phrase, or effects. Reading Potion of Healing must not heal;
reading an attunement-required profile must not create an attunement or count toward a limit.

### Invariants, failure behaviour, and non-goals

- A published profile is immutable; corrections make a reviewed successor identity/version rather
  than rewriting facts an instance may later reference.
- No profile duplicates Feature 23 physical facts or Feature 24/25 combat facts. If a source item
  needs a physical base, that relationship stays unimplemented until the confirmation boundary.
- No declared effect family is executable in this slice. In particular, no generic item effect,
  activity, spell cast, charge spend, Bonus/Magic Action, healing, damage, condition, or AC change
  is created.
- Rejection leaves existing catalog artifacts and every campaign/actor/item state byte-identical.

### Slice 1 implementation sequence

1. Re-read the source registry, Feature 23 item-definition/instance/equipment contracts, relevant
   Feature 16/31/32/33 contracts, and existing immutable content-definition convention. Repeat
   searches for attunement, magic items, charges, profile, item effect, and activation ownership.
2. Stop for confirmation of the permanent IDs, static physical relation, source fixtures, and
   closed vocabulary. Do not widen the ordinary item-definition schema by assumption.
3. Author the component/procedure/content entities and focused validation together. Verify each
   source entry’s exact locator and no copied rule prose.
4. Test canonical readback, duplicate/version/source/vocabulary rejection, effect-free reads, and
   no mutation of representative creature, item, campaign, or inventory state.
5. Query each artifact back; run `roleplay validate catalog`, focused tests, the full suite, and
   `git diff --check`; write a receipt and stop before Slice 2.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source profiles | Each confirmed representative has one active versioned identity, correct category/rarity/attunement declaration, and fixed SRD locator. |
| Attunement declaration | A required-attunement profile and Potion of Healing differ only in their source-defined metadata; neither produces an actor attunement. |
| Closed data | Missing/null/wrong-type/unknown/duplicate/out-of-order/extra field, mismatched content key/version, and wrong source reference reject unchanged. |
| Immutability | A second entity for an existing item key/version and an attempted in-place rewrite reject; a reviewed new version remains distinct. |
| Isolation | Readback returns zero effects and leaves custody, equipment, quantity, HP, AC, conditions, actions, spell resources, and campaign state byte-identical. |
| Determinism | Repeated reads of equivalent catalog data return byte-identical canonical results with no random call. |
| Repository | Catalog validation, focused tests, full suite, diff check, and source/catalog query-backs pass. |

### Slice 1 exit gate

Slice 1 is verified only when the confirmed representative profiles have one immutable catalog
owner, exact source-cited closed data, no runtime behaviour, evidence for all rejection/isolation
rows, catalog validation, repository checks, and a receipt. Stop before physical instances,
attunement state, or any effect.

## Later magic-item family map

```text
magic profile declaration
├─ physical ownership / held / worn ───────────> Feature 23, then Features 24–25
├─ attunement start/end / cap / copy rule ──────> Feature 33 + item/actor reference lifecycle
├─ death, distance, lost prerequisites ────────> Features 17, 20, 27, 31 + clock
├─ Potion of Healing / instant recovery ───────> Feature 16 healing transition
├─ magic weapon/armor/Shield bonuses ──────────> Features 6, 8–9, 24–25 derived readers
├─ resistance / condition / vision / movement ─> Features 13, 15, 20, 34
├─ charge spend / next dawn ───────────────────> instance charge owner + clock lifecycle
├─ spell from item / Concentration ────────────> Features 18, 31–32
└─ curse / sentience / crafting / artifact ────> excluded future owners
```

## Plan-quality audit

- One capability, concrete official source, explicit scope/non-goals, ownership search, and
  recursive dependency graph: yes.
- Immutable definitions, physical instances, attunement state, derived eligibility, transient use,
  and downstream mechanics have separate named owners: yes.
- Slice 1 is an independently valid, effect-free leaf with closed data, immutability, source,
  failure, readback, isolation, and repository gates: yes.
- No runtime game artifact was created by this planning pass: yes.

## Plan-change rule

Revise before implementation if Feature 23 selects a different item-definition reference model,
Feature 33 cannot supply an interruption-aware focused-rest handoff, a compatible magic profile
owner exists, or an item requires a new effect family. Do not make a generic item script, copy
magic data into inventory instances/creatures, treat possession as attunement, reset charges on
transfer, infer a missing attunement list as a legal state, or implement a magic bonus outside the
reader/state owner it changes.
