---
id: procedure.mechanic.dnd2024.class-progression
category: ruleset.dnd2024.core.advancement.class-progression
name: Govern D&D 2024 class progression declarations
governs: commit(kind: "component") declaring dnd2024.class-progression; catalog authoring immutable class-progression content; commit(kind: "mechanic") authoring mechanic.dnd2024.class-progression.read; commit(kind: "action") reading class progression
status: active
---

## Description

Owns immutable, source-backed progression facts for a D&D 2024 class content definition: Hit Die
size, fixed Hit Point gain before Constitution, and exact level entitlement identities. It reads
those facts without deciding whether an actor may advance or making any actor/gameplay change.

## Instructions

1. Source basis: `source.dnd2024.srd-5.2.1`; preserve the exact class section and stable PDF-page
   locator on both the class content identity and its progression declaration.
2. Attach one closed `dnd2024.class-progression` only to an active immutable
   `dnd2024.character.content-definition` whose `kind` is `class`. Catalog content authoring is
   the normal write path; a changed source creates a successor content entity rather than editing
   a published declaration.
3. Keep `hitDieSides` and `fixedHitPointGainBeforeConstitution` paired as d6/4, d8/5, d10/6, or
   d12/7. Levels are strictly ascending and unique; feature and choice-set IDs are sorted unique.
4. `mechanic.dnd2024.class-progression.read` accepts exactly a class level and reports the exact
   supported declaration or diagnostics. It returns no effects, makes no random call, and never
   treats an absent level as an empty declaration.
5. A declared feature identity is an entitlement only. Feature 27 does not implement Action Surge,
   Tactical Mind, a resource, rest recovery, class membership, Hit Points, proficiency bonus, or
   campaign authorization in this slice.

## Constraints

- The component contains no actor id, total/class level, XP, Constitution modifier, die result,
  current/final HP, resource count, feature state, grant receipt, campaign, authorization, or
  effect.
- Reader source validation compares the class identity and progression provenance exactly. Missing,
  malformed, invalid, archived, or mismatched state is unknown, never a valid empty class.
- Catalog fixtures/tests must prove every declared feature/choice identity exists with compatible
  active source-backed content; the sandboxed reader cannot dynamically query undeclared entities.
- CH4/CH12 own persisted class membership; CH9/C14 own the governed transition and authorization;
  Feature 33 owns rest recovery; named feature owners own their mechanics.
