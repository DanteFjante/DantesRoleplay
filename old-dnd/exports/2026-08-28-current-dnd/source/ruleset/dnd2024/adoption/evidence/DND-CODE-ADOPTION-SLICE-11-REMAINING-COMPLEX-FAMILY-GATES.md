# D&D code-adoption Slice 11 — remaining complex-family gates

Date: 2026-08-26  
Status: **accepted defer map; Parent 11 closed**

## Closure rule

Parent 11 selectively adapts dependency-complete complex behavior. It does not make every archived
feature current merely because a component or partial test exists. A family closes Parent 11 as
either delivered, already owned, or explicitly deferred with a concrete prerequisite. Deferred
families retain their archive evidence and move to independent future feature plans; they are no
longer ambiguous pending adoption work.

## Already owned before Parent 11

| Capability | Current accepted owner/evidence | Parent 11 disposition |
| --- | --- | --- |
| turn/round lifecycle and action economy | encounter-turn and turn-budget families accepted through Slices 7–8 | retain current; no duplicate timing owner |
| Conditions and Exhaustion effects on current D20/turn calculations | Conditions writer/state-effects and turn-budget composition accepted through Slice 8 | retain current; later consequence consumers require their own roots |
| equipment/inventory and AC aggregation | item/equipment/reader families accepted through Slice 8 and static content through Slice 10 | retain current |
| base weapon attack/damage and base Speed | weapon and Speed families accepted through Slices 7–8 | retain current; tactical movement and special attacks are separate |
| identity/origin/experience/progression reads | character state through Slice 8; selected Fighter identities through Slice 10 | retain read-only identity owners; do not imply feature behavior |

## Delivered by Parent 11

| Family | Accepted leaves | Delivered boundary |
| --- | --- | --- |
| damage mitigation | 11A–11D | canonical typed mitigation, Condition-derived Petrified resistance, SRD ordering in weapon damage |
| Temporary HP and healing | 11E–11H | positive optional buffer, explicit grant choice/expiry, capped healing, mitigation -> buffer -> HP atomic weapon damage |

## Deferred families and executable close conditions

| Candidate | Evidence actually available | Why it cannot activate as a complete current family | Close condition outside Parent 11 |
| --- | --- | --- | --- |
| dying, dropping to 0, death saves, stabilization, death | archived Feature 17 verifies only policy/state/condition-guard slices 1–3; later reactions were planned | current direct application actions reject event/notification output; automatic turn/healing/damage reactions and elapsed Stable recovery have no accepted root | design one current action-composition/automatic-timing owner with HP, Conditions, Exhaustion, turn, healing, replay and rollback; source-review all pp. 17–18 branches |
| reactions and timing windows | current turn budget stores reaction availability | no reaction-window, trigger, ordering, interrupt, or adjudication owner exists | add a generic declared timing-window seam and one bounded D&D reaction family without rule logic in C# |
| tactical movement, terrain, range, line of effect, Shortbow range, unarmed Grapple/Shove, multiattack | base Speed and weapon data exist; archived Features 20–22 are explicitly partial | no canonical position/grid/occupancy/difficult-terrain/reach/line-of-effect owner; special attacks also need size/save/condition composition | accept tactical-map/position components and movement transaction first, then one attack family at a time |
| Fighter feature behavior and general advancement | Fighter levels 1–2 identity/read data only; historical evidence is partial | no feature-resource, choice-set, rest-reset, multiclass advancement, or grant-application owner | author independent Second Wind, Action Surge, Tactical Mind, Fighting Style, mastery-choice, and advancement plans after resource/rest/choice prerequisites |
| species, backgrounds, feats, and full origin application | selected shared identity state only; Parent 10 gate records missing family schemas | partial entities would omit family meaning and application behavior | add confirmed family schemas/IDs and a transactional character-build/grant owner; keep optional/homebrew content in pre-campaign sources |
| standard rest behavior and Temporary HP Long Rest expiry | archived Feature 33 has a static policy and clock-scoped episode, not benefit completion | current D&D application has no authoritative elapsed-time bridge; Hit Dice, resource/slot resets, interruption, and several benefits are absent | decide the application-to-world-clock dependency and implement begin/reconcile/complete roots with each benefit delegated to its owner; never accept elapsed time from caller authority |
| Heroic Inspiration | archived Feature 39 has presence/grant only | no verified consumption/reroll context, original-die binding, replacement-result integration, or overflow-recipient owner | integrate one-use state with the generic dice/result context and explicit transfer choice; preserve the SRD reroll/new-result rule |
| spellcasting ability/DC and spell resources | Slice 9 records deferred derivations; archived spell identities/profiles are partial static evidence | granting feature, prepared/known spell state, class/Pact/multiclass tables, slots/resources, concentration, targets, and consequence roots are absent | establish spell identity/profile and feature-owned casting ability/resources, then implement one resolution family per effect/timing class |
| monsters and bestiary behavior | archive contains runtime fixtures/partial statblock evidence, not a current schema-ready cohort | no monster profile/bootstrap/action/multiattack owner; campaign/runtime fixtures are not static application content | add confirmed statblock schemas and bootstrap one SRD creature vertical before homogeneous cohorts |
| magic items | archived Feature 29 has partial static profiles only | missing magic-item schema plus attunement, charges, activation, consumption, and effect-family owners | establish the profile and one activation/effect family; optional/non-SRD items remain separate sources |

## Parent exit boundary

No remaining candidate has all of: exact current owner, complete SRD-reviewed behavior, closed
component/projection dependencies, supported effects/timing, one transaction root, and compatible
tests. Parent 11 therefore has no safe additional family to activate. The deferred rows are product
feature work, not adoption cleanup and not permission to bulk-copy `old-dnd/`.

This closure changes no live database, campaign binding, source profile, public operation, generic
kernel contract, archived file, or optional extension. The next adoption work is Parent 12 full
acceptance/maintenance evidence, while each deferred gameplay row must receive its own dependency
tree and active implementation document before runtime changes.
