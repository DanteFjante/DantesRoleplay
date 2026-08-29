# D&D 2024 web UI dependency plan — private player and GM workspace

Status: **Orders 0–7D3 accepted 2026-08-27; known-place map Order 7E is next**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Ruleset alignment: **dnd2024-compatible**
Source: **not applicable to the UI itself**. The UI consumes the accepted `dnd2024` catalog,
component schemas, mechanics, procedures, and exact `source.dnd2024.srd-5.2.1` provenance without
redefining rule meaning.

## Outcome and non-goals

Deliver one responsive private D&D 2024 workspace centered on the information a player needs at the
table: the selected character sheet, current place and available imagery, remembered player-safe
knowledge, and switchable people in the current scene. Accepted dice, character, inventory, and
encounter controls remain available without dominating the viewport. The player or GM selects the
exact `dnd2024` campaign and relevant entities, while the page continues to use current application
state and the existing application-scoped conversation.

The first accepted outcome is a bounded core-play UI for the rules that exist today. It is not a
claim that the complete SRD is implemented.

Slice 5A restores campaign discovery for registered state spaces whose historical campaign graph
predates the current D&D action descriptors. It is a read-only compatibility surface: the page
must follow the canonical campaign-to-participation-to-actor relationships rather than infer a
campaign from identifiers, names, source paths, or directories. It does not make a stale binding
eligible for current D&D action controls.

Non-goals:

- do not calculate modifiers, proficiency, damage, mitigation, carrying capacity, turn economy, or
  outcomes in browser code;
- do not let HTML call SQL, MCP, catalog JavaScript, the action runner, or internal control-center
  routes directly;
- do not accept caller-supplied derived values, effects, revisions, authorization, or confirmation
  truth;
- do not use `<system-form>` or `<system-action-button>` for D&D actions; those controls are
  intentionally limited to `system.*` capability authority;
- do not make application chat a hidden transport for an ordinary button or form;
- do not create duplicate character, encounter, item, dice, or rules state in browser storage;
- do not add a frontend framework, Node build, server-rendered D&D view model, or D&D-specific C#
  rule logic; and
- do not create empty spell, monster, tactical-map, rest, death, Inspiration, or character-builder
  components before their independent gameplay owners are accepted.
- do not infer a current place, scene membership, player knowledge, image attachment, or map
  visibility from entity names, paths, ID prefixes, or browser-local conventions.

## Existing owners and evidence

| Concern | Owner | State | Evidence/consequence |
| --- | --- | --- | --- |
| Private HTML pages, ZIP assets, revision preview/publish/rollback, CSP, quotas, and SSE page invalidation | `web-interface` | verified | Reuse the existing page/bundle lifecycle. |
| Shared navigation and general system chat | `<system-navigation>` and `<system-chat>` in `/components/system-workspace.js` | verified | Embed unchanged; system chat receives no D&D ECS state. |
| Application-scoped conversation | `<application-conversation>` and `ApplicationConversationService` | verified | Reuse exact `dnd2024` plus state-space binding. |
| System action/form controls | `<system-action-button>` and `<system-form>` | verified, wrong authority for D&D | Their implementation pattern is evidence only; never widen them to application mechanics. |
| Registered D&D application and state-space discovery | application registry/state-space owners | verified | The workspace must bind exact current application/state-space identities. |
| D&D component state | current application ECS and 27 active `dnd2024` component definitions | verified | Components remain canonical; the page stores only selected IDs and disposable render state. |
| D&D behavior | 56 accepted catalog JavaScript mechanics and 45 procedures | verified | Every action control names one current mechanic; browser code supplies only schema-valid source input and role IDs. |
| D&D static content | 43 accepted core records | verified but deliberately narrow | Content browser may show current armor, gear, currency, weapon, and Fighter identity records only. |
| Generic ECS inspection | control-center `ControlStructureExplorer` | verified, operator-control scope | Reuse underlying application services through a dedicated application-page read adapter; do not call `/api/control/**` from the D&D page. |
| Legacy dynamic page reads | `/api/data/entity/**` and `/api/data/{componentType}/{entityId}` over `IWorldStore` | verified, not the application ECS owner | Do not treat this route as D&D application authority. |
| Direct application action controls | no web owner | **missing** | Add a ruleset-neutral descriptor/read/prepare/confirm/execute adapter over existing application owners. |
| Application-to-page association | no accepted owner | **missing** | A reviewed mapping is required before shared navigation can link `dnd2024` to a dedicated page. |
| Application ECS change invalidation | no proven application-page stream | **missing** | Start with bounded refresh; add revision-aware invalidation only after its owner is proven. |
| Spells, monsters, tactical position, rest, dying, Inspiration use, and complete character construction | independent D&D feature gates | planned/missing | No first-release UI component may imply these capabilities exist. |

## Proposed web component inventory

All new IDs below are proposals, not authorization to create permanent custom elements. They require
confirmation before the first implementation slice. The user confirmed the first-release direction
and instructed implementation to continue on 2026-08-27. That confirmation adopts `dnd2024-play`,
`/components/application-workspace.js`, `/components/dnd2024-workspace.js`, and the listed first-
release element IDs, while later gameplay-gated surfaces still receive no IDs.

### Reused without change

| Element | Use in the D&D workspace |
| --- | --- |
| `<system-navigation>` | Private navigation and current application link. |
| `<application-conversation>` | Exact `dnd2024`/state-space chat, planning, progress, proposal, and receipt display. |
| `<system-chat>` | Optional operator/system help kept visibly separate from game scope. |

### Ruleset-neutral application controls

These belong to the generic web/application seam and are reusable by other applications.

| Proposed element | Purpose | Authority boundary |
| --- | --- | --- |
| `<application-entity-picker>` | Select one exact current application, state space, entity, and allowed role binding. | Discovery only; emits IDs and current revisions, never inferred roles or effects. |
| `<application-action-button>` | Prepare one exact effect-free or fixed-input application mechanic. | Cannot execute a write directly; current server descriptor and policy decide confirmation. |
| `<application-form>` | Render the current closed mechanic input/role contract and show result/proposal/receipt truth. | Server schema, role requirements, action owner, authorization, revisions, and effects remain authoritative. |

Recommended module route: `/components/application-workspace.js`.

### D&D 2024 presentation and composition controls

These may know stable `dnd2024.*` owner IDs and labels, but contain no D&D formulas or outcome
branches.

| Proposed element | First-release responsibility | Current owner coverage |
| --- | --- | --- |
| `<dnd2024-workspace>` | Page shell, player/GM mode, exact state-space/actor/encounter selection, refresh and scope status. | registry, state-space, application-page association |
| `<dnd2024-character-sheet>` | Compose profile, level/XP, derived sheet result, proficiencies, size, Speed, defenses, and inventory summaries. | character/profile/progression/read mechanics |
| `<dnd2024-vitals>` | AC, HP, Temporary HP, damage mitigation, Conditions, Speed, and explicit unknown/absent states. | current combat/state components |
| `<dnd2024-abilities>` | Ability scores plus effect-free character-sheet, ability-check, and saving-throw result presentation. | abilities/check/save/character-sheet mechanics |
| `<dnd2024-inventory>` | Current containment, item definitions/instances/stacks, quantity, equipment state, burden, capacity, and currency. | accepted inventory/equipment/read mechanics and 43 content records |
| `<dnd2024-action-panel>` | Schema-driven entry points for checks, saves, attacks, damage, healing, Temporary HP, and inventory transitions. | generic application form plus exact current mechanics |
| `<dnd2024-dice-tray>` | Seeded dice request/result history for the current page session; no authoritative outcome mutation. | `mechanic.dnd2024.dice` |
| `<dnd2024-encounter-tracker>` | Initiative order, round/current participant, lifecycle controls, and participant selection. | encounter Initiative/turn owners |
| `<dnd2024-turn-budget>` | Display and spend Action, Bonus Action, Reaction, object interaction, and remaining movement. | turn-budget read/write/spend owners |
| `<dnd2024-content-browser>` | Browse only currently activated D&D core/selected-extension content with source/profile labels. | public application catalog and exact source profile |

Recommended D&D module route: `/components/dnd2024-workspace.js`. Recommended page ID:
`dnd2024-play`. Both are confirmed for the private first release.

### Confirmed interaction language

The player-facing surface must feel like a game table, not an administration console:

- ability scores are large tappable tiles; edit mode uses visible minus/plus steppers and bounded
  increment feedback rather than raw number fields;
- HP, Temporary HP, AC, Speed, and Exhaustion use dedicated meters, shields, pips, or counters;
- Action, Bonus Action, Reaction, object interaction, and movement use spendable resource tokens;
- Conditions and proficiencies use selectable chips with clear active state;
- Initiative uses ordered participant cards with a strongly marked current turn;
- inventory uses item cards, equipment slots, quantity steppers, and contextual move/equip/use
  controls;
- dice, checks, saves, attacks, damage, and healing use purpose-built controls with immediate result
  presentation; and
- conventional labeled forms remain appropriate for names, descriptive profile text, complex
  source choices, and advanced exact input, but raw JSON/schema forms are an optional operator
  inspector rather than the normal game experience.

These controls still submit only closed source input. Minus/plus buttons and tokens never calculate
or commit locally; they prepare an exact current mechanic request and render server result/receipt
truth.

### Explicit future components — no IDs yet

Do not assign permanent element IDs until the named gameplay gates close:

| Future surface | Blocking D&D owner |
| --- | --- |
| Complete character builder | species/background/feat schemas, choice/grant transaction, advancement/resources |
| Spellbook and casting panel | spell identities/profiles, prepared/known state, resources, targeting, concentration, effects |
| Bestiary/monster sheet | statblock schema, creature bootstrap, actions and multiattack |
| Tactical map | position/grid/occupancy, terrain, range, reach, line of effect, movement transaction |
| Rest panel | application-to-world clock dependency and rest benefit/resource owners |
| Dying/death panel | zero-HP consequences, death saves, stabilization, recovery and death timing |
| Heroic Inspiration control | grant/consume/reroll binding and overflow transfer |
| Magic-item panel | profile, attunement, charges, activation, consumption and effect owners |

## Target architecture

~~~text
versioned private page: dnd2024-play
│
├─ existing <system-navigation>
├─ <dnd2024-workspace>
│  ├─ generic application read adapter
│  │  └─ registered application ECS/catalog services
│  ├─ read-only D&D presentation components
│  └─ <application-form> / <application-action-button>
│     └─ application web interaction adapter
│        ├─ exact current mechanic descriptor + schemas + role requirements
│        ├─ private authorization and current revision revalidation
│        ├─ existing application action/coordinator owner
│        └─ typed result/proposal/effects/receipt
├─ existing <application-conversation application-id="dnd2024" ...>
└─ optional existing <system-chat> in a separate operator section
~~~

Browser components are request and presentation surfaces. The application action owner evaluates
catalog JavaScript, proposes typed effects, rechecks observed revisions, commits one transaction,
and returns the receipt. The page never constructs or applies effects.

## Dependency tree

~~~text
Private D&D 2024 player/GM web workspace                              [in progress]
├─ A. Confirm product and permanent web identities                    [confirmed]
│  ├─ A1. Player + GM first-release scope and private-only access       [confirmed]
│  ├─ A2. Page/module/custom-element IDs listed above                   [confirmed]
│  └─ A3. Exact application-to-page association contract               [confirmed boundary; implementation planned]
├─ B. Generic application web read seam                               [in progress]
│  ├─ B1. Exact app/state/entity/component reads with revisions         [accepted]
│  ├─ B2. Safe catalog/content/descriptor projection                    [planned]
│  └─ B3. Cross-app/state/control-route isolation evidence              [accepted for state reads]
├─ C. Read-only D&D workspace                                         [in progress]
│  ├─ C1. Shell, selection, scope/unknown/error states                  [accepted]
│  ├─ C2. Character sheet, vitals, abilities                           [accepted]
│  ├─ C3. Inventory and activated content browser                       [accepted]
│  ├─ C4. Encounter tracker and turn-budget display                     [accepted read-only]
│  └─ C5. Player-first character/campaign/combat viewport composition   [Order 7B accepted]
├─ D. Generic application action seam                                 [accepted]
│  ├─ D1. Safe current mechanic descriptor/input/role projection        [accepted]
│  ├─ D2. Prepare/read versus confirm/write coordinator adapter         [accepted]
│  ├─ D3. Idempotency, stale authority, replay, rollback and receipts   [accepted]
│  └─ D4. Generic entity picker, action button and form                 [accepted]
├─ E. D&D action controls                                             [accepted current scope]
│  ├─ E1. Seeded dice, ability checks and saving throws                 [Slice 5 accepted]
│  ├─ E2. Attack, damage, mitigation, HP, Temporary HP and healing      [Slice 6A accepted]
│  ├─ E3. Recorded encounter lifecycle and turn-budget spending         [Slice 7A accepted]
│  └─ E4. Ordinary transfer/equip/stack/activity flows; admin bootstrap excluded [accepted]
├─ F. Player-relevant campaign context                                [planned]
│  ├─ F1. Current location and switchable co-present people             [Order 7C accepted]
│  ├─ F2. Audience-safe remembered player knowledge                     [Order 7D blocked at reviewed live knowledge state]
│  ├─ F3. Display-only known-place map from accepted anchors            [planned after F1/F2]
│  └─ F4. Location/person visual attachments                            [missing authoritative owner]
├─ G. Browser quality and live behavior                               [blocked by C and F]
│  ├─ G1. Keyboard/screen-reader/mobile/high-contrast acceptance        [planned]
│  ├─ G2. Revision-aware bounded refresh/invalidation                   [planned]
│  └─ G3. Loading/empty/unknown/denied/stale/replay/error states         [planned]
├─ H. Page bundle, activation and player-viewport acceptance          [blocked by A–G]
│  ├─ H1. Versioned D&D page bundle and application association         [planned]
│  ├─ H2. Disposable browser/action/catalog/full-suite verification     [planned]
│  └─ H3. Backed-up private live activation and smoke test              [planned]
└─ I. Later combat authoring and tactical play                        [deferred]
   ├─ I1. Campaign encounter registration and roster/Initiative authoring [awaiting semantic confirmation]
   ├─ I2. Weapon attack and damage controls                              [planned]
   └─ I3. Grid, occupancy, terrain, route, range and movement UI         [blocked by gameplay owners]
~~~

## Conflicts and decisions

| Conflict | Required decision |
| --- | --- |
| Existing system forms versus D&D application actions | Keep `system.*` controls unchanged; add a separate generic application-authority seam. |
| Control-center ECS routes versus player page reads | Reuse underlying services, not `/api/control/**` routes or operator-control presentation contracts. |
| Existing `/api/data/**` versus application ECS | Treat `/api/data/**` as its existing `IWorldStore` owner; add exact registered-application reads rather than aliasing authorities. |
| Chat execution versus ordinary UI controls | Both may reach the existing application coordinator/action owner, but buttons/forms do not create hidden chat turns. |
| Browser convenience versus D&D rule authority | Browser may format values and labels only; every derivation and outcome comes from current catalog mechanics/results. |
| Direct actions versus review | Every direct web action, including an effect-free mechanic, uses the current prepare/explicit-confirm/execute policy and shows exact receipts. |
| Player versus GM visibility | First slice must define one explicit private visibility policy; hiding controls in CSS is not authorization. |
| Current supported rules versus desired complete UI | Render only accepted owners; future surfaces stay absent rather than disabled mock functionality. |
| Live refresh versus stale state | Every action revalidates current server authority regardless of refresh timing; stale UI can never cause a blind write. |
| Pre-order encounter identity versus arbitrary containers | Add a canonical campaign-owned encounter registration; never infer encounter identity from names, ID prefixes, or mere containment. |
| Campaign scope versus D&D mechanics | Reuse one effect-free `game.core` character-participation verifier and a canonical campaign-to-encounter link; do not copy campaign IDs into D&D state as caller assertions. |
| One-shot Initiative versus actual tie choice | Preserve the accepted one-shot mechanic for compatibility. Add a catalog-owned roll draft that records derived counts/tie groups, then a separate finalize root that accepts order only inside those recorded groups. |
| Initiative and active rests | The roll-draft transaction applies the existing child rest-interruption plans when Initiative is actually rolled. Finalization never repeats or invents rest effects. |
| Dashboard controls versus player information | Make character and current-context information the primary viewport; keep accepted controls in contextual Character or Combat views rather than treating every panel as equally prominent. |
| Current actor location versus child-only containment reads | Add one generic, exact parent-containment/context projection before showing a current location or co-present people; do not scan every possible container in the browser. |
| Player knowledge versus arbitrary fact browsing | Expose only the existing audience-authorized knowledge owner through a private web adapter; the browser may never choose hidden visibility or treat every fact entity as known. |
| Geographic map versus tactical battle map | A display-only known-place map may consume accepted anchors after visibility is safe. Grid occupancy, blocked squares, routes, range, and movement remain a later gameplay system. |
| Attached imagery versus path/name conventions | Add a reviewed authoritative visual-reference owner before associating an asset with a location or person; never guess from filenames or directories. |

## Ordered leaves

One effort point (EP) is one focused engineering/review block, roughly two to four hours of
human-equivalent work after the slice contract is prepared. EP is a planning estimate, not elapsed
model runtime or a delivery promise.

| Order | Leaf | Depends on | Primary model | Reasoning effort | EP | Review | Exit gate |
| ---: | --- | --- | --- | --- | ---: | --- | --- |
| 0 | Confirm first-release roles, `dnd2024-play`, module routes, custom-element IDs, application-page association meaning, and game-control interaction language | accepted web/D&D owners | `gpt-5.6-sol` | high | 1–2 | Confirmed by the user's 2026-08-27 instruction to continue | Confirmed; no runtime artifact in Order 0. |
| 1 | **Accepted:** read-only game viewport foundation: exact application read adapter plus `<dnd2024-workspace>` shell and page | 0 | `gpt-5.6-sol` | high | 7–10 | [Slice 1 receipt](DND2024-WEB-UI-SLICE-1-RECEIPT.md) | Exact authorized app/state/entity/component reads feed a recognizable game HUD; unknown/cross-scope inputs fail closed and no action/write control exists. |
| 2 | **Accepted:** character/encounter detail in [Slice 2A](DND2024-WEB-UI-SLICE-2A-RECEIPT.md), bounded direct custody/item cards in [Slice 2B](DND2024-WEB-UI-SLICE-2B-RECEIPT.md), activated item facts in [Slice 2C](DND2024-WEB-UI-SLICE-2C-RECEIPT.md), and bounded nested inventory in [Slice 2D](DND2024-WEB-UI-SLICE-2D-RECEIPT.md) | 1 | `gpt-5.6-terra` | high | 5–8 | Focused/full browser and containment review | Current values and explicit absent/unknown states render without duplicating derived authority. |
| 3 | **Accepted:** generic application mechanic descriptor and prepare/execute adapter | 0–1 | `gpt-5.6-sol` | xhigh | 8–13 | [Slice 3 receipt](DND2024-WEB-UI-SLICE-3-RECEIPT.md) records authority/transaction review and full evidence | Exact schema/role projection; effect-free results work; every action requires current authority and returns typed receipts. |
| 4 | **Accepted:** generic application entity picker/button/form ([Slice 4 receipt](DND2024-WEB-UI-SLICE-4-RECEIPT.md)) | 3 | `gpt-5.6-terra` | high | 4–6 | Sol high verifies that presentation cannot widen authority | Accessible schema/role controls cannot bypass confirmation, tamper with fingerprints, or construct effects. |
| 5 | **Accepted:** D&D stateless actions ([Slice 5 implementation](DND2024-WEB-UI-SLICE-5-IMPLEMENTATION.md), [receipt](DND2024-WEB-UI-SLICE-5-RECEIPT.md)); campaign selection/read compatibility ([Slice 5A implementation](DND2024-WEB-UI-SLICE-5A-IMPLEMENTATION.md), [receipt](DND2024-WEB-UI-SLICE-5A-RECEIPT.md)) | 2–4 | `gpt-5.6-terra` | high | 3–5 | Sol high reviews any apparent rule/result mismatch | Dice/check/save results match direct accepted mechanics for equal state/input/seed; campaign reads stay exact and stale actions stay locked. |
| 6 | **Accepted:** [Slice 6A](DND2024-WEB-UI-SLICE-6A-RECEIPT.md) healing/Temporary HP; [Slice 6B](DND2024-WEB-UI-SLICE-6B-RECEIPT.md) equip/unequip; [Slice 6C](DND2024-WEB-UI-SLICE-6C-RECEIPT.md) ordinary transfer, stack, and item-use controls | 2–4 | `gpt-5.6-sol` | high | 5–8 | Sol high transaction/replay review | HP/healing/Temporary HP and ordinary inventory/equipment flows commit exactly once; stale/replay/rollback cases pass. |
| 7A | **Accepted:** recorded campaign encounter selection, turn start/advance/end, and spendable Action/Bonus Action/Reaction/Interaction/movement tokens ([receipt](DND2024-WEB-UI-SLICE-7A-RECEIPT.md)) | 2–6 | `gpt-5.6-sol` | xhigh | 3–5 | Sol xhigh composition and concurrency review | A valid order-bearing campaign encounter can run lifecycle and budget spends through reviewed receipts without browser-owned turn rules. |
| 7B | **Accepted:** player-first viewport and the already-confirmed `<dnd2024-character-sheet>` composition element ([receipt](DND2024-WEB-UI-SLICE-7B-RECEIPT.md)) | 1–7A | `gpt-5.6-terra` | high | 2–3 | Sol high verifies authority-preserving composition and absent future surfaces | Character is the default accessible view; Campaign and Combat are deliberate secondary views; every accepted panel keeps working and no scene/map/knowledge placeholder is invented. |
| 7C | **Accepted:** current location and switchable people in the current scene ([receipt](DND2024-WEB-UI-SLICE-7C-RECEIPT.md)) | 7B plus accepted containment/location owners | `gpt-5.6-sol` | xhigh | 4–6 | Sol xhigh public-surface, campaign-scope, and visibility review | One exact generic direct-parent containment read identifies the selected actor's recorded place; bounded direct `presence` contents yield campaign actors and recurring world actors without browser-wide container scans. |
| 7D0 | **Accepted:** authorized knowledge core in current modular runtime owners ([receipt](DND2024-WEB-UI-SLICE-7D0-RECEIPT.md)) | accepted knowledge semantics plus current modular kernel boundary | `gpt-5.6-sol` | xhigh | 8–13 | Architecture, vocabulary, authorization, bypass, focused, and 1,396-test acceptance review complete | Current code owns provider-neutral policy/result contracts, application-ECS canonical projection, effective state, pre-limit allowlisted retrieval, and safe answer coordination without a runtime dependency on `old-dnd`. |
| 7D1 | **Accepted:** fixed loopback-only Orban actor seat, activated application binding, and exact campaign participation verification ([receipt](DND2024-WEB-UI-SLICE-7D1-RECEIPT.md)) | 7D0 plus selected audience owner | `gpt-5.6-sol` | xhigh | 4–6 | Identity, revocation, source-drift, participation, cross-campaign, 1,402-test, and protocol review complete | Ambient host policy—never browser input—selects the exact campaign actor, activated catalog metadata owns D&D vocabulary, and denial precedes game reads. |
| 7D2 | **Accepted in the combined 7D2–7D3 batch:** reviewed campaign actor knowledge state ([receipt](DND2024-WEB-UI-SLICE-7D2-7D3-RECEIPT.md)) | 7D0–7D1 plus explicit live-state synchronization boundary | `gpt-5.6-sol` | high | 4–7 | State-authority, no-inference, activation, atomic synchronization, and live readback complete | Eleven exact reviewed `known` relationships are active; descriptive visibility and presence create no knowledge. |
| 7D3 | **Accepted in the combined 7D2–7D3 batch:** player knowledge notebook ([receipt](DND2024-WEB-UI-SLICE-7D2-7D3-RECEIPT.md)) | 7D0–7D2 | `gpt-5.6-terra` | high | 4–6 | Audience/authorization, safe projection, accessibility, search, and live browser review complete | The page lists only authorized current-player/current-campaign knowledge; hidden and cross-campaign records never enter the response. |
| 7E | Display-only known-place map | 7C–7D plus `game.core.world.map.anchor` | `gpt-5.6-terra` | high | 3–5 | Sol high visibility and no-tactical-semantics review | Current and player-known locations render from accepted coordinates with explicit unknown/absent states; no grid, route, distance, terrain, or movement authority is implied. |
| 7F | Location and person visual attachments | 7C plus confirmed visual-reference contract | `gpt-5.6-sol` | high | 4–6 | Sol high schema/asset/visibility review | Exact reviewed state associates safe page assets with a location/person; missing imagery stays explicitly absent and filenames never become authority. |
| 8 | Responsive, accessible and revision-aware player viewport | 7B–7F | `gpt-5.6-terra` | high | 4–7 | Sol high reviews stale-state and visibility boundaries | Keyboard, screen-reader semantics, mobile layouts, refresh and every failure state pass browser tests. |
| 9 | Versioned bundle, private activation and combined player-viewport acceptance | 1–8 | `gpt-5.6-sol` | high | 4–6 | User acceptance after Sol evidence review | Backup, disposable validation, D&D/full tests, browser smoke, exact live revision readback and acceptance receipt. |
| 10A0–10A4 | **Deferred:** campaign encounter registration, roster mutation, Initiative draft, tie resolution, and final order | 7A plus confirmed campaign/encounter contracts | `gpt-5.6-sol` | xhigh | 10–15 | Sol xhigh cross-owner/transaction review | Encounter setup is authoritative, replay-safe, and campaign-scoped; it does not block the information-first viewport. |
| 10B | **Deferred:** weapon attack and damage controls | 7A | `gpt-5.6-sol` | xhigh | 4–6 | Independent Sol xhigh target/damage/transaction review | Attack preview and separately confirmed damage application bind exact weapon/target roles and preserve mitigation/HP authority. |
| 10C | **Deferred:** interactive tactical battle map | 10A–10B plus accepted grid/occupancy/terrain/range/route owners | `gpt-5.6-sol` | xhigh | 6–10 | Sol xhigh rules/transaction review; Terra high interaction/accessibility review | Blocked squares, legal routes, range, and movement choices are rendered only from accepted gameplay owners and commit through reviewed mechanics. |

Estimated remaining information-first viewport work (Orders 7E–9): **15–24 EP**. Deferred combat
authoring and tactical play (Order 10) add **20–31 EP** after their gameplay contracts are ready.
Estimates do not authorize combining leaves.

## Model assignment policy

- `gpt-5.6-sol` owns cross-owner semantics, permanent/public identities, application-state
  isolation, authorization, confirmation, effects, transactions, replay/rollback, combat
  composition, and final acceptance.
- `gpt-5.6-terra` owns contained browser composition, schema-driven controls, D&D presentation,
  accessibility, and focused tests after the authority contract is frozen.
- `gpt-5.6-luna` at medium reasoning may assist with repetitive closed fixture generation,
  viewport/accessibility matrices, and deterministic browser cases in Orders 2, 5, and 8. It is
  never the primary model and cannot decide rule meaning, visibility, authorization, component
  identity, action semantics, or acceptance.
- Model output is never self-authorizing. Repository confirmation gates, current code/catalog
  authority, executable tests, and acceptance receipts still decide whether a slice is complete.

## Lowest ready leaf

Orders 1–7D3 and Slice 5A are accepted. The
[combined 7D2–7D3 receipt](DND2024-WEB-UI-SLICE-7D2-7D3-RECEIPT.md) records activation revision 3,
eleven exact reviewed Orban knowledge relationships, safe private projection, search/filter and
keyboard interaction, excluded-secret checks, and live browser acceptance. The lowest next leaf is
7E, a display-only player-known place map. It still requires the accepted
`game.core.world.map.anchor` coordinate owner and must not imply tactical grid, route, distance,
terrain, or movement authority.

Order 7F requires a new
authoritative visual-reference contract and permanent/schema confirmation. The previous encounter
setup and weapon work moves unchanged in intent to deferred Order 10; tactical combat additionally
waits for grid, occupancy, terrain, range, route, and movement owners.

## Acceptance evidence by boundary

- Positive: current application/state/entity/component/content/action results render exactly.
- Negative/no-change: unknown app/state/entity/component/mechanic, malformed input, wrong role,
  unauthorized player/GM scope, hidden/private catalog entry, and unsupported future feature fail
  without a write.
- Boundary: general system chat cannot access D&D ECS; D&D page cannot call system administration;
  one application cannot read or act in another application's state space.
- Determinism: equal accepted state/input/seed produces the same D&D result and receipt identity.
- Replay: equal idempotent retries return the prior receipt; changed retries conflict.
- Rollback: rejected/stale/failed composed actions preserve all component and containment revisions.
- Compatibility: existing home, control center, application page, system controls, and application
  conversation tests remain green.
- Browser: keyboard-only, screen-reader labels/live regions, narrow viewport, high contrast, loading,
  empty, unknown, denied, stale, replay and server-unavailable cases.
- Acceptance: D&D focused suite, web/application orchestration suites, disposable catalog validation,
  solution build, full suite, page backup/activation readback, and private live smoke.

## Confirmation gates

Confirmation is required before:

1. adopting page ID `dnd2024-play`;
2. adopting `/components/application-workspace.js` or `/components/dnd2024-workspace.js`;
3. adopting any proposed custom-element ID;
4. defining the application-to-page association contract;
5. adding application read/action web routes or changing private authorization capabilities;
6. deciding player-versus-GM visibility or action-confirmation semantics;
7. changing any existing D&D component/mechanic/procedure meaning or adding a missing D&D family;
8. activating or publishing the live D&D page; and
9. accepting the completed feature.

No new SRD rule, house rule, compatibility behavior, migration, public MCP kind, source profile, or
live-state change is authorized by this plan.

## Planning receipt

- Runtime artifacts created: none.
- Catalog, database, live page revisions, application registrations, state spaces, routes,
  authorization, and public operations changed: none.
- New file: this dependency plan only; the web roadmap links it as prospective Feature 5 work.
- Existing D&D mechanics/components/content and existing web components were inventoried as
  dependencies, not modified.
- Exact stop: await confirmation of Order 0 before authoring one implementation slice.
