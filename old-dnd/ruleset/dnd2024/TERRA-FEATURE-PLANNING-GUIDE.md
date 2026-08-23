# D&D 5e 2024 feature-planning adapter

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Active ruleset-specific instructions**

## Use with the repository guides

This file adds D&D 5e 2024 requirements to the shared LLM workflows. It does not repeat them.

Read:

1. `AGENTS.md`;
2. `docs/IMPLEMENTATION_DOCUMENT_READING.md`;
3. `docs/DEPENDENCY_TREE_AUTHORING.md` when planning dependencies;
4. `docs/FEATURE_IMPLEMENTATION_AUTHORING.md` when preparing a slice; and
5. the one relevant row in `ruleset/dnd2024/ROADMAP.md`.

`procedure.system.create-feature` remains the governing runtime planning contract. A planning pass
creates no permanent ID, schema, catalog record, migration, public kind, fixture, or implementation
code.

## Canonical ruleset identity

- Product/ruleset: D&D 5e, 2024 revision.
- Repository scope: `dnd2024`.
- Category prefix: `ruleset.dnd2024.*`.
- Registered source: `source.dnd2024.srd-5.2.1`.
- Catalog IDs: `dnd2024.*` for ruleset-owned definitions/mechanics and
  `mechanic.dnd2024.*` / `procedure.mechanic.dnd2024.*` for executable/instruction owners.

Every `dnd2024-owned` dependency tree and implementation document cites the source ID plus an exact
SRD 5.2.1 section/locator. Do not substitute model memory, 2014 rule text, unofficial summaries, or
another edition.

## Alignment rules

1. **Use 2024 mechanics and terminology.** If 2014 differs, the 2024 SRD owner wins.
2. **Label deviations.** Compatibility behavior, optional rules, and house rules are separate
   confirmed decisions; never blend them into the core 2024 contract.
3. **Reuse existing owners.** Abilities, Proficiency Bonus, D20 Tests, Advantage/Disadvantage,
   action economy, conditions, damage, movement, equipment, rests, advancement, species,
   backgrounds, feats, classes, and spells are composed rather than copied.
4. **Derive rule values.** Callers do not provide authoritative modifiers, DCs, eligibility,
   resource costs, damage classifications, movement costs, or outcomes that existing state/source
   owners can resolve.
5. **Separate content, state, and behavior.** Immutable source content belongs in catalog profiles;
   actor/world state belongs in components; calculations/transitions belong in catalog JavaScript.
6. **Keep C# generic.** C# may materialize declared context, sandbox scripts, validate/apply generic
   typed effects, own transactions/audit/retrieval, and coordinate declared children. It may not
   contain D&D IDs, formulas, choice patterns, eligibility, timing, or outcome branches.
7. **Preserve provenance.** Source identity/version and content identity remain inspectable through
   creation, selection, grants, and later reads.
8. **Test interactions, not isolated prose.** A feature that touches existing D&D owners proves
   compatibility with them and no-change behavior when required state is absent or invalid.

## Ruleset readiness checklist

A D&D feature leaf is ready only if the plan answers:

- What exact 2024 player/GM capability is added?
- Which SRD 5.2.1 locator supplies each rule meaning?
- Which existing catalog owners supply all prerequisite state and calculations?
- Which values are derived and therefore forbidden as caller input?
- Which immutable content, authoritative state, JavaScript mechanics, and generic engine seams are
  used or revised?
- How do action/resource timing, conditions, damage, movement, equipment, rest, or advancement
  owners interact with this feature, if relevant?
- What differs from 2014, optional rules, or house rules, and is that difference confirmed?
- What are the typed result/effects, transaction owner, replay/rollback behavior, and negative cases?
- Can the feature be accepted in a fresh catalog/database without conversation context?

If any answer is missing, descend the dependency tree or stop for confirmation.

## Ruleset feature document header

```markdown
Feature: <number and capability>
Slice: <one lowest ready leaf>
Status: <draft | awaiting confirmation | active | blocked | accepted>
Ruleset alignment: dnd2024-owned
Ruleset scope: dnd2024
Source: source.dnd2024.srd-5.2.1 — <exact locator>
Roadmap: ruleset/dnd2024/ROADMAP.md#<row>
Dependency tree: <path and leaf>
```

Use `dnd2024-compatible` or `ruleset-neutral` only when the feature is not defining a D&D rule; it
must still avoid contradicting or duplicating the 2024 owners it consumes.

## Completion

Implement one confirmed lowest slice, validate catalog changes in a disposable database, run
focused tests and the full suite at acceptance, write a concise receipt, update the roadmap/tree,
and stop. The receipt records evidence; the plan does not become a historical diary.
