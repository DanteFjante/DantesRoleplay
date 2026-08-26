# D&D code-adoption Slice 7C implementation — AC, HP, weapons, and damage

Status: **verified 2026-08-25 — Sol runtime review approved**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7 / 7C
Ruleset alignment: **dnd2024-owned**
Source ID and locators: `source.dnd2024.srd-5.2.1`; `Playing the Game > D20 Tests > Attack Rolls`, `... > Armor Class`, `Playing the Game > Damage and Healing > Hit Points`, and `Equipment > Weapons`

## Delivered boundary

This cohort adds canonical Armor Class, Hit Points, weapon proficiency, and weapon-profile components with closed record/correct writers. Weapon attack and damage remain effect-free result mechanics. A parent damage application declares and receives the pure damage child result, then performs the sole target-HP component replacement in the normal application action transaction.

Attack derives the selected permitted ability modifier and character-level Proficiency Bonus only when the weapon category is recorded. Natural 20 hits and natural 1 misses; otherwise the total is compared to the target's canonical Armor Class. Damage rolls the profile dice (twice their number on a critical hit), adds the ability modifier once, and floors damage at zero. Applying damage clamps current Hit Points at zero and retains the existing maximum/source reference.

## Closed behavior

Callers cannot supply AC, HP, proficiency bonus, target threshold, profile data during attack, dice results, damage total, or resulting Hit Points. All component writers validate exact record or correct modes. The action host resolves the complete declared parent/child component closure and applies only validated parent effects.

## Evidence and exclusions

The fresh-host tests record all four canonical combat state types, resolve an attack/damage result, and commit composed damage through the active action path. Multiweapon attacks, properties, ranges, resistance/immunity/vulnerability, healing, death saves, conditions, and attack consequences are excluded.
