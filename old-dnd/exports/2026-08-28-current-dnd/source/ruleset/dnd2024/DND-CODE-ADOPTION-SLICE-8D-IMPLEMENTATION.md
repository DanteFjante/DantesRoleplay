# D&D code-adoption Slice 8D implementation — identity and origin state

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8D  
Prerequisites: accepted Slice 3 ability foundation and Parent 8 source-registry adaptation  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, Character Creation, Creature Size, Languages, and Tools  
Outcome: Recover the five classified identity/origin recorders and their closed state contracts.  
Exclusions: Character creation orchestration, content grants/eligibility, campaign membership,
authorization, derived checks, inventory, migrations, public operations, and archive deletion.  
Allowed areas: the five classified components/mechanics, their four governing procedures, D&D
activated-path tests, Parent 8 evidence, and this plan.  
Stop point: all five mechanics pass activated add/set/replay/failure tests.

## Adaptation boundary

The current application source registry is the only source authority. Character-content definitions
therefore fix their SRD source reference directly and do not revive the archived `dnd2024.source`
role/component. The immutable content-definition and write-once Size recorders remain add-only.
Profile, language, and tool state use explicit `record|correct` transitions against absent or valid
existing state. Profile is recovered as an administrative state recorder; it cannot prove campaign
scope or become a character-creation transaction.

Every schema and input is closed. Each success proposes exactly one typed add/set effect; malformed
or invalid prior state refuses correction without mutation. The generic action runner owns exact
revision authorization, transaction, rollback, and operation replay.

## Acceptance

Acceptance covers canonical source/state bytes, content identity and locator boundaries, all six
Size categories, optional trimmed profile fields and length bounds, canonical language/tool
vocabularies and sorting, record/correct preconditions, corrupt prior state, closed inputs, exact
failed-state preservation, revision increments, replay, catalog validation, and full regressions.
