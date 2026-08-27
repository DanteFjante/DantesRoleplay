---
id: procedure.mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage to Hit Points
governs: mechanic.dnd2024.weapon-damage.apply
status: active
---

## Description

Consumes one declared weapon-damage child and one dependency-aware defender profile, calculates SRD
mitigation, then spends optional Temporary Hit Points before target current Hit Points.

## Instructions

Inherit the closed ability/critical input into exactly one damage child. Compose exactly one
`mechanic.dnd2024.damage.resolve` child with fixed `{}` input and `defender` bound to the target.
Validate both envelopes. Immunity prevents matching damage; otherwise apply one Resistance halving
with floor when the profile contains the type or Petrified, then apply one matching Vulnerability
doubling. Preserve both Resistance reasons but halve once. Reject unsafe arithmetic before effects.
Validate a present positive Temporary HP buffer after mitigation is calculated. Spend it before HP,
removing it at zero or setting the positive remainder. Apply only leftover damage to HP, clamp HP at
zero, and preserve maximum/provenance. Return the buffer split, actual HP damage, and post-buffer
overkill. Emit no effects when final damage is zero.

## Constraints

The parent never rerolls or accepts caller damage/mitigation/buffer/result values. It grants no
Temporary HP and has no event, zero-HP consequence, death, healing, concentration, adjustment,
threshold, bypass, range, turn, or attack-legality owner. Buffer and optional HP effects commit in
one existing generic root transaction.
