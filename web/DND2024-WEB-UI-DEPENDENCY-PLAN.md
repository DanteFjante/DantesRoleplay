# D&D 2024 web UI dependency plan — private player and GM workspace

Status: **Orders 0–4 accepted 2026-08-27; Slice 5 stateless D&D controls implemented, awaiting acceptance confirmation**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Ruleset alignment: **dnd2024-compatible**
Source: **not applicable to the UI itself**. The UI consumes the accepted `dnd2024` catalog,
component schemas, mechanics, procedures, and exact `source.dnd2024.srd-5.2.1` provenance without
redefining rule meaning.

## Outcome and non-goals

Deliver one responsive private D&D 2024 workspace where a player or GM can select the exact
`dnd2024` state space and relevant entities, inspect current character/encounter/inventory state,
invoke accepted D&D mechanics through typed controls, review results and receipts, and continue the
existing application-scoped conversation.

The first accepted outcome is a bounded core-play UI for the rules that exist today. It is not a
claim that the complete SRD is implemented.

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
│  └─ C4. Encounter tracker and turn-budget display                     [accepted read-only]
├─ D. Generic application action seam                                 [accepted]
│  ├─ D1. Safe current mechanic descriptor/input/role projection        [accepted]
│  ├─ D2. Prepare/read versus confirm/write coordinator adapter         [accepted]
│  ├─ D3. Idempotency, stale authority, replay, rollback and receipts   [accepted]
│  └─ D4. Generic entity picker, action button and form                 [accepted]
├─ E. D&D action controls                                             [E1 confirmation pending]
│  ├─ E1. Seeded dice, ability checks and saving throws                 [Slice 5 implemented]
│  ├─ E2. Attack, damage, mitigation, HP, Temporary HP and healing      [planned]
│  ├─ E3. Initiative, encounter lifecycle and turn-budget spending      [planned]
│  └─ E4. Inventory create/move/transfer/equip/stack/activity flows     [planned]
├─ F. Browser quality and live behavior                               [blocked by C–E]
│  ├─ F1. Keyboard/screen-reader/mobile/high-contrast acceptance        [planned]
│  ├─ F2. Revision-aware bounded refresh/invalidation                   [planned]
│  └─ F3. Loading/empty/unknown/denied/stale/replay/error states         [planned]
└─ G. Page bundle, activation and combined acceptance                  [blocked by A–F]
   ├─ G1. Versioned D&D page bundle and application association         [planned]
   ├─ G2. Disposable browser/action/catalog/full-suite verification     [planned]
   └─ G3. Backed-up private live activation and smoke test              [planned]
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
| 5 | **Implemented; acceptance pending:** D&D stateless actions ([Slice 5 implementation](DND2024-WEB-UI-SLICE-5-IMPLEMENTATION.md), [receipt](DND2024-WEB-UI-SLICE-5-RECEIPT.md)) | 2–4 | `gpt-5.6-terra` | high | 3–5 | Sol high reviews any apparent rule/result mismatch | Dice/check/save results match direct accepted mechanics for equal state/input/seed; no-change failures proven. |
| 6 | D&D character/inventory mutation controls | 2–4 | `gpt-5.6-sol` | high | 5–8 | Sol high transaction/replay review | HP/healing/Temporary HP and inventory/equipment flows commit exactly once; stale/replay/rollback cases pass. |
| 7 | D&D encounter/combat controls | 2–6 | `gpt-5.6-sol` | xhigh | 8–13 | Independent Sol xhigh composition and concurrency review | Fresh-host two-participant encounter works through UI with Initiative, attack/damage and turn lifecycle receipts. |
| 8 | Responsive, accessible and revision-aware behavior | 2–7 | `gpt-5.6-terra` | high | 4–7 | Sol high reviews stale-state and visibility boundaries | Keyboard, screen-reader semantics, mobile layouts, refresh and every failure state pass browser tests. |
| 9 | Versioned bundle, private activation and combined acceptance | 1–8 | `gpt-5.6-sol` | high | 4–6 | User acceptance after Sol evidence review | Backup, disposable validation, D&D/full tests, browser smoke, exact live revision readback and acceptance receipt. |

Estimated total: **49–78 EP**, with **19–44 EP remaining after accepted Orders 0–4**. Estimates do
not authorize combining leaves.

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

Orders 1–4 are accepted. [Slice 5](DND2024-WEB-UI-SLICE-5-RECEIPT.md) is implemented and awaits
the required feature-acceptance confirmation. State writes, live activation, and unsupported
gameplay owners remain excluded.

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
