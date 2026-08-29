---
id: procedure.information.answer
category: information
name: Answer from generic grounded information
governs: query(kind: "information-answer")
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Return a bounded answer using only records selected from one host-authorized generic information scope.

## Instructions
1. Supply an authorized concrete scope or terminal `.*` namespace selector and a bounded question.
2. Optionally narrow to source ids within that same scope.
3. Treat cited record ids as evidence; an unknown answer means the selected records did not support a claim.

## Constraints
- The caller cannot widen access beyond the host policy.
- The model receives no tools and cannot retrieve neighboring records.
- This query reads no campaign, world, ruleset, or game state.

