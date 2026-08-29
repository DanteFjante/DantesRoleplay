---
id: procedure.mechanic.dnd2024.feat-profile
category: ruleset.dnd2024.character.feat-profile
name: Define immutable D&D 2024 Origin-feat profiles
governs: catalog authoring of dnd2024.feat-profile on versioned dnd2024.character.content-definition feature entities
status: active
---

## Description

Defines source-cited immutable catalog identities for the four SRD 5.2.1 Origin feats. A profile
belongs only to a versioned feature definition and records no active benefit.

## Instructions

1. Attach `dnd2024.feat-profile` only to a matching active
   `dnd2024.character.content-definition` with `kind: feature`.
2. Use the exact SRD identities Alert, Magic Initiate, Savage Attacker, and Skilled at
   `Feats > Origin Feats` (PDF p. 87). Each has `category: origin`.
3. Record `repeatable: true` only for Magic Initiate and Skilled; Alert and Savage Attacker are not
   repeatable.
4. Treat the profile as immutable source identity. Benefit behavior requires a separate named
   mechanic owner and must not be inferred from profile presence.

## Constraints

This component cannot select or grant a feat, record actor state, evaluate a prerequisite, choose a
spell/list/proficiency, or execute any benefit. A correction requires a reviewed new content
version rather than rewriting an established definition.
