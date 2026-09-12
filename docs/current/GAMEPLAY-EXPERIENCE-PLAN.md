# Player knowledge, maps, campaign preparation, and conversation memory

## Authorization and outcome

The user requested this durable plan on 2026-09-12 and explicitly authorized immediate implementation by coordinated tasks without another confirmation. Deliver working features on master and in the local running system, with the necessary tested migrations, retained assets, catalog synchronization, release selection, and verification. This request supersedes the previous product decision to expose only a shared full-context website with no DM/Player switch.

Preserve the existing campaign, character state, authored facts, secrets, history, and retained assets. Export live records before editing corresponding files. Use supported stores, typed operations, and version-checked publication. Keep recovery copies and the previous release. No unrelated branch deletion, remote publication, database reset, global conversation scraping, or unrestricted secret exposure is authorized.

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

The website task may coordinate P1 and P2 with separate backend and frontend workers. The content task owns P3. The existing inner-AI task owns P4. Assign exact task IDs and worktree paths in the checkpoint before dispatch. Tasks read this document and only their exact implementation owners, not the entire planning archive.

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

- Status: P1/P2, P3 and P4 are running in the three assigned tasks with GPT-5.6 Sol high. Dispatch followed plan commit `51aa6d3a`. No live mutation yet.
- Root task: `01a09056-6d13-76d0-8706-40e435fb8b03`; master `ddcd5eed` before this plan.
- Reconnaissance: `workflow_publication_finish` reads audience/map owners; `final_guide_alignment` reads conversation/capture owners; `website_integration` exports the live starting-area/audience/map context and owns later deployment.
- Decisions fixed: Player sees party-union knowledge; DM is server-authorized; labels/locations are data overlays; consistent zoom uses shared geography; capture is linked/scoped and preserves real visible conversation turns; dreamed memories are evidence-linked derived records.
- Important security finding: `AllowAnonymousPublicAccess` currently grants operator capabilities, and `SharedWebsiteContext.IsTrusted` includes that identity. Remove that elevation as part of P1; a presentation switch alone is insufficient. Preserve a protected local-owner DM path. Test direct/raw routes as well as rendered projections.
- Existing map owners already use normalized 0..1000 frames and DOM markers. Extend those owners, preserve authoritative anchors, and replace baked labels in retained artwork. Do not create a second map engine.
- Live content baseline: campaign `campaign.caldris.measure-of-mercy`, current location `location.caldris.bramblebridge`, active party member `actor.caldris.ganji`; no active session/conversation/encounter. Relevant chain: Caldris atlas → Eredane → Alderwick → Bramble Country → Bramblebridge → Gilded Kettle. Exact records, anchors, images, revisions and hashes are preserved under `DantesRoleplay.MCPServer/data/backups/caldris-starting-area-recon-20260912T1330Z`.
- Memory design: reuse the `play-recording` owner for a separate scoped outer-message journal; do not insert unreviewed outer messages into authoritative gameplay planning history. Existing MCP `query`/`commit` verbs may expose `system.conversation-memory`; canonical manuals may use `procedure.system.conversation-memory` and `procedure.system.conversation-dream`. These scoped IDs/contracts are authorized by this implementation plan. Start private to the linked principal/session, with explicit authorized promotion through normal operations when desired. Derived output cannot promote itself.
- Codex adapter: installed CLI 0.153.4 supports read-only `thread/read`; the existing bridge is pinned to 0.149.1. Use a tested version-aware adapter and repository-scoped, explicitly linked gameplay sessions. Do not scrape global conversations or rely on an unstable transcript format.

| Owner task | ID | Detached checkout | Boundary |
| --- | --- | --- | --- |
| Implement plan 06 website interface | `01a0917b-34d0-7cc1-bbbf-9689abd0b2e9` | `C:/repo/DantesRoleplay-dnd-web` | P1 + P2; audience/auth, website, map geometry/rendering; owns shared frontend files |
| Implement runtime authoring | `01a09179-4f0e-7c00-9411-cddb4b9ecbba` | `C:/repo/DantesRoleplay-gameplay-content` | P3; prepared content, retained image/map assets and coordinate manifest; no frontend/auth edits |
| Implement focused INNER AI workers | `01a0917a-67f5-78d0-9555-cd9265c27348` | `C:/repo/DantesRoleplay-gameplay-memory` | P4; journal, capture adapter, memory/dream manuals, focused tests; no website map edits |

- Root owns this plan, README, integration, acceptance and runtime selection. Implementation tasks return commits and concise evidence; they do not modify live data or the original checkout. Deployment worker remains the only live writer.
- P2/P3 agreed geometry: 0..1000 normalized coordinates, top-left origin, east/right, south/down, north-up. A child `mapAnchor` is a point in its parent's frame. Missing coordinates remain unplaced. Explicit child bounds/transforms require authored evidence; do not infer alignment from pixel dimensions or center every child automatically.
- Asset inventory confirms baked labels/markers in all six active levels (atlas, Eredane, Alderwick, Bramble Country, Bramblebridge, Gilded Kettle). P3 must replace the coherent family and retain its old hashes; nearby detail can reuse the settlement and inn plus another verified site. Both old player/DM variants currently use identical map bytes.
- P4 will isolate captured messages from `ApplicationPlayMessageRecord`; private source journals and derived-memory references use existing play-recording persistence and INNER tasks. Shared migration/DI changes are confined to the memory lane and integrated by root.
- Workspace preparation encountered limited disk space. New lanes use sparse detached checkouts. Seven idle, verified-clean checkouts were sparsified to remove only redundant tracked export/world/PDF copies from their working trees, leaving Git history, original files, live data and recovery copies intact; about 5.3 GB was free afterward. Check capacity before release copies.
- Next: implement P1–P4 concurrently, then continue through P5 without another user confirmation.
