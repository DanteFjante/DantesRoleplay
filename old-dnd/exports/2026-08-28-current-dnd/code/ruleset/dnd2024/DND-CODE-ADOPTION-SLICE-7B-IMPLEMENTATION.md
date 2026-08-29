# D&D code-adoption Slice 7B implementation — Initiative and turn flow

Status: **accepted 2026-08-26 — Sol runtime review approved**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7 / 7B
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; `Playing the Game > Combat > The Order of Combat > Initiative` and `Playing the Game > Combat > The Order of Combat`

## Delivered boundary

The encounter owns one immutable Initiative-order component and one lifecycle component. Its containment roster is authoritative: generic host composition fans out a declared, effect-free Initiative child for every contained participant, then the parent validates and persists the resolved order. Start, advance, and end mechanics derive the active participant from that order; they never duplicate it in lifecycle state.

The generic application evaluator now executes declared child mechanics with bounded depth and fan-out, stable derived seeds, closed role/input bindings, cycle checks, and a complete transitive component mapping. It rejects direct-action children that propose effects, events, or notifications so no child proposal can be silently dropped.

## Closed behavior

- Initiative is Dexterity plus an explicit 7A3-style d20 circumstance result.
- A caller supplies exactly one empty child input per contained participant and explicit ordering for each tied count.
- The parent atomically adds the order; start atomically adds lifecycle state; advance/end replace lifecycle state only.
- Roster/order mismatch, duplicate/missing participants, absent/ended lifecycle state, bad ties, cycles, undeclared child data, and bounds failures produce no partial write.

## Evidence and exclusions

The fresh-host acceptance in [Slice 7D](DND-CODE-ADOPTION-SLICE-7D-IMPLEMENTATION.md) covers fan-out, order persistence, start, advance, wrap to a new round, and end. No surprise, condition, reaction, delayed action, monster-specific initiative feature, or encounter persistence beyond the two declared components is included.

