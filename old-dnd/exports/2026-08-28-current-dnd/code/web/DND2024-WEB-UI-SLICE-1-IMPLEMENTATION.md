# D&D 2024 web UI Slice 1 implementation — read-only game viewport foundation

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 1
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**; this slice renders accepted state without implementing or
changing a D&D rule.
Outcome: expose exact private registered-application ECS reads and render one recognizable,
read-only D&D game viewport for the selected state space and entity.
Exclusions: action execution, writes, confirmation, mechanic descriptors, derived calculations,
catalog/content browsing, inventory containment traversal, live page activation, page association
persistence, new D&D catalog records, and every gameplay-gated future component.
Allowed files/areas: `src/system/web-interface/**`, focused web tests, this Slice 1 document/receipt,
the D&D web dependency plan, and the web roadmap.
Stop point: the authored `dnd2024-play` page and `<dnd2024-workspace>` can discover exact state
spaces/entities/components and render game-styled current/unknown state. No button can mutate game
state.

## Confirmed decisions

The user's 2026-08-27 instruction to continue confirms the private player-and-GM first-release
direction, page ID `dnd2024-play`, module route `/components/dnd2024-workspace.js`, element ID
`dnd2024-workspace`, and the application-state read routes below. The same instruction confirms a
game-table interaction language: large ability tiles, meters, shields, pips, tokens, cards, chips,
dice controls, equipment slots, and contextual plus/minus controls in later writable slices. It
rejects a generic generated-form dashboard as the normal player experience.

## D&D 5e 2024 alignment

| Concern | Accepted owner | Slice consequence |
| --- | --- | --- |
| Ability, HP, AC, Speed, Conditions, mitigation, turn budget, level, and proficiency state | current `dnd2024.*` component definitions | Parse and label current stored values only; missing means unknown/absent according to the owner. |
| Modifiers and derived sheet values | catalog JavaScript mechanics | Do not calculate them in JavaScript UI. |
| Mutations and outcomes | application action runner plus catalog mechanics | Excluded; the viewport has refresh/selection controls only. |
| Rules provenance | existing component source references and registered source profile | May display bounded provenance; never synthesize it. |

## External implementation reference

No Foundry dnd5e review is required because this slice implements no D&D rule or rule-specific
calculation. It adopts no external code, assets, data flow, or UI dependency.

## Prerequisite evidence

- The accepted application-aware workspace proves private shared navigation and exact
  application/state-space chat binding.
- `ControlStructureExplorer` already composes application registry, state-space, ECS component, and
  public catalog owners for read-only control inspection.
- Current web security provides local/Tailscale identity, private read authorization, security
  headers, and rate limits.
- The accepted D&D application currently has 27 component definitions; this slice changes none.

## Runtime artifacts

Confirmed new private GET routes:

- `/api/applications/{applicationId}/state-spaces`
- `/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities`
- `/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}`
- `/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components`
- `/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}`

Confirmed module route and element:

- `/components/dnd2024-workspace.js`
- `<dnd2024-workspace>`

The module is a reviewed browser asset copied with the web project and resolved by the generic
bounded `/components/{name}.js` asset handler. This keeps D&D vocabulary out of compiled C# while
preserving the confirmed public resource URL.

Confirmed authored page source:

- `src/system/web-interface/examples/dnd2024-play/index.html`

No database row, migration, catalog ID, public MCP kind, application registration, state-space, or
live page revision is created.

## Authoritative state and closed input

The routes accept only bounded path IDs and existing cursor/limit query values. The server resolves
application registration, exact state-space binding, entity/component existence, type version,
schema hash, value revision, and component JSON. A state space belonging to another application is
reported as unavailable through the requested application scope.

The element accepts only `application-id`, optional `state-space-id`, and optional `entity-id`.
It fixes its supported visual vocabulary to known accepted component IDs but treats their server
values as untrusted JSON and renders an explicit unavailable state on parse/shape failure. It does
not accept state JSON, revisions, schemas, effects, formulas, or authorization through attributes.

## Behavior, result, and typed effects

On connection, the element loads current state spaces for the exact application, selects the
requested or first available state space, loads current entities, selects the requested or first
available entity, loads its component summaries/details, and renders:

- actor identity and scope status;
- HP/Temporary HP, AC, level, Speed and Conditions;
- six ability-score tiles without browser-derived modifiers;
- turn-budget resource tokens;
- mitigation/proficiency summaries; and
- explicit loading, empty, unknown, denied, stale and unavailable states.

State-space/entity selectors and Refresh are real controls. They update selection and rerun bounded
reads. This slice proposes and applies no typed effect and owns no transaction.

## Failure, replay, and rollback contract

- Malformed application/state/entity/component IDs fail closed with bounded JSON errors.
- Unknown applications/state spaces/entities/components return 404 without exposing another scope.
- A state space owned by another application returns `STATE_SPACE_WRONG_APPLICATION` and no data.
- Cursors from another kind/scope/page size return the existing stale/invalid result.
- Unauthorized private requests return 403 before reads.
- Component JSON parse/shape failures remain local display errors and do not replace other valid
  panels.
- Repeated reads are side-effect free; database total-change evidence remains constant.
- No rollback case exists because the slice has no write or transaction.

## Implementation sequence

1. Add exact application-scoped explorer methods and focused cross-scope/no-change tests.
2. Add private GET route handlers and remote-path allowlisting limited to the new read shapes.
3. Add the module/element with immediate game-HUD shell and bounded hydration.
4. Add the authored `dnd2024-play` page using existing navigation and application conversation.
5. Verify compile, JavaScript syntax, focused tests, route/security compatibility, and a bounded
   local visual preview; write the receipt and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Exact reads | Correct application/state/entity/component revisions and value JSON are returned. |
| Cross-application isolation | A valid state-space ID under the wrong application fails without data. |
| No change | All reads preserve database total changes and component/entity revisions. |
| Route surface | Exactly the five confirmed GET routes and one module route are added with private security/rate limits. |
| Remote boundary | Only the exact new application-state read path shapes become eligible remotely; conversation/execute paths are not widened. |
| Game presentation | The first viewport is recognizably a D&D play surface, not a generated form or control-center table. |
| State semantics | Missing, absent, empty, corrupt and unavailable values remain visibly distinct. |
| Accessibility | Selectors/buttons are labeled; status changes use a live region; color is not the only state indicator. |
| Responsive | The HUD remains usable at narrow and wide viewport widths without horizontal page overflow. |
| Compatibility | Existing home/control/application pages and shared components remain unchanged and green. |

## Verification commands

- Extract `Dnd2024WorkspaceElement.Script` and run `node --check`.
- Run focused `WebInterfaceTests` covering explorer, routes, remote access, module, authored page,
  and no-change behavior.
- Build `DantesRoleplay.slnx --no-restore`.
- Run the broader web/application tests required by touched owners.
- Run `git diff --check` on slice files.

Catalog validation and the public MCP protocol walk are not required because this slice changes no
catalog record, application activation, MCP kind, or dependency registration.

## Completion receipt and exit gate

[The Slice 1 receipt](DND2024-WEB-UI-SLICE-1-RECEIPT.md) records the delivered boundary and passing
evidence. Order 1 stops before any action adapter, plus/minus mutation, dice/check execution,
content browser, live page upload/activation, or application-to-page association persistence.
