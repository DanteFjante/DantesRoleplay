# Application kernel Slice 12A implementation — planner-neutral catalog handoff and host independence

Status: **accepted 2026-08-24**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel J / AI and host consumption](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Accept the existing effective public-catalog provider/navigator as the read-only kernel
handoff for future local and remote planners, and prove deterministic zero-application and
multi-application isolation without vectors or local completion.  
Exclusions: A new orchestration component; planner prompts or completion loops; feature-document or
vector indexes; application-base blending; intent, plan, receipt, recipe, or execution contracts;
new MCP kinds/fields; persistence or migrations; authorization expansion; action execution; web
conversation surfaces; application-specific adapters; and final Slice 12 acceptance.  
Allowed files/areas: Focused catalog-navigation and existing system-catalog protocol tests;
production catalog-navigation/MCP code only if the closed proof exposes a defect; this plan/receipt
and concise application-kernel dependency/roadmap status links. No catalog source, application
record, database, migration, local-AI assembly, or game adapter may change.  
Stop point: Stop after one zero-application host seam, two simultaneously published non-game
activated fixture applications, and the already accepted `dnd2024` application each prove bounded
list/browse/search/inspect behavior and cross-application isolation through the same existing
provider/navigator contract, with the local model and vectors absent.

## Confirmed decisions

- Slice 0 S0.10 already confirms that local and remote models consume the same effective qualified
  records and exact catalog contracts while remaining downstream, non-authoritative consumers.
- Slices 9, 10A, and 11H already accept immutable vector-free navigation, the remote
  `system.catalog.*` projection, explicit publication policy, and active procedure/mechanic
  materialization.
- `IPublicApplicationCatalogProvider` plus `ICatalogNavigator` is the existing application-scoped
  read boundary. This slice proves and documents that seam; it creates no parallel retrieval owner.
- The user's 2026-08-24 request to continue with Slice 12 confirms starting this bounded read-only
  sub-slice. It does not confirm the downstream interaction-orchestration component name, storage,
  authorization/redaction port, public interaction kinds, execution consent, or recipe policy.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Ruleset behavior | None selected or interpreted. | Application contracts/mechanics | No SRD locator or rule implementation applies. |
| Application identity | Opaque application scope only. | Application kernel | Generic tests use non-game fixtures; production code may not branch on `dnd2024`. |
| Discovery | Exact accepted public catalog summaries and contracts. | Catalog navigation | Consumers locate and inspect; they do not execute or validate game behavior. |

## External implementation reference

No Foundry dnd5e review applies. This is a ruleset-neutral host-isolation and consumption-boundary
proof, not D&D behavior or data modeling.

## Prerequisite evidence

- [Slice 11H receipt](receipts/APPLICATION-KERNEL-SLICE-11H-RECEIPT.md) proves exact active action
  documents materialize into a deny-by-default public navigator and the live `dnd2024` walk passes.
- [Slice 10A receipt](receipts/APPLICATION-KERNEL-SLICE-10A-RECEIPT.md) proves the unchanged remote
  list/browse/search/inspect projection and its typed cursor failures.
- Slice 9 contracts own the immutable manifest, application-bound requests, lexical rank, bounded
  pages, exact inspection, and authenticated snapshot cursors reused here.

## Runtime artifacts

- No new runtime artifact is expected. Extend closed tests around the production activated provider
  and the existing protocol adapter.
- If the proof finds a defect, make only the smallest generic correction inside the existing
  catalog-navigation/MCP owners and record it in the receipt.
- Add no permanent ID, public kind, schema, table, migration, source format, catalog record, or
  application-specific C#.

## Authoritative state and closed input

Application registration, source registration, active winner evidence, allowed-root configuration,
and the explicit publication policy remain authoritative. The provider yields one immutable
application-bound navigator only after that evidence materializes successfully.

The consumer supplies only an existing `ApplicationIdentifier` and the accepted bounded catalog
requests. It cannot provide source paths, hashes, publication claims, manifest fingerprints,
ranking, hidden applications, cursor keys, model output, or validation/execution assertions.

## Behavior, result, and typed effects

- Empty publication policy resolves no application and discloses no collection/count.
- Two allowed, independently activated non-game applications resolve concurrently through one
  provider instance; each returns only its own collection, records, paths, content, and cursors.
- An exact ID, phrase, or record from one application cannot be searched, inspected, paged, or
  inferred through the other application scope.
- Direct provider/navigator consumption and remote `system.catalog.*` consumption observe the same
  qualified IDs and exact content for a fixture record.
- Existing `dnd2024` live evidence remains the one-application application-owned proof.
- No local completion/embedding provider is constructed or called. Effects, state writes,
  interactions, executions, and database transactions: none beyond existing read audit behavior.

## Failure, replay, and rollback contract

Unknown, unpublished, inactive, malformed, drifted, or cross-application access remains fail-closed.
Remote failures use existing `PUBLIC_CATALOG_UNAVAILABLE`, `INVALID_PAYLOAD`, catalog-not-found, and
cursor errors. Direct consumers receive existing argument/not-found failures. No result may fall
back to another application, blend application scopes, consult a model, or mutate source,
activation, state, or catalog authority. There is no write transaction or rollback path.

## Implementation sequence

1. Add a production-provider test with no published app and two exact activated non-game fixture
   apps, including positive deterministic retrieval and negative cross-app searches/inspection.
2. Extend the existing direct protocol proof so two application catalogs are remotely traversable
   through the same provider and exact direct/remote results agree.
3. Re-run the existing live `dnd2024` walk and component/game-literal guards.
4. Run full shared/local-AI suites, warning-free isolated solution build, and `git diff --check`;
   write the Slice 12A receipt and status links.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Zero application | Empty policy/provider reveals no catalog and requires no local AI. |
| Multi-application | Two active non-game apps coexist and independently list/search/inspect. |
| Isolation | Cross-app search, ID, content, cursor, and unpublished-app access do not leak. |
| Symmetry | Direct and remote reads return the same exact qualified record/content. |
| Determinism | Existing ordering, ranking, page bounds, and snapshot cursor rules remain unchanged. |
| Optional AI/vector | No completion, embedding, or vector service is needed for any accepted read. |
| One application | Existing live `dnd2024` activation/catalog walk remains green. |
| Ruleset neutrality | Generic production code/local AI contain no application ID or game branch. |
| Compatibility | Exactly three verbs and every existing catalog request/result remain unchanged. |

## Verification commands

- Focused activated-catalog and system-catalog protocol tests plus component/game-literal guards.
- Full shared and standalone local-AI test suites.
- Warning-free isolated-output solution build and `git diff --check`.
- Catalog validation is not required because this slice changes no catalog artifact.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-12A-RECEIPT.md`, mark this document accepted,
update the application-kernel J/status links once, and stop. The downstream interaction-
orchestration Slice 12B confirmation package remains the next semantic gate; do not create its
component, contracts, migrations, public kinds, completion loop, execution path, or recipe store in
this slice.
