# Player knowledge, maps, campaign preparation, and conversation memory

## Authorization and outcome

The user requested this durable plan on 2026-09-12 and explicitly authorized immediate implementation by coordinated tasks without another confirmation. Deliver working features on master and in the local running system, with the necessary tested migrations, retained assets, catalog synchronization, release selection, and verification. This request supersedes the previous product decision to expose only a shared full-context website with no DM/Player switch.

Preserve the existing campaign, character state, authored facts, secrets, history, and retained assets. Export live records before editing corresponding files. Use supported stores, typed operations, and version-checked publication. Keep recovery copies and the previous release. No unrelated branch deletion, remote publication, database reset, global conversation scraping, or unrestricted secret exposure is authorized.

On 2026-09-13 the user resumed delivery and explicitly requested cleanup afterward. Inventory the
generated build/runtime/dependency copies in this checkout and its retained worktrees; remove
verified disposable outputs after acceptance. Preserve authored or uncommitted work, databases,
blobs, selected artwork, necessary recovery evidence, the live release and its usable predecessor.
Reuse one build graph while finishing; report measured generated-space totals and actual recovery.
The disk-full interruption has cleared (33.7 GB free at resumption). Master `b3a141ab` contains the
pending bounded Validate/Activate correction; its final focused checks and copied/live delivery
still need to finish.

Baseline: master `ddcd5eed`; live DND page 80, application activation 66, state-space binding 40, Green & Wood theme. Confirm actual state before operations; these are baseline values, not future expected values.

## Product decisions

### DM and Player views

- Player view means the combined knowledge of the active campaign's current party members. A fact known by one current party member can be shown, with its provenance. Public campaign information is also eligible. Merely existing in storage does not make a secret known.
- DM view includes the full authorized campaign/world context, including secrets, hidden locations, undiscovered clues, and private objectives. It does not grant access to unrelated applications, campaigns, users, or private conversations.
- A website switch changes the requested presentation. Server-side authority determines whether the caller may use DM view. An authorized DM can preview the party's knowledge. Player clients must not gain DM authority by changing a selector, URL, actor ID, request body, header, or stored preference.
- Reuse current identity, session/grant, audience, campaign, and observer owners. Public/unprivileged entry must be player-safe. Establish a usable protected local-owner DM path using the existing access mechanisms; do not make public anonymous callers DMs. Preserve the owner's ability to enter DM view after deployment.
- Apply the same visibility to summaries, details, search, map overlays, map artwork, images, labels, object reads, events, and caches. A Player response must not contain hidden content for CSS to conceal. Party membership and knowledge changes invalidate the affected projections.
- Switching perspective, campaign, or party scope must cancel stale reads and reset scope-dependent caches. Do not briefly display cached DM secrets in Player view. The active view must remain clear, keyboard accessible, and compatible with every shared theme.

### Maps and prepared starting area

- Map artwork depicts terrain and architecture. Location names, icons, interactive locations, roads/routes where appropriate, party position, and discovered/secret state belong to retained overlay data.
- Reuse the existing map renderer, pan/zoom, scope hierarchy, placement, and overlay owners. Add missing fields or adapters only where the existing model cannot express the requirement.
- Establish a canonical coordinate frame and orientation for each map family. Parent/child map bounds and transforms must agree. Zooming within a map uses the same terrain; alternate resolution images must be derived from the same master artwork. Do not independently regenerate zoom levels and assume they align.
- A child settlement/interior map can add finer detail, but its entrances, watercourses, roads, orientation, and connection to the parent must match recorded geography. Record anchors and validate transforms. Missing coordinates remain explicitly unplaced, not assigned invented positions at render time.
- User clarification: every closer map level should show more detail while retaining the previous level's geography. Independent detail frames are a rendering boundary, never permission to move or reverse a river, coast, road, mountain range, settlement, bridge, or entrance.
- Replace baked names/markers in the active campaign's published map family with clean retained artwork and overlays. Keep the previous images and asset references recoverable. Raster edits use image generation; deterministic map transforms and overlays use ordinary code.
- Adding or renaming a location must update data and its overlay without rebuilding a raster map. Player overlays and the underlying image must not reveal DM-only places or labels. Avoid separate geography for Player and DM; authorized overlays supply additional DM information.
- Prepare a bounded starting-area package grounded in the live campaign: the actual starting settlement, its immediate approach, and nearby adventure sites. Identify exact existing locations before choosing additions. Target at least three useful nearby detailed location maps and three scene illustrations, plus a short playable situation with NPC goals, clues, consequences, and optional hooks. Reuse existing sites and facts where possible.
- Store new story, NPCs, clues, knowledge visibility, images, and map records in the existing catalog/live authoring model. Separate DM truth from what the party currently knows. Do not resolve quests, award knowledge, move characters, or claim new encounters already happened. Content authored for future play must remain distinguishable from played events.

### Outer-AI conversation memory

- Retain the actual user and outer-assistant message text through an explicit capture integration. A server cannot observe text the client never sends. Implement and verify a usable Codex capture route in addition to the generic ingestion/read contract.
- Reuse existing conversation, transcript, execution, retrieval, and background-job owners. Do not create a parallel unscoped message database or log game rules into the generic C# kernel.
- Scope each captured conversation to its installation/application/campaign and authorized owner/audience. Preserve source client, external conversation/turn/message IDs, role, timestamp/order, original text, and capture provenance. Replays and retries must be idempotent; edits/deletions must remain distinguishable from duplicate delivery.
- Capture user messages and user-visible assistant responses. Exclude hidden reasoning, credentials, approval internals, and arbitrary tool dumps. Do not ingest unrelated Codex tasks or other personal conversations by default. A captured statement is evidence of a conversation, not proof that an in-world action occurred.
- Prefer supported client events and stable thread reads. Use a pinned, version-tested adapter when the installed Codex version requires transcript parsing. Restrict capture to explicitly linked gameplay sessions and this installation; fail visibly and safely for unsupported formats. Do not quietly claim automatic capture when only a manual API exists.
- Include a resumable checkpoint and a bounded local delivery queue so a server restart does not silently lose turns or duplicate messages. Capture must not block user turns indefinitely. Surface capture state, failures, retry, disconnect, and clear/delete controls through existing management surfaces.
- Make authorized transcripts available to relevant AI context and a bounded dreaming/consolidation operation. Derived memories must cite original message IDs, distinguish player statements from DM secrets, and respect the source audience. They are retained derived records, not automatic authoritative gameplay commits.
- Reuse existing provider and job configuration. Verify consolidation with deterministic test providers and truthful unavailable-provider behavior; do not run an unlimited background AI loop or silently buy external model usage.

## Shared implementation contracts

1. The audience lane owns the server definition of party knowledge and the authorized effective perspective. Other lanes consume it; they do not reimplement visibility lists or hardcode a party member.
2. The map lane owns geometry, parent/child transforms, dynamic overlays, and asset bindings. Content supplies real records and coordinates; the renderer never parses labels from raster artwork.
3. The memory lane owns transcript identity, deduplication, capture checkpoints, retrieval, and derived-memory provenance. Gameplay mechanics consume explicit authorized records and typed actions.
4. Root owns cross-lane decisions, master integration, the central checkpoint, final acceptance, and live browser inspection. One designated deployment worker owns live database, blob, profile, activation, and publication writes.
5. Shared files with likely conflicts—MCP registration, dependency registration, EF migrations, `game-server-context.js`, connected hub models, and `DndInformationHub.tsx`—must have one active writer. Coordinate a patch or handoff rather than editing concurrently.
6. Keep C# generic. Put party/game knowledge semantics and starting-area story/rules in catalog JavaScript/data. Update canonical AI manuals when a capability or workflow changes, with an export before editing existing live-authored records.

## Work packages and delegation

Use the existing user tasks where possible, with GPT-5.6 Sol for implementation and focused subagents. Root reviews the shared decisions and integration. Limit each task to two useful independent subagents; no recursive review trees or repeated full suites. All implementation worktrees remain detached so master stays the only local branch.

| Package | Deliverable and ownership | Dependency |
| --- | --- | --- |
| P0 Inventory and preservation | Exact audience/auth owners; live party/knowledge/map/start-area exports; conversation/capture owner inventory; baseline and recovery evidence | First; root and existing reconnaissance agents |
| P1 Audience and website | Protected DM/party Player projections, switch, scoped caches, secret-safe reads/assets/events, usable owner/player access | P0; owns website audience/read adapters and header wiring |
| P2 Map presentation | Reusable overlays, labels, add/rename location flow through existing APIs, consistent frames/transforms/zoom, audience-safe visible features | P0 and P1's audience contract; can start renderer work in parallel |
| P3 Campaign maps, illustrations, story | Reviewed clean map family, at least three nearby detailed maps and three scene images, grounded playable starting-area material, asset/coordinate/visibility manifest | P0 exports, P2 geometry contract; art can proceed independently |
| P4 Conversation memory | Durable ingestion/retrieval, real scoped Codex capture, recovery/deduplication, capture management, evidence-linked dreaming/context integration | P0; independent of website/map edits except shared registration |
| P5 Integrated verification and delivery | Merge reviewed commits, one combined acceptance run, copied-state rehearsal, guarded live changes, verified DM/Player and map/memory flows | P1–P4 |
| P6 Optional follow-up: interactive roads | Authored path geometry, authorized route projections and reusable SVG interaction over the painted roads | After P5; deferred under the user's “unless it's too much work” condition |

The website task may coordinate P1 and P2 with separate backend and frontend workers. The content task owns P3. The existing inner-AI task owns P4. Assign exact task IDs and worktree paths in the checkpoint before dispatch. Tasks read this document and only their exact implementation owners, not the entire planning archive.

### Interactive road/path follow-up

The user would like connections to follow painted roads or paths as interactive overlays. This is
recorded for a later enhancement because current route/adjacency owners store topology and travel
facts, while map scopes expose point anchors only. No existing owner stores the path's shape.
Inferring a straight connection or detecting a road from the bitmap would not satisfy the request.

1. Add an application-owned presentation component on a route: exact map owner/frame, a bounded
   ordered curve in normalized coordinates, and a small road/trail/path style. Keep travel rules
   and topology in their existing owners.
2. Extend bounded scope snapshots and catalog JavaScript projections to return eligible routes
   and authored geometry. Both endpoints must be visible in the current map/audience; verify
   geometry bounds and endpoint/anchor agreement. Hidden roads must be absent from Player bodies.
3. Render a reusable SVG layer beneath place markers, with a subtle themed stroke following the
   painted road, a wider transparent hit area, keyboard focus, accessible labels, hover/focus
   highlighting, and route-detail selection. Selecting a path must not silently move the party.
4. Author the first curves against the accepted Bramblebridge artwork, retaining the raster hash.
   Verify alignment under pan/zoom and across the existing themes, then publish the reviewed
   component/query/mechanic/data and page changes through the same guarded release process.

This adds no route geometry or frontend changes to P1–P5. No scheduled automation was requested or
created; P6 remains an explicit optional follow-up in this document.

## Verification and release gates

- **Audience:** prove an unknown secret and hidden location are absent from Player response bodies, search results, asset access, event payloads, and caches; a known clue appears when any current party member knows it; removal/revocation changes eligibility; authorized DM can see both. Forged DM requests fail. Verify actual public/player and owner/DM origins separately.
- **Maps:** show one location added or renamed by data changes alone, with the same raster hash; verify overlay alignment at multiple zoom levels and parent/child anchor consistency; keyboard/pan/zoom and existing location navigation still work; new assets have no baked location names/secret markers. Check all delivered map-family levels and nearby maps visually.
- **Content:** validate retained records and links; inspect all generated final images; verify start-area anchors/story agree with current facts and distinguish private truth from known clues; no played state is fabricated.
- **Memory:** a real linked Codex interaction retains its user and visible assistant turns; duplicate/retry delivery does not duplicate records; restart resumes capture; unrelated tasks stay uncaptured; access and deletion/retention are enforced. A consolidation result cites source messages and does not disclose a DM-only message to players or mutate gameplay by itself.
- Run focused tests while each owner iterates. Run catalog validation after catalog changes against disposable data. Freeze the integrated source for one full suite; run the protocol walk because memory/audience capabilities may change the MCP surface. Repeat only failed or newly affected checks after corrections.
- Rehearse schema/catalog/application/asset changes against a consistent live copy before live mutation. Preserve database, blob, catalog, and current profile/release recovery evidence. Use compare-and-swap revisions, verify stored asset bytes, update pinned host/source/page expectations, and restart only the identified managed process.
- Finish with master clean and the live site ready, protected DM access and Player view verified, all delivered maps/assets visible in their intended scopes, capture state truthful, and manuals retrievable through the real MCP path. Record limitations explicitly; do not call an unconnected capture API or unactivated asset a completed feature.

## Codex integration references

Official [hooks documentation](https://learn.chatgpt.com/docs/hooks) describes prompt/stop events and warns that transcript files are not a stable format. Official [app-server documentation](https://learn.chatgpt.com/docs/app-server) provides non-resuming stored-thread reads. Check the installed pinned CLI before choosing the final adapter. These references guide client integration, not game-state authority.

## Current checkpoint

Updated 2026-09-13. **P0–P5 are accepted and live on port 6217**, with production source `d0481f48` integrated on master. Application activation 69, state binding 43 and page 81 passed final public/owner browser and MCP checks after the second restart. The release-only grant is revoked and the review queue is empty. The rehearsal server is stopped and the guarded compression pass is complete; P6 roads remain an optional follow-up.

| Work package | Source status | Remaining acceptance |
| --- | --- | --- |
| P0 Preservation | Complete: fresh live backup/export, component byte witnesses and exact asset baseline retained | Passed |
| P1 Audience | Truthful nonprivileged PlayerGroup, protected DM, server filtering, cache reset and authorized media tickets live | Public/owner access and denial checks passed |
| P2 Maps | Data-backed overlays, consistent parent/detail frames, zoom and keyboard pan live | Passed; rename remains outside the existing mutation contract |
| P3 Starting area | Eight maps, three illustrations, fourteen child locations, two new unrevealed clues and a future situation published | Copied full visual matrix and live representative checks passed |
| P4 Memory | Private journals, capture, replay/retry/disconnect and source-pinned INNER derivation delivered; matching helper/manuals verified | Passed; capture stays unconnected until a real gameplay task is linked |
| P5 Delivery | Live host/source/Tools/page, migrations, three fresh reviews, permission cleanup and two restarts verified | Accepted; post-delivery disk cleanup is separate |
| P6 Roads | Optional documented follow-up | No implementation or scheduled automation in this release |

### Ownership

| Owner task | ID | Detached checkout | Boundary |
| --- | --- | --- | --- |
| Implement plan 06 website interface | `01a0917b-34d0-7cc1-bbbf-9689abd0b2e9` | `C:/repo/DantesRoleplay-dnd-web` | P1/P2; delivered, no pending production edits |
| Implement runtime authoring | `01a09179-4f0e-7c00-9411-cddb4b9ecbba` | `C:/repo/DantesRoleplay-gameplay-content` | P3; delivered |
| Implement focused INNER AI workers | `01a0917a-67f5-78d0-9555-cd9265c27348` | `C:/repo/DantesRoleplay-gameplay-memory` | P4; delivered; final setup/evidence handoff |

Root task: `01a09056-6d13-76d0-8706-40e435fb8b03`. Tasks return commits and concise evidence and do not modify the original checkout or live state. Implementation history remains in Git; the checkpoint describes current state.

### Accepted implementation boundaries

- PlayerGroup carries no actor or GameMaster authority. Public locations can be eligible independently of party admissions; the existing zero-admission notebook remains empty until real knowledge is recorded. Do not invent admissions merely to populate the interface.
- Media authorization uses strict query-owned `mediaOwnerReference` declarations: owner `/scope/id`, availability `/state` equal to `ready`, and exact route-role agreement. Tickets pin the query contract, current binding and asset metadata, and recheck Player eligibility at redemption. Echoed names and sibling unavailable results do not establish authority.
- Map frames use top-left origin, east/right, south/down and north-up. A child's anchor belongs to its direct parent. Site/interior children require an active retained map visual. Missing positions remain unplaced.
- All eleven selected images and direct-parent geographic relationships were visually reviewed. Atlas v2 corrects the earlier Eredane coast/watershed inconsistency. Finer maps retain major rivers, roads, terrain, crossings and entrances while adding detail. Previous maps remain recoverable. Selected package: `catalog/applications/dnd2024/assets/caldris/measure-of-mercy/`.
- Illustrations have ordinary location-gallery payloads, merged by content hash with fresh version checks. The two new clues and canonical DM-only secret have explicit world-root containment. Three established Q01 clues are reused without changing their records or relationships; all new support links target the existing Q01 quest truth. The complete future situation, “Before the Fourteenth Stroke,” is not a played event. Its package ID is application-qualified; existing campaign/world entity IDs are unchanged.
- Append map visual schema version 2/hash `096846284D7D198FAD231F32CDB301FA7C498836E67D986CE42231F6420E5D1A` through typed registration, preserving immutable version 1/hash `7C443530D2436D1089E5639154DDA0B1122D9F4CAE92F80806891E39DA42A760`. Strict rich and legacy fixture shapes are disjoint; runtime media remains rich-only. No application rebind is needed solely for this unpinned media schema.
- Private messages remain separate from `ApplicationPlayMessageRecord`. Five runtime journal tables are retained by database backups and deliberately excluded from authored catalog exports.
- The executable dream definition is the exact application-owned `{application}.procedure.conversation-dream`; DND provides `dnd2024.procedure.conversation-dream`. Shared `procedure.system.conversation-dream` is guidance. A reviewed `governs` declaration admits only the conversation-memory read capability for that exact convention. No broad system-tool bridge, new configuration registry, relaxed standing-grant ownership or DND-specific C# logic was added.
- Candidate review compares changed query/procedure contracts with exact retained predecessor bytes and origin evidence, independently of advisory search wording. The full change reason remains in the model input. Manual packets retain fingerprints and explicit omission reasons: omitted advisory sections are permitted for review, while incomplete source enumeration, omitted alternatives/recipes and unknown omission causes fail. Historical query/procedure inspection requires the normal application Read grant and exact retained-owner revalidation; it grants no historical execution or mutation.
- Durable submission, fenced completion, readback and source-pinned derivation passed with a deterministic provider outcome staged through the real lifecycle owner. This does not claim a paid provider run. Derived results remain private candidates and cannot promote themselves into gameplay truth.
- Installed Codex 0.153.4 was tested using non-resuming metadata/turn/item reads. A real isolated acceptance interaction at thread `01a09620-7483-7982-8fca-e0505d34100f`, turn `01a09620-75ca-7f53-a504-e158f80b62c7`, supplied one user message and one visible assistant response. Compiled-helper delivery, idempotent replay, saved-queue retry and disconnect passed on isolated state. Engineering tasks are not captured; live capture is not yet connected.

### Verification and recovery

- Combined .NET build: passed, zero errors and one existing nullable test warning. Disposable catalog validation: 620 records passed, seven existing Trail Survival warnings.
- Website: typecheck and 569 Node tests passed; 316/318 mounted checks initially passed, both failures corrected and 24 affected checks passed; 101 shared-browser checks passed. Production page is built from `54aec476`.
- Full .NET regression completed: 3,526 passed and four failed out of 3,530. All four failures are corrected: bootstrap manual memory-kind listing (`d6fd10e4`), two private-storage catalog-coverage classifications (`777ab2de`), and prepared-package namespace (`48461729`). Focused reruns, package checks and the protocol walk then passed: 31 passed, two deliberately retired protocol skips, zero failures. Final catalog validation again passed 620 records with seven existing warnings. Do not repeat the complete suite without a new reason.
- Capture now accepts current `hook_event_name`, handles the Windows input BOM, and keeps automatic stdout empty. Hook completion timing differs between Codex hosts, so the portable `capture-memory --watch` mode polls only the explicitly linked thread through bounded app-server reads and reuses the existing durable queue. With hooks disabled, a real new turn (`01a09683-0f02-7410-864c-c27781a207b5`) was captured automatically as one delivery with two visible messages and no manual retry. Sixteen focused checks passed. Final review also checks that a bounded history window cannot silently omit a backlog. Memory management uses private-operator MCP; no website memory controls are claimed.
- Copied publication exposed and corrected two query gates: namespace `dnd2024.query` now admits retained query contracts as well as documents (`697a0b24`); the single-query review grammar now permits an explicit reviewed null-to-valid media-owner declaration and fingerprints that opt-in (`7241621e`). Removal or replacement remains rejected. Publish each changed query and the dream procedure as separate reviewed candidates. The real pipeline test passed candidate write, review, validation, activation and readback, including negative removal/replacement cases.
- Copied world preview caught nonexistent truth references and three duplicate clues before any world write. The corrected packet reuses the three existing Q01 clues, creates two distinct canonical-story clues, and keeps eight new relationships within the world root (`4cfd337e`, schema correction `1b0678a9`). Copied import applied 87 effects and seventeen new entities; replay returned the same operation, database integrity checks passed, and all five checked public location scopes returned ready/complete. Sixty retained map bindings advanced to immutable map schema v2 with exact data and entity/relationship hashes preserved; the two new map bindings bring the v2 total to 62.
- Production review execution is implemented in `384a60ce`: an explicitly enabled worker invokes the existing validation service with its authority, lease, lifecycle, fencing and receipt owners, a host-selected smaller model and no tools. It defaults disabled. Twenty-three focused checks passed; both copied and live queues were empty before intended submissions. Three bounded real review calls are authorized for this release after exact provider configuration is verified. Do not bypass review or synthesize a provider receipt.
- Procedure publication closure `e71b0f3c` uses separate versioned evidence for the exact conversation-memory capability and its application-owned dream procedure. Unknown capabilities and wrong procedure owners are rejected. Fourteen focused checks passed, including candidate write, review, validation and activation.
- Browser testing found valid map URLs with `?perspective=dm` were rejected by a suffix-only frontend check. `154dcaa9` accepts the exact relative media route and an optional single approved perspective parameter; 47 focused checks passed. A new page bundle is required before map canvas, marker and pan/zoom acceptance. Three illustration galleries already loaded real images; switching to Player cleared the DM gallery and scope.
- Root logs: `.tmp/gameplay-combined-build.log`, `.tmp/gameplay-combined-tests.log`, `.tmp/gameplay-combined-catalog.log`, `.tmp/gameplay-frontend-acceptance.log` and `.tmp/gameplay-frontend-build.log`.
- Preservation root: `DantesRoleplay.MCPServer/data/backups/caldris-starting-area-recon-20260912T1330Z`. It contains 24 entity/36 component snapshots, previous artwork, audience/secret witnesses, a verified online database backup, a 277-record rules export and an immutable rehearsal baseline with 152 verified blobs. Keep the baseline unchanged; use a working copy for rehearsal.
- Original online backup: `dantesroleplay.db.backup-20260912T133944585Z`, 100,007,936 bytes, SHA-256 `4F8E16C7EBC93E43906C21B7EBA6083E85AF665278636B855FA085EC6F8A9290`. This is recovery evidence, not a substitute for fresh live preservation before cutover.
- Selected page ZIP: `p5-staged-ace05b87/dnd2024-play-54aec476.zip`, 63 exact production entries, SHA-256 `9C164955F0233DB7AF5239E38544D55AF3C9A7A4BB0D0E79E60C3A88BC8F5DE1`.
- Previous theme delivery exposed stale embedded bootstrap content: restarting the old binary reseeded an older web-composition manual. Final host, source, Tools and catalog must match. Include new P3/P4 files and the corrected system.use manual in the source closure, rebuild embedded bootstrap, and verify manual hashes/revisions after a second owned restart. An earlier worktree capture executable is acceptance evidence only and must not ship as the combined release.
- Publish the new parser host before reviewing/activating the two incompatible location-query contracts with media declarations. Use a fresh candidate review, typed schema registration, exact-byte media verification, world sync, gallery merge/CAS, page publication and any required state upgrade. Retain prior revisions, blobs and profile. Never reuse stale compatible review receipts.
- Preserve campaign `campaign.caldris.measure-of-mercy`, location `location.caldris.bramblebridge`, actor `actor.caldris.ganji`, existing campaign history and Green & Wood default. Test configured owner and public origins separately; restore owner DM presentation after browser checks.

### Final copied release checkpoint

- Current production source: `d0481f48`. The capture client checks the returned task's repository as well as its ID. The manual lists all six host binding fields, requires a durable initialized watcher before gameplay, and documents the initial history baseline. Bounded pagination fails explicitly on missing anchors or excessive history, including an initially empty task.
- Root final affected checks: 37 passed and two deliberately retired protocol skips. After the last capture-only correction, 18 capture checks passed and disposable catalog validation passed 620 records with seven existing warnings. A transient test-compiler access violation cleared on one retry; no test failure remains. The full-suite evidence above remains the broad regression run.
- Corrected copied page 82: 63-entry bundle SHA-256 `976503EF5D05B0FE09E35FC724A25453FA47A47B4CCEF75AE9A726A638D9ECF7`; all 62 stored assets verified against the archive. Typecheck and production build passed with the pinned TypeScript toolchain.
- Copied browser checks passed all eight real maps, with marker counts 3/6/8/9/15/5/4/5; three 1536×1024 galleries; the same map canvas under zoom and keyboard pan; Player scope/gallery reset; and absence of DM blocks in Player. Stable fresh loads showed no stale-view banner or API/page errors. Fourteen added child locations and exact query-to-overlay IDs, labels and anchors demonstrate data-backed overlays. The existing mutation owner rejected the proposed rename in dry-run with `ENTITY_RENAME_UNSUPPORTED`; no mutation or bypass followed, and a new rename API is excluded from this release.
- Actual retained-data diagnosis found the first query closure rejected an omitted query input schema paired with an explicit empty-object projection schema. `0654cbb0` aligns review with existing runtime behavior: omitted query input is fixed `{}`, which the projection schema must accept. Explicit input contracts still require exact equality and cannot change through this grammar. The complete gateway publication regression and negative cases passed; query review grammar/evidence is now v3. No source-basis reset or legacy activation bypass was needed.
- Copied candidate `de8bf6583dd37be5fa076bfde6a69b80` revision 2 kept the exact same query bytes and used reason `mediaOwnerReference`. Its real Sol task `task.70c0a337867bf72946204351e5796267` completed with an inconclusive judgment because retrieval supplied no existing-query alternative. Revision 3 used `scanning map`, which retrieved the predecessor but failed to explain the extension, and received `reuseExisting`. Neither receipt was validated or activated. Exactly two paid calls used 24,338 and 29,418 tokens; an earlier lease expired before dispatch and was not a paid call. Further wording experiments are stopped. Fix the review owner to supply the canonical predecessor and preserve the complete meaningful change reason before another call.
- A general review-context issue remains as a follow-up: the ordinary explanatory reason matched 87 manual sections because substring scoring includes words such as “the” and “for.” Discovery selects eight, marks the packet bounded, and validation rejects it. Feature search combines terms differently, so a narrow phrase may produce no alternatives even when its manual packet fits. `mediaOwnerReference` actually returned zero feature hits; an earlier claim of two was incorrect. Verify both actual feature alternatives and manual bounds before each review. `conversation-dream` scores eight global manual sections. Future work should give review a dedicated, explicitly bounded context contract and include the canonical predecessor automatically, instead of relying on keyword phrasing or treating omitted low-score sections as missing review authority. Do not mutate packets, suppress alternatives or relax grants as an operational workaround.
- `3e6f6228` resolves the release-blocking part of that issue through the accepted retained-predecessor/advisory-manual contract above. Query grammar is v4, procedure grammar v3 and reviewer profile v2. Sixty-three focused checks passed, including the real query publication flow with a reason longer than 256 characters, exact predecessor inclusion, post-activation review reconstruction and idempotent activation replay; procedure publication; manual omission causes; retained-source/origin checks and denied mutation permissions. The test run used the existing query/procedure publication, manual-context, reuse-input, reviewer-profile, historical-compatibility and standing-grant test owners; no duplicate full-suite run was needed.
- Matching Release compilation passed in 22.6 seconds with zero warnings/errors using the compiler workaround below. DataAccess SHA-256 `E6D417F95B86A15715192531B1A7142627418E2BA5647672749D0DE0E5247AE8`; domain assembly SHA-256 `E17DBB91BB46F1D5BC9F62247CAB56F015DEBFEF92352891029B3A2DB6BD9A35`. Both assemblies must be updated together in host and Tools. Build evidence: `.tmp/gameplay-dataaccess-release-3e6f6228.log`.
- Copied public-origin checks passed before query activation: Player scope only, no raw asset hashes/provenance, forged DM/raw entity/blob/media access denied; owner loopback retains DM reads. Successful Player media-ticket issuance/redemption remains a post-activation gate. Evidence: `.tmp/gameplay-browser/public-security-preactivation.json`.
- The corrected real review input was admitted for candidate `4d5e1b82efeee3a1c40708ba09dba70c` revision 1, task `task.e93b94cc64f2875f73a7a88e8e2a8140`: full reason, one exact predecessor, reviewer v2 and a 33,914-byte input. The task exhausted three leases before provider reservation or dispatch; **zero additional provider calls/tokens** were spent. Its failure remains auditable and cannot authorize activation. Copied activation 66/page 82 remain unchanged.
- The preceding worker failure came from full context preparation in deferred read transactions and again inside provider admission. With SQLite DELETE journaling, these boundaries blocked renewal and could deadlock nested readers against a pending writer. The correction below preserves the journaling mode, lease limits and real provider evidence. Failure evidence: preservation root `p5-evidence/candidate-scope-final-preprovider-failure-proof.json`, SHA-256 `ADEAB01DFCB328431A3026D364CBE96C032420F43084135AE0AF6F160E7B7312`.
- `575c97e8` implements that worker transaction split. Ten focused checks passed in 19 seconds: query/procedure publication, validation/invoker boundaries, a real DELETE-mode worker with manual preparation held for six seconds while another writer and lease renewal succeeded, and revoked permission before atomic admission with zero reservations/dispatches. The successful contention case retained exactly one reservation, one dispatch and one provider call. The real copied task must be resubmitted through normal admission; do not reset its failed lifecycle rows.
- Matching Release build passed in 22.1 seconds with zero warnings/errors. Final graph SHA-256: DataAccess `4BDC34B20513ACEA86B1A11ECDB603FC558EEEFED7FEC411D8544E16F1D53B50`, domain `448DA3DDEEEFD0057E8EB80ACBFBB4D0F16E624A9BC4F8517EC7233FAE0692CC`, LocalAI `B0465E3AE7752E16CF3776C60050ED3CBF93E1D0F19A3664DCED83FD1B2719A3`. Build evidence: `.tmp/gameplay-dataaccess-release-worker.log`. Reuse the existing build graph; recovery copies and exact retained bytes remain preserved.
- The sidebar “Active chapter information unavailable” notice is present on both old live and copied page 82: the existing campaign-summary projection does not provide chapter details. This is an existing data-contract limitation, not a regression from the new website. Evidence: `.tmp/gameplay-browser/chapter-compare.json`.
- Matching copied host/Tools now use `575c97e8`. The unchanged candidate `4d5e1b82efeee3a1c40708ba09dba70c` revision 1 received a successful real Sol review in task `task.f757445c1bb50d42a2c516470cbc0098`: one completed attempt, one settled reservation, 29,987 tokens, and `extendExisting` for the exact retained predecessor. There was no lease/lock failure. The earlier two paid reviews remain retained; the failed pre-provider task spent no tokens. No live feature write has occurred.
- Cold query closure reconstruction and subsequent manual lookup requested the full catalog while Validate already owned a transaction. Catalog object registration then opened a nested transaction, rejected as `CATALOG_OBJECT_INVALID` before receipt checks. A read-only comparator against the exact copied source and database proved that prewarming reconstructed the stored closure (`2F4827…`), input (`0A345A…`) and manual (`AFF077…`) fingerprints and recovered the successful review. Receipt evidence: preservation root `p5-evidence/candidate-scope-final-receipt-pin-export.json`, SHA-256 `C8A44DE05B4AE9AE8180FE8FA6D2883AEF6D79E5CB9968142D4FA4C020DB1E4D`.
- `676182b7` corrects that cold path. Query and procedure review prime the exact retained permission snapshot in the scoped catalog provider; subsequent feature/manual reads reuse it without object registration. Ordinary cold feature lookup, public registration, legacy activation behavior, publication policy and freshness remain intact. The strengthened DELETE-mode gateway regression contains a retained object and real SQLite registry, reuses the completed review, rejects stale resolution, activates and replays. Temporarily restoring the old path reproduced the failure. Final affected checks: 10 passed/zero failed; earlier catalog cache checks: five passed; procedure publication checks: seven passed. Rebuild the matching Release graph, restart the copied host, and reuse the successful review unchanged before continuing the two remaining copied candidates. Copied activation 66/page 82 and live state remain unchanged at this checkpoint.
- Local branch cleanup is complete: only `master` remains. Detached checkouts, their commit references and recovery evidence remain preserved; remote branches were untouched.
- Release compilation encountered a reproducible Roslyn nullable-flow-analysis crash. Same-source normal Debug checks passed. Building Release with `Nullable=annotations` retained type annotations and runtime behavior while skipping that compiler warning pass; the build passed with zero errors. Corrected DataAccess assembly SHA-256: `F578035F18390EE0677B490910E1DC328D63D7D6B44CA50535CE8F8C1BB4DA0B`. Preserve `.tmp/gameplay-dataaccess-release.log` and the compiler-failure log as evidence. Keep final host and Tools on the same verified assembly graph.

The matching `676182b7` Release build passed in 22.82 seconds with zero warnings/errors. The copied
host now runs the verified framework-dependent artifact-bin graph, but fresh Validate operation
`0369f43b7be6a483e0fb363c1bf49eb2` still persisted `unavailable` with dependency, reuse-review and
pure-closure unavailable diagnostics. The exact read-only comparator ruled out stale assemblies,
DI and receipt pins: the same proof resolves with adequate time, but cold reconstruction takes
11.923 seconds and exceeds the capability's ten-second deadline. `b3a141ab` gives only synchronous
Validate/Activate a host-owned sixty-second maximum, still clipped to permission expiry.
Queued review submission, worker leases, AI budgets and receipt checks are unchanged. Candidate,
successful review, copied activation 66/page 82 and live state remain unchanged. No additional
provider call was made.
Evidence: preservation root `p5-evidence/candidate-scope-validation-after-cold-fix.json`.

The final deadline/grant gateway checks passed 3/3; query/procedure publication checks passed 8/8
in fifteen seconds. The exact read-only copied comparator recovered review evidence `8AFE623F…`
in 20.673 seconds with the sixty-second allowance. No provider call or database write was needed.
The matching Release build passed with zero warnings/errors. Final assembly hashes: DataAccess
`27250720DB64C906E2A087F826FA59B70F6342B0B999ED569206DDF90F412887`, domain
`D9BD349BC7F8B394499E489ECEA7BEBCF99A445E73943173CD765959943592BC`, LocalAI
`33F761E0053EA03EABF041C1C6E35BE3C6B0C107F66D201E9632228F38C653CB`.
Build log: `.tmp/gameplay-dataaccess-release-budget.log`. The sole writer is authorized to resume
the copied activation sequence with that matching graph; live writes remain gated by acceptance.

The resumed full HTTP Validate still returned unavailable (operation
`1cdbde93702696c959d7d5740db54039`, 16.411 seconds). A second exact read-only reproduction found
the missing production condition: empty-sample validation has two operations, uses one itself,
then tries procedure review before query review. Both readers reconstructed the manual before
checking their grammar. With one operation left, the inapplicable procedure reader consumed it;
the query reader then failed. Query-only reconstruction with the same two-operation budget
recovers `8AFE623F…`. `c9005393` fixes the common receipt reader to check its owner-verified captured
grammar before binding manual context. Both readers together now recover the same review within
the unchanged two-operation budget. Cold full gateway query and procedure checks passed 3/3 in
fourteen seconds. No operation limit or paid review changed. Evidence:
`.tmp/receipt-diagnostic/artifact-check/budget-order.log` and `budget-order-fixed.log`.

Matching `c9005393` Release build passed in 22.82 seconds, zero warnings/errors. Final graph hashes:
DataAccess `CD43AF94D9C37B208D1DADC39463F9A5B775906FE87414AD99C1916AB24F8549`, domain
`D76555D50E0BADC31955D1271A7B71181E193ED410F8F14ED51B289955BB1C7F`, LocalAI
`8B0CF3BB5F8401BB757766A857837B6D2E7BA74B4D4F198FB6FAD58C9385B0D6`.
The sole writer is updating the existing copied graph and resuming sequential activation; inspect
the valid outcome before activation. Build log: `.tmp/gameplay-dataaccess-release-grammar.log`.

Cleanup inventory measured 22.90 GiB of generated build/dependency outputs across 34 worktrees;
12.45 GiB is in clean detached worktrees, with at least another 5.6 GiB in old main-checkout
rollout build folders. These are current logical sizes, not an exact historical growth measurement.
Automatic approval review blocked the attempted cleanup before enumeration/removal; nothing was
deleted. The checked inventory/removal helper is `.tmp/clean-detached-generated-after-delivery.ps1`
(inventory-only by default). Keep cleanup pending and report its real outcome after delivery.

Fresh copied Validate operation `31e754c1ac8d5b67fb7a8677e069afef` completed in 14.611 seconds with
`valid` reviewed-query evidence, exact dependencies and the unchanged successful review. Inspect
correctly rejected its audit guard: the writer retained the earlier generic unavailable runtime
report after reviewed-query completion replaced the preparation. The writer correction omits that
unrelated report for reviewed-query/procedure guards; verification remains strict. Keep the old
operation unchanged and create a fresh validation using the existing review, without another AI call.
The strengthened cold gateway regression reproduced the invalid Inspect result before the fix and
now passes Validate, Inspect, validation replay, activation and activation replay with both readers
and generic preparation enabled. Affected query, procedure and dream publication checks passed 3/3.
The matching `d0481f48` Release build passed in 21.14 seconds with zero warnings/errors; evidence is
`.tmp/gameplay-dataaccess-release-proof.log`. Final graph hashes: DataAccess
`5D27E4951AA143FDFDD16C7D906906B960E74DEF0F5002341AA45550F9F62BDB`, domain
`5AD036A9737F84BBC33B8424162797FD40ED412AC517A29915CB4619F132D542`, LocalAI
`CC796265B1ECD57BE27D7EED64C2BA9CAB165DA24A49EDF4CF752B6D5BFA5F11`.

Live execution must override every rehearsal operator default explicitly: URL, database, blobs,
matching Tools/runtime, package, schema and page bundle. Compare those paths to the saved profile;
verify the fresh active-page CAS and reviewed bundle hash before applying. Record exact live
grant revisions/fingerprints for all publication operations, with expiry covering their deadlines.

The first two copied queries are now active. Scope validation `773f5327085349b4e92c84c73c37a435`
inspected valid with no generic runtime report; activation `f23f7d020e4bf5b02fe61671dc4e5b9a`
and its replay matched exactly. Paginated scope received `extendExisting` from one real Sol
attempt (`task.3ae37426781f4097ce5c3cf20756cb80`), validated as
`51659bcb92fb7b1d748e9303025855b4`, and activated as `de19edf5f13091024e79d2b287fdf17c`.
Dream candidate `9c295fe16fbb71c233c4a0e6de5feb0c` revision 1 received `justifiedNew` from normal
review task `task.f7ed8baaadc625cf7d1167a5f4ff0a10`. Validation
`009e326feaf4b0a46da4bf3e2d1fb465` inspected valid; activation
`523701825cda851a0139a6dfdc6249e9` committed. Copied application activation is now 69, fingerprint
`2E250D74C07A708B6EA9A5037C3818A64BA6F94ADF350C8A06923F33F5604EB2`. Compatible state binding
automatically advanced to 43, fingerprint
`820EE6F7A56D0E3FA7B6C3E6ACA50C6A9174E44089C06FCDCDFEA39885AA9831`, and pins activation 69.
Page 82 is active/latest. The owned second restart preserved the use, web-composition, runtime
services and trigger-scheduling manual revisions/hashes. No live feature write has occurred.
The three successful copied candidates used one paid attempt each: 29,987, 30,902 and 23,116
tokens. The two earlier unsuccessful paid reviews remain separately accounted for above.

Final copied browser gates passed: public Player projection issued two scoped tickets without
internal asset hashes or DM fields; the Gilded Kettle map ticket redeemed the exact retained PNG
(`F43C2BC732B3CCE3383FF19327AA54266E9460B41C770AB318D0DBFD75722E5E`, 2,935,466 bytes,
1447×1087). Forged DM and hidden/raw/direct access were denied; invalid tickets returned 404.
Owner DM stayed ready for sixty seconds with no stale banner, fatal error or API failure. The
existing focused revocation-on-change test covers revocation without mutating frozen state.
Evidence: `.tmp/gameplay-browser/public-security-postactivation.json` and `final-page82-stability.json`.

Root authorized live cutover on 2026-09-13 after those gates. Preflight still showed original
live PID 25008, application 66, binding 40, page 80 and no standing grants. The sole writer will
preserve fresh live state and publish into isolated `platform-gameplay-d0481f48-20260913-host`
and `-source` release paths. No copied database or review receipt may replace live state.

Fresh live preservation completed: online backup `dantesroleplay.db.backup-20260913T070957841Z`
is 100,007,936 bytes, SHA-256 `4F8E16C7EBC93E43906C21B7EBA6083E85AF665278636B855FA085EC6F8A9290`,
with a clean integrity check. The 277-record rules export and all 152 retained blob hashes
(314,544,806 bytes) were verified, with no missing, mismatched or extra blobs. The old owned
PID 25008 was stopped after ownership verification; isolated `d0481f48` host PID 26596 now serves
port 6217 and completed migrations. Active application 66/page 80 remain preserved at this
checkpoint. Owner loopback resolves DM; actual public Host/Origin resolves PlayerGroup.
Namespace synchronization changed only `dnd2024.query`; the narrow catalog workset updated
map anchor/visual contracts and left 277 records unchanged. Immutable map schema v2 was registered
with the old v1 hash preserved. Same-ID component migration and live content/candidate/page
publication were pending at the host-switch checkpoint. Evidence stays under the existing
preservation root `p5-evidence`.

The live same-ID migration subsequently passed: all sixty map component data-byte hashes stayed
identical and revisions advanced exactly once. The world/media packet applied eleven assets,
seventeen entities and eight relationships. A fresh release-only grant was issued with bounded
expiry, and the first live map-query candidate received `extendExisting` in one real Sol attempt,
validated and activated with an exact retry. Two sequential live candidates remain at this checkpoint.
Additional focused regression coverage for the shared receipt reader passed 2/2 on the existing
Debug graph: workflow publication/replay and stateful publication/execution/replay. No additional
build or provider call was used for those checks.

Final live acceptance is complete. All three fresh live reviews completed in one attempt each;
validation, activation and exact retries passed. Live activation 69 is
`69421896F570E2C6D34EB06FC6E460EAEEE04396C497433CA716E35618FD084C`, binding 43 is
`7C49F32E65FF3D435CA675AFBC0735FB830FA35E6E81EDCB644548939B6FD6F2`, and page 81 content is
`390FA0244C541ADCFB376A40B513DBAD85F050FB9BFDF20425B24B983EC80B33`. The saved launch profile
hash is `9F9123664C5D606DD320A9AD9106E6F54B6F5BAB162CF516E617D4CC4C22C911`; the final live host
is PID 9112. The three-origin readiness and manual hashes stayed stable across the second owned
restart, and the separate release grant was revoked after replay/inspection. No unfinished review
task or unsettled provider reservation remains.

Live browser checks passed public Player image issuance/redemption and secret/raw/forged access
denials, town and inn overlays with zoom/pan, Player cache clearing, Green & Wood, and a sixty-second
stable owner session. Evidence: `.tmp/gameplay-browser/public-security-live.json`,
`live-focused-presentation.json` and `final-live-stability.json`. All eight maps and three galleries
were already checked in the byte-identical copied bundle. Live MCP orientation, intent-directed
manual retrieval, active dream procedure, protected unconfigured memory state and matching Tools
capture help also passed; evidence is `p5-evidence/live-final-manual-context-proof.json` under the
preservation root. Detailed recovery/profile witnesses are in [local deployment](upgrade/06-local-deployment.md).

Human setup instructions for explicitly linked gameplay capture are in
[OPERATIONS.md](OPERATIONS.md#link-a-gameplay-task-to-conversation-memory); live capture remains
unconnected until an actual gameplay task is selected.

### Post-delivery cleanup result

The owned rehearsal process was stopped; live PID 9112 remains the sole listener on 6217, and
16217 is closed. Only the local `master` branch remains. Automatic approval review rejected
generated-file deletion with “blocked by policy”; no deletion was retried through another route.
The safer lossless NTFS compression pass completed across the checked inactive generated outputs,
excluding the active checkout, toolchain, authored source/assets, databases, live releases and
recovery evidence. All 444 targets and 52,179 files passed count, logical-size and representative
hash preservation checks; 437 native compression calls succeeded and seven empty targets required
none. There were no native or preservation failures, and nothing was deleted, moved or replaced.

The observed drive free-space gain was 3,058,266,112 bytes (2.85 GiB), ending at 36,247,543,808
bytes (33.76 GiB) free. The summed per-path storage metric fell by 5,570,169,285 bytes (5.19 GiB);
that is a different measurement, and the report does not establish the cause of the difference.
Do not describe that summed figure as the observed gain in drive space. Report:
`.tmp/compress-detached-generated-after-delivery.json`, SHA-256
`AA95251D4501EBB19FEF104A15EA73A2C5181F2057803C8C3C28E2EECBFBECB3`.
Reuse the current build graph for further work; avoid retaining a new complete build for each
diagnostic attempt. Source worktrees and recovery copies remain available.
