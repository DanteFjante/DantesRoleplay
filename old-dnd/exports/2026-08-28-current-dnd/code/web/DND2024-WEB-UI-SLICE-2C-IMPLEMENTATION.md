# D&D 2024 web UI Slice 2C implementation — activated item facts

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 2 partial delivery, B2/C3 immutable-content projection
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable to this projection**; exact source ID/locator values remain
inside each accepted activated content record and are displayed without changing their meaning.
Outcome: project activated immutable entity documents into the explicitly published application
catalog, expose one exact private catalog-record read, and enrich direct inventory cards with the
matching item-definition facts and provenance.
Exclusions: nested containment, content installation into campaign state, content editing, generic
catalog browse/search UI, prices or missing item behavior, rule calculations, burden/capacity totals,
actions, +/- controls, migrations, MCP changes, and live page activation.
Allowed files/areas: generic catalog-navigation materialization/tests, the application web catalog
read endpoint and remote-path policy, the existing D&D browser asset, focused web tests, this
document/receipt, the D&D web dependency plan, and the web roadmap.
Stop point: each valid direct inventory item may display exact activated item-definition content
when its application catalog is explicitly published. Missing/unpublished/drifted content remains
visibly unavailable and never falls back to runtime guesses.

Model assignment: `gpt-5.6-sol`, high reasoning, because this slice freezes a new public catalog
kind and application route across activation, source trust, catalog navigation, and web isolation.
Contained D&D presentation remains within the previously assigned `gpt-5.6-terra` high boundary.

## Confirmed decisions

The user's 2026-08-27 instruction to continue follows the Slice 2B receipt, which names activated
immutable item information as the next boundary. It confirms:

- public catalog kind `entity` for activated text JSON documents below a `content/entities/`
  source path;
- qualified navigation identity `<applicationId>.<entityFile.id>`, retaining the authored entity
  ID as an exact search alias and inside the content JSON; and
- private GET route
  `/api/applications/{applicationId}/catalog/records/{qualifiedId}` with required `collection`.

Publication remains controlled exclusively by `IPublicApplicationCatalogPolicy`. Application
registration, source activation, local access, or a runtime definition component does not publish
content. Existing mechanic/procedure/query catalog identities and behavior remain unchanged.

## D&D 5e 2024 alignment

| Concern | Accepted owner | Slice consequence |
| --- | --- | --- |
| Immutable definition identity | activated `content/entities/**` entity document | Match `dnd2024.item-instance.definitionId` to the authored entity `id`; never infer by name. |
| Item facts | `dnd2024.item-definition` component within the activated entity document | Display stored kind, stack policy, rational mass, capacity, equipment modes, currency denomination, and profile references only when present and structurally valid. |
| Source provenance | item definition `sourceRef` plus activated winner source ID/path/fingerprint | Show the exact source ID/locator and retain projection provenance; do not replace or broaden a locator. |
| Publication | `IPublicApplicationCatalogPolicy` | An unpublished application has no public content projection even when its source is activated. |
| Runtime state | application ECS item instance/quantity/equipment and containment owners | Catalog facts enrich presentation only; they do not become runtime components or override custody/state. |

## External implementation reference

No Foundry dnd5e review is required because this slice adds no D&D rule, calculation, eligibility,
transition, or item behavior. No external code, data, or asset is adopted.

## Prerequisite evidence

- [Slice 2B receipt](DND2024-WEB-UI-SLICE-2B-RECEIPT.md) accepts exact direct custody and records the
  immutable public-content projection as the next missing owner.
- The accepted D&D static-content design states that records below
  `catalog/applications/dnd2024/content/entities/` are immutable, source-hashed activation winners
  and are not automatically installed into campaign state.
- `EntityFile.Parse` is the existing generic parser for the authored entity envelope. This slice
  reuses it instead of adding a second JSON-envelope parser in catalog materialization.
- `ActivatedApplicationCatalogMaterializer` already verifies active source registration, winner
  trust/path/hash/length, explicit publication, drift, bounds, and record provenance for the public
  catalog.

## Runtime artifacts

- Extend `ActivatedApplicationCatalogMaterializer` to recognize text JSON winners structurally
  beneath `content/entities/`, parse them with `EntityFile`, and emit kind `entity` records in the
  existing application collection.
- The public record retains exact source content JSON, authored entity ID/name, a deterministic
  qualified navigation ID, exact local-ID alias, active status, projection version 1, and activated
  source provenance. Its navigation path is `entities/` plus validated source subdirectories.
- Add the one private application catalog-record GET route over the existing
  `IPublicApplicationCatalogProvider`; extend remote-path matching only for that exact shape.
- Extend direct item-card hydration with one exact catalog record read and display accepted
  `dnd2024.item-definition` fields/provenance. Add no new custom element.
- Add no database table/row, migration, catalog source file, source registration, activation,
  component/mechanic/procedure ID, MCP operation, or D&D C# branch.

## Authoritative state and closed input

Catalog materialization accepts only the current active manifest and source-root resolver. A
candidate must be an active text JSON winner with a path containing exact adjacent segments
`content/entities`; it must pass retained source registration/fingerprint/length/path checks and
`EntityFile.Parse`. The public policy must explicitly publish the application.

The route accepts exact application ID and qualified record ID plus required collection. Existing
catalog bounds apply. The browser constructs the deterministic qualified ID from its exact current
application ID and the stored item `definitionId`, requests only that record, and verifies that the
returned entity JSON has the same authored ID before reading the item-definition component.
Callers never supply content JSON, source truth, fingerprints, item facts, derived values, or
authorization.

## Behavior, result, and typed effects

- Existing mechanic/procedure/query records retain identity, path, ordering, search, inspect, and
  content behavior.
- Entity records are navigable under `entities/<source subdirectories>`, searchable by their exact
  authored entity ID alias, and inspectable only through their qualified record identity.
- Malformed/duplicate/out-of-application IDs, invalid paths/content, source drift, untrusted missing
  roots, or catalog bounds fail the entire public snapshot closed.
- A valid direct inventory card adds authored definition name, kind, stack policy, exact rational
  mass/capacity, equipment modes, currency denomination when present, and exact source locator.
- Missing/unpublished/unknown/corrupt/mismatched definitions keep the runtime item card and show
  “Definition unavailable.” Browser code does not fall back to runtime entity names as static facts.
- No typed effect, transaction, content installation, or state transition exists.

## Failure, replay, and rollback contract

Unknown application, unavailable public catalog, unknown collection/record, malformed IDs, wrong
application-qualified IDs, and forbidden remote path shapes fail without a write. Source drift or
invalid content prevents publication rather than returning a partial catalog. One item-definition
read failure is isolated to that card and does not erase custody, quantity, equipment, or other
character panels. Repeated reads are side-effect free; replay and rollback are not applicable.

## Implementation sequence

1. Extend and test activated public catalog materialization for generic entity documents while
   preserving existing record behavior and publication failure closure.
2. Add and test the exact private application catalog-record route, rate limit, isolation, error
   mapping, and remote-path closure.
3. Enrich inventory cards from exact catalog content with explicit unavailable states.
4. Run catalog validation, JavaScript/build/focused/full verification, hand off one disposable
   preview, write the receipt, update Order 2 once, and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive projection | Valid activated entity JSON becomes one kind `entity` record with exact content/provenance and deterministic qualified ID/path/alias. |
| Existing compatibility | Mechanic/procedure/query counts, identities, search, browse, and inspect behavior remain green aside from deliberate entity additions. |
| Publication boundary | Empty/unpublished policy exposes no entity content; explicit policy exposes only the selected application. |
| Drift/invalid content | Source length/hash/root drift and malformed/duplicate/invalid entity documents fail the snapshot closed. |
| Route boundary | Exact private GET returns a public record; unknown/wrong-app/unpublished inputs fail; no control route is used. |
| Item enrichment | Matching exact definition ID displays accepted facts and source locator without recalculation. |
| Partial failure | Missing/unpublished/corrupt/mismatched definition is visibly unavailable while runtime item/custody remains. |
| No state authority | No campaign component, containment, source, activation, catalog file, or browser storage is changed. |
| Game presentation | Facts appear as compact item badges/detail strips, not a generic JSON form or administrative table. |

## Verification commands

- Focused `ActivatedApplicationCatalogTests` for entity projection, exact alias/inspect, publication,
  source drift, and existing records.
- Focused `WebInterfaceTests` for exact route inventory/rate limit, application/publication
  isolation, remote-path closure, browser definition hydration/presentation, and no-write behavior.
- `roleplay validate catalog` against a fresh disposable database.
- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- `dotnet build DantesRoleplay.slnx --no-restore`
- Full core and local-AI test suites.
- `git diff --check` plus trailing-whitespace checks over Slice 2C files.

The MCP protocol walk is not required because this slice changes no MCP operation or dependency
registration.

## Completion receipt and exit gate

Write `DND2024-WEB-UI-SLICE-2C-RECEIPT.md`, mark activated item facts accepted, and stop. Nested
inventory, generic content browser, calculations, actions, +/- controls, application-page
association, live invalidation, and live activation remain outside this slice.
