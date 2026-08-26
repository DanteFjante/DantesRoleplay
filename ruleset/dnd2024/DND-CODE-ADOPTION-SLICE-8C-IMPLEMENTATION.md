# D&D code-adoption Slice 8C implementation — Conditions and shared state effects

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8C  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Rules Glossary > Conditions` and each named Condition,
including `Rules Glossary > Exhaustion` (PDF pp. 178–188; Exhaustion at PDF p. 180)  
Outcome: Recover canonical creature Condition state, its administrative writer, and the one
effect-free condition-to-D20/resource derivation owner.  
Exclusions: Event subscriptions/guards, death-state mutation, duration/expiry, damage or healing,
turn-budget spending, movement execution, consumer integration, fixtures, migrations, public
operations, and archive deletion.  
Allowed areas: D&D Condition catalog artifacts, activated D&D tests, this plan, Parent 8 evidence,
and one 8C receipt.  
Stop point: condition transitions and effect-free derivation pass standalone acceptance.

## Decisions and ownership

- Reuse the classified IDs `dnd2024.conditions`, `mechanic.dnd2024.conditions.write`,
  `mechanic.dnd2024.d20-test.state-effects`, `procedure.mechanic.dnd2024.conditions`, and
  `procedure.mechanic.dnd2024.d20-test.state-effects`.
- Store explicit condition instances only. Implied conditions, D20 circumstances, automatic save
  failures, Exhaustion modifiers, and resource prohibitions are derived by the shared reader and are
  never persisted.
- Retain optional existing-entity source identity for independently clearable instances. Caller
  input never supplies a source ID.
- Level-six Exhaustion is stored and reported as lethal in writer data. The archived event output is
  adapted to no direct application event because the accepted matrix contains no such event ID and
  direct application event output is unsupported. This preserves the authoritative lethal state and
  result fact without creating a duplicate/deferred event owner; later death handling must consume
  canonical Conditions through its own accepted boundary.

## Rule and Foundry review

SRD 5.2.1 defines fifteen Conditions, says a Condition does not stack with itself unless its own
text says otherwise, and defines the individual implications. Exhaustion is cumulative to level 6,
reduces D20 Tests by twice the level, reduces Speed by five feet per level, and is lethal at level 6.

Pinned Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected as
engineering reference only. `module/config.mjs` models named statuses, implied Incapacitated
statuses, movement prohibitions, Poisoned check/attack disadvantage, and Initiative disadvantage;
actor attributes store Exhaustion separately and derive its effects. The recovered catalog keeps
one component owner and one pure derivation mechanic rather than importing Foundry ActiveEffects,
settings, globals, assets, or runtime code.

## State, input, and transitions

The component contains exactly `entries` and fixed `sourceRef`. Entries are canonically sorted,
unique by `(condition, sourceEntityId)`, capped at 100, and use fourteen non-Exhaustion IDs plus one
source-free Exhaustion entry with level 1–6. Petrified and Poisoned cannot coexist.

The writer supports closed modes: `record` creates known-empty state; `apply`/`clear` accept unique
non-Exhaustion IDs and optional source role; `exhaust`/`recover` accept integer levels 1–6 without a
source. Charmed, Frightened, and Grappled require a non-self source. Petrified removes Poisoned
atomically. Every successful transition proposes exactly one complete component add/set.

The state-effects reader accepts exactly `{}`. Missing state yields `conditionsKnown:false`; present
malformed/invalid state fails. Valid state produces stable effective/implied Conditions,
source identities, D20 branches, Exhaustion modifier, and resource-unique prohibitions with no
effects/events/notifications and no randomness.

## Acceptance

Acceptance covers record/apply/source-specific clear, canonical ordering/uniqueness,
Petrified–Poisoned behavior, Exhaustion gain/recovery/level-six fact, corrupt/no-change failures,
absent versus known-empty derivation, implied Conditions, every derived branch, exact prohibition
ordering, deterministic output, action replay, catalog activation, and existing D&D compatibility.
