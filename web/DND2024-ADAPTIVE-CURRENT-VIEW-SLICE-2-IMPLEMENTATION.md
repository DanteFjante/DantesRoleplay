# D&D 2024 Current View Slice 2 implementation — authoritative current scene

Status: **implementation complete; feature acceptance pending 2026-08-30**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 12
Ruleset alignment: `dnd2024-compatible`
Source ID and locator: not applicable; this slice selects existing state and does not calculate a D&D rule
Outcome: resolve one campaign-owned current scene and render Exploration, Conversation, or Combat
without browser inference.
Exclusions: scene-authoring UI, encounter/conversation mutation, dialogue generation, tactical maps,
combat actions, player-known route projection, travel execution, and live activation.
Allowed files/areas: the current dependency tree/roadmap, generic campaign catalog component and
procedure, `src/system/web-interface/dnd2024`, focused tests, and the completion receipt.
Stop point: source implementation and verification; do not upload, activate, or write live game state.

## Confirmed decisions

- The user confirmed an authoritative current-situation owner on 2026-08-30 and required runtime
  namespaces to follow existing D&D 2024 application qualification.
- The authored generic ID is `game.core.campaign.current-scene`; application registration qualifies
  it as `dnd2024.game.core.campaign.current-scene`, matching existing
  `dnd2024.game.core.campaign.*` runtime components without a doubled prefix.
- The closed record contains an exact location and optional conversation and encounter references.
  Encounter wins over Conversation, which wins over Exploration. Keeping both focus references lets
  an interrupted conversation resume after its encounter reference is removed.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Encounter state | Existing encounter participation, Initiative, rounds, turns, and budgets remain authoritative. | `dnd2024.encounter.*`, `dnd2024.combat.*` | The website validates and projects existing records; it calculates no combat outcome. |
| Conversation | Durable conversation identity and participants are existing world-interaction state. | `game.core.world.interaction` and its participant links | The current-scene pointer selects the active conversation; it does not become a dialogue engine. |

## External implementation reference

No Foundry dnd5e rule implementation is relevant because this slice performs no D&D calculation or
action resolution. Existing repository encounter lifecycle contracts remain the only rule owner.

## Prerequisite evidence

- Exploration Current View source implementation and focused tests are recorded in
  `web/DND2024-EXPLORATION-CURRENT-VIEW-SLICE-1-RECEIPT.md`.
- Exact encounter participation, Initiative, active-round, active-turn, and turn-budget contracts
  already exist under `catalog/applications/dnd2024`.
- `game.core.world.interaction` already owns accepted conversation identity and exact participant
  relationships, but is not itself the current-scene selector.

## Runtime artifacts

- New authored component: `game.core.campaign.current-scene`.
- Runtime qualified component: `dnd2024.game.core.campaign.current-scene`.
- New procedure: `procedure.campaign.current-scene`.
- Revised private website envelopes and Current View components. No new HTTP route or game write.

## Authoritative state and closed input

The component is attached only to the campaign root and contains exactly:

- required `location: { entityId }`;
- optional `conversation: { entityId }`; and
- optional `encounter: { entityId }`.

The server verifies referenced entities and their accepted components/relationships. The browser may
never supply, select, or infer these references. Actor seats additionally require their exact
`presence` containment to agree with the scene location. A DM seat may use the campaign scene
location without inventing an actor.

## Behavior, result, and typed effects

- An encounter reference selects Combat.
- Otherwise a conversation reference selects Conversation.
- Otherwise the exact location selects Exploration.
- Without the component, an actor seat may retain the accepted containment-derived Exploration
  behavior; a DM seat receives an explicit unavailable Current View.
- Invalid, missing, unauthorized, or mismatched focus state makes only Current View unavailable.
- This read slice emits no effects, events, notifications, model calls, or state writes.

## Failure, replay, and rollback contract

Malformed closed data, unauthorized locations, mismatched actor presence, missing focus entities,
wrong component kinds, ambiguous active round/turn links, and corrupt referenced state fail closed.
Repeated reads are deterministic and read-only. No partial focus data is transported to the browser.

## Implementation sequence

1. Add and validate the catalog component/procedure.
2. Add bounded server normalization and audience-safe scene projection.
3. Extend the connected/ready envelopes and adaptive Current View presentation.
4. Add focused positive, priority, malformed, mismatch, and secret-exclusion tests.
5. Run catalog validation, website tests/build, then record a receipt.

## Acceptance matrix

- Exploration: exact location and no focus references.
- Conversation: exact accepted conversation and authorized participant subset.
- Combat: exact encounter with accepted Initiative/round/turn data when present.
- Priority: encounter plus conversation resolves Combat.
- Missing: no DM scene owner renders unavailable; actor containment fallback remains Exploration.
- Invalid/mismatch: no inferred fallback from corrupt authoritative focus.
- Audience: Player bytes exclude unapproved participant identities and conversation summary.
- Compatibility: older state spaces without the component retain accepted actor Exploration behavior.

## Verification commands

- `roleplay validate catalog`
- `npm test` and `npm run build:server` in `src/system/web-interface/dnd2024`
- focused .NET catalog/component tests affected by the new generic schema count
- full suite only at separately confirmed feature acceptance

## Completion receipt and exit gate

Record delivered files, commands, results, and deliberate exclusions in
`web/DND2024-ADAPTIVE-CURRENT-VIEW-SLICE-2-RECEIPT.md`. Stop before live catalog import, page upload,
activation, scene authoring, travel choices, or final feature acceptance.
