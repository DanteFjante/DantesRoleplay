# D&D 2024 weapon activity harness convergence receipt

Status: **accepted**  
Date: **2026-08-30**  
Implementation: `ruleset/dnd2024/DND2024-WEAPON-ACTIVITY-HARNESS-CONVERGENCE-IMPLEMENTATION.md`  
Ruleset alignment: **dnd2024-compatible**

## Delivered boundary

- Did not create `dnd2024.weapon-profile.json`. The component is intentionally retired.
- Removed the retired component registration and mapping from the broad D&D harness.
- Registered the existing normalized weapon/activity owners and converted the representative combat
  fixture to a weapon plus an explicit active member activity.
- Added the required `activity` role to retained weapon attack and damage tests.
- Converted the retained weapon writer integration test to the normalized six-effect facet write.
- Removed two superseded broad tests whose old profile/entity layout is already replaced by the
  focused current-schema weapon-activity acceptance owner.
- Prevented other retired component names from aborting every harness construction; affected legacy
  tests now fail at their own subsystem boundary instead of masking the whole suite.
- Corrected the shared calculation-reference regex in 33 current D&D component schemas from the
  retired `mechanic.*` namespace to the confirmed `dnd2024.mechanic.*` namespace.
- Corrected the harness catalog lookup so already-qualified current IDs are not prefixed twice.

## Verification

- Weapon, namespace, writer, and representative combat checks: **12 passed**.
- Catalog validation: **156 valid records**, **27 existing near-duplicate warnings**, no live data
  touched.
- `git diff --check`: passed for the delivered files; only line-ending notices were emitted.
- Broad `Dnd2024AbilityCheckTests` run: attempted for 98 seconds and deliberately stopped after it
  repeatedly exposed the same independent retired-owner groups. It no longer fails on
  `dnd2024.weapon-profile` or double-qualified mechanic IDs.

## Next independent blockers observed

1. Character creation still reads retired `content/entities/character-creation` fixtures and old
   monolithic components such as feature grants and species profiles.
2. Conditions and damage application still require retired `dnd2024.conditions` instead of the
   current `dnd2024.effect.*` model.
3. Rest mechanics and fixtures still require retired rest-policy/rest-episode records instead of
   `dnd2024.exploration.rest` and the authoritative clock owners.
4. Inventory/equipment mechanics still depend on old item-definition, item-activity, and
   equipment-state monoliths instead of current item facets.

These are semantic migrations, not missing-file repairs. Creating compatibility JSON files for the
retired IDs would introduce duplicate authorities and is deliberately excluded.
