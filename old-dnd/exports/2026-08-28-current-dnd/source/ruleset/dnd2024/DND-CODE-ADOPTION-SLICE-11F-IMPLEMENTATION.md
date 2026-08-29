# D&D code-adoption Slice 11F implementation — Temporary HP state and bounded healing

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), Temporary HP/healing 11F  
Ruleset alignment: `dnd2024-owned`  
Source ID and locators: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing > Healing`
(PDF p. 17) and `Temporary Hit Points` (PDF p. 18)  
Outcome: activate the closed positive Temporary HP owner/writer and a bounded, effect-only healing
transition against current Hit Points.  
Exclusions: damage absorption, Long Rest expiry, dying/death/condition consequences, healing sources,
events, notifications, migrations, public operations, and production C#.  
Allowed files/areas: this document; one Temporary HP component/schema; its writer contract/script and
procedure; one healing contract/script/procedure; focused `Dnd2024AbilityCheckTests`; Parent 11 status
and 11F receipt.  
Stop point: independent grant/keep/replace/expire and capped healing transitions; no weapon change.

## Authoritative state and inputs

`dnd2024.temporary-hit-points` is closed state with exactly a positive safe-integer `amount` and the
fixed Temporary Hit Points source reference. Component absence is the sole zero representation.
The writer accepts exactly first grant `{mode,amount}`, existing grant `{mode,amount,onExisting}`, or
expiry `{mode:"expire"}`. It never inspects HP.

Healing requires current `dnd2024.hit-points` and exactly `{amount:<positive safe integer>}`. It
derives the bounded result. At maximum it returns complete audit data and no effect, avoiding an
identical-value revision. Otherwise it proposes one complete `component.set`, preserving maximum
and source reference. It never projects or changes Temporary HP.

## Effects and failures

- first Temporary HP grant: one `component.add`;
- explicit keep: no effect and original bytes remain;
- explicit replacement: one `component.set`;
- expiry: one `component.remove`;
- positive applied healing: one HP `component.set`;
- healing lost entirely to the maximum: no effect.

Malformed/invalid stored state, extra input, non-safe or non-positive amounts, missing HP, absent
expiry, and ambiguous existing-buffer grants fail before effects. Outputs contain no event or
notification because the current direct application action owner does not support them.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| First grant | positive component added with exact source |
| Existing choice | keep is no-change; replacement accepts either higher or lower incoming amount |
| Expiry | present component removed; absent expiry rejected |
| Closed boundaries | zero, unsafe, extra, ambiguous, corrupt state rejected unchanged |
| Healing | positive amount raises current only to maximum and reports discarded excess |
| Healing at maximum | zero effect/revision and complete result data |
| Separation | healing leaves Temporary HP unchanged; grants leave HP unchanged |
| Replay | identical action commits at most once |
| Activation/regression | fresh source activation, D&D suite, catalog validation, build, full suite |

## Verification commands

- `node --check` for both new scripts;
- focused tests filtered to `Temporary_hit_points` and `Healing`;
- full `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog`;
- `dotnet build DantesRoleplay.slnx --no-restore`;
- `dotnet test DantesRoleplay.slnx --no-build`; and
- Slice 11-scoped `git diff --check`.

No MCP protocol walk is required because no MCP surface or dependency registration changes.
