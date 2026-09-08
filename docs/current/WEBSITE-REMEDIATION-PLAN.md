# Website reassessment and remediation plan

## Scope and decision

This is the requested evidence-based follow-up to [WEBSITE-OBJECT-PLAN.md](WEBSITE-OBJECT-PLAN.md), dated 2026-09-08. It reviews W00–W11 and proposes independently implementable slices R00–R14. Given the breadth of the findings, this uses the user's offered planning fallback for the larger repair. This audit changes documentation only; it does not repair or publish the website.

The product direction was clarified during the audit: **this is one shared website. Anyone who has access to it has application authority.** Normal use must not require a user account, Actor seat, role command-line argument, or a different server process. World, campaign, and character are workspace selections. DM/Player, if retained, are deliberate ways to view information, not different users or permission tiers. Optional character-knowledge previews must select an observer inside the UI.

The required deployment target is [http://98.128.172.181/ui/dnd2024-play](http://98.128.172.181/ui/dnd2024-play). Localhost checks supplement that address; they cannot substitute for it. The earlier plan's mandatory separate website Actor-host journey is superseded by this shared-access requirement. Generic rules, knowledge projection, transaction integrity, and non-website protocol callers still have their own contracts.

Keep SQLite as live-state authority, catalog JavaScript as rule authority, and C# as the generic host. Reuse the existing React components, registered queries, and bounded resource store. Correct presentation and missing reads before deciding that stored data is wrong. The requested item and recipe registry is a new feature, distinct from repairing the existing carried-item screen.

## Evidence collected

Source baseline: commit `3242154149c71acb2db1f2697475316a31cbfe51`, plus explicitly inspected pre-existing working-tree changes. Those changes include public-access handling, startup configuration, HTTP-compatible browser hashing, and change recovery; they are not part of this audit commit and are not evidence of what the published bundle contains.

Live observations occurred around 15:10–15:18 UTC. Initially localhost readiness was 200 with catalog/page revision 63. Later both the requested public origin and localhost returned 503 with `SOURCE_FILE_DRIFT`, while their audience endpoints still reported `game-master`. The observations below retain that timing distinction. No stable-runtime benchmark or complete live location census was obtained.

| Evidence | Observed result and cause | Owning implementation |
| --- | --- | --- |
| E01 — public startup | The public browser displayed only Rules/Installed Content and “Private campaign views remain locked.” `/api/audience-context` returned 200, `role: game-master`; readiness returned 503, `SOURCE_FILE_DRIFT` for `dnd2024.mechanic.campaign.context.project.md`. This reproduced a runtime failure presented as restricted access. Two server processes were observed: the frozen W11 executable on loopback and the main-checkout executable on `0.0.0.0:6217`. Exact origin-to-process routing needs a fresh check. | [launcher](../../run-mcp-server.ps1), [runtime paths](../../DantesRoleplay.MCPServer/RuntimeStoragePaths.cs), [source verification](../../src/system/catalog-navigation/persistence/ActivatedApplicationCatalogProvider.cs), [fallback selection](../../src/system/web-interface/dnd2024/src/data/hub-availability.js), [Rules-only screen](../../src/system/web-interface/dnd2024/src/components/RulesOnlyHub.tsx) |
| E02 — source bytes and HTTP | The flagged main-checkout file has 893 characters, the frozen copy 917; their SHA-256 hashes differ, but their text is equal after CRLF/LF normalization. Startup defaults the source root to the editable checkout unless an argument overrides it. The committed browser also assumes `crypto.subtle`; a fallback exists only in the dirty checkout. The live verifier currently rejects non-loopback plain-HTTP origins. | [launcher](../../run-mcp-server.ps1), [resource store](../../src/system/web-interface/dnd2024/src/data/resource-store.ts), [read client](../../src/system/web-interface/dnd2024/src/data/view-read-client.ts), [live verifier](../../src/system/web-interface/dnd2024/scripts/verify-live-release.mjs) |
| E03 — Campaign contract and false emptiness | Opening Adventure Log reproduced the exact contract error. A read-only run of the production reader and converter obtained one chapter and one arc, then `TableResourceOwner.loadCampaignDetails` rejected the converted model. `isCampaignDetails` requires `id`, `chapters`, `arcs`, `sessions`, and `visitedLocations`; `CampaignReadModel` instead has presentation fields including `adventureLog`, `placesVisited`, `quests`, and `threads`. The cache test fabricates the wrong shape and casts it through `unknown`, so it passes. The screen continues showing zero/empty summaries underneath the error, and the alert follows navigation to unrelated views. | [resource validator](../../src/system/web-interface/dnd2024/src/data/object-resources.ts), [actual type](../../src/system/web-interface/dnd2024/src/data/hub-types.ts), [production conversion](../../src/system/web-interface/dnd2024/src/server-host/main.tsx), [fixture mismatch](../../src/system/web-interface/dnd2024/test/mounted/object-resources.test.tsx) |
| E04 — location reachability | The live World scope returns just `location.caldris.atlas`. Querying that known child's scope returns Eredane, Lantern Sea, and Solasca, with real anchors. The Locations browser shows one row. Its selection callback changes local selection without requesting child scopes; the deeper-load callback is attached to map navigation. Search filters only the already-loaded array despite inviting searches for places/regions. The atlas is also labelled “Current” because the selected-location fallback is reused to determine current location. | [scope reader](../../src/system/web-interface/dnd2024/src/server/game-server-context.js), [hub navigation](../../src/system/web-interface/dnd2024/src/components/DndInformationHub.tsx), [fallback helper](../../src/system/web-interface/dnd2024/src/state.js), [location browser](../../src/system/web-interface/dnd2024/src/components/LocationBrowser.tsx) |
| E05 — map root | The live media batch returns no attachment for `world.caldris`, but a 2000×1500 PNG map for `location.caldris.atlas`. `buildLiveMapTree` rejects a map owner whose non-map parent exists in the loaded set. It consequently builds `map.live.world.unavailable` with no features or scope links. The browser showed “Map unavailable map,” zero places, and no closer maps. The atlas's immediate children have anchors, so this is not evidence that the map needs to be redrawn or locations invented. | [map adapter](../../src/system/web-interface/dnd2024/src/server/connected-hub-envelope.ts), [scope mechanic](../../catalog/applications/dnd2024/mechanics/world/dnd2024.mechanic.world.location-scope.project.js), [media reader](../../src/system/web-interface/dnd2024/src/server/game-server-context.js) |
| E06 — Character first entry | A fresh Party visit remains at “Character details not yet loaded.” `CharacterWorkspace` intentionally selects no loader for Overview; its content depends on a sheet/dossier loaded by a later tab. Roster labels remain identity placeholders even after richer selected-character data arrives. | [Character workspace](../../src/system/web-interface/dnd2024/src/components/PartyView.tsx), [Overview](../../src/system/web-interface/dnd2024/src/components/character/CharacterOverview.tsx), [character projection](../../src/system/web-interface/dnd2024/src/features/character/project-character.ts) |
| E07 — inventory CSS and detail layout | On first Inventory entry, the real item buttons had `display: inline-block`, `cursor: default`, and grey `rgb(107,107,107)` backgrounds. Only the global and Character stylesheets had loaded. `.character-inventory__open` and `.character-inventory__node` are defined in `item-page.css`, which loads only with ItemWorkspace. The supplied screenshots also show repeated Inventory headings, weak click cues, and an item page whose only parent navigation is “Back to inventory.” | [InventoryTree](../../src/system/web-interface/dnd2024/src/components/character/InventoryTree.tsx), [Character styles](../../src/system/web-interface/dnd2024/src/character-page.css), [misplaced styles](../../src/system/web-interface/dnd2024/src/item-page.css), [ItemWorkspace](../../src/system/web-interface/dnd2024/src/components/items/ItemWorkspace.tsx) |
| E08 — Installed Content cost | Two consecutive read-only calls to the production loader each issued **29 HTTP requests and read 1,881,655 response-body bytes to show 4 extension records**. Observed durations were 16,412 ms and 14,162 ms. These are two diagnostic traversals, not p50/p95 measurements. The reader walks every 100-record page, discards base winners in the browser, then renders; the component owns only mount-local state and passes no cancellation signal to the transport. | [reader](../../src/system/web-interface/dnd2024/src/server/effective-content.ts), [component](../../src/system/web-interface/dnd2024/src/components/InstalledContentView.tsx), [loader wiring](../../src/system/web-interface/dnd2024/src/server-host/main.tsx) |
| E09 — cache behavior | A real shared cache exists: same-key deduplication, 30-second default retention, byte/entry bounds, and late-response fencing. Rules and Installed Content do not use it. Item caches are cleared on every focus and visibility change. Several feature keys include Campaign-summary source revision; source envelopes and rendered results are retained separately. These are concrete ownership/invalidation issues, not evidence that all caching is absent. | [ResourceStore](../../src/system/web-interface/dnd2024/src/data/resource-store.ts), [resource owners](../../src/system/web-interface/dnd2024/src/data/object-resources.ts), [Item lifetime](../../src/system/web-interface/dnd2024/src/components/items/ItemWorkspace.tsx), [change routing](../../src/system/web-interface/dnd2024/src/data/scoped-change-stream.ts) |
| E10 — Rules coverage | Before readiness failed, the live rules endpoint returned 4 rules in 4 sections: Reading a Character Sheet, Half-Elf (Caldris Homebrew), Weapon Attacks, and Long Rests. The screen's initial render allowance is 80, so it is not hiding a large published rule set. Publication selects `game.core.rules.readable` records; most mechanic/content records are not readable-rule articles. The reader also converts HTTP, parsing, and contract failures to `[]`, causing errors to look like an empty library. | [rules projector](../../src/system/catalog-navigation/domain/ReadableRuleContracts.cs), [reader](../../src/system/web-interface/dnd2024/src/server/rules-reference.ts), [RulesView](../../src/system/web-interface/dnd2024/src/components/RulesView.tsx), [authored articles](../../catalog/applications/dnd2024/content/entities/readable-rules) |
| E11 — boundaries and scale | W01's large fixture says 1,000 locations, 1,000 people/holdings, and 512 inventory entries. Delivered Inventory is a four-level, 100-node read with no continuation; World is 100 immediate children; People/Holdings is a four-level, 200-record projection. People returns `complete: true` for its projected hierarchy without a deeper-results continuation. These contracts do not prove the promised large-world experience. | [feature contracts](../../src/system/web-interface/dnd2024/contracts/website-feature-contracts.json), [Inventory requirements](../../catalog/applications/dnd2024/mechanics/data/dnd2024.mechanic.inventory-container.project.md), [People requirements](../../catalog/applications/dnd2024/mechanics/world/dnd2024.mechanic.world.people-holdings.project.md) |
| E12 — acceptance gaps | The live verifier requires three declared browser-check strings and optional item evidence. The complete-workload harness lists Character, map, Current, history, lore, locations, people, factions, and context, omitting Campaign detail, Inventory/item tabs, Rules, and Installed Content from its required traversal list. It excludes `/content` reads and rejects all POSTs even though media-batch is a read-only POST. Passing tests of these gates is not a measured complete workload. | [release checks](../../src/system/web-interface/dnd2024/scripts/release-runtime-verification.mjs), [workload gate](../../src/system/web-interface/dnd2024/scripts/complete-workload.mjs), [sampler](../../src/system/web-interface/dnd2024/scripts/sample-browser-baseline.mjs) |

The current page and activation were both revision 63, with page fingerprint `A3AD65289006958C51A65446A653726531EF9B90CD121159C9677ECD5397C485` and activation fingerprint `643AC78F2A05EAF4042FA292E9B4ED0921ED579946BE95B4C124FBF868FB005D`. Matching revision numbers alone did not establish runtime health or usable screens.

## Assessment of all original slices

“Implemented” below means the owner/path exists. It is not renewed acceptance. Earlier claims that W11 and the whole workload were complete were too broad; the reproduced failures reopen that acceptance.

| Original slice | Assessment | What to retain; what to repair |
| --- | --- | --- |
| W00 — live baseline/recovery | Reopen | Retain frozen releases, supported backup/publication, and exact hash verification. Normal startup and the public origin do not reliably select the tested runtime. No complete paired baseline supports a performance closeout. R00, R01, R14. |
| W01 — feature contracts/fixtures | Partial | Authority boundaries and shared-party decision are useful. Replace synthetic acceptance fixtures with production-shaped fixtures; reconcile page/capacity contradictions and whole-workload coverage. E03, E11, E12; R02, R03, R08, R12, R14. |
| W02 — resource/component infrastructure | Implemented, integration gaps | Preserve ResourceStore deduplication and cancellation. Fix consumer contracts, cover Rules/Content, separate result revisions from scope lifetime, and verify invalidation against actual notices. R02, R05, R06, R11. |
| W03 — Campaign/Party | Broken in production detail flow | Summary v3, participation graph, and context queries are useful. Campaign details reject their own converted model; failed details look empty. Party membership must remain separate from selected-character detail. R02, R07. |
| W04 — Character | Partial | Sheet/detail resources and presentation components exist. Selected Overview never requests its data, and section/character navigation is mostly local state. R07. |
| W05 — Inventory | Partial and visibly regressed | Retain Item query owners and containment semantics. Repair first-entry CSS, navigation, count/completeness presentation, and the 100-node versus 512-item boundary. R08. |
| W06 — World/maps | Broken for the live hierarchy | Retain exact scope queries, anchors, and media ownership. Connect location navigation to child reads; represent root-with-atlas topology and maps without images; stop inventing a current location through selection fallback. R03, R04. |
| W07 — People/Factions/Lore/history | Implemented within restricted bounds; broader acceptance unproven | Factions continuation and separate knowledge/chronology owners are useful. Review fixed four-level People traversal, 200-record ceiling, deep omission, classification and World-link completeness using independently checked records. R12. No fresh full live acceptance was possible after source drift. |
| W08 — Current/play composition | Owner implemented; requested UI change | Retain Resume, Current Scene, Encounter Board, and typed action boundaries. Remove the mounted chat composer for now and verify that visiting Current causes no conversation creation. R13. |
| W09 — mapped premise edit | Implemented; revalidate with repaired Campaign and shared access | Preserve optimistic revisions, same-key uncertain retries, no-op handling, and draft recovery. Existing endpoint tests use a test host; they do not prove an actual public-origin save. Validate using disposable runtime state after R01/R02. |
| W10 — shared mechanic pilot | Scoped implementation supported by inspected source/tests | Carrying capacity consumes exact Strength/Size object input and one burden child. Existing tests check exact result, DM/Player parity, missing source rejection, source revisions, and absence of duplicate parent inventory materialization. No new defect was established; preserve this pilot and rerun its focused tests at R14. Do not expand it into a mechanics rewrite. |
| W11 — cleanup/release acceptance | Reopen | Retain removal of truly unused assets and supported generic endpoints. First-entry CSS, live content parity, external HTTP behavior, and complete traversal were not established by the prior sign-off. R00–R14 close the gaps. |

## Implementation rules

- Execute R00–R14 in order by default. Dependencies name the minimum prerequisites if work is reordered. Each slice ends with a coherent commit containing its slice ID, changelog, tests/results, and remaining limitations.
- Inspect and preserve existing working-tree edits. Particularly avoid silently replacing the in-progress public-access, HTTP crypto, startup, and change-recovery work. Integrate overlapping work deliberately and verify the committed release, not a mixture of unstaged files.
- Before editing any live-backed catalog records, export/compare their live versions. Back up SQLite and matching blobs before an actual synchronization or migration. Review diffs; never overwrite newer gameplay to match an old fixture or make a screen look populated.
- Reuse current registered IDs where their meanings fit. A new query or changed output meaning needs the existing registration/versioning workflow, an exact schema, audience/selection semantics, and its own measured bound. The conceptual registry names below are not registrations.
- Resource responses remain in bounded memory. Persist selection/preferences separately. Preserve hashes, source revisions, same-origin writes, idempotency, and generic rule validation. Shared website authority does not make a browser cache authoritative state.
- A missing source, a failed query, an unloaded child scope, and an honestly empty collection need different states. Never improve a benchmark by rendering an unavailable or partial screen as successful emptiness.
- Add regression tests for demonstrated failures at their production boundary. Avoid fabricated models cast through `unknown`; use catalog/API-shaped fixtures passed through the real reader, adapter, resource owner, and mounted feature.

## Remediation slices

### R00 — Make the public release reproducible

**Repairs:** W00/W11, E01/E02. **Dependencies:** none.

Resolve the public address, listener, process/build, database/blob paths, registered source root, activation, state binding, and served page together. Diagnose the two-process observation before changing a running process. Make normal startup select the saved compatible release and exact source bytes without needing `-SourceRoot`; an editable checkout must not replace an activated snapshot accidentally. Keep source hash verification intact, including line-ending differences. Use supported activation/publication only after export/review and isolated validation.

Make startup detect an existing conflicting instance and report the real bound addresses and readiness of the selected instance. Provide one ordinary start/restart workflow; retain database/recovery overrides only for explicit administration. Extend the release verifier to accept the explicitly configured public HTTP target and its own runtime/audience evidence rather than silently verifying localhost instead. Do not globally accept arbitrary origins or redirects.

**Acceptance:** restart through the ordinary launcher; both the public URL and intended local alias serve the same reviewed bundle/runtime; readiness remains healthy across restart and editing an unrelated development file. Database and blob recovery points are verified. Test expected failure for altered release bytes and source-root misconfiguration. A raw byte mismatch must be diagnosed, not normalized away inside verification.

### R01 — Deliver the shared website access model and honest startup states

**Repairs:** W00/W02/W03, public-access requirement, E01/E02. **Dependencies:** R00.

Implement one trusted website context for every visitor admitted to this site. Eliminate per-visitor Actor binding, role startup arguments, and a second Actor process from ordinary website use. Select campaign/world/character through the existing UI and saved workspace settings. Default to the full shared view. An optional “View knowledge as…” control chooses an observer for presentation without changing website authority or requiring relaunch.

Review the existing dirty `LocalKnowledgeAudience`, `WebRemoteAccess`, endpoint, configuration, and startup changes before designing additional mechanisms. Propagate the shared context consistently to bootstrap, registered reads, media, streams, mapped edits, and mechanic actions. Keep game-rule checks and explicit action review where it serves the operation; scope this change to website use rather than changing unrelated MCP callers.

Replace the universal Rules-only fallback with distinct application-unavailable, connection-error, and genuinely denied states. A deployment failure must not claim the user lacks authority; retry must reload the application, not only Rules. Integrate and test the existing HTTP hashing/request-ID fallback so the required public origin works without secure-context-only browser APIs.

**Acceptance:** fresh browser at the exact public URL opens World, Campaign, Party, Current, Rules, and Content without role/Actor arguments. Character selection, images, item tabs, and an allowed edit work in the same browser. Optional knowledge preview can be entered and exited without locking normal pages. Simulate source drift and failed bootstrap: show a recoverable service problem, never a fabricated permission restriction. Verify public-origin save/duplicate/conflict behavior against disposable data, and preserve same-origin request and transaction checks.

### R02 — Repair Campaign contracts, data display, and edit integration

**Repairs:** W01/W02/W03/W09, E03. **Dependencies:** R00/R01.

Make the transport schema, adapter output type, resource validator, and mounted Campaign agree. Reuse or generate the validator for the actual boundary; do not add dummy `id`/chapter fields to satisfy the wrong test. Replace the fabricated cache fixture with the real registered Campaign response and production conversion. Defer source-envelope mutation until validation and scope-freshness checks succeed, or remove that duplicate source mutation from the feature path.

Load the selected Campaign section and authoritative chapter/arc summary deliberately. Keep an unloaded count out of “0 recorded” facts. Show detail failure with local retry and preserved last valid content. Clear or localize errors when leaving Campaign. Check Adventure Log, Places, Outcomes, Quests, Threads, Clues, and premise edit after details arrive.

**Acceptance:** the production-shaped one-chapter/one-arc case renders correctly without contract error; truly empty and failed cases remain distinct. Test zero/multiple chapters, stale continuation, fast scope switches, and Back/Forward. Exercise premise edit, conflict, no-op, duplicate submit, lost-response retry, rollback, and refresh through the actual browser/HTTP flow on a disposable campaign. Return revisits reuse a fresh validated result.

### R03 — Make all location scopes reachable and preserve location truth

**Repairs:** W01/W06, E04/E11. **Dependencies:** R00/R01/R02.

Separate World summary, location directory/search, selected-location detail, and current-location identity. Keep the World root as context and its atlas/regions as navigable children. Every location selection or disclosure must be able to request its bounded child scope without requiring an image or map marker. Add explicit parent navigation and source-bound continuation for scopes exceeding one page. If the existing query cannot express this, add a compatible version through the registered owner; do not slice a fully hydrated world in the browser.

Specify whether search is “this scope” or World-wide. World-wide search must use a bounded server owner; a filter over the first loaded page must not pretend to search all places. Refreshes replace the affected scope's membership so removed/moved locations do not persist through merge-only caches. Preserve the exact current-location ID independently of what has loaded and use a nullable presentation until its detail is available.

**Acceptance:** with the real Caldris topology, navigate World → atlas → Eredane/Solasca → deeper places from Locations alone. Compare reachable IDs/counts against an independent fixture query. A selection fallback never receives the “Current” badge. Test renamed IDs, empty scopes, no-image scopes, 101 siblings, 1,000 total locations, deleted/moved records, deep links, keyboard access, and source changes between pages. Unrelated world size must not increase a visible scope's hydration work.

### R04 — Restore the atlas image, markers, and map hierarchy

**Repairs:** W06, E05. **Dependencies:** R03.

Model the distinction between the World root and its declared map owner explicitly. Resolve the existing atlas from authored containment/map metadata; do not require an image on every structural ancestor or use a hard-coded Caldris ID. Preserve map/list scopes when an image is absent. Scope links must not disappear merely because a child has no anchor or image; omit unplaced markers while retaining navigable location information.

Load the initial map owner's children before declaring its map ready. Resolve owner-bound media, exact coordinate spaces, anchors, overlays, and nested-map transitions through their existing owners. Add image loading/failure states and keep useful list navigation available. Retain button zoom, keyboard pan, and ordinary page scrolling.

**Acceptance:** the public page decodes the existing 2000×1500 atlas image and shows the independently verified Eredane/Lantern Sea/Solasca markers at their recorded coordinates. Parent/child map navigation reaches deeper maps and returns correctly. Test a non-map World parent, missing ancestor image, missing child anchor, unavailable media, and narrow layout. Missing map content may pass only when independently shown to be absent, not because the adapter dropped it.

### R05 — Make cache reuse visible and invalidation precise

**Repairs:** W02/W04/W06/W07/W08, E09. **Dependencies:** R01/R02.

Retain the existing ResourceStore and adopt a documented resource policy: application/state-space/workspace generation, actual selection, query version/fingerprint, normalized inputs and continuation belong in keys; result/source revision evidence belongs alongside results. Include the selected knowledge observer where relevant. Audit unnecessary dependence on Campaign-summary revision and the parallel `characterSources` envelope cache before changing either.

Expose a fresh cached result immediately on return. Define freshness and revalidation for each resource rather than increasing every timeout. Preserve last valid data during an allowed refresh, deduplicate concurrent consumers, and retire caches on actual context/release replacement. Measure why focus, visibility, stream errors and change notices clear data; replace unconditional item focus/visibility clearing only with a tested refresh policy. Unknown notices and lost history still require broad reconciliation.

Add development-only counters for hit, miss, expiry, invalidation reason, in-flight sharing, retained bytes, and active requests. Do not persist private response bodies in local storage. Integrate the existing change-recovery work only with its own verified semantics.

**Acceptance:** immediate return within freshness causes zero duplicate feature data reads; two consumers of one key share one read; different keys do not cancel each other. A transfer, edit, definition change, release change, and scope switch invalidate the right data without stale flashes. Test tab hide/show, reconnect, slow old responses, failed refresh and bounds/eviction. Record actual request counts and retained bytes for repeated navigation.

### R06 — Page and cache Installed Content

**Repairs:** W01/W02/W11, E08. **Dependencies:** R05.

Render extension summaries and the first contribution page without walking every catalog page. Apply source/owner/kind/search selection in the existing effective-content owner before page selection and expensive record hydration. Extend its contract narrowly if it cannot currently return extension contributions directly; preserve other callers. Use a shared resource keyed by effective resolution and actual page/filter inputs, with cancellation and local retry.

Keep a compact contribution list with source/type filters and explicit Load more or paging. Reuse completed pages on return. Clearly label page counts versus total counts. Core content discovery belongs in Rules/Registry where suitable; do not read thousands of core winners only to discard them.

**Acceptance:** the four live extension contributions are reachable from a first-page read rather than 29 sequential catalog reads. Fresh revisit issues no content requests. Assert the first-screen request bound of one content page, at most 100 records and 524,288 response bytes; display extension metadata from that response or declare and measure a necessary separate request. Test 20,000 contributions, filtering before hydration, stable continuation, changed resolution mid-page, cancellation and errors. Record cold/revisit costs separately.

### R07 — Load useful Character overviews and stabilize Party navigation

**Repairs:** W03/W04, E06. **Dependencies:** R01/R02/R05.

Keep roster discovery cheap, but load the selected character's overview on first entry. Reuse the smaller existing Character projection if it contains the required summary; add a specific summary query only if measurement proves the existing owner is too broad. Do not eagerly fetch every party member or all recipes/activities. Render a real loading skeleton, then identity, class/level, origin and available overview facts; update the selected roster summary consistently.

Make selected character and dossier section addressable, preserving reload and Back/Forward. Retain Party navigation while entering details. Remove normal-use Actor-binding notices under the shared model. Empty biography means no biography is recorded, not that the whole character remains unloaded.

**Acceptance:** fresh Party entry renders Ganji's overview without requiring another tab click; switching away/back reuses fresh data. Verify 0/1/3/20 members, withdrawn membership, slow character switching, failed reads and retry. Routes preserve character/section and focus. Optional observer preview never contaminates the full shared view's cached data.

### R08 — Repair Inventory design, container completeness, and Item parent navigation

**Repairs:** W05 and existing Item UX, E07/E11. **Dependencies:** R05/R07.

Move Inventory styles to the Inventory/Character lazy owner so direct entry is styled. Use compact full-width item rows with a consistent icon/thumbnail, clear name, useful quantity/equipped/container metadata, a visible chevron or “View details” cue, hover treatment and focus ring. Remove duplicated headings and technical definition/version labels from primary item copy. Keep details in secondary disclosure where useful. Balance Inventory and Wallet at wide widths and stack them on narrow screens.

Retain the shell and Party context on Item pages. Add breadcrumbs such as Party → Ganji → Inventory → Carving Knife, and preserve expanded containers, search, scroll and focus on return. Put return context in a typed navigation model that can also represent Registry origins. Item tabs keep their existing query owners. Condense repeated knowledge/source decoration and absent-image placeholders; show recorded description/properties when available, with no invented content.

Resolve the four-level/100-node limitation against the promised larger inventory. Prefer scoped container pages and explicit expansion over enlarging a full-tree response. Follow supported generic containment/object capabilities and version any required contract. Totals/wallet must identify whether their own calculation is complete; visible item counts must not imply complete recursive totals.

**Acceptance:** first entry and direct Inventory URL look correct before any Item route has loaded. Every row is clearly clickable with mouse and keyboard. Test 320/390/768/1440-pixel widths, no overflow, nested/empty containers, 101/512 items, cycle rejection, transfer invalidation, unknown definitions, and exact return focus. Opening Inventory causes no recipe/use request. Deep content remains reachable without misrepresenting completeness.

### R09 — Add the known-item Registry under Party

**New requested feature. Dependencies:** R05/R07/R08.

Add one Party-level **Registry** destination with **Items** and **Recipes** sections. Items are known definitions/records, not a union of currently carried inventory instances. Preserve the distinction in query contracts, routes, quantities, ownership labels and details. Full shared access can browse recorded catalog content; an explicitly selected character-knowledge filter uses the existing knowledge/discovery owners. Label the active filter and do not infer discovery merely from possession or naming.

First inspect reusable Item definition snapshot objects, catalog lookup and knowledge selection. Add a bounded registered list/detail owner only for the missing cross-item view; do not call every carried item's details/recipes to assemble a registry. Reuse ItemDetails and supporting presentational components, with inventory-only fields shown only for actual instances. Searches and filters page on the server and keep source-bound continuation.

**Acceptance:** known-but-not-carried items appear; unknown and suspected records follow the chosen filter; multiple instances of one definition do not duplicate a definition entry. Entries open a detail view with Party → Registry → Items parent navigation and return to the same query/page/focus. Test empty, multi-page, renamed, retired, incomplete and changed-definition cases. Pin request/SQL/byte/cache bounds before registering new contracts.

### R10 — Add the crafting-recipe Registry and cross-links

**New requested feature. Dependencies:** R09.

Populate the Registry's Recipes section from known recipe records independently of possession of a matching item. Existing `inventory-item-recipes` deliberately requires a carried item and matches its materials/outputs; retain that contract for Item tabs. Reuse recipe snapshot records and the existing recipe presentation where their semantics fit. Add a bounded recipe-list/detail projection for the new use case, including item/material/output links.

Display recorded tools, inputs, outputs, duration, knowledge and supported requirement descriptions. Reuse the existing statuses such as `not-evaluated` and `definition-incomplete`; no browser crafting-eligibility engine. This slice provides readable recipes and navigation, not a new crafting action.

**Acceptance:** a known recipe remains discoverable when its output/material is not carried. Verify multi-page search, item ↔ recipe links, absent/partial definitions, unsupported requirements, stale continuation, cache reuse and preserved Registry return state. Do not fetch every Item recipe tab to build the list. No action, time advance or inventory mutation occurs while browsing.

### R11 — Expand Rules and distinguish failures from an empty library

**Repairs:** W01/W11 and requested content coverage, E10. **Dependencies:** R05/R06.

Audit the catalog's existing mechanics/procedures against its published readable-rule records. Create a reviewed coverage matrix in the owning tests/data, then add readable articles for supported areas such as checks/saves/skills, actions and turns, movement, damage/healing, conditions, equipment/containers/carrying, rests/resources, character creation/progression, spellcasting, travel and crafting where the installed mechanics provide authority. Use existing authored sources and exact citations; identify unsupported topics explicitly. Do not turn raw catalog internals into player guidance or invent executable rules.

Keep the current readable-rules publication owner and source/extension attribution. Use the shared resource policy; return structured loading/error/empty states instead of `[]` for every failure. Preserve last valid rules on refresh failure. Add helpful category/search navigation and cross-links to Item/Recipe entries when declared. Page server-side if the expanded publication exceeds its existing justified response bound.

**Acceptance:** every supported topic in the reviewed matrix has a published, navigable article or an explicit documented gap; the four initial articles remain available. Test effective extension overrides, search, related links, errors, source changes and cache reuse. Verify the public URL renders the actual expanded publication. Published article count must come from the endpoint, not a UI constant.

### R12 — Reconcile People, Factions, Lore, and History completeness

**Repairs:** W07, E11. **Dependencies:** R03/R04/R05.

Compare independent source selections against rendered lists and links. Replace or version the fixed whole-World People projection where it cannot express paging/deeper results. Avoid treating the host's depth-limited snapshot as proof of a complete world directory. Check whether people classification from motive/creature components and holdings classification cover the authored data; propose an exact missing model only when proven, not ID-prefix inference.

Reuse Factions continuation and existing knowledge/chronology owners. Keep World/location membership, known facts, uncertain beliefs and recorded history distinct. Apply the shared website model consistently while retaining optional observer filtering as a presentation choice. Make links load their target detail even when it was not in an already-loaded location page.

**Acceptance:** test more than 200 relevant records, the W01 1,000-record cases, more than four containment levels, unrelated population scaling, deep-linked locations, archived records, empty history and failed reads. No silently missing deep people/holdings, repeated continuation or mixed revisions. Verify cache invalidation after relevant facts change and narrow-screen list/detail navigation.

### R13 — Simplify Current View and remove the chatbox

**Repairs:** W08 and explicit UI request. **Dependencies:** R01/R03/R05.

Remove the `PlayConversationPanel` mount and unused Current-only loading/styles. Preserve recorded conversations/history in their existing owner, along with scene summary, location, encounter board, turn data and supported controls. Do not delete gameplay records or generic conversation components used elsewhere.

Make Current a focused scene view with a useful heading, location context, recorded situation, optional illustration and board/action area only when relevant. Keep exact current location independent of directory paging. Local failures offer retry without replacing the current scene with a guessed place.

**Acceptance:** opening/revisiting Current loads no conversation custom element and creates no conversation or gameplay write. Test exploration, existing conversation, combat, no scene, missing image, board review/accept on disposable data, changed scene and late replies. Retain operation history and mechanic result ownership. Verify the public browser shows no bottom chatbox.

### R14 — Close all slice evidence against the served public website

**Repairs:** W01/W11 acceptance, E12. **Dependencies:** R00–R13.

Extend the acceptance harness to traverse every delivered feature: startup, context switches, all Campaign sections, Party overview/sheet, Inventory, all Item tabs and return paths, Registry Items/Recipes, World scopes/maps, People/Factions/Lore/history, Current, Rules and Installed Content. Include direct entry before unrelated lazy CSS has loaded, reload, Back/Forward and error/retry. Record a complete data ledger, including the read-only media-batch POST and content/rules requests. Keep mutation blocking for browsing samples and separate disposable write/board tests.

Use production-shaped fixtures and independently checked expected record sets. Measure cold first-ready, full traversal and warm return separately; include mandatory lazy JS/CSS, deferred work, real HTTP/SQL/source reads, response bytes, and retained caches. Run small/large/doubled-unrelated-population cases. Keep existing stricter budgets unless an explicit measured contract change justifies a revision; do not label hard-coded ceilings as measurements.

For any reported p50/p95 improvement, obtain at least 20 valid comparable before/after samples with the same data, machine/browser, audience/view and fixture identity. If the original broken screen has no valid successful baseline, record its failure rate and establish a repaired baseline; do not claim a latency improvement for that screen. Shared-site and optional observer previews replace the old separate website-user profiles; retained generic Actor/MCP contracts still receive their relevant regression tests.

Run catalog validation after catalog changes, website typecheck/tests/production build, affected generic tests, W10 carrying-capacity parity tests and the required full feature acceptance suite. Run the protocol walk if MCP descriptors/dependency registration change. Sign and verify the final matched host/catalog/state/page release using browser evidence from the **exact public URL**, plus any local aliases; manually typed “passed” flags without the underlying observed journey are insufficient. Verify representative map pixels/markers, actual Campaign records, first-entry Inventory styling and both Registry sections.

**Acceptance:** every original W00–W11 row and new R00–R13 slice has an evidenced disposition; no remaining failure is silently marked complete. Ordinary restart serves the same compatible release, the previous release and data/blob recovery procedure remain usable, and no rollback overwrites newer gameplay without reconciliation. Deliver concise slice commits and final results/limitations rather than another unverified completion claim.

## Audit verification and limits

The read-only investigation reproduced Campaign rejection using the real API reader → converter → resource owner, observed the initial Inventory computed styles, queried the World/atlas scopes and media metadata, read the rules publication, measured two Installed Content traversals, and opened the exact public URL. It made no catalog imports, activations, page publications, permission changes, or gameplay writes.

Using Node 24, the focused existing suites passed **26 Node tests and 12 resource-owner tests**:

```text
node --test test/campaign-navigation.test.js test/world-location-scope.test.js test/live-map-placement.test.js test/effective-content.test.js test/rules-reference.test.js test/complete-workload.test.js
node --import ./test/support/register-css-module-loader.mjs --import tsx --test test/mounted/object-resources.test.tsx
```

These passes demonstrate the coverage gap alongside the reproduced defects; they are not release acceptance. No full .NET suite, catalog validation, new feature implementation, complete location census, or paired performance benchmark was performed for this documentation change. W09/W10 judgments include source/test inspection, not a new live mutation or freshly executed .NET result. Later implementers must recheck runtime identity and counts because the live state changed health during this audit.
