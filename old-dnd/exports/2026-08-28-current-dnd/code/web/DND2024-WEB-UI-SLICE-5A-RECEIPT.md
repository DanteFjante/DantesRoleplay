# D&D 2024 web UI Slice 5A receipt — registered campaign selection and scoped reads

Status: **accepted 2026-08-27**
Completed: **2026-08-27**
Implementation: [Slice 5A](DND2024-WEB-UI-SLICE-5A-IMPLEMENTATION.md)
Parent: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-compatible**

## Delivered boundary

- Added a private, generic, read-only outbound-relationship page scoped to one registered application, state space, source entity, and qualified relationship kind. Its cursor is bound to that exact scope and it exposes no relationship body or mutation operation.
- Replaced the D&D workspace's stale-descriptor gate for campaign discovery with registered campaign-root discovery. The top campaign selector loads the selected campaign's stored premise, goals, and tone/boundaries.
- The actor selector follows only campaign → active participation → actor links. It admits a current D&D character or the retained legacy character record, never a fact, unrelated entity, source path, directory, identifier-prefix guess, or arbitrary state-space entity.
- The legacy Brackenford campaign is readable without pretending it is current: action controls remain disabled until an explicit reviewed migration updates its binding and character records.

## Verification

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js` — passed.
- Focused `WebInterfaceTests` — **89 passed, 0 failed**.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with **0 warnings, 0 errors**.
- Live read-back at `http://127.0.0.1:6217` — page returned 200; Brackenford resolved exactly to participation `campaign.thalorien.brackenford.participation.actor.thalorien.brackenford.orban`, then actor `actor.thalorien.brackenford.orban`.
- Visual verification — the top selector displayed **The Waystone at Brackenford**, the campaign dossier rendered its stored data, the actor selector displayed **Orban**, legacy context was shown, and unsafe current-rule actions remained unavailable.
- `git diff --check` passed; only pre-existing line-ending notices were emitted.

## Deliberate exclusions

No campaign-directory model, state-space migration, D&D rule change, action compatibility bypass, catalog change, or write path was added. Campaign folders remain a separate persistence-policy decision.

## Acceptance

The user's 2026-08-27 instruction to continue with the next web-UI slice accepts this delivered
campaign-selection/read-compatibility boundary. It does not accept or authorize a campaign-state
migration, and it does not relabel this compatibility work as Order 6 mutation functionality.
