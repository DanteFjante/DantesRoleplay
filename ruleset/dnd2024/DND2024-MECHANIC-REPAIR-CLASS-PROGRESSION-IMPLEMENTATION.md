# DND2024 mechanic repair CP1 — canonical class progression read

Status: **implemented; focused acceptance passed, parent acceptance pending**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md), character creation, progression, and rest
Ruleset alignment: **dnd2024-compatible**
Outcome: adapt `mechanic.dnd2024.class-progression.read` to the canonical class definition and its
referenced progression entity without recreating retired progression state.
Exclusions: character creation, applying grants, advancement transactions, feature behavior, Hit
Point changes, class membership changes, schema changes, and live data.
Allowed areas: this document and repair tree; the class-progression mechanic, contract, procedure;
focused current-schema tests.
Stop point: the mechanic resolves `dnd2024.advancement.class.progressionRef`, reads the exact level
from `dnd2024.advancement.progression`, and no retired component ID remains in its active closure.

## Confirmed boundary

- A class definition owns `dnd2024.advancement.class`, including `hitDieRef` and `progressionRef`.
- The referenced progression definition owns a level-keyed `dnd2024.advancement.progression` map.
- A level entry may declare `grantRefs`; an empty entry is a valid supported level.
- The old fixed Hit Point gain and source-matching fields have no canonical owner and are not
  reconstructed in the mechanic result.
- Reading entitlements does not apply them or assert that referenced feature behavior exists.

## Behavior and failures

The mechanic accepts exactly one class level from 1 through 20, validates the class component,
requires the declared progression reference to resolve to the matching entity, and validates the
canonical progression map. It reports the class, progression, Hit Die reference, requested level,
support status, and canonical grant references.

Malformed class or progression state, an unavailable/mismatched progression reference, malformed
grant references, and invalid input fail before output. A valid progression without the requested
level returns `unsupported-level`. The mechanic remains read-only.

## Acceptance

- supported and unsupported levels using current canonical payloads;
- exact progression reference and grant preservation;
- missing/mismatched reference and malformed payload failures;
- JavaScript body compilation, contract-owner audit, focused tests, and `git diff --check`.

Five focused current-schema tests pass across this repair and the previously repaired burden family;
the class-progression body compiles and its active contract contains only registered components.
