# D&D 2024 CC3D1A implementation — Alert Initiative Proficiency

Status: **accepted**
Evidence: [CC3D1A receipt](evidence/DND2024-CHARACTER-CREATION-CC3D1A-RECEIPT.md)
Feature/slice: **D&D 2024 character creation / CC3D1A**
Recommended model: **`gpt-5.6-sol` xhigh**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3D1A](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; *Feats > Origin Feats > Alert*
(PDF page 87) and *Character Advancement > Proficiency Bonus*.
Outcome: an actor with one valid Alert identity grant can explicitly use Initiative Proficiency and
add exactly its current Proficiency Bonus to an otherwise unchanged Initiative roll.
Exclusions: Alert Initiative Swap, ally willingness, same-combat proof, Incapacitated gating,
post-roll reactions/windows, persistent initiative counts, generic feature-expression schemas, and
all other Origin-feat behavior.
Allowed files/areas: the existing Initiative mechanic/contract/procedure, basic-creator pending
evidence and its contract/procedure, D&D acceptance-test harness, this dependency plan/roadmap/status
line, and this slice's evidence.
Stop point: the optional Alert bonus is source-bound, level-derived, composable through the existing
encounter root, fail-closed, and no longer mislabeled wholly unimplemented; swap remains explicit.

## Confirmed boundary and ownership

The user's standing approval for D&D-2024-aligned optional mechanics and the 2026-08-27 instruction
to continue only `gpt-5.6-sol` xhigh/max slices confirms this existing-public-surface extension.
No new permanent ID, component schema, migration, endpoint, MCP kind, C# rule, transaction owner, or
stored initiative state is introduced.

`dnd2024.character-feature-grants` remains the actor-side entitlement identity owner.
`dnd2024.character-level` remains the level owner from which Proficiency Bonus is derived.
`mechanic.dnd2024.initiative.roll` remains the only individual Initiative calculation owner.
The encounter Initiative root continues to compose that effect-free child without duplicating the
feat rule.

## Source behavior and closed input

Alert's Initiative Proficiency says the holder can add its Proficiency Bonus when it rolls
Initiative. Because this is optional, the existing closed Initiative input gains only
`useAlertInitiativeProficiency`, a Boolean. Omission/false preserves the prior result; true requires
exactly one valid non-repeatable Alert Origin-feat grant and exact current character-level state.
The caller cannot supply a bonus, level, feature ID, grantor, modifier, source, or final count.

The mechanic validates the complete optional feature-grant envelope before trusting Alert. An Alert
definition under the wrong grant kind/configuration or more than once fails closed. Other valid
grants remain behavior-neutral. A subject without feature-grant state retains the legacy Initiative
path and cannot opt into Alert.

## Derived result and composition

Proficiency Bonus is `2 + floor((level - 1) / 4)`, producing +2/+3/+4/+5/+6 across the five level
bands. The result reports availability, use, exact bonus, and Alert source provenance and appends
one explicit modifier only when used. Dexterity, Advantage/Disadvantage, seeded rolls, rest
interruption planning, encounter tie handling, effect ownership, and transaction behavior remain
unchanged.

The basic creator continues to grant Alert identity to Criminal characters. Its pending ledger
changes only that grant from the inaccurate whole-feature `behavior` entry to
`behavior:initiative-swap`; the implemented Initiative Proficiency is no longer pending. Other
Origin feats and every class feature retain their existing behavior denial.

## Failure and compatibility contract

Unknown/extra input, non-Boolean use, use without Alert, malformed/duplicate/misconfigured Alert,
or missing/corrupt level on an Alert holder fails without effects. Existing creatures with only
abilities, existing non-Alert created actors, explicit false, and omitted input preserve their
seeded counts byte-for-byte apart from additive result evidence. Encounter composition accepts the
new child evidence but remains the sole order/rest-effect owner.

## Implementation sequence

1. Extend Initiative's optional input and strict grant/level derivation in catalog JavaScript.
2. Update the Initiative and basic-creation contracts and narrow only Alert's pending key.
3. Add level-band, opt-in/omission, created-Criminal, denial/corruption, encounter-composition, and
   pending-ledger tests.
4. Run focused Initiative/creation tests, complete D&D tests, disposable catalog validation, full
   solution, protocol walk, independent review, and diff hygiene.
5. Write one receipt and update CC3D1A status once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Level bands | Alert opt-in adds exactly +2/+3/+4/+5/+6 at levels 1/5/9/13/17 |
| Optionality | Alert omission/false and non-Alert omission preserve the prior seeded result |
| Creation | Criminal grants Alert identity and leaves only Initiative Swap pending |
| Composition | encounter order consumes the Alert-adjusted child and retains rest behavior |
| Denial | no grant, wrong kind/configuration, duplicates, corrupt grant state, or bad level fails effect-free |
| Ownership | no C# rule, stored Initiative count, parallel PB field, or caller-authored modifier |
| Surface | catalog validation and protocol walk discover the extended dependency/input contract |

## Completion receipt and exit gate

Acceptance requires a CC3D1A receipt containing delivered scope and exact command results. This
slice stops without implementing Initiative Swap or claiming Alert fully complete.
