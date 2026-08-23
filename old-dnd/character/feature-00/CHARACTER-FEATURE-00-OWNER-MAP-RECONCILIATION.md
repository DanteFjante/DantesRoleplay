# Character Feature 0 — first-build owner-map reconciliation

Status: **Completed planning audit; no runtime change authorized**  
Reviewed: 2026-08-21

## Purpose and boundary

This reconciliation executes Row 0 of
[character-creation MCP interface dependency plan](../feature-06/CHARACTER_CREATION_MCP_INTERFACE_DEPENDENCY_PLAN.md). It checks the ratified Human Soldier
Fighter path against the repository/catalog state before assigning a character-creation runtime
slice. It confirms ownership and drift only. It creates no catalog record, component, mechanic,
procedure, item, actor, campaign state, or MCP surface.

The result is intentionally strict: an immutable feature identity is not an implemented character
benefit, an administrative HP/AC writer is not a source-derived resolver, and an inventory engine
does not imply the chosen item definitions or starting grants exist.

## Documents and evidence read

- `AGENTS.md`, `STATUS.md`, `KNOWN_ISSUES.md`, and `CHARACTER_CREATION_PLAN.md`
- CH0, CH3, CH4, and CH5 dependency plans and the CH1/CH2/CH5 Slice 0 receipts
- Campaign C15 participation plan and Slice 2 receipt
- Feature 23 implementation status; Features 25, 27, and 28 dependency plans/receipts
- Current catalog entities for Human, Soldier, Fighter, Alert, Savage Attacker, Fighter level-one
  feature identities, and Chain Mail
- Current catalog/content search for Greatsword, Flail, Javelin, and Dungeoneer's Pack

## Reconciled owner map

| Ratified result | Current owner/evidence | Reconciled state | Next owner action |
| --- | --- | --- | --- |
| Campaign-scoped actor attachment | Campaign C15 planner/active-scope verifier | **Ready** | CH5 consumes the existing typed fragment. |
| Name, pronouns, appearance, biography, and source provenance | CH1 profile/content-definition contracts | **Ready** | CH5 consumes the existing planners. |
| Standard Array, raw abilities, and total level 1 | CH2 and existing ability/level owners | **Ready for base allocation** | CH3/CH4 supply only their separately owned changes/grants. |
| Human immutable identity and selected species | Feature 26 profile/selection seam | **Identity/selection ready** | Do not infer Human traits from the static profile. |
| Soldier immutable identity and `+2 Strength/+1 Constitution` | CH1 content identity plus Feature 28 Slice 2 | **Ready as a composition fragment** | CH3 later resolves the bound background selection. |
| Common, Dwarvish, Giant and dice-set proficiency | Feature 28 language/tool owners and origin-language resolver | **Owner ready** | CH3 later supplies approved selections through the owner. |
| Skills, saves, and weapon-category membership | Existing closed recorders | **State owners ready** | CH3/CH4 must supply closed source grants and receipts. |
| Alert and Savage Attacker identities | `content.dnd2024.feature.alert.v1`, `content.dnd2024.feature.savage-attacker.v1`, `dnd2024.feat-profile` | **Static identity present; behavior absent** | Plan/implement only their approved effect owners; do not mark either active through a receipt alone. |
| Human Resourceful / Skillful / Versatile traits | Feature 39 owns one held Heroic Inspiration instance; Feature 28/CH3 own skill and Origin-Feat selection | **Held Inspiration state ready; source-grant and feat behavior absent** | E8/Feature 33 must first establish completed-rest evidence; Feature 39 then owns the Resourceful grant. CH3 later records selected grants. |
| Fighter identity, hit-die facts, and level-one entitlement list | Feature 27 progression declaration | **Static declaration present; actor membership/benefit behavior absent** | CH4 owns initial class membership; class/HP and each feature owner must supply real fragments. |
| Fighting Style, Second Wind, and Weapon Mastery | Fighter feature identities; Feature 27 entitlement reader returns `behaviorStatus: "unimplemented"` | **Not playable** | Implement/confirm the individual feature owners; Feature 25 separately owns mastery semantics. |
| Level-one Hit Points | Existing HP writer stores a supplied final pair | **No source-derived owner** | Plan a source-cited class/HP resolver; CH4/CH5 must never accept caller-calculated HP. |
| Final Armor Class | Feature 24 Slice 4 derives AC from Dexterity and direct equipped armor/Shield evidence | **Ready** | CH5 must compose legal starting equipment and the derived AC reader; callers never supply AC. |
| Fighter Package A items | Chain Mail, Greatsword, Flail, Javelin, and every Dungeoneer's Pack definition are source-cited catalog content | **Source content ready** | CH5/Items Slice 6 still own one atomic grant and placement path; do not represent the pack as an opaque substitute item. |
| Item instances, containment, equipment state, currency, and inventory reads | Feature 23 Slices 1–11 | **Infrastructure ready** | It can consume approved definitions but does not supply missing package definitions or a creation grant itself. |
| Atomic new-actor composition | CH5 Slice 0 staged composer | **Ready foundation only** | CH5 Slice 1/2 remains the sole validate/create and transaction root. |

## Drift found

1. CH0’s historical owner-map wording says campaign attachment, CH1, and CH2 are blocked/planned.
   Their current receipts show C15, CH1, and CH2 are accepted. This is documentation drift, not a
   reason to recreate those owners.
2. Feature 28’s dependency-plan header described Slice 4 Origin-feat identity as awaiting
   confirmation while the catalog and focused tests already contained its completed artifacts.
   This was reconciled on 2026-08-21 in `FEATURE-28-SLICE-4-RECEIPT.md`. The accepted identity
   boundary must not be widened into a behavior claim.
3. The static entities above do not close the behavioral gaps. Their schemas explicitly exclude
   a benefit/executable payload, and Feature 27 explicitly marks Fighter entitlements
   `unimplemented`.
4. The earlier statement that Heroic Inspiration and derived Armor Class lacked owners is now
   stale. Feature 39 Slice 1 owns one held Inspiration instance, and Feature 24 Slice 4 owns
   derived default/armor/Shield AC. Neither result implements Human Resourceful's completed-rest
   trigger, Origin-Feat behavior, or starting-equipment creation.

## Decision and stop gate

The current Human Soldier Fighter is **not source-complete for CH3/CH4/CH5**. The proposed CH3
grant/choice/receipt vocabulary remains unconfirmed because its source-complete owner map is a
prerequisite, not a consequence, of that vocabulary.

The Human/Origin-feat behavior path is now reconciled in
`ruleset/dnd2024/feature-39/FEATURE-39-FIRST-BUILD-BEHAVIOR-OWNER-RECONCILIATION.md`. Its next
platform prerequisite is E8's event-binding/fan-out work, followed by active/completed rest
evidence before Feature 39 can implement Human Resourceful. It must not add CH3 or CH4 permanent
IDs, actor class membership, HP values, item instances, or an MCP creation interface in the same
pass.

After that dependency chain is accepted, repeat this map. Continue with the next missing owner
until all rows are ready, then confirm and implement CH3 Slice 1. This preserves the CH5 rule that
every created character is coherent in one transaction rather than a partial actor with deferred
benefits.

## Required next-pass reads for Terra

1. `AGENTS.md`, `STATUS.md`, `KNOWN_ISSUES.md`, this reconciliation, and
   [character-creation MCP interface dependency plan](../feature-06/CHARACTER_CREATION_MCP_INTERFACE_DEPENDENCY_PLAN.md).
2. `ruleset/dnd2024/feature-28/FEATURE-28-DEPENDENCY-PLAN.md`,
   `ruleset/dnd2024/feature-28/FEATURE-28-SLICE-1-RECEIPT.md`,
   `FEATURE-28-SLICE-2-RECEIPT.md`, `FEATURE-28-SLICE-3-RECEIPT.md`, and the current Alert/Savage
   catalog entities, procedure, schema, and tests.
3. Feature 25/27 plans plus the exact Initiative, damage, weapon-attack, and D20 contracts only
   if the chosen owner path consumes them.
4. Live `procedure.system.create-feature` first, then `procedure.system.modify` before proposing
   any permanent ID/schema/public behavior.
5. `SUBSYSTEM_IMPLEMENTATION_HANDOFF.md` and the D&D Terra planning/implementation guides before
   assigning a runtime slice.

Stop for confirmation if that search reveals an existing compatible Heroic Inspiration, feat,
initiative, or damage owner; if not, the new owner plan must identify its exact source rule,
state, action/timing, transaction, and recovery boundaries before implementation begins.
