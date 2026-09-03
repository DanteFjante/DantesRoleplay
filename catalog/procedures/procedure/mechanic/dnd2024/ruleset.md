---
id: procedure.mechanic.dnd2024.ruleset
category: ruleset.dnd2024.core.governance
name: Build a contract-first SRD 5.2.1 ruleset
governs: commit(kind: "component"), commit(kind: "effects"), commit(kind: "mechanic"), and commit(kind: "action") for any D&D ruleset component or mechanic with scope "dnd2024-srd-5.2.1".
status: active
createdBy: "llm"
changeNote: "Adds a core subcategory under ruleset.dnd2024 so core rules can later be overridden by campaign- or instance-specific categories."
---

## Description
Governs the incremental creation and revision of SRD v5.2.1-compatible D&D ruleset components, including useful source-cited explanations for players and hosts.

## Matches

## Instructions
1. Define one small game component with a permanent id and one responsibility.
2. Choose one primary category path from the D&D taxonomy under ruleset.dnd2024.core: governance, play, host, data.<component>, gameplay.<area>.<rule>, combat.<area>.<rule>, magic.<area>.<rule>, advancement.<area>.<rule>, or content.<area>.<rule>. Use lowercase, hyphen-delimited words within dot-delimited segments. Category paths describe purpose; the separate scope identifies the SRD ruleset version.
3. Search procedures, component definitions and mechanics for an overlap. Revise an existing component rather than duplicate it.
4. Create or revise that component's dedicated procedure contract before declaring its data, mechanics or test entities. Its id must be procedure.mechanic.dnd2024.<component-id>.
5. The component contract must state its purpose and non-goals, SRD v5.2.1 source reference and attribution, dependencies, data shape, invariants, creation order, action input/output, deterministic verification cases, and revision or retirement rules.
6. When useful to a player, host or implementer, include a concise plain-language SRD explanation in the component contract. Label it as an explanation, cite the exact SRD version and section, and distinguish it from the component's executable behaviour.
7. Read the active component contract immediately before every change it governs.
8. Add only the data definition or mechanic named in that contract; do not broaden the change.
9. Dry-run every supported effects or mechanic write, read every reported check, then commit the identical payload.
10. Query back what was written, verify the component contract's tests, record the operationId, and stop for review before beginning another component.

## Constraints
- No D&D ruleset component may be created or revised without a matching active procedure contract.
- One component contract authorizes exactly one game component; it must not combine unrelated definitions or mechanics.
- Every new D&D procedure or mechanic must use one leaf category path under ruleset.dnd2024.core; use exact-category lookup until recursive catalog search exists.
- Every rules component must cite SRD v5.2.1 and include the required CC-BY attribution in the ruleset source registry; non-SRD material is out of scope.
- Store structured mechanics and citations. An explanation must be concise, source-cited, and must not reproduce non-SRD rulebook text or replace the authoritative SRD source.
- Raw ability scores are stored; mechanics derive modifiers and roll totals.
- A component is incomplete until its contract-defined deterministic tests pass and the stored result is queried back.
- Existing ids are permanent; revise rather than rename or repurpose.
