---
id: procedure.mechanic.dnd2024.abilities
category: ruleset.dnd2024.core.data.abilities
name: D&D 2024 ability scores
governs: commit(kind: "component") declaring dnd2024.abilities; commit(kind: "effects") attaching or changing dnd2024.abilities; deriving an ability modifier
status: active
createdBy: "llm"
changeNote: "Created. First component of the D\u0026D SRD 5.2.1 ruleset track."
---

## Description
How a creature's six ability scores are stored and how a modifier is derived from one. The base numeric layer every check, save and attack in this ruleset reads.

## Instructions
Source: *System Reference Document 5.2.1*, "Playing the Game — Ability Scores",
Wizards of the Coast, CC-BY-4.0, <https://www.dndbeyond.com/srd>. SRD 5.2.1 carries the 2024
(5.5e) revision of the core rules. Do not copy rulebook prose into this system; store the facts,
the parameters and this citation.

1. Ability scores live in ONE component definition, `dnd2024.abilities`, attached to the creature
   entity. Its data has exactly six integer keys and no others:
   `{"str":10,"dex":10,"con":10,"int":10,"wis":10,"cha":10}`.
2. Use those six three-letter keys verbatim, lowercase. Every rule in this ruleset reads them by
   that name, so a creature spelling one differently is invisible to every rule at once.
3. A score is an integer from 1 to 30. 20 is the ordinary ceiling; a score above it comes only from
   a rule that says so. A creature with no `dnd2024.abilities` component has no scores — that is a
   fact to report, not a zero to assume.
4. The modifier for a score is `floor((score - 10) / 2)`. Compute it wherever it is needed. In
   JavaScript that is `Math.floor((score - 10) / 2)`, which is correct for scores below 10 because
   `Math.floor` rounds toward negative infinity: 8 gives -1, 7 gives -2.
5. NEVER store the modifier. Two facts that can disagree is one fact too many, and the score is the
   authoritative one.
6. Attach the component with `commit(kind: "effects")` using `component.add`, which fails if it is
   already present — the correct behaviour when attaching twice would be a bug.
7. To change one score, use `component.merge`. `component.set` replaces the data wholesale and
   would silently discard the other five scores.
8. Declare this component in a mechanic's `requirements` under whatever role name fits, and
   `JSON.parse` the component before reading it — components arrive as JSON strings.

## Constraints
- The data holds the six ability scores and nothing else. A seventh key is a different concept and
  belongs in a different component definition — proficiency, hit points and conditions each get
  their own.
- Never store a derived value here: no modifiers, no saving-throw totals, no passive scores.
- Never use `component.set` on this component to change a single score.
- Never create a per-creature definition such as `dnd2024.abilities.orban`. One definition, many
  entities carrying it.
- Do not assume this component implies proficiency, class, level, or any ability to act. It is six
  numbers and their source; anything further needs its own contract.
