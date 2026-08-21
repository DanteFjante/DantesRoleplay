# Feature 39 dependency plan — Heroic Inspiration

Status: **Slice 1 is implemented and accepted in scope; source grants and reroll composition remain blocked.**  
Last updated: 2026-08-21

## Execution rule

This is a repository planning artifact. It creates no catalog/runtime content, fixture, live-game
state, or MCP surface. An implementation pass must re-read `AGENTS.md`,
`procedure.system.create-feature`, `procedure.system.modify`, the relevant state/action contracts,
and the current Feature 3, 4, 5, 8, and 9 artifacts. It must re-run the owner search immediately
before proposing permanent vocabulary, implement only one accepted slice, validate a disposable
catalog, write a receipt, and stop.

Feature 39 owns the fact that a player character currently holds the one allowed instance of
Heroic Inspiration and the eventual authorised consumption of that fact. It does not own a
character's species selection, a Long Rest, an Origin Feat grant, a particular D20 Test's rule,
or any other feature's consequence.

## Target capability

A player character can hold at most one Heroic Inspiration instance through a source-backed,
auditable state owner; later approved rule owners can grant, transfer, or consume that exact
instance without inventing a second resource or accepting a caller-supplied reroll result.

### Included

- One presence/absence state representing whether an already identified player character currently
  has Heroic Inspiration.
- A guarded, normal grant path for a character with no existing instance, suitable for later GM,
  Human Resourceful, or other source-specific owners to compose.
- Later source-owned Human Resourceful Long-Rest grant/overflow handling and a single
  state-consuming reroll composition protocol.
- Readback and rejection evidence that distinguishes no instance from one available instance.

### Excluded

- Species selection, Human trait selection, character creation, campaign attachment, player
  authentication, party membership, and player-facing UI or MCP verbs.
- A Long Rest action, rest episode, clock advancement, or Resourceful trigger; Feature 33 owns
  rest timing and Feature 26/CH3/CH5 own species/origin composition.
- Alert, Savage Attacker, Lucky, Indomitable, damage rerolls, advantage/disadvantage, initiative
  swaps, feature grants, or an arbitrary "reroll" button.
- A copy of a die, result, total, modifier, D20 circumstance, roll history, source prose, source
  choice, rest episode, encounter, or resource pool on the actor.

## Official source basis

The source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the Coast
LLC, 2025-05-01, CC-BY-4.0):

- **Rules Glossary > Heroic Inspiration** (PDF p. 182): a player character that has Heroic
  Inspiration may expend it immediately after rolling any die to reroll that die and must use the
  new result.
- **Playing the Game > D20 Tests > Advantage and Disadvantage > Heroic Inspiration** (PDF p. 11):
  a character can never hold more than one; an attempted grant while it already has one may instead
  give it to a player character in the group that lacks it. With Advantage or Disadvantage, only
  one die may be rerolled.
- **Character Origins > Character Species > Human** (PDF p. 86): Resourceful grants Heroic
  Inspiration when the Human finishes a Long Rest.

The first slice records only the present/absent fact. It does not assert a particular grant source
or apply the Long-Rest rule. The exact source-specific authority is supplied by the future calling
owner, rather than copied into Heroic Inspiration state.

## Verified existing dependencies

| Dependency | Evidence and decision |
| --- | --- |
| Player-character marker | CH1's accepted `dnd2024.character.profile` marks an existing campaign-attached actor as a player character. It is the eligibility predicate; Feature 39 never writes profile or campaign state. |
| Character scope | Campaign C15 and CH1 retain participation/scope authority. Slice 1 only requires the existing profile marker; later trusted roots must additionally enforce their own scope rules. |
| D20 convention | Feature 3 supplies Advantage/Disadvantage for ability checks and a shared result convention. Its own plan and current procedure explicitly exclude rerolls. |
| Existing consumers | Ability checks, saving throws, Initiative, weapon attacks, weapon damage, and generic dice each own their own random calls and result arithmetic. They currently expose no common safe reroll receipt/child boundary. |
| One-turn timing | Feature 11 provides encounter turns, but Heroic Inspiration has no once-per-turn restriction. Turn state is therefore not a prerequisite for the state slice. |
| Long Rests | Feature 33 is planned; its first static-policy slice is the next rest implementation candidate. It has no active-rest completion or resource-recharge dispatch yet. |
| Species state | Feature 26 has a static Human profile and selected-species seam, but explicitly defers Heroic Inspiration and trait consequences. |
| Search result | Searches for `heroic inspiration`, `inspiration`, `reroll`, and `reroll any die` found no existing state component, recorder, consumer, procedure, or test owner. Existing references only reserve/defer the rule. |

## Recursive dependency analysis

```text
Heroic Inspiration: one source-backed held instance and lawful use          [blocked parent]
├─ player-character profile marker                                           [implemented: CH1]
├─ one-instance presence state plus guarded grant recorder                  [missing Slice 1 leaf]
├─ source-specific grant / overflow selection                               [blocked parent]
│  ├─ Human Resourceful completed-Long-Rest evidence                        [blocked: Feature 33]
│  ├─ selected Human/origin composition                                     [blocked: Feature 26 + CH3/CH5]
│  └─ eligible recipient/group selection for existing-holder overflow       [blocked: party/choice owner]
├─ generic authorised reroll composition                                    [blocked parent]
│  ├─ held-instance consumption transition                                  [blocked after Slice 1]
│  ├─ common pre-result/post-roll replacement protocol                      [missing platform/ruleset decision]
│  ├─ ability checks, saves, Initiative, weapon attacks, damage, dice       [existing independent consumers]
│  └─ result/transaction semantics for "must use" replacement               [blocked after protocol]
└─ CH0 Human Soldier Fighter source-complete behaviour                      [blocked parent]
```

The only lowest independent leaf is the one-instance state and guarded grant recorder. It is
useful on its own: it gives all future grants and consumers one authoritative representation while
creating no invented roll behavior. It does not make a new character playable through Heroic
Inspiration until the later parents are accepted.

## Dependency and ownership decisions

1. **Heroic Inspiration is presence state, not a count.** The actor either has one instance or
   does not. The component is present only while held and has an empty closed data object. Absence
   means the character has none; an explicit `false`, numeric count, balance, expiry, grant log,
   or duplicate resource would create an unnecessary second source of truth.
2. **CH1 owns eligibility.** Slice 1 requires `dnd2024.character.profile` to be present and valid.
   It does not infer player-character status from an entity name, encounter membership, campaign
   containment, species, or an authentication claim.
3. **The generic state owner does not decide why a grant is allowed.** A later source owner proves
   Human Resourceful, a GM award, or another source before invoking the guarded grant transition.
   No source ID, trait key, rest evidence, or provenance text belongs in the presence component.
4. **Consumption belongs to the feature but only via a ratified composition protocol.** A die's
   face count, generation, original result, replacement result, and rule-specific recomputation
   remain with its existing resolver. Feature 39 may remove the held instance only when a future
   composed parent proves an immediate eligible reroll and commits the replacement result.
5. **Overflow is not silently discarded or freely targeted.** The official rule permits giving an
   attempted duplicate grant to another player character in the group who lacks it. Recipient
   discovery and choice require a later party/authorisation owner; Slice 1 rejects an ordinary
   duplicate grant unchanged rather than guessing a recipient.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | One-instance presence state and guarded grant recorder | Permanent vocabulary and CH1 eligibility predicate confirmed | One valid player-character actor can receive exactly one empty presence component; duplicate/ineligible input fails unchanged. |
| 2 | Source-specific grant and duplicate-grant policy | Slice 1, selected-source owner, and recipient/party decision | A named source can grant once; an already-held grant is either correctly transferred through its owner or rejected with a named recovery. |
| 3 | Reroll composition protocol | Slice 1 and a confirmed cross-mechanic pre-result/post-roll protocol | One held instance is consumed atomically only when a validated reroll replaces one authorised die and the replacement must be used. |
| 4 | Consumer integration | Slice 3 plus each consumer's reviewed composition boundary | Each approved check/save/Initiative/attack/damage/dice owner exposes exactly the authorised Heroic-Inspiration path without copied roll logic. |
| 5 | Human Resourceful integration | Slices 1–4, Feature 33 completion, Feature 26/CH3/CH5 origin evidence | A completed Long Rest for a selected Human invokes the source-specific grant path once, including approved overflow behaviour. |

## Slice 1 — one-instance presence state and guarded grant recorder

### Runtime artifacts

After permanent-ID confirmation, add exactly:

- `dnd2024.heroic-inspiration`: a new actor component/schema whose only valid value is `{}` and
  whose presence means the character holds the single available instance.
- `procedure.mechanic.dnd2024.heroic-inspiration`: a new D&D 2024 state contract governing only
  the state and normal grant recorder in this slice.
- `mechanic.dnd2024.heroic-inspiration.grant`: a new internal, effect-producing recorder. It is
  not a public character-creation root or a generic player command.
- Focused catalog tests and a receipt.

The implementation must stop if the pre-write owner search finds a compatible existing resource
or character-state owner. It must revise that owner rather than create parallel state.

### Governing contracts and source locator

Re-read `procedure.system.create-feature`, `procedure.system.modify`, the CH1 character-profile
procedure, the action/effect contract, and one accepted presence-style writer/remover contract
immediately before authoring. Use the exact SRD locators above. Do not add a rest, species, feat,
or source-definition procedure in this slice.

### Data/input contract and required state

- The component schema is a closed empty object. It has no properties, including `available`,
  `count`, `sourceRef`, `source`, `recipient`, `roll`, `die`, `usedAt`, `expiry`, or `history`.
- The grant recorder has one `subject` role. Its closed input is exactly `{}`; it accepts no
  actor ID in input, mode, quantity, source, recipient, result, override, or derived value.
- `subject` must have a valid `dnd2024.character.profile` object according to CH1's accepted
  schema. A missing, corrupt, or non-profile actor fails before effects.
- A missing Heroic Inspiration component means no current instance and is the sole valid grant
  precondition. A present malformed component fails; an already valid component is a duplicate
  grant failure. The recorder never corrects, clears, transfers, or consumes state.

### Recording behavior, result, and effects

After all validation, return exactly one `component.add` effect for
`dnd2024.heroic-inspiration` with the canonical empty object. Return structured data identifying
the subject, `heldBefore: false`, `heldAfter: true`, the fixed SRD source locator, and one proposed
effect. It uses no randomness and does not inspect a rest, species, feat, campaign, encounter,
party, roll, item, class, or resource.

### Invariants, failure behavior, and non-goals

- At most one instance can exist because the component is a presence marker and duplicate grants
  fail with zero effects and byte-identical state.
- The recorder is deliberately narrower than a general correction/admin tool. CH7 or a future
  explicitly governed correction policy owns recovery from corrupt or mistaken state.
- It grants no benefit in a D20 Test yet. It must not modify or replay a mechanic, remove a token,
  select a recipient, record a source-specific trait/feat grant, or call a rest action.
- The result is deterministic. Equivalent valid inputs produce the same state transition proposal;
  rejected calls make no random calls and propose no effects.

### Slice 1 implementation sequence

1. Re-run proposed-ID and synonym searches across catalog, code, tests, and the imported catalog;
   inspect the current CH1 profile and a comparable presence/removal mechanic.
2. Stop for confirmation of the three permanent IDs, empty-object presence semantics, strict
   profile eligibility, normal grant-only input, and source-locator convention.
3. Add the procedure, schema/component registration, mechanic metadata/source, and focused tests
   as one coherent slice. Do not add any Human, feat, rest, creation, or roll artifact.
4. Exercise a disposable valid profiled actor, then read back the exact empty component; compare
   before/after state for each rejection. Remove only disposable fixtures through the established
   safe cleanup path.
5. Run the focused tests, `roleplay validate catalog`, the full suite once, and `git diff --check`.
   Write a receipt with concise test evidence and stop. Do not begin Slice 2 or a reroll consumer.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Valid grant | A valid CH1-profiled disposable actor with no component returns one `component.add` proposal and reads back exactly `{}`. |
| One-instance rule | A second grant to the same actor fails before effects; all component bytes and revisions remain unchanged. |
| Eligibility | Missing, malformed, null, or extra-field character profile state rejects before effects. A generic creature is not treated as a player character. |
| State integrity | A malformed present Heroic Inspiration component rejects; the recorder never overwrites, repairs, clears, or duplicates it. |
| Closed input | Null, non-object, unknown field, actor ID, source/trait/feat/rest field, count, recipient, die, roll, outcome, or mode fails with zero effects. |
| Isolation | Valid and invalid cases leave abilities, level, proficiencies, conditions, HP, inventory, campaign attachment, encounter/turn state, and every die-result owner unchanged. |
| Determinism/routing | Equivalent calls are byte-stable, use no random calls, and only clear Heroic-Inspiration wording selects this mechanism; ordinary dice and D20 intents stay with their current owners. |
| Cleanup/repository | Temporary fixture state is removed by the approved cleanup path; focused tests, catalog validation, full-suite result, and diff check are recorded. |

### Slice 1 exit gate

Slice 1 is accepted only when the profile-gated, empty presence component and its grant-only
recorder pass every matrix group, the disposable catalog validates, focused and full-suite evidence
is recorded, no persistent database import occurs, and a receipt states that rerolls, transfer,
Human Resourceful, rest recovery, and character creation remain unimplemented. **Implemented and
accepted; see `FEATURE-39-SLICE-1-RECEIPT.md`.** Stop for review.

## Plan-quality audit

- One player-facing capability and explicit non-goals: yes.
- Official source/version/locators: yes; local SRD extract and official D&D Beyond SRD pages agree.
- Existing ownership/overlap search: yes; all results reserve rather than implement Heroic
  Inspiration or rerolls.
- Every missing dependency expanded: yes. The resource state is a standalone leaf; source grants,
  reroll composition, and consumer integration remain named blocked parents.
- State/derived/transient ownership: yes. Only held-instance presence is persistent; roll facts
  remain consumer-owned and no grant provenance is copied.
- Exactly one lowest slice: yes, profile-gated presence plus normal grant recorder.
- Closed input, malformed/duplicate-state behaviour, effects, determinism, cleanup, and
  repository checks: specified in Slice 1.
- Planning stops before implementation: yes.

## Plan-change rule

Revise this plan before implementation if a compatible resource/presence owner appears, CH1's
profile marker changes meaning, a party/recipient owner is accepted, or the action-composition
runtime reveals a safe generic reroll protocol. Do not bypass any missing owner with a numeric
counter, a caller-supplied replacement die/result, an embedded Human/rest/feat flag, a copied D20
resolver, or a fourth MCP verb.
