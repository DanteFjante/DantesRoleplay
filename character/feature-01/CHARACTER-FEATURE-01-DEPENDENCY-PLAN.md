# Character Feature 1 dependency plan — actor profile and immutable content provenance

Status: **Implemented and accepted; full-suite verification passed.**
Last updated: 2026-08-21

## Execution rule

This is a repository planning artifact. It follows AGENTS.md, `procedure.system.create-feature`,
`procedure.world.model`, `procedure.world.change`, `procedure.system.modify`,
`procedure.mechanic.dnd2024.source-registry`, and the authoritative
[Character Creation Plan](../../CHARACTER_CREATION_PLAN.md). Repository files are authoritative in
development. This plan writes no catalog record, database state, procedure, component, entity,
mechanic, public operation, event, notification, audit, fixture, or source content.

CH1 defines the character-owned identity and provenance boundary. It does not create a playable
character. CH5 remains the sole owner of an atomic character-creation receipt and of invoking the
profile writer as part of a completed creation transaction.

## Target capability

After its two slices pass their gates, the game has a reusable, versioned way to identify approved
character-source content without copying source rules, and a campaign-scoped actor can carry a
minimal player-character profile without becoming a second owner of campaign, item, or D&D rules
state.

The first ratified species, background, class, and any required feature or choice set are acceptance
fixtures only. The contracts must support each stated content kind; they must not encode the chosen
fixture's name, grants, or rule prose into their schema or procedures.

### Included

- A generic immutable character-content-definition convention for `species`, `background`, `class`,
  `feature`, and `choice-set` entities.
- A canonical `sourceRef` using the existing `dnd2024.source` registry identity and locator format.
- A minimal profile component that marks an existing, campaign-attached actor as a player character.
- A guarded normal profile-recording path for CH5 to call after the campaign owner has attached the
  actor; it has no independent actor-creation mode.
- Reader/discovery behaviour limited to approved content definitions and the profile visible to the
  actor's campaign audience.
- Tests for immutability, provenance, campaign scope, omission-versus-empty semantics, and absence
  of copied rules or duplicate state.

### Excluded

- Ratifying the first build (CH0), creating an actor (CH5), or adding a campaign character-attachment
  relation or authorization policy (campaign owner / CH14).
- Ability scores, modifiers, level, class levels, proficiencies, hit points, AC, attacks, spells,
  feats, grants, choice resolution, starting equipment, inventory, containment, or equipment state.
- A creation receipt, completion flag, root operation ID, player-control relationship, browser form,
  public MCP command kind, partial draft, secret backstory, or a free-form source-text field.
- Reusing `game.core.world.motive`: it is recurrent NPC/world-actor motivation and must not be
  overloaded as a character identity or biography record.

## Source and contract basis

| Authority | CH1 use and constraint |
| --- | --- |
| `catalog/world/entities/source.dnd2024.srd-5.2.1.json` and `dnd2024.source` | Use the existing source ID `source.dnd2024.srd-5.2.1`; `sourceRef` is `{ sourceId, locator }`, where the locator is a stable SRD heading and PDF pages when available. The registry holds attribution and licensing, not rules copied onto character content. |
| `procedure.mechanic.dnd2024.source-registry` | Source IDs are registry-owned. CH1 accepts only the registered SRD 5.2.1 source for the initial fixture and must reject an unknown source ID or blank locator. |
| `procedure.world.model` | Components are JSON object state with permanent IDs. Entity names remain the display name; CH1 must not duplicate that name in a profile field. |
| `procedure.world.change` | State changes are atomic effects. Profile recording must either validate campaign attachment and apply the one profile effect, or fail unchanged. |
| `procedure.system.modify` | Component, procedure, mechanic, and relation IDs are semantic boundaries requiring confirmation before authoring. |
| Character Creation Plan, CH0–CH5 | CH0 selects fixture content; CH1 supplies only identity/provenance; CH2–CH4 add rules composition; CH5 owns actor creation, receipts, and the root transaction. |
| Campaign Creation Plan and campaign owner | Campaign scope and the actor-to-campaign attachment relation are campaign-owned. CH1 consumes its verifier and never stores a copied campaign ID on the profile. |

## Proposed permanent vocabulary — confirmation required

Both permanent-ID families below are confirmed and implemented. The profile family is governed by
the campaign-owned attachment verifier under `procedure.system.modify` and the D&D ruleset
governance contract:

| Role | Proposed permanent ID |
| --- | --- |
| Immutable content-definition component | `dnd2024.character.content-definition` — **verified** |
| Its governing procedure and normal recorder | `procedure.mechanic.dnd2024.character-content-definition`; `mechanic.dnd2024.character-content-definition.record` — **verified** |
| Character-profile component | `dnd2024.character.profile` — **accepted** |
| Its governing procedure and guarded normal recorder | `procedure.mechanic.dnd2024.character-profile`; `mechanic.dnd2024.character-profile.record` — **accepted** |

The profile recorder is a governed internal dependency of CH5, not a public action. If owner search
finds an existing compatible character-profile or provenance component, stop for a semantic
decision: reuse it only if its complete schema and owner boundary are compatible, otherwise amend
this plan before naming a second record.

## Ownership and data model

### Immutable content-definition base — Slice 1

Subject to the permanent-ID confirmation, introduce one component with the proposed role
`dnd2024.character.content-definition`. It is attached to a versioned content entity, not to a
character actor. The entity name is the display title; the component contains only:

| Field | Meaning and validation |
| --- | --- |
| `kind` | Closed enum: `species`, `background`, `class`, `feature`, or `choice-set`. A new kind requires a later confirmed schema change. |
| `contentKey` | Canonical, lowercase stable key. It identifies the option family independently of presentation or entity ID. |
| `contentVersion` | Positive version token identifying this immutable declaration. A correction makes a new versioned entity; it does not rewrite the approved declaration. |
| `status` | Closed enum `active` or `archived`. Archive removes future selection eligibility without deleting historical references. |
| `sourceRef` | Existing source-registry shape `{ sourceId, locator }`. The initial fixture permits only `source.dnd2024.srd-5.2.1` and a nonblank verified locator. |

No field holds mechanical grants, choice selections, descriptive source passages, statistics, item
definitions, an actor ID, campaign ID, or a calculated value. CH3 and CH4 decide the future
relationships that select one of these definitions for an actor; CH1 must not pre-empt them with a
generic unvalidated reference array.

The content recorder is write-once for `kind`, `contentKey`, `contentVersion`, and `sourceRef`.
Only an authorised archival transition may change `status`; CH7 later plans correction,
supersession, migration, and expansion policy. A content key/version collision, malformed locator,
unknown source, unsupported kind, or mutation of an immutable field fails unchanged.

### Actor profile shell — Slice 2

Subject to campaign attachment and vocabulary confirmation, introduce one component with the
proposed role `dnd2024.character.profile`. Its presence identifies the attached actor as a player
character. The actor entity's existing `name` is the character's display name; no `name`,
`campaignId`, inventory, class, source option, or calculated D&D field belongs in the component.

The proposed component shape is deliberately small:

| Field | Meaning and validation |
| --- | --- |
| `pronouns` | Optional trimmed text, 1–80 characters when present. Omit when unstated; reject `null` and empty text. |
| `appearance` | Optional trimmed campaign-visible descriptive text, 1–1,000 characters when present. Omit when unstated; reject `null` and empty text. |
| `biography` | Optional trimmed campaign-visible descriptive text, 1–2,000 characters when present. Omit when unstated; reject `null` and empty text. |

All three fields are campaign-visible descriptive information, not access-control data. Secrets,
GM-only facts, clues, and faction/world facts belong with the campaign/world knowledge owner.
CH14 must not infer authority from this component or its audience convention.

The normal recorder takes the already-created actor and campaign scope, verifies the active
campaign-owned attachment through the campaign owner's contract, validates this shape, and writes
one profile component. It must not create the actor, establish membership, accept a caller-supplied
attachment assertion, or make partial drafts. CH5 calls it only within its root atomic creation
operation. A later controlled correction path belongs to CH7.

## Required decisions and dependency graph

Before implementation, confirm the proposed component/procedure/mechanic IDs and the exact
campaign-owned attachment verifier. Do not silently use any similarly named record.

~~~text
CH0 ratifies exact first SRD 5.2.1 build and locators                 [missing semantic leaf]
├─ permanent CH1 vocabulary and schema meaning confirmed             [required semantic confirmation]
│  └─ Slice 1: immutable content-definition component + recorder
│     └─ first content-definition fixtures from CH0                  [then permitted]
└─ C15 campaign-owned actor participation + active-scope check       [verified external contract]
   └─ Slice 2: character profile component + guarded recorder
      └─ CH2–CH4 composition, then CH5 atomic creation receipt
~~~

The campaign relationship is intentionally not named or created in this plan: its cardinality,
lifecycle, membership semantics, and permission check are owned by the campaign feature family.
If no such contract exists after inspection, create a dedicated campaign dependency plan before
Slice 2 rather than inserting a character-owned campaign ID or relation as a shortcut.

## Implementation slices

### Slice 1 — record immutable approved content

**Prerequisites:** CH0 has a signed complete path; permanent component/procedure/mechanic IDs and
the immutable-version semantics are confirmed; current catalog owner search is repeated.

1. Add the confirmed component schema and its non-public catalog authoring/recording contract.
2. Validate the closed kind set, canonical key, version, registered source, and locator before an
   effect is built.
3. Record only the CH0-approved versioned definition entities. They contain source identity and
   locator, not quoted source prose or mechanical grants.
4. Provide a read/query projection that returns identity, status, and source reference only; it
   cannot imply that every active definition is available for every future character.
5. Add focused positive and unchanged-on-failure tests, then run `roleplay validate catalog`.

**Exit:** a reviewer can find each first-path content definition by stable ID/key, trace it to the
registered SRD locator, prove it has no rules copy or mutable version fields, and observe invalid
or duplicate authoring leave state unchanged.

**Status: Accepted.** Receipt: [CH1 Slice 1 receipt](CHARACTER-FEATURE-01-SLICE-1-RECEIPT.md).

### Slice 2 — record campaign-scoped character profile

**Prerequisites:** Slice 1 is accepted; the campaign owner has a confirmed active
actor-to-campaign attachment contract and verifier; profile field visibility is confirmed as
campaign-visible only; permanent IDs are reconfirmed.

1. Add the confirmed profile schema and a guarded internal recorder, not a new public MCP kind.
2. Have the recorder read the campaign-owner attachment in the same operation, validate profile
   data and entity-name rules, then emit one profile effect or fail unchanged.
3. Expose only a reader/projection whose campaign visibility is enforced by the existing campaign
   scope policy; do not add authentication claims or a separate visibility ACL.
4. Test profile presence on a valid attached actor, omitted optional fields, invalid blank/null/
   overlong fields, unattached/wrong-campaign actor, repeated profile add, and no cross-owner
   duplicate data.
5. Run `roleplay validate catalog`; run focused mechanic/procedure tests. The full suite waits for
   a completed character feature acceptance boundary.

**Exit:** a profile can be recorded only for an existing actor proven attached to the stated active
campaign; the entity name is the sole display name; failed scope or validation checks write no
state; and the result contains no rules, items, campaign ID, or authority assertion.

**Status: Accepted.** Receipt: [CH1 Slice 2 receipt](CHARACTER-FEATURE-01-SLICE-2-RECEIPT.md).

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Source traceability | Every initial content fixture has exactly one registered SRD 5.2.1 source reference and a nonblank ratified locator. |
| Generic breadth | The schema accepts each declared content kind and a second key/version in tests; no fixture name or class-specific grant is hard-coded. |
| Immutability | A repeated key/version or attempted change to kind/key/version/source fails unchanged; correction requires later CH7 supersession work. |
| No source copying | Content definitions and profiles contain no source-rule prose, stat block, grant, choice, calculated result, or item data. |
| Profile identity | An attached actor's entity name is the character display name. Optional profile values are either valid nonempty text or omitted. |
| Scope | The profile recorder rejects a missing, inactive, wrong-campaign, or caller-asserted attachment and writes nothing. |
| Ownership | No profile field stores campaign identity, inventory, equipment, class/level, ability, AC, HP, relationship, or player authorization. |
| Public surface | CH1 adds no public MCP command kind. CH6 performs public discovery and any future `procedure.mcp.add-tool` confirmation. |

## Evidence, handoff, and change control

Keep the implementation receipt short: confirmed IDs and schema meaning, CH0 source fixtures and
locators, campaign attachment contract cited for Slice 2, focused test names/results, catalog
validation result, and known exclusions. Do not duplicate this evidence into the base roadmap.

Return to this plan before expanding kinds, importing a different source edition, changing
profile visibility, adding private biography data, changing immutable content, creating a profile
outside CH5, or exposing a new public command. Each is a semantic boundary and needs either a
confirmed amendment or its own successor plan.
