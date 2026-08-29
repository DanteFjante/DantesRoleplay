# D&D code-adoption Slice 7D implementation — fresh-host encounter acceptance

Status: **accepted 2026-08-26 — Sol runtime review approved**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7 / 7D
Ruleset alignment: **dnd2024-owned**

## Acceptance boundary

`Dnd2024AbilityCheckTests` creates a new SQLite fixture, previews and activates the authored D&D source, registers the exact component schemas, and uses the ordinary application evaluator/action runner. It imports no donor campaign state and does not use a live database.

The acceptance exercises prior 7A ability/skill/save seams plus this cohort's closed combat writers, attack and damage result mechanics, composed HP application, containment-driven Initiative fan-out, order persistence, turn start, advance, round wrap, and end. The composition test obtains child inputs from the same deterministic derived-seed convention used by the host, including the explicit tie-decision shape when needed.

## Required evidence

- focused activated-host test suite passes;
- fresh catalog validation succeeds without touching live data;
- generic child execution has depth, count, cycle, exact-catalog, closed-input, and pure-child safety checks;
- broader failures are recorded rather than attributed to this D&D boundary.

The receipt is [Slice 7B–7D evidence](adoption/evidence/DND-CODE-ADOPTION-SLICE-7B-7D-RECEIPT.md).
