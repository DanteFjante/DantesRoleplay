# D&D 2024 character creation CC1 completion receipt

Status: **accepted**
Completed: 2026-08-27
Implementation: [CC1 ability generation and background increases](../DND2024-CHARACTER-CREATION-CC1-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Character Creation > Step 3: Ability Scores* (PDF p. 21) and
*Character Origins > Character Backgrounds > Parts of a Background / Soldier* (PDF p. 83)

## Delivered boundary

- Re-adopted the immutable `dnd2024.character.ability-assignment-policy` and
  `dnd2024.background.ability-increase-options` declaration families into the current application
  catalog without restoring their archived C# rule validators.
- Added source-cited Standard Array and Soldier fixtures.
- Added `mechanic.dnd2024.character-abilities.resolve`, a pure role-bound JavaScript resolver that
  supports fixed-multiset and point-budget declarations, both legal background patterns, and the
  post-increase score cap.
- The resolver emits no effects, events, or notifications; it stores no modifiers or duplicate
  ability state and contains no content-ID branch.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reference-reviewed for
  content-bound advancements, staged choices, cap enforcement, and final bulk application. No
  Foundry code, data, assets, or runtime dependency was adopted.

## Acceptance evidence

| Check | Result |
| --- | --- |
| Focused character-ability cases | 10 passed: Standard Array, point cost, both Soldier patterns, canonical ordering, seed independence, replay, malformed/derived input, source drift, ineligible patterns, and score cap |
| Full D&D regression class | 102 passed |
| Catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full solution | 1,127 shared tests passed and 21 Local AI tests passed in Release |
| Whitespace | `git diff --check` reported no whitespace error; only existing line-ending notices |

The first Debug test attempt was blocked before compilation by the running MCP server locking its
Debug output. Verification used the independent Release output and did not interrupt the live host.

## Deliberate exclusions

CC1 does not record scores on an actor, select or complete the Soldier background, grant its feat,
skills, tool, or equipment, select a species/class, create a character, attach campaign
participation, add a completion receipt, or change the MCP/public surface. Those remain explicit
leaves in the character-creation dependency tree.
