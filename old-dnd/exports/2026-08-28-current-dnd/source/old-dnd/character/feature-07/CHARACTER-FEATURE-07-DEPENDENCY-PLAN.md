# Character Feature 7 dependency plan — regression evidence, profile correction, and controlled expansion

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; starts only after a real CH6 create-and-play receipt is accepted.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.world.change`, catalog import/export safeguards, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), and accepted CH0–CH6 contracts. It writes no runtime artifact.

CH7 is the quality and expansion gate, not a shortcut for unfinished character rules. It preserves existing created characters, adds evidence fixtures, permits one narrow profile correction path, and permits one reviewed source-content addition only when the played first path proves the underlying capability. It never silently revises a source definition or recomputes an existing character.

## Target capability

After an actual CH6 session creates a character and performs the recorded first safe action, maintainers can prove that first path remains deterministic and rollback-safe, correct only campaign-visible descriptive profile fields through a governed action, and add one further source-cited content option without mutating existing actors or treating a generic schema as blanket SRD support.

### Included

- A compact deterministic fixture pack for valid/invalid creation, duplicate grants, rollback, source version preservation, and fresh-session reconstruction.
- A short CH6 played-session evidence receipt that records contract/mechanic versions, fixture IDs, outcome, audit/history reference, and known exclusions without copying game state or source text.
- One trusted-host correction operation for CH1 `pronouns`, `appearance`, and `biography` only.
- One-at-a-time source-content expansion using existing CH1–CH6 declaration/resolution forms and an explicit owner map.
- Catalog validation/import-export checks that keep authored content separate from live campaign characters.

### Excluded

- Corrections to character ID/name, campaign attachment, ability scores, policy, background, species, class, class level, proficiencies, HP, AC, equipment, items, grant receipts, source references, completion receipt, audit/event history, or player ownership.
- Respec, source migration, retroactive feature grant, source-text edit in place, schema-meaning change, component migration, automatic revalidation of existing actors, bulk import of campaign characters, spellcasting, advancement, feats, multiclassing, retirement, or authorization.
- Expansion through an arbitrary generic field, a second unreviewed resolver, an opaque language/tool/trait value, or a public MCP kind/tool.

## Ownership and preservation rules

| Concern | CH7 rule |
| --- | --- |
| Existing actor and receipts | Immutable selected definition IDs and grant/creation receipts remain historical truth. A newer content entity never replaces an actor reference. |
| Source content | CH1 content identity/version owns immutable source data. A correction or new official option creates a new versioned entity; archive eligibility may change only through its governed status path. |
| Component schemas | Definitions are not versioned by the catalog importer. A schema meaning change is a separate semantic/migration plan, never a CH7 content expansion. |
| Profile correction | CH1 owns profile data; CH7 supplies its only normal correction action. Entity name is excluded because the world effect vocabulary has no ordinary rename operation. |
| Items/campaign/world | Items own instances/containment, campaign owns attachment, world owns location/knowledge. CH7 correction never crosses these boundaries. |
| Audit/events | Existing ActionRunner/history/event ledger own immutable operation evidence. The correction result links to its audit rather than copying audit IDs into character state. |

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Correction contract | `procedure.character.correct`, restricted to the CH1 profile fields listed above. |
| Correction mechanic | `mechanic.dnd2024.character.profile.correct`, run through existing `commit(kind: "action")`; it validates campaign attachment and replaces the complete profile component only. |
| Evidence receipt | No persistent component. A short repository receipt under `character/feature-07/` is durable implementation evidence; runtime audit/history remains the operation record. |
| Expansion contract | No new generic expansion mechanic. Reuse each owning CH1–CH6 content/choice/grant/class contract after a new source review. |

Confirm the two permanent IDs and exact profile schema/attachment verifier under `procedure.system.modify` before authoring. If an existing compatible correction owner is found, reconcile rather than adding a second profile writer. This feature does not pre-authorize a source-supersession relationship, because stable new content entity IDs and existing actor references are sufficient until a real migration use case exists.

## Correction boundary

The correction mechanic receives an existing campaign-scoped character as declared role plus a closed object containing only optional `pronouns`, `appearance`, and `biography`. Each present value follows CH1 trimming/length rules; omission removes the corresponding optional value from the replacement profile. `null`, empty text, whitespace-only text, unknown fields, all-fields-omitted input, source/content/equipment fields, and a missing/wrong/inactive campaign attachment fail unchanged.

After validation it emits exactly one `component.set` for `dnd2024.character.profile`, preserving no stale optional field and changing no other component, relationship, containment, or entity name. It requires an existing valid profile. It is a trusted-host correction until CH14; a profile visibility label is not permission. CH6 discovers it only after CH7 is accepted, and no separate correction kind is added.

## Fixture, replay, and catalog discipline

The fixture pack has stable source locators and canonical input/output comparisons, not copied SRD prose. It includes: one complete valid build; one each missing/extra/derived value; unknown/archived/stale definition; duplicate/missing/cross-source choice; duplicate grant; campaign/scope/ID failure; each child failure injection; guard/reaction rollback; and the one profile correction boundary.

Creation itself has no randomness. Compare two valid creations with distinct approved character IDs after removing permanent identity, audit/event IDs, and item-instance IDs from the projection; all source selections, component values, receipt data, containment roles, and declared capability result must match. For a seeded first action, replay uses the recorded ActionRunner seed and mechanic version. A changed content definition, contract/mechanic version, or resolver fingerprint is a different fixture case, not a replay of the old character.

Repository catalog validation runs against a disposable migrated database. It may import source definitions and fixtures but never treats a live campaign actor as catalog-authored content. Before any persistent import, inspect `roleplay import catalog --dry-run`; export or resolve file/live conflict rather than forcing either side. A content expansion records its new content entity in the catalog; campaign-created actors remain SQLite-authoritative and are verified through their saved state/history.

## Dependency graph and slices

~~~text
Played CH6 fixture: discover → validate → create → inspect → safe first action      [missing]
├─ CH5 atomic rollback/audit evidence and current catalog validation                  [blocked parents]
├─ confirmed profile correction vocabulary + campaign attachment verifier              [semantic/external gate]
└─ expansion candidate with source locator, same declared forms, and owner map         [future gated leaf]
   ├─ Slice 1: regression fixtures and played-session receipt
   ├─ Slice 2: narrow profile correction
   └─ Slice 3: one reviewed source-content addition
      └─ CH8 UI parity and CH9 advancement
~~~

### Slice 1 — evidence and regression gate

**Prerequisites:** CH6 has a successful fresh-session protocol walk and one recorded first action; CH5 receipt/audit/event evidence is queryable; fixture IDs and comparison exclusions are confirmed.

1. Add the complete valid/invalid/rollback/replay fixture pack and short played-session receipt.
2. Test catalog round-trip, fresh database validation, source identity/version preservation, normalized deterministic create comparison, seeded action replay, and no partial state after every injected failure.
3. Run `roleplay validate catalog`, focused tests, and full suite. Record results without changing the base roadmap into a receipt.

**Exit:** a reviewer can reproduce the supported path and every named failure from versioned fixtures, distinguish a replay from a new content version, and locate actual CH6 evidence.

### Slice 2 — profile-only correction

**Prerequisites:** Slice 1 accepted; CH1 profile and campaign attachment contracts are live; correction IDs/schema meaning are confirmed.

1. Add `procedure.character.correct` and the profile-correction mechanic under existing action routing.
2. Verify scope/profile presence, closed optional-field semantics, full-component replacement, and exact no-cross-owner effect set.
3. Test every valid omission/change combination plus absent/corrupt profile, scope failure, empty/null/overlong/extra input, duplicate match phrase, guard/reaction rollback, and history readback.
4. Run catalog validation and focused tests; run protocol walk only if action routing changes.

**Exit:** the authorised descriptive correction writes one valid replacement profile or no character-world state, and existing source/mechanical/item/campaign/audit truth is unchanged.

### Slice 3 — one controlled content expansion

**Prerequisites:** Slices 1–2 accepted; a second candidate has exact SRD 5.2.1 locator, complete choices/grants, supported allocation/grant forms, and every target owner; no schema/migration/new surface is required.

1. Repeat CH0 review for the candidate only; explicitly map any difference from the first fixture to an existing capability and owner.
2. Add its versioned content/choice/grant records through CH1–CH4 contracts. If it supersedes a source correction, create a new entity/version and preserve old actor references; do not edit the old record's immutable fields.
3. Add one valid and all relevant invalid fixture cases, then run the CH6 discovery/create/play walk for both first and second options.
4. Run catalog validation and full suite. Stop after this single reviewed addition.

**Exit:** the second option is source-cited, independently discoverable, and creation-safe without changing the first character's projection, receipt, historical action replay, or supported-rule meaning.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Regression evidence | Valid/invalid/rollback/source/replay fixtures run deterministically with named comparison exclusions and a real CH6 receipt. |
| Immutable history | Existing actor definition IDs, grant receipts, creation receipt, audit, and event history remain byte-for-byte unchanged after content expansion. |
| Profile correction | Only approved optional descriptive fields may change through one profile set; entity name and all mechanical/campaign/item state are untouched. |
| Correction failure | Bad scope/profile/input/guard/reaction leaves profile and all other character-world state unchanged; only ordinary failure audit evidence remains. |
| Content correction | A corrected source declaration becomes a new versioned entity; old immutable fields are never rewritten and no actor migrates automatically. |
| Expansion discipline | Exactly one reviewed candidate uses existing forms and real owners. Any new field, schema, owner, grant form, source edition, or public surface stops for a successor plan. |
| Catalog/live safety | Disposable catalog validation succeeds; persistent import is dry-run reviewed; live campaign actors are not overwritten or silently catalogized. |

## Evidence and change control

The CH7 receipt records played-session evidence, fixture versions, test commands/results, catalog validation, content candidate/source locator, import/export decision, and known exclusions. It is evidence of completed work, not runtime truth.

Amend CH7 before permitting mechanical correction, rename, respec, migration, retroactive grant, schema change, multiple simultaneous source options, random creation, public correction surface, player authorization, or UI workflow. Those boundaries belong to a dedicated migration/respec plan, CH9–CH14, CH8, `procedure.mcp.add-tool`, or the owning ruleset/item/campaign plan.
