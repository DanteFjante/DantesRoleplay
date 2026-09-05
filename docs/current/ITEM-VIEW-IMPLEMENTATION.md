# Item view implementation plan

Version 1.9 · 5 September 2026

## Purpose and delivery boundary

Build a full item dossier that opens from a character's inventory. It has three tabs: **Details**, **Known recipes**, and **Known uses**. Recipes are split into **Makes this item** and **Uses this item**, as requested. The dossier describes the actual item instance and the knowledge of the selected character, while preserving the current character sheet, nested inventory, and DM and Player switch.

This document specifies implementation work; it is not evidence that the published feature exists. IV00 contracts were approved by the user on 5 September 2026. IV01–IV08 are implemented within their documented supported boundaries, including IV06 association review and disposable fixtures; IV09–IV10 remain pending. Validation results and exclusions are recorded in the implementation commits. The concrete review packet is [item-view-contracts/README.md](item-view-contracts/README.md). The requested document does not itself authorize new runtime identities, catalog synchronization, or activation. Apply the repository working agreement at each concrete boundary, using authorization already present in the implementation task rather than repeatedly requesting it.

The first release is read-only. Opening a tab must not equip, consume, identify, reveal, craft, advance time, or record a discovery. Use and Start crafting are explicitly deferred to follow-up slices after this release. Do not infer missing recipes, properties, or uses from prose or item names.

The Markdown document is the execution source. Read `AGENTS.md`, `docs/current/README.md`, and the relevant current topic guide before executing a slice. Paths below are relative to the repository root; aliases are defined in the owner map.

## Product decisions

| Area | Required behavior |
| --- | --- |
| Entry | An item's name and image open its dossier. A container's disclosure control expands its contents independently. Avoid a link or button nested inside another interactive control. |
| Presentation | Use the existing dark character-dossier style. Show a large illustration, known display name, item type, owner or container context, and the three tabs. Use a full workspace view, not a small tooltip. |
| Details | Show known description, quantity, weight and material; current equipment state; and applicable weapon, armor, tool, consumable, charge, durability, or attunement information when supported by records. Missing fields are omitted or described as unavailable; never default them to zero. |
| Recipes | Show two independently empty groups: Makes this item and Uses this item. A recipe can occur in both when its authored associations warrant it. Show ingredients, outputs, tools, time, requirements, provenance, and supported availability information. |
| Uses | Show known activities, effects, costs, requirements, and recorded applications. A known purpose can be descriptive even when no executable action exists. Label suspected or believed information explicitly rather than presenting it as a confirmed capability. |
| Return | Back to inventory restores the same character, inventory scroll position, and expanded containers. Browser Back and Forward behave consistently. |
| Direct navigation | Preserve the item, character, tab, and requested perspective in a navigable route. Reload reauthorizes the route. URLs identify a selection; they grant no access. |
| Perspective | Player means the selected character's knowledge. DM shows the complete authorized record and can distinguish the selected character's knowledge. A DM preview must not invent an Actor identity or alter the ambient host seat. |
| Mobile | Fit at 390 CSS pixels and at narrow supported widths without horizontal page overflow. Tabs remain reachable, images preserve their aspect ratio, and properties wrap. |

The item instance is primary. Ganji's quarterstaff may differ from another quarterstaff even when both share a definition. Instance names and properties are not automatically safe to display: an unknown magical identity can be disclosed by a name or image just as easily as by descriptive text.

## Verified starting points

The following observations came from source inspection on 5 September 2026. Recheck the specific owners before implementation because this checkout contains concurrent work.

- `InventoryTree.tsx` renders nested inventory with native disclosures and item identity content. It does not currently provide an item dossier action.
- `CharacterInventoryItemV2` contains instance and definition identities, quantity, containment position, depth, child count, equipment slots, and optional media. The character dossier includes definition summaries and source references, not a complete item view.
- The inventory contract is bounded to depth 4 and at most 512 items. Preserve explicit omission information; do not promise access to descendants that were not materialized.
- The generic read-model endpoint authorizes an Actor to read their own entity and allows an authorized GameMaster to request Player projection. The GM seat has no bound Actor ID. Generic Player audience alone does not establish what a particular character knows.
- Catalog owners exist for physical properties, weapons, tools, consumables, item activities, crafting recipes, and magic-item knowledge. General knowledge already has character-specific effective-state and authorization owners.
- Some crafting records are incomplete. The inspected potion-of-healing recipe has an empty output list and prerequisite predicates with empty arguments. Its name is not a substitute for reviewed associations or working eligibility rules.
- The web application serves immutable published bundles. Building source alone does not update the running page. The earlier character-layout work demonstrated why live publication and browser verification must be separate acceptance steps.

## Owner map

Aliases: **WEB** = `src/system/web-interface/dnd2024`; **APP** = `catalog/applications/dnd2024`; **KNOWLEDGE** = `src/system/knowledge`; **READMODELS** = `src/system/interaction-orchestration`.

| Concern | Existing owner or proposed file |
| --- | --- |
| Inventory entry and character selection | `WEB/src/components/character/InventoryTree.tsx`, `WEB/src/components/PartyView.tsx` |
| Browser composition and audience loading | `WEB/src/components/DndInformationHub.tsx`, `WEB/src/server-host/main.tsx`, `WEB/src/server/game-server-context.js`, `WEB/src/server/connected-hub-envelope.ts` |
| Types and existing visual styles | `WEB/src/data/hub-types.ts`, `WEB/src/data/section-state.ts`, `WEB/src/character-page.css` |
| Proposed item UI | `WEB/src/components/items/ItemView.tsx`, `ItemDetails.tsx`, `ItemRecipes.tsx`, `ItemUses.tsx`; `WEB/src/item-page.css` |
| Proposed route and read client | `WEB/src/data/item-view-route.ts`, `WEB/src/server/item-view-client.ts`; reuse the existing `WEB/src/data/view-read-client.ts` |
| Generic read authorization | `DantesRoleplay.MCPServer/ApplicationReadModelWebEndpoint.cs`, `READMODELS/domain/ApplicationReadModelContracts.cs`, `READMODELS/hosting/ApplicationReadModelService.cs` |
| Character knowledge | `KNOWLEDGE/persistence/AuthorizedKnowledgeCoordinator.cs`, `AuthorizedKnowledgeNotebookReader.cs`, `ApplicationKnowledgeEffectiveStateResolver.cs`, `ApplicationKnowledgeActorParticipationVerifier.cs`; relevant contracts under `KNOWLEDGE/domain/` |
| Knowledge declarations | `APP/metadata/authorized-knowledge.json`, `APP/components/dnd2024.magic-item.knowledge.schema.json` |
| Item definitions and actions | `APP/components/dnd2024.item.*.schema.json`, `APP/components/dnd2024.item-activity.schema.json`, `APP/mechanics/data/dnd2024.mechanic.item-activity.use.*`, `APP/mechanics/data/dnd2024.mechanic.item.equipment.read.*` |
| Recipes and later execution | `APP/components/dnd2024.crafting.recipe.schema.json`, `APP/content/entities/equipment/crafting/`, `APP/mechanics/downtime/dnd2024.mechanic.downtime.begin.*` |
| Existing character projection | `APP/queries/character/dnd2024.query.character-dossier-v1.json`, `APP/mechanics/data/dnd2024.mechanic.character-dossier-v1.project.*` |
| Media | `DantesRoleplay.MCPServer/EntityMediaWebEndpoints.cs`, `src/system/entity-media/persistence/EntityMediaService.cs`, `WEB/src/components/EntityMediaGallery.tsx` |
| Publication | `DantesRoleplay.Web/Pages/WebPageAdministration.cs`, `DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs`, `WEB/scripts/create-release-manifest.mjs`, `WEB/scripts/verify-live-release.mjs` |

Proposed files are not existing owners or registered identities. New catalog files must use the repository's actual layout and contract format established in IV00. Do not add a new abstraction where an existing owner can fulfill the same responsibility.

## Read architecture

Use actor-scoped inventory projections for the first release. Keep the existing entity read-model route pointed at the selected character, not the item. Each projection verifies the selected item against the character’s bounded inventory and returns only that item’s authorized tab data. IV00 found that full dossiers for an entire inventory cannot reliably fit the existing 65,536-byte limit. A proposed generic closed query-input extension carries the selected item ID and pagination offsets; it grants no item authority. See the IV00 packet for exact drafts and backward compatibility requirements.

Create three effect-free catalog queries, with these proposed names reserved for contract review in IV00:

- `dnd2024.query.inventory-item-details`
- `dnd2024.query.inventory-item-recipes`
- `dnd2024.query.inventory-item-uses`

Use corresponding `dnd2024.mechanic.inventory-item-details.project`, `dnd2024.mechanic.inventory-item-recipes.project`, and `dnd2024.mechanic.inventory-item-uses.project` mechanics. Each binds `subject` to the authorized character and `campaign` to the host-authorized campaign. These six identities were approved in the task on 5 September 2026. Register them only in their owning implementation slices with exact canonical hashes and the required validation; runtime synchronization remains IV10. A new contract must not reinterpret the meaning of an existing character query.

The host resolves the authorized observer, campaign participation, and effective knowledge before sensitive content is materialized. Reuse the generic knowledge resolver and catalog-declared binding metadata. If the mechanic context lacks an authorized knowledge input, extend its generic materialization contract explicitly; do not place a D&D recipe list, knowledge formula, or item ID in C# and do not let the browser supply trusted knowledge state.

For Player and DM preview, the observer is the selected, authorized character. For DM, the same selection supplies the comparison of known information while the DM grant controls full visibility. Never select the first party member implicitly to resolve knowledge. Unknown or inactive observers fail closed before loading private details.

The catalog owns item semantics, instance-versus-definition precedence, recipe associations, eligibility interpretation, and use descriptions. SQLite owns live instances and learned knowledge. Public rule text does not by itself prove that a character knows a special recipe, item identity, or hidden use.

### Projection and state contract

Each response carries schema version, character ID, item ID and perspective in its data; state-space, resolution, source-revision and result fingerprints remain in the existing generic read-model envelope. Knowledge and inventory revision changes must invalidate the result. Details contain item identities, known display fields, safe property groups, containment context, and media descriptors. Recipe and use responses contain authorized associations keyed by item ID and source references suitable for display.

Sections use distinct `ready`, `empty`, `partial`, `forbidden`, `unavailable`, and `stale` states. Loading belongs to the client. Empty means a complete authorized query found no known matches. Partial or unavailable must never become “No recipes known.” Do not send hidden entries, hidden counts, private source locators, or unknown identifiers to the browser.

Keep the depth-4 and 512-item inventory membership boundary and enforce it during materialization. Details returns one item. Recipes returns up to 16 authorized entries per group in one request; Uses returns up to 32. Follow explicit offsets against an expected source revision, after authorization and deduplication. Partial results carry typed reasons and optional advancing continuation offsets, with no hidden totals. Candidate retrieval must authorize before limiting or ranking, within a 10,000-candidate work cap. Serialize a fitting prefix within 65,536 UTF-8 bytes; an unfit individual record fails explicitly. Do not relax the host safety limit.

Fetch Details on opening the first item. Fetch recipes and uses on their first tab visit. Reuse the current read client for cancellation, validation, retries, and freshness. Cache by principal/binding, application, state space, campaign, observer, perspective, query version, selected item, page offsets and source revision. Never reuse a DM payload in Player view. A perspective change clears the currently visible item content before the replacement read resolves.

## Slice dependency map

| Slice | Deliverable | Depends on |
| --- | --- | --- |
| IV00 | Contract and ownership checkpoint | None |
| IV01 | Authorized observer and effective item knowledge | IV00 |
| IV02 | Canonical item Details projection | IV01 |
| IV03 | Safe item media | IV01 and IV02 |
| IV04 | Item route and navigation shell | IV00 |
| IV05 | Connected Details tab | IV02, IV03 and IV04 |
| IV06 | Reviewed recipe and discovery associations | IV01 |
| IV07 | Known recipes projection and tab | IV04, IV05 and IV06 |
| IV08 | Known uses projection and tab | IV01, IV04 and IV05 |
| IV09 | Integrated correctness and accessibility | IV03, IV05, IV07 and IV08 |
| IV10 | Reviewed runtime publication and live acceptance | IV09 |

Execute sequentially by default. Dependencies allow independent preparation, but this document does not instruct agents to spawn other agents. Concurrent work requires explicit authorization and disjoint ownership. Shared contracts, manifest edits, synchronization, and activation have one integrator.

## IV00 Contract and ownership checkpoint

**Status:** Complete. The user approved the six proposed IDs and generic contract extensions on 5 September 2026. [Review packet and executable schema checks](item-view-contracts/README.md). No catalog registration, runtime writes or publication occurred in IV00.

**Owner and files:** Integration owner; this document, the owner map, `APP/queries/`, `APP/mechanics/`, and focused read-model and knowledge tests. No runtime writes.

**Work:** Inspect the current worktree and listener, identify active tests and other publishing work, and verify the files in the owner map. Capture the exact existing character, knowledge, containment, and media contracts in the task. Prepare the three closed query and output schemas, read-only mechanic requirements, generic authorized-observer context, limits, and stable error semantics for review. Confirm bounded actor-inventory materialization, the host byte limit and any missing query-input/observer support; implement no caller-selected item authority shortcut. IV00 confirmed existing containment/reference support, but generic input, trusted observer context, materialization caps and knowledge-aware source revisions require IV01 extensions.

**Completion:** Every field has an existing authoritative owner or an explicitly proposed extension. The three query contracts agree on observer identity, perspective, item IDs, revisions, and empty versus partial semantics. Record approval for new permanent IDs and any schema-meaning or public-surface changes before registration. Unsupported facts remain unavailable, not fabricated.

**Rollback and stop:** Discard only task-owned drafts. Stop dependent slices if authoritative knowledge cannot be bound or a proposed permanent contract remains unapproved. Continue independent UI preparation against the approved fixture shape only.

## IV01 Authorized observer and effective item knowledge

**Status:** Implemented and focused checks passed. The generic input, authorization, bounded materialization and knowledge-context foundation is available in source. IV01 itself registered no item query; IV02 adds Details, while rendering and the remaining tabs stay with IV03–IV08. See the implementation commit messages for validation results and acceptance boundaries.

**Owner and files:** Generic authorization and knowledge owner; existing read-model endpoint, `READMODELS` contracts/materialization, `KNOWLEDGE` resolver and participation owners, and catalog knowledge declarations. Proposed focused test: `DantesRoleplay.Tests/ItemViewAudienceTests.cs`.

**Work:** Bind the query subject to the actual character. Actor seats remain restricted to themselves; GM preview validates the selected active character in the selected campaign and state space. Implement the reviewed closed query-input and trusted-context extensions, enforce materialization caps before unbounded loading, and bind source revisions to authorization, knowledge and candidate-set changes. Preserve legacy empty-input queries and existing error behavior. Compose applicable baseline knowledge, explicit character knowledge and exceptions, and item-specific identity/property/curse discovery. Treat public item information, special properties, recipe knowledge, and use knowledge separately. Use the existing effective-state precedence and uncertainty vocabulary rather than a new browser-specific knowledge system.

**Completion:** Fixtures cover two characters with different knowledge of the same item definition, an explicit unknown overriding a baseline, a known identity with an unknown curse, inactive participation, cross-campaign and cross-state-space selection, and Actor attempts to select another character. Denied reads expose neither private data nor existence-sensitive errors. DM preview equals the corresponding Actor projection for the same authorized observer and snapshot.

**Rollback and stop:** Restore task-owned generic changes without modifying live knowledge. Stop if filtering would occur after returning sensitive content, or if a caller can choose trusted knowledge or promote its audience. No broadening of loopback, remote, or principal access is permitted.

## IV02 Canonical item Details projection

**Status:** Implemented in the authored catalog, without live activation. The closed binding-only Details query pins its exact mechanic and output schema. Supported definition measurements/facets, instance quantity/equipment, DM durability/attunement and authorized statements are covered by real database/sandbox tests. Raw private facets remain DM-only; unsupported rule references and charges report unavailable dependencies. No quantity-zero migration is introduced: the current quantity schema requires a positive integer, while missing quantity is null and authored zero measurements/durability remain zero. The implementation commit contains the verification receipt.

**Owner and files:** D&D catalog owner; Details query and mechanic under `APP/queries/data/` and `APP/mechanics/data/`, existing item schemas, and `DantesRoleplay.Tests/ItemDetailsProjectionTests.cs`.

**Work:** Materialize the subject's bounded inventory and referenced definitions. Return known instance identity and applicable property groups. Use each field's existing precedence rule; do not introduce a blanket JSON merge between instance and definition. Include quantity, containment, equipment, physical properties, and applicable rule facets only when authored and authorized. Preserve units and distinguish unknown from zero. Return display-safe provenance and structured omission information. Reuse canonical calculations and child mechanics; do not calculate weapon or crafting rules in React or C#.

**Completion:** Tests distinguish two differently named or modified instances of one definition, quantity stacks, nested containers, missing definitions, absent optional facets, zero values, unidentified items, and depth limits. Projection has no effects, events, notifications, or live activation. Closed schemas, exact child fingerprints, and catalog validation pass.

**Rollback and stop:** Revert only the new unactivated query/mechanic records and their owned manifest entries. Do not replace live item records. Stop on unresolved precedence, missing required references, or contract/hash disagreement.

## IV03 Safe item media

**Status:** Implemented without live publication. Details now returns opaque, view-bound image links after catalog identity and inheritance checks. Each content request replays the registered view and rechecks current media visibility/metadata and the caller's grant. Instance roles take precedence; only definition illustrations/icons inherit. Missing, unknown, oversized or unavailable media uses a neutral fallback. The reusable item gallery removes stale image/caption elements synchronously on scope changes and accepts only these links. Route wiring and the connected item page remain IV04/IV05; this slice does not add an inventory click target. See the implementation commit for focused, web, protocol and catalog results and any full-suite limitation.

**Owner and files:** Entity-media owner; existing media service and endpoint, `WEB/src/components/EntityMediaGallery.tsx`, media projection helpers, and `src/system/entity-media/tests/EntityMediaTests.cs`.

**Work:** Resolve instance media first and permitted definition-role inheritance second. Bind discovery and content access to the same observer and perspective used by item Details. A GM Player preview must not enrich an item through an ambient-GM media read. Filter image content, captions, alt text, filenames exposed as labels, galleries, and source references consistently. Retain a neutral fallback for unavailable or unauthorized media; do not generate images automatically.

**Completion:** Direct content URLs cannot bypass the preview's disclosure boundary. Tests cover a safe instance image, inherited illustration, missing asset, mixed-visibility gallery, unknown identity, and rapid perspective switches. The public UI never receives blob paths or unauthorized digests. If authorized preview delivery is unavailable, show the fallback and make that limitation explicit in acceptance.

**Rollback and stop:** Restore the existing media path and neutral fallback. Never delete or rewrite live blobs as a rollback. Stop any path that would expose a GM-only image in Player preview.

## IV04 Item route and navigation shell

**Status:** Implemented without live publication. Item identity buttons open the instance selection independently of native container disclosures. Bounded `#item?...` and `#inventory?...` fragments preserve the published pathname/query and carry character, campaign, requested perspective, item and tab selections. Existing authorized hub loading resolves requested campaign/perspective; URL values and return state grant no access. All three tabs remain explicitly unavailable in this slice, and even a valid deep link displays only the neutral “Item” heading until IV05 supplies validated Details. No inventory label, media or definition is reused as a header. Tab changes replace the current item entry; Back/Forward traverse item/inventory entries. Return state retains bounded expanded IDs, scroll and item focus, including after reload; Escape returns to inventory and tab arrows/Home/End follow tab semantics. Invalid selections fail safely. Mounted tests and a local browser fixture cover nested return, focus, reload and narrow layouts; the commit receipt distinguishes isolated web proof from failures caused by concurrent catalog changes in the full solution run.

**Owner and files:** Web navigation owner; `InventoryTree.tsx`, `PartyView.tsx`, `DndInformationHub.tsx`, proposed item route and `ItemView.tsx`, and proposed `WEB/test/mounted/item-view-navigation.test.tsx`.

**Work:** Add an explicit item-opening callback using the instance ID. Keep container expansion independent. Follow the existing application route convention, encoding character, item, tab and perspective without treating them as trusted bindings. Keep Details, Known recipes, and Known uses visible with appropriate states. Preserve the inventory return context in navigation state, never as authoritative game state. Back, Forward, reload, Escape where appropriate, and keyboard focus must be deliberate.

**Completion:** Opening an item does not toggle its container or mutate inventory. Back restores the correct character, disclosures, scroll position, and focus. Unknown tabs fall back to Details; malformed or unauthorized item selections produce a safe unavailable view. A deep link reauthorizes before showing even the item header. Tests use fixtures and do not require modifying a live character.

**Rollback and stop:** Remove the opening affordance while retaining the existing inventory. Do not replace the working character page navigation wholesale. Pause integration if unrelated navigation changes conflict.

## IV05 Connected Details tab

**Status:** Implemented in source without live publication or catalog synchronization. The item route makes one bounded, actor-scoped Details read and validates the exact envelope, selection, output schema and scoped media links before showing its name or contents. Details renders authorized description, media, quantity, containment, equipment, properties, source disclosures and knowledge qualifiers; missing values remain absent and recorded zero/false values survive. The reusable client keeps at most eight selections for thirty seconds within one authorized hub-envelope lifetime. Context changes and view-invalidation, focus, page-hide and visibility events clear cached content; slow or failed reads cannot restore retired selections. Display expiry clears details and offers an explicit refresh without polling. Server changes without a delivered invalidation are detected on the next authorized refresh or bounded expiry, not instantaneously. Mounted tests and a disposable browser preview cover ordinary, special, container and unidentified items, including narrow screens. Recipes and Uses remain unavailable. See the implementation commit for the complete verification receipt.

**Owner and files:** Web item-view owner; proposed `item-view-client.ts`, `ItemDetails.tsx`, `item-page.css`, `hub-types.ts`, and focused mounted tests. Reuse current read-client and section-state owners.

**Work:** Connect the actor-scoped Details query to the item route. Render the selected instance from its authorized single-item response. Add image, description, property groups, containment context, source disclosures, and explicit states. Load on entry and cache per authorized context. On item transfer, removal, perspective change, observer change, or stale response, hide incompatible data and re-resolve. Failed reads must not resurrect a previous character's item.

**Completion:** Fixture tests and a local browser preview show a mundane item, a special instance, a container, and an unidentified item. Changing selection during a slow read never displays the old result under the new header. Item opening uses one Details request with bounded media requests, not a new campaign/world bootstrap or a per-item catalog scan.

**Rollback and stop:** Disable only item navigation and leave the existing inventory available. Stop if a render fallback turns missing data into fabricated properties or stale private content.

## IV06 Reviewed recipe and discovery associations

**Status:** Reviewed and covered by executable disposable fixtures using the unchanged recipe schema and IV01 authorization materializer. Exact definition references in `outputs` and `materialRequirements` establish independent Makes and Uses associations. A knowledge record must have one unambiguous recipe subject and an effective content-bearing state for the selected observer; uncertainty stays qualified. Knowing or carrying an item does not teach its recipe. Tests cover both groups, deduplication, unknown/familiar records, different observers, ambiguous subjects, learned/forgotten knowledge, changed material links and known incomplete recipes. No new schema meaning, IDs, catalog content or production code was needed. IV07 owns actual association projection, completeness states and rendering.

All twelve currently authored crafting records have empty outputs, absent material requirements and empty tool/crafter predicate arguments. The nonmagical and scroll templates have no canonical recipe-parameter resolution contract in the current downtime owner, which consumes a separately authored `dnd2024.downtime.definition`. They remain unresolved; the healing-potion record remains incomplete. Do not repair these records from their names, source locators, possession or tool proficiency. A known incomplete recipe with an explicit ingredient link can appear in Uses with `definition-incomplete`; an incomplete record with no explicit link cannot establish either group. Supported concrete links can proceed into IV07 without fabricating template expansion. The review packet records the exact supported boundary; the commit contains verification results.

**Owner and files:** D&D crafting and knowledge owners; existing recipe schema and records, authorized-knowledge declarations, relevant shared knowledge relationships, and proposed `DantesRoleplay.Tests/ItemRecipeAssociationTests.cs`.

**Work:** Resolve Makes associations from authored outputs and Uses associations from authored material requirements. Resolve parameterized recipes through their existing canonical rule owner; never match names or guess their output. Link character-known recipe facts through existing knowledge subject and state relationships. Possessing an ingredient, knowing an item, or having tool proficiency does not automatically teach its recipe. Reuse existing state shapes; any required semantic extension is a reviewed contract change.

**Completion:** Author disposable fixtures for one output association, one ingredient association, both associations, an unknown recipe, a recipe known by only one character, and an incomplete recipe. The inspected incomplete healing-potion record is reported as incomplete until reviewed authoritative content supplies its missing semantics. No live Caldris or Ganji records are changed to make a demonstration appear populated.

**Rollback and stop:** Revert task-owned authored changes before activation. Export any corresponding live records before editing their file counterparts. Stop on absent source authority, ambiguous definition links, or unresolved knowledge semantics. Incomplete recipes do not block truthful empty or unavailable UI states.

## IV07 Known recipes projection and tab

**Status:** Implemented in source and the authored catalog without live activation. The binding-only Recipes query pins its effect-free JavaScript projection and closed schema; the approved mechanic has an enabled discovery namespace. Makes and Uses independently match exact definition references, deduplicate by recipe ID and page in stable order. Unknown recipes and unknown selected-item identities cannot reveal associations. Referenced labels hydrate only after effective knowledge permits them (or the real DM grant applies); missing labels stay neutral and partial. Generic host code handles reference materialization and rejects stale continuation before evaluation; recipe semantics remain in catalog JavaScript.

The Recipes tab loads on first visit, preserves independent page selections across tab changes, and offers explicit refresh after expiry or source changes. It renders outputs, materials, quantities, supported tools, literal duration, requirements, uncertainty and source disclosures. Each response replaces the current bounded pages; no client-side crafting calculations or actions are added. Cache keys include the authorized client lifetime, complete selection, query hash, offsets and source revision. Context invalidation retires all item-tab caches. Empty, partial, incomplete, unavailable and stale results remain distinct. Browser verification covers populated, empty, incomplete and changed-source states, source expansion, paging, and 390/320-pixel layouts.

Current recipe predicates have no complete canonical eligibility evaluator. Production therefore returns `not-evaluated` or `definition-incomplete`, while the renderer preserves all reviewed availability states. Missing inventory materials do not hide a known recipe. Single-reference tool/crafter proficiency requirements and literal measured/special durations are supported; compound predicates, unresolved references, material-cost interpretation, completion effects and parameterized templates report incomplete or unavailable supporting data. The byte budget uses a conservative UTF-8 upper bound to return a fitting prefix within the existing sandbox memory limit; pages can contain fewer than sixteen entries. No authored incomplete recipe or live discovery was repaired to populate acceptance fixtures. The commit records validation results and deliberate exclusions.

**Owner and files:** Catalog crafting projection and web item-view owners; proposed recipes query/mechanic, `ItemRecipes.tsx`, `item-view-client.ts`, and proposed `WEB/test/mounted/item-recipes.test.tsx`.

**Work:** Query known recipe associations only on first tab visit, using IV01 knowledge and IV06 reviewed links. Render Makes this item and Uses this item separately. Show authorized outputs, quantities, materials, tools, duration, and requirements. Derive availability only from existing canonical evaluators and current authorized inventory. Distinguish known but missing requirements, known but incomplete definition, and availability not evaluated. Do not add Start crafting in this slice.

**Completion:** The same recipe is not duplicated within one group; both groups may intentionally contain it. Unknown recipes contribute no names, IDs, counts, or hidden placeholders. Known recipes with missing materials remain visible. Truncation and dependency failure do not become “No known recipes.” Unit and integration tests cover ingredient stacks in containers and changes in character knowledge or inventory.

**Rollback and stop:** Return the Recipes tab to its explicit unavailable state without affecting Details. Stop on client-calculated crafting rules or any new live recipe/discovery created solely for acceptance.

## IV08 Known uses projection and tab

**Status:** Implemented in source and the authored catalog without live synchronization or publication. The binding-only Uses query pins the effect-free projection and reviewed closed output schema. Linked activities from instance/definition membership are deduplicated and require knowledge of the activity itself before Player content hydration. Knowing a statement about an activity grants that statement only. The generic optional `activities.linked` declaration supplies membership fields, target components and bounded label-reference paths; the host performs authorization and fingerprinting while catalog JavaScript owns interpretation. Hidden identity suppresses canonical associations, and unknown reference labels stay neutral.

The tab loads on first visit, preserves its page across tab changes, clears retired data on selection/perspective/invalidation/expiry, and requires a matching source revision for continuation. It displays literal activation/resource costs, supported attack/damage/healing/check/effect information, requirements, provenance and uncertainty. Recorded item/definition statements remain descriptive and require DM adjudication; no prose becomes a cost, effect or action. Rows never execute mechanics. Inline consume-and-grant activities retain the established DM-only raw boundary because they have no distinct per-activity knowledge identity. Their existing execution support is distinguished from unmet stack/quantity requirements. Linked activity descriptors have no evaluated execution binding or complete eligibility evaluation, so these remain unsupported/not-evaluated rather than inventing readiness. Dynamic expressions, unresolved references, complex activation predicates and other unsupported facets remain partial supporting data. Missing authored tool or consumable activities are not invented. Browser and test evidence is recorded in the slice commit; IV09 owns the wider integrated matrix.

**Owner and files:** Catalog item-activity and knowledge owners; proposed uses query/mechanic, existing item-activity records and procedure, `ItemUses.tsx`, and proposed `WEB/test/mounted/item-uses.test.tsx`.

**Work:** Compose catalog-linked activities with character-known statements about this instance or definition. Display activity name, purpose, activation cost, consumed resources, prerequisites, and effects when supported. Keep execution support separate from knowledge state and current eligibility. A recorded unconventional use may be descriptive and require GM adjudication. Preserve uncertainty labels from the knowledge system. Do not fabricate a use from an item name, description, or AI suggestion.

**Completion:** Tests cover a weapon activity, tool application, consumable effect, learned special use, uncertain statement, known-but-currently-unavailable activity, and no known uses. All rows carry authorized provenance or a clear canonical activity source. Unsupported execution is described honestly. Opening or selecting a row executes no mechanic and consumes no charges.

**Rollback and stop:** Return Uses to an explicit unavailable state while retaining Details and Recipes. Stop if prose is being treated as executable mechanics or unknown properties become visible through costs, targets, or effect descriptions.

## IV09 Integrated correctness and accessibility

**Owner and files:** Integration owner; affected item and character tests, `WEB/test/mounted/`, focused .NET tests, release verification scripts only where item evidence must be added.

**Work:** Exercise the entire navigation and audience lifecycle. Use real mounted tests for behavior rather than assertions that merely search source strings. Keep fake worlds, actors and recipes in disposable fixtures. Check appearance at 1600, 1100, 820, 390 and 320 CSS pixels. Measure layout and request counts instead of relying only on screenshots or timing impressions.

**Completion:** All matrix cases below pass. Cold item entry fetches one bounded Details projection plus authorized media; each other tab fetches at most one projection before explicit continuation, retry or invalidation. Returning to the same item and fresh authorization scope reuses its cached tab data. No world-directory reload, polling loop, N-per-inventory-item expansion, accidental scroll zoom, or duplicate action occurs. All authorized knowledge is filtered before counts or labels are created.

**Rollback and stop:** Keep the item view unactivated if tests fail. If another task runs a full suite or publishes the same page, coordinate ownership before starting another runner or replacing a revision. Do not classify an interrupted suite as passed or stop another task's processes.

## IV10 Runtime publication and live acceptance

**Owner and files:** Release integrator; `WEB/package.json`, current release scripts, maintained source, runtime catalog synchronization tools, and registered web page administration endpoints. No direct SQLite edits.

**Work:** Discover the actual running listener, application, state space, campaign, seat and selected actor; recheck them rather than copying an old port or revision. Preserve the database and adjacent blobs before any live synchronization. Export matching MCP-authored records before file edits, review the catalog delta, dry-run it, commit the identical reviewed delta at an authorized boundary, and read back. Capture the prior application and page revisions for recovery.

Build the browser bundle from reviewed source. Generate the required version-2 release manifest and runtime target; use the existing signing key and independently trusted public key workflow without printing private key material. Stage through the registered page entity's bundle-drafts endpoint with the observed latest revision. Read the immutable draft back and compare every HTML and asset byte/hash. Activate with the expected active revision only after all prepublication checks and the required authorization are satisfied. Reject concurrent revision drift instead of overwriting it.

**Completion:** Refresh the real published page, open Ganji's knife and quarterstaff in both perspectives, and verify Details, Recipes and Uses. Empty knowledge is a valid result when the records justify it. Exercise a real Actor seat in an isolated authorized verification host using the same reviewed artifacts, without flipping the shared live seat merely for testing. Verify same-observer DM Player preview parity, keyboard return behavior, mobile layout, and Back/Forward. Capture the actual served asset hashes, selected perspective, character and item identities, tab states and errors. Run the current signed live verifier with that browser evidence. A build or draft alone is not delivery.

**Rollback and stop:** On page failure, activate the captured previous page revision through the same revision guard. On contract or catalog failure, use the reviewed catalog recovery procedure; a page rollback cannot undo state or schema changes. Stop on changed database identity, missing backups, unknown signing trust, hidden-data leakage, failed live checks, concurrent activation, or readiness failure. Preserve prior revisions and data. Report the exact delivered boundary and remaining failures in the task, without an unsolicited permanent receipt.

## Acceptance matrix

| Case | Required observable outcome |
| --- | --- |
| Ganji as Player preview | Actual knife and quarterstaff details render, with only Ganji-known recipes, uses, identity and properties. |
| Ganji as DM | Complete authorized item data renders; knowledge comparison refers specifically to Ganji. |
| Actual Actor and GM preview | The same actor and snapshot produce equal Player content, including omissions and media authorization. |
| Two characters | Learning a recipe or use for one character does not teach the other unless an existing baseline rule does so. |
| Unidentified item | Hidden name, magical properties, curse, image, alt text, source locator, recipes and uses are absent from the response as well as the DOM. |
| Rejected selection | Another actor, another campaign/state space, removed item or inaccessible descendant cannot be opened through an edited URL. |
| Nested container | Expand and open are distinct actions. Returning restores container disclosures, focus and scroll. |
| Recipe associations | Makes and Uses use authored links, deduplicate internally, and never infer associations from names. |
| Known but unavailable | Missing materials, charges, requirements or execution support are distinct from lack of knowledge. |
| Empty versus failure | Complete no-match results, incomplete definitions, partial collections, denied access and transport failures have different truthful states. |
| Switching and stale reads | Slow DM responses cannot arrive into Player view; inventory/knowledge changes invalidate cached results. |
| Read-only behavior | Viewing creates no effects, operation writes, item consumption, discoveries, crafting jobs or elapsed time. |
| Published layout | All three tabs work on the actual active bundle; screenshots and DOM measurements show no clipping or page overflow. |
| Request budget | Loading an item does not repeat campaign discovery or issue unbounded item, recipe, knowledge or media calls. |

## Validation commands and evidence rules

Run only the current task's foreground test runner. Check for active `dotnet` and `testhost` processes before full validation and identify their ownership. Preserve unrelated worktree changes. Stop the exact live server only if a required build needs its locked binaries, restart from the correct project directory, and verify readiness and the configured listener afterward.

From WEB during iteration and final web validation:

```powershell
npm run typecheck
node --test test/item-*.test.js
npm run test:mounted
npm run verify
```

The focused item wildcard applies after those test files exist. Prefer named affected files while iterating. Add the missing focused presentation/client tests to the relevant slice; do not treat a command matching no tests as a pass.

From the repository root for affected host and catalog work:

```powershell
dotnet build DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~ItemViewAudienceTests|FullyQualifiedName~ItemDetailsProjectionTests|FullyQualifiedName~ItemRecipeAssociationTests|FullyQualifiedName~ApplicationReadModelWebEndpointTests"
.\roleplay.cmd validate catalog
```

Catalog validation uses a disposable database. Full feature acceptance additionally requires `dotnet build DantesRoleplay.slnx` and a completed `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj` run. Run the protocol walk only if the MCP surface or dependency registration changed. Separate pre-existing failures from regressions using focused evidence; neither skipped nor interrupted tests establish full acceptance.

Use `npm run release:manifest` and `npm run release:verify-live` with the actual arguments and signing/evidence requirements in `docs/current/DEVELOPMENT.md` and the maintained scripts. Do not invent a successful release signature or use an old browser observation as evidence for a new asset hash.

## Deferred follow up slices

**F01 Use an item:** After IV10, design an explicit action preview and confirmation flow through the existing item-activity owner. Revalidate actor, item possession, knowledge, targets, costs and source revisions at commit time. Test idempotency, stale state, cancellation and rollback. No new rule execution in the UI.

**F02 Start crafting:** After IV10 and reviewed executable recipe support, connect the existing downtime workflow. Preview exact ingredient and currency reservations, duration, tool requirements, output and cancellation behavior. Commit only through the canonical transaction owner. Knowing a recipe does not authorize consuming materials merely by opening it.

**Excluded from the first release:** Shops, loot transfers, trade, item editing, recipe authoring UI, automatic identification, new crafting rules, autogenerated discoveries, and wholesale rewriting of historical content. These need separate scope and acceptance boundaries.

## Slice completion checklist

For each slice, report in the implementation task: the delivered behavior, exact files/owners changed, checks with exit results, deliberate exclusions, live synchronization performed or not performed, and the next dependency now unblocked. Use a coherent commit when appropriate. Do not mark a slice complete because its UI looks plausible, a fixture is populated, or source builds. IV10 is complete only when the actual published item view passes the live matrix.
