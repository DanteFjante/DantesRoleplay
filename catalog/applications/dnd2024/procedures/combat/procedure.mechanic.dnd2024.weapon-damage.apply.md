---
id: procedure.mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage to Hit Points
governs: mechanic.dnd2024.weapon-damage.apply
status: active
---

## Description

Consumes one declared weapon-damage child and atomically replaces only target current Hit Points.

## Instructions

Inherit the closed ability/critical input into exactly one damage child, validate its envelope, clamp
current HP at zero, preserve maximum/provenance, and propose one component set.

## Constraints

The parent never rerolls or accepts caller damage. It has no mitigation, temporary HP, event,
zero-HP consequence, death, healing, range, turn, or attack-legality owner.
