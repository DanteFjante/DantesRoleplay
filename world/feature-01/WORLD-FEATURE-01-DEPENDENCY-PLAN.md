# World Feature 1 dependency plan — persistent world topology

Status: **Planned; awaiting confirmation of the permanent vocabulary before Slice 1 is authorised**
Last updated: 2026-08-20

## Execution rule

This is a repository-planning artifact. It follows
[`procedure.system.create-feature`](../../catalog/procedures/system/procedure.system.create-feature.md),
[`procedure.system.verify`](../../catalog/procedures/system/procedure.system.verify.md), and the
quality structure in
[`TERRA-FEATURE-PLANNING-GUIDE.md`](../../ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md).

The catalog is the development authority. Slice 1 edits canonical catalog files, proves them with
`roleplay validate catalog` against a disposable migrated database, runs its focused test and then
the full suite at feature acceptance. It does not require importing into the persistent database.
Current catalog/database drift is an integration-play or release gate only; it must be resolved
before a persistent import, never force-overwritten.

One reviewed slice lands with its procedure contract, component definitions, catalog fixture, and
focused test. No code, catalog artifact, or live state is created by this planning pass.

## Target capability

An author can create and inspect a small persistent world consisting of one world root, one region,
three locations, and canonical travel connections, so later campaign and movement features have a
stable setting topology to reference.

### Included

- A world-root component and a reusable location component.
- Containment as the only parent/location hierarchy.
- One undirected-by-convention travel-edge relationship between locations.
- One small catalog-owned topology fixture and fresh-import readback coverage.
- A discoverable procedure describing the normal direct-effect authoring path.
- Closed status, kind, visibility, ID, containment-slot, and relationship-order conventions.

### Excluded

- Moving an actor, distance, travel time, terrain, routes, coordinates, maps, or pathfinding.
- Factions, NPC motives, facts, rumours, secrets, clues, clocks, campaigns, quests, character
  creation, combat, visibility enforcement, player authentication, or generated world content.
- A world-specific kernel table, migration, MCP tool/kind, workflow, action mechanic, event type,
  subscription, or generic schema-enforcement feature.
- A second parent, `worldId`, `locationId`, connection array, or copied adjacency state.

Movement is World Feature 2. Lore, factions, and motives are later World Features. The campaign
plan consumes this topology; it does not create another world-root representation.

## Source and contract basis

There is no external SRD rule for authored setting topology. The authoritative basis is the
repository's generic world model and the approved product boundary:

| Authority | Exact locator | Decision supplied |
| --- | --- | --- |
| Feature creation workflow | `catalog/procedures/system/procedure.system.create-feature.md`, instructions 1–12 | Repository-authoring mode, one coherent slice, no kernel game vocabulary, catalog validation, release-only persistent import. |
| World model contract | `catalog/procedures/world/procedure.world.model.md`, instructions 1–7 | Entity/component/containment/relationship ownership and permanent component-ID rules. |
| World-change contract | `catalog/procedures/world/procedure.world.change.md`, effect vocabulary and constraints | Transactional direct authoring, permanent entity IDs, containment move, directed relationships, and read-before-write discipline. |
| Naming contract | `catalog/procedures/world/procedure.world.naming.md` | Permanent world-entity naming rules. |
| Existing world/lore plan | `WORLD_AND_LORE_PLAN.md`, World root, Locations, and Slice 1 | Product boundary and later consumers. |
| Cross-system owner map | `GAME_SYSTEM_MASTER_PLAN.md`, Concept and ownership map | World/lore owns setting topology; campaign references it. |

The following are architectural decisions, not claims about a sourcebook rule: a world root and
location have different data responsibilities; containment is hierarchy; a relationship is travel
adjacency; visibility is descriptive until an audience policy exists.

## Planning inventory and overlap result

| Inquiry | Repository evidence | Conclusion |
| --- | --- | --- |
| Existing world-topology owner | Catalog searches for `world.root`, `world.location`, `location.connected`, `travel`, `region`, and `campaign` returned no existing world component, procedure, mechanic, or fixture owner. | `world.root`, `world.location`, `procedure.world.location`, and `world.location.connected-to` are new responsibilities, subject to permanent-ID confirmation. |
| Generic hierarchy owner | `procedure.world.model` and `WorldStoreTests.Containment_cycles_are_refused`. | Containment already owns one-parent hierarchy and rejects direct/indirect cycles. Do not store parent or world IDs in component data. |
| Generic link owner | `procedure.world.change` and `WorldStoreTests.Relationships_are_many_and_directed`. | Relationships are directed generic edges; an undirected travel edge needs a feature-level canonical ordering convention. |
| Duplicate behavior | `WorldStoreTests.Relating_the_same_pair_and_kind_updates_rather_than_duplicating`. | Identical directed triples update; reverse edges remain distinct, so World Feature 1 must prescribe one lexical orientation and test its fixture. |
| Component runtime boundary | `procedure.world.model`, `IEffectApplier.CheckJsonObject`. | Generic component writes require a JSON object but do not turn component schemas into a new game-specific kernel validator. Feature semantics belong in the component contract and focused catalog fixture test. |
| Catalog development gate | `CatalogValidationTests.Repository_catalog_validates_without_changing_its_files` and `procedure.system.verify`. | `roleplay validate catalog` is the disposable import/round-trip gate and does not modify the persistent database. |
| Existing fixture pattern | `CatalogFeature10Tests` and `catalog/world/entities/*.json`. | A catalog fixture plus fresh-import test is an established way to prove data shape and graph reconstruction. |

No existing artifact owns a persistent setting topology. The generic persistence layer remains its
owner only for storage and structural integrity.

## Verified existing dependencies

| Dependency | Evidence | Required behavior |
| --- | --- | --- |
| Entity/component persistence | `DantesRoleplay.DataAccess/WorldStore.cs`; catalog fixture tests | An entity has a permanent ID/name; components are JSON-object data attached by definition ID. |
| Containment | `procedure.world.change`; `WorldStoreTests.Containment_cycles_are_refused` | Each entity has at most one container; invalid containment cycles reject atomically. |
| Relationships | `procedure.world.change`; `WorldStoreTests.Relationships_are_many_and_directed` | A relationship has from, to, kind, and object data; relationships are not containment. |
| Atomic structural authoring | `procedure.world.change`, instructions 3–8 | An effect list validates and applies as one transaction or not at all. |
| Catalog fixture import | `CatalogFeature10Tests`; `CatalogValidationTests` | New fixture data can be imported into a fresh migrated database and read back without touching the live database. |
| Plan authority | `WORLD_AND_LORE_PLAN.md` Slice 1; `GAME_SYSTEM_MASTER_PLAN.md` ownership map | World/lore is the sole owner; campaign is a future reference-only consumer. |

## Recursive dependency analysis

```text
World Feature 1: persistent world topology
├─ generic entity/component persistence                             [implemented]
├─ one-parent containment and cycle rejection                       [implemented]
├─ directed generic relationships                                   [implemented]
├─ transactional direct-effect authoring                            [implemented]
├─ disposable catalog import/round-trip validation                  [implemented]
└─ world topology vocabulary and fixture                            [missing parent]
   ├─ permanent component/procedure/relationship identifiers        [missing leaf: Slice 1 approval]
   ├─ closed world-root and location data contracts                  [missing leaf: Slice 1]
   ├─ canonical containment and undirected-edge convention          [missing leaf: Slice 1]
   ├─ discoverable topology authoring contract                       [missing leaf: Slice 1]
   └─ catalog fixture and graph readback regression                  [missing leaf: Slice 1]

World Feature 2: actor movement                                    [blocked; consumes Feature 1]
Campaign Feature 1: existing-world attachment                      [blocked; consumes Feature 1]
```

The five leaves form one coherent slice. A component contract without a normal authoring contract
would be undiscoverable; a topology convention without a fixture/readback test would not prove
that a fresh process reconstructs it. Movement and campaign attachment are separate consumers and
remain blocked.

## Dependency and ownership decisions

1. **World identity is an entity plus `world.root`.** The entity's ID and name are its permanent
   identity/display name. `world.root` holds only authoritative world-level state; it does not
   contain child IDs, locations, factions, campaign IDs, clues, or an actor position.
2. **Place identity is an entity plus `world.location`.** Region and playable places use the same
   component with a closed `kind`. Parentage is derived exclusively from containment.
3. **Adjacency is one `world.location.connected-to` relationship.** It has empty object data and
   is treated as an undirected edge by future readers. Exactly one directed record is stored with
   its `from` entity ID lexically smaller than its `to` entity ID. A self edge and a reverse or
   duplicate fixture edge violate this feature contract. No component carries a connection list.
4. **Visibility is descriptive metadata.** `public`, `party`, and `gm` classify intended audience
   only. Slice 1 does not claim authorization or conceal data from trusted MCP readers.
5. **Status is present state, not a hidden control flow.** `draft`, `active`, and `archived` are
   the full initial vocabulary. `archived` remains readable but is excluded from normal discovery
   by a later projection feature; Slice 1 creates no archive transition mechanic.
6. **The normal creation path is catalog fixtures in development and one transactional
   `commit(kind: "effects")` list in live authored play.** `procedure.world.location` makes the
   component/containment/relationship ordering and conventions discoverable. It does not create a
   world-specific MCP command or bypass generic correction safeguards.
7. **Component JSON Schema is documentation and fixture-test input, not an implied generic runtime
   validator.** A later generic schema-enforcement feature, if needed, owns universal write-time
   enforcement. Slice 1 never adds setting vocabulary to C# merely to reject an invalid direct
   administrative effect.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- |
| 1 | World root, locations, canonical topology fixture | The permanent vocabulary in this plan is confirmed; generic world/catalog baseline remains green. | A fresh disposable catalog import reconstructs one root, one region, three locations, four containment edges, and two canonical adjacency edges; focused negative graph assertions and repository gates pass. |

World Feature 2 is deliberately not a slice of this plan. It begins with a separate dependency
plan after Slice 1 evidence exists.

## Slice 1 — world root, locations, canonical topology fixture

### Status and prerequisite

Pending approval of the permanent IDs and schema meanings below. Before writing, re-read the five
contracts in the source-and-contract table, search each proposed ID plus `place`, `region`,
`location`, `travel link`, `adjacency`, and `world root` across `catalog/`, and confirm the current
catalog validation baseline. Do not broaden the slice to movement, lore, factions, or campaign
creation.

### Runtime artifacts

| Artifact | Proposed ID/path | Change |
| --- | --- | --- |
| Root component definition | `world.root`; `catalog/components/world.root.json` and `.schema.json` | New. |
| Location component definition | `world.location`; `catalog/components/world.location.json` and `.schema.json` | New. |
| Governing contract | `procedure.world.location`; `catalog/procedures/world/procedure.world.location.md` | New, governing topology recording, correction boundary, inspection, and recovery. |
| Topology relationship kind | `world.location.connected-to` | New feature convention stored in generic relationship rows; no kernel registry or schema. |
| Fixture root | `world.feature-01.fixture`; `catalog/world/entities/world.feature-01.fixture.json` | New catalog-owned test world. |
| Fixture region | `region.feature-01.fixture`; `catalog/world/entities/region.feature-01.fixture.json` | New child location. |
| Fixture locations | `location.feature-01.gate`, `.market`, `.observatory`; files under `catalog/world/entities/` | New child locations. |
| Fixture links | `catalog/world/relationships.json` | Revise only to add two canonical topology edges. |
| Focused regression | `DantesRoleplay.Tests/CatalogWorldFeature1Tests.cs` | New fresh-import/readback and negative-convention coverage. |
| Catalog manifest | `catalog/manifest.json` | Update through the established catalog hash/manifest workflow. |

No mechanic source, event type, subscription, migration, C# world-rule helper, public command kind,
or production campaign fixture is created.

### Governing contracts and source locator

Immediately before implementation, read `procedure.system.create-feature`, `procedure.system.verify`,
`procedure.world.model`, `procedure.world.change`, `procedure.world.naming`, and the exact Slice 1
section of `WORLD_AND_LORE_PLAN.md`. There is no SRD source reference because this is authored
setting structure, not a D&D rule.

### Data/input contract and required state

`world.root` data is a complete object with exactly:

| Field | Type and closed values | Semantics |
| --- | --- | --- |
| `status` | string: `draft`, `active`, `archived` | Required lifecycle classification. |
| `summary` | trimmed nonempty string, 1–1,000 Unicode scalar values | Required concise setting premise. Entity name is not repeated here. |
| `visibility` | string: `public`, `party`, `gm` | Required descriptive audience classification. |

`world.location` data is a complete object with exactly:

| Field | Type and closed values | Semantics |
| --- | --- | --- |
| `kind` | string: `region`, `settlement`, `site`, `interior` | Required location category. A region is a location that can contain locations. |
| `status` | string: `draft`, `active`, `archived` | Required lifecycle classification. |
| `summary` | trimmed nonempty string, 1–1,000 Unicode scalar values | Required player-facing short description. |
| `visibility` | string: `public`, `party`, `gm` | Required descriptive audience classification. |

For both components: missing required fields, `null`, wrong scalar type, empty/whitespace summary,
unknown object key, unknown enum, array root, and bare scalar root are invalid fixture/contract
data. Explicit empty collections are not valid because this release contains no collection field.
There are no optional fields. `component.set` replaces the whole object; `component.merge` is not
the normal correction path for these closed records.

Fixture required state and canonical graph:

| Entity | Components | Container / slot |
| --- | --- | --- |
| `world.feature-01.fixture` | `world.root` | none |
| `region.feature-01.fixture` | `world.location` with `kind: region` | world / `region` |
| `location.feature-01.gate` | `world.location` with `kind: settlement` | region / `location` |
| `location.feature-01.market` | `world.location` with `kind: site` | region / `location` |
| `location.feature-01.observatory` | `world.location` with `kind: interior` | region / `location` |

The two fixture relationships are, in lexical endpoint order:

1. `location.feature-01.gate` -> `location.feature-01.market`, kind
   `world.location.connected-to`, data `{}`.
2. `location.feature-01.market` -> `location.feature-01.observatory`, kind
   `world.location.connected-to`, data `{}`.

No root has a parent. Only `world.location` entities may be endpoints of this kind. The component
does not prove endpoint roles at the generic effect layer; `procedure.world.location` and the
focused fixture test own the convention until a future guarded topology authoring feature is
separately planned.

### Recording behavior

For catalog authoring, add definitions before entities, use entity-file container metadata for the
four containment edges, and add exactly the two relationship records above. The catalog importer
performs the structural writes in dependency order.

For a later live authoring session, `procedure.world.location` requires one read of intended
entities/definitions, then one `commit(kind: "effects")` list ordered as: entity creation, component
adds, containment moves, relationship creates. A normal creation creates all required records in
one transaction. Corrections are explicit full component replacement or a deliberate structural
effect list after inspection; direct generic effects are administrative and must follow this
contract, not be narrated as player movement.

### Result and effects

Catalog validation/import must produce exactly five fixture entities, five component instances,
four containment edges, and two relationship edges. It does not run a mechanic, roll randomness,
create a campaign, emit a world-feature semantic event, or create notifications.

The live normal authoring equivalent is one accepted effects operation containing 16 effects:
five `entity.create`, five `component.add`, four `containment.move`, and two
`relationship.create`. If any structural effect is invalid, the entire list is rejected and leaves
no partial fixture state.

### Invariants, failure behavior, and non-goals

- Entity IDs are permanent and exact; names are display values only.
- A location's current parent derives solely from containment; world membership derives by walking
  containment to a root and is never copied into component data.
- Each canonical adjacency edge has empty object data and one lexical orientation; readers must
  consider both incoming and outgoing edges when later presenting connected locations.
- Containment cycles, self containment, missing endpoints, and invalid JSON effects are rejected by
  existing generic structural validation. A reverse adjacency is not generically rejected; the
  feature contract/test catches it in authored fixture data and a later guarded writer may enforce
  it for untrusted inputs.
- This slice does not assert player-safe visibility filtering, reachability, route existence,
  movement legality, distance, current actor location, campaign attachment, or any lore state.

### Slice 1 implementation sequence

1. Confirm the permanent IDs and data meanings listed above with the reviewer. If any owner search
   finds an overlap, stop and revise this plan.
2. Re-read the governing contracts and inspect component/fixture/manifest conventions in the
   current catalog.
3. Add the two component definitions and schemas, the procedure contract, five fixture entities,
   and two relationship records in canonical catalog locations.
4. Add the focused fresh-import test. It must assert component JSON, containment hierarchy,
   adjacency orientation/data, entity/component/edge counts, and that no unrelated existing fixture
   changed.
5. Add negative fixture/contract assertions for unknown component fields, invalid enum, empty
   summary, root-with-container, self containment, containment cycle, non-location endpoint,
   self relationship, reverse edge, duplicate edge, and nonempty edge data. Where the generic
   runtime cannot reject a feature convention, assert the focused catalog fixture validator/test
   rejects it rather than claiming universal direct-effect enforcement.
6. Run focused tests while iterating. Run `roleplay validate catalog` after catalog edits, then
   the full suite once the Slice 1 matrix passes. Run `git diff --check`.
7. Do not import into the persistent database unless preparing integration play. If that later
   occurs, inspect `roleplay import catalog --dry-run`, resolve catalog/live drift deliberately,
   import, and verify agreement.
8. Record commands/results in a short World Feature 1 receipt, mark only Slice 1 verified, update
   the roadmap/handoff, and stop. Do not begin movement.

### Slice 1 acceptance matrix

| Test class | Input/setup | Exact expected result | State/evidence assertion |
| --- | --- | --- | --- |
| Happy path | Fresh import of the five-entity fixture | Root, region, three locations, 5 components, 4 containment edges, 2 relationships | Exact IDs, component data, slots, endpoint order, kind, and `{}` data read back. |
| Hierarchy | Read region and all fixture locations | Region is directly in root; each location is directly in region | No entity has a duplicated world/parent field; root has no container. |
| Differential | Change only `location.feature-01.market` summary in a disposable catalog copy | Only that component data differs | All IDs, containments, relationships, and other component bytes are identical. |
| Closed data | Each invalid component fixture case | Focused schema/fixture validation identifies the exact invalid field | No disposable import state is accepted for that invalid fixture. |
| Structural invalidity | Self containment, indirect containment cycle, root container, missing endpoint | Existing structural validation rejects the invalid graph | No partial import/effects result; baseline fixture remains unchanged. |
| Canonical adjacency | Reverse duplicate, duplicate, self edge, non-location endpoint, nonempty edge data | Feature fixture test rejects each convention violation | Valid fixture has exactly the two expected lexical edges and no reverse companion. |
| Determinism | Two fresh imports of identical catalog | Equivalent graph and component JSON | Same IDs/counts/slots/relationships; no random state. |
| Readback | Query/store read of every new definition and fixture record | Intended active definition/fixture state exists | Component definitions and entities exactly match catalog files. |
| Restoration | Disposable invalid-fixture database/copy | Test cleanup removes temporary copy/database | Repository catalog and existing Feature 10 fixture hashes remain unchanged. |
| Repository | Focused test, full suite, `roleplay validate catalog`, `git diff --check` | All commands pass | Record final command output/counts in the receipt; no persistent import required. |

### Slice 1 exit gate

Slice 1 is verified only when the component/procedure vocabulary is confirmed, every acceptance
row passes against a fresh disposable catalog import, exact graph readback is recorded, all
temporary test material is removed, `roleplay validate catalog`, the full suite, and
`git diff --check` pass, and a receipt names the result. Otherwise it remains planned. World
Feature 2 movement and every campaign/lore consumer remain blocked.

## Plan-quality audit

1. Yes — one setting-topology outcome with explicit exclusions.
2. Yes — the non-SRD design basis and exact governing contracts are stated.
3. Yes — catalog and repository overlap searches cover proposed IDs and common synonyms.
4. Yes — every existing dependency cites a contract or focused test.
5. Yes — each missing dependency is a Slice 1 leaf; later movement/campaign work is blocked.
6. Yes — entity identity, component state, containment, adjacency, derived membership, and
   transient authoring input have one owner each.
7. Yes — Slice 1 includes component contracts, normal authoring procedure, fixture, and test.
8. Yes — Slice 1 is the sole proposed implementation pass.
9. Yes — closed fields and missing/null/empty semantics are explicit.
10. Yes — canonical graph/order, effect counts, result state, and failure limits are testable.
11. Yes — the matrix covers positive, differential, closed input, missing/corrupt graph,
    determinism, effects/state integrity, readback, cleanup, and repository checks.
12. Yes — repository validation is correctly distinguished from optional persistent import.
13. Yes — disposable fixture cleanup and unchanged existing fixtures are explicit.
14. Yes — the exit gate is objective and all-or-nothing.
15. Yes — no executable source, commit payload, or copied runtime schema is embedded.
16. Yes — this planning pass stops before implementation.

## Plan-change rule

Stop and revise before implementation if an owner search finds an existing setting/location
artifact, if the catalog format cannot represent the fixture graph without a kernel change, if the
reviewer changes the permanent vocabulary or visibility/status semantics, or if a later consumer
requires a field that would duplicate containment or adjacency. Descend to a new plan rather than
adding campaign, movement, lore, routing, or authorization behavior to this slice.
