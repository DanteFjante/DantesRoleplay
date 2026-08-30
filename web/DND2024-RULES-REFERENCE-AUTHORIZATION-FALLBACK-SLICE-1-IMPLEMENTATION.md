# DND2024 Rules reference authorization fallback Slice 1

Status: **accepted 2026-08-30**

Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5

Dependency tree/leaf: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaves 13 and 14

Ruleset alignment: **ruleset-neutral presentation fallback**. No D&D rule meaning, calculation,
eligibility, outcome, source citation, or catalog record changes.

## Outcome and boundary

When the server cannot authorize the private campaign envelope, the hosted D&D page still opens
the already accepted public, read-only Rules reference. World, Campaign, Party, and Current View
remain disabled and no campaign data is requested or synthesized. A successful audience binding
continues to open the complete private table unchanged.

The user's report that the unavailable-table gate prevents access to the Rules tab confirms this
bounded presentation repair. It does not authorize a campaign migration, application activation,
state-space rewrite, authentication change, or a broader public data surface.

Allowed files: the D&D server-host bootstrap, a rules-only shell component, backward-compatible
navigation availability presentation, focused state/tests, styles, this plan/receipt, the existing
production bundle, and page publication evidence.

Forbidden work: catalog/game-state writes, schema or ID changes, audience-policy bypasses, secret
projection, rule editing, application activation, state-space migration, or fallback campaign data.

## Behavior and acceptance

- `ready` opens the full private table exactly as before.
- denied, unavailable, character-creation-required, malformed, or failed campaign bootstrap opens
  Rules directly.
- The four campaign-owned tabs are visibly disabled in the fallback; Rules refreshes from the
  registered catalog and retains the accepted source-built fallback.
- The authorization message is shown as context, not as a sign-in wall around public rules.
- Focused state tests, the full D&D web test suite, the production server build, publication, and a
  live browser smoke prove the repair.

Stop point: the live `/ui/dnd2024-play` page shows the complete Rules library when campaign
authorization is denied, without changing live SQLite or exposing a private table view.

Completion receipt:
`web/evidence/dnd2024/DND2024-RULES-REFERENCE-AUTHORIZATION-FALLBACK-SLICE-1-RECEIPT.md`.
