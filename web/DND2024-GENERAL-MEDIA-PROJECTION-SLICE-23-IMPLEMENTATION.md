# D&D 2024 general media projection Slice 23 — entity visuals and Current scene art

Status: **accepted**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5; World-tab completion Leaf E
Dependency tree/leaf: `web/DND2024-WORLD-TAB-COMPLETION-DEPENDENCY-TREE.md`, E1–E2
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**; reviewed presentation metadata defines no D&D rule
Outcome: authorized portraits, location settings, current-scene art, and clue handouts render from
one generic entity-owned media record without leaking hidden media metadata
Exclusions: maps, media upload/editing, generated chat/session history, inferred scenes or
participants, clue reveal writes, D&D mechanics, migrations, and Caldris-specific C# logic
Allowed files/areas: the new generic catalog component/procedure and manifest; the authorized
knowledge notebook entry DTO/reader and HTTP projection needed to retain an already-admitted media
owner; the D&D 2024 web adapter/read models/components/styles/tests and reviewed asset copy list;
one Caldris update-only World manifest; owning roadmap/dependency rows; publication backup/bundle;
this plan and receipt
Stop point: the three confirmed Caldris bindings are live and the verified page revision is active;
stop before adding new content entities, changing reveal/current-scene state, or implementing chat

## Confirmed decisions

The user's 2026-08-30 confirmation approves:

- permanent component ID `game.core.world.media.visual` and procedure ID
  `procedure.game.core.world.media`;
- the closed portrait/setting/scene/handout, Player/DM variant, provenance, hash, dimensions, and
  lifecycle semantics in this document;
- additive public read-model fields containing only authorized resolved media;
- registration/activation, identical dry-run/commit live import, page-bundle publication, and final
  acceptance inside this boundary.

No migration, public protocol kind, relationship kind, or media-authoring endpoint is introduced.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Entity, clue, scene, and audience state | repository presentation state, not an SRD rule | World knowledge/current-scene/audience owners | media never changes identity, reveal, participants, or scene selection |
| Maps | display-only map metadata | `game.core.world.map.visual` | map navigation and imagery remain independent |
| D&D mechanics | excluded | existing D&D owners | no calculation, eligibility, action, DC, outcome, or C# rule branch |

## External implementation reference

Foundry dnd5e inspection is not applicable because this slice adds no D&D rule, mechanical state,
character statistic, or encounter behavior.

## Prerequisite evidence

- Current View Slices 1–5 already resolve exact Exploration, Conversation, and Combat selectors,
  visible participants, authorized locations, and scene affordances without prose inference.
- The knowledge endpoint omits unrevealed Player clues and the DM Player-preview path does not read
  bound knowledge.
- Map visuals already prove exact audience-variant selection while remaining a separate semantic
  owner.
- `world/caldris/CALDRIS-VISUAL-PACK.md` and the accepted playable-opening receipt record reviewed
  bytes, dimensions, hashes, and grounded storybook style.
- The accepted live Caldris records already contain `actor.caldris.tibb-fallow`,
  `location.caldris.bramblebridge`, and `clue.caldris.q01.barge-timing`.

## Runtime artifacts

- Generic component and schema: `game.core.world.media.visual`.
- Generic governing procedure: `procedure.game.core.world.media`.
- One update-only manifest adds active media components to the three existing Caldris owners.
- The published bundle includes only the three confirmed reviewed images and maps their admitted
  asset keys to revision-scoped page assets.
- Closed projected media contains resolved URL, alt text, dimensions, and slot only. It never emits
  catalog asset keys, hashes, provenance, hidden variants, or a hidden-media indicator.

## Authoritative state and closed input

SQLite remains live authority after governed activation/import. The catalog owns component meaning.
Existing location, person, clue/reveal, interaction/encounter, current-scene, and audience owners
select the only eligible media owner IDs. The browser supplies no entity ID, slot, asset key,
variant, participant, reveal state, or scene priority.

The server adapter reads a media component only for an already-authorized owner and selects the
exact effective perspective. Inactive, malformed, extra-key, missing-variant, hash/asset mismatch,
or unregistered values project nothing.

## Behavior, result, and typed effects

- Tibb's authorized portrait appears in World People, Bramblebridge People, Exploration people,
  and exact visible Conversation participants; initials remain the independent fallback.
- Bramblebridge's setting appears on location Details and as the large Current scene plate. Exact
  conversation/encounter scene art would override it when present; no such Caldris record is
  fabricated by this slice.
- The token handout attaches to the existing barge-timing clue. DM receives it; Player receives it
  only when the existing clue owner includes that clue in authorized knowledge.
- Location maps continue using `game.core.world.map.visual` and the existing map registry.
- Reads are side-effect free. Live authoring is one update-only `system.world-state.sync`
  transaction after component registration/activation, previewed and committed with identical
  content and expected revisions.

## Failure, replay, and rollback contract

- Any malformed component or registry miss omits the complete affected slot and leaves a stable
  icon/initials fallback; broken images switch to the same fallback without layout collapse.
- Unauthorized owners and missing audience variants contribute zero serialized media fields.
- Duplicate/unknown slots, URLs/paths, invalid dimensions/hash/MIME/provenance, stale revisions, or
  unknown component types reject authoring before state changes.
- The World sync token is replay-safe; a stale or rejected import changes nothing. The pre-change
  application activation, live page export, and active page revision are rollback evidence.

## Implementation sequence

1. Add and validate the generic component, procedure, catalog manifest entries, and focused schema
   evidence.
2. Add closed media parsing/projection and negative Player-byte tests before UI rendering.
3. Add responsive portrait, setting, scene, and handout presentation with resilient fallbacks.
4. Copy only reviewed bytes, build, run focused/full acceptance, and inspect emitted Player bytes.
5. Register/activate the component, preview and commit the identical Caldris manifest, publish the
   bundle, verify DM/Player in the browser, then write the receipt and close the dependency row.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Tibb in World/location/Current people | authorized portrait; initials if absent or failed |
| Bramblebridge Details/Exploration | setting plate; map remains separately usable |
| exact Conversation | location scene plus only authorized participant portraits |
| exact interaction/encounter with scene media | situation scene overrides location scene/setting |
| unrevealed token clue as Player/DM preview | no URL, key, alt, ID, count, or existence signal |
| token clue as DM or revealed Player | handout renders from the same projected shape |
| malformed/inactive/unknown media | complete slot omission and clean fallback |
| repeat read/import | deterministic read; replay-safe import; no unrelated state change |

## Verification commands

- `./roleplay.cmd validate catalog`
- focused Node tests for schema, authorized media parsing, precedence, UI fallbacks, and Player-byte
  non-disclosure
- `npm test` and `npm run build:server` in `src/system/web-interface/dnd2024`
- focused web-host tests plus the full repository suite at acceptance
- identical application preview/activation and World sync preview/commit/readback
- live browser checks in DM and Player preview with map navigation regression

No MCP protocol walk is required because no protocol kind or dependency registration changes.

## Completion receipt and exit gate

Record bindings, live registration/activation/import receipts, page revision/hash, test results,
browser evidence, and deliberate chat/media-authoring exclusions in
`web/evidence/dnd2024/DND2024-GENERAL-MEDIA-PROJECTION-SLICE-23-RECEIPT.md`. Mark this document
accepted and stop at the stated boundary.
