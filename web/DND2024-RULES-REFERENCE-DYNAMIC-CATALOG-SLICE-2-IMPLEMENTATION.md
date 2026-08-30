# DND2024 Rules reference Slice 2 implementation — dynamic registered catalog

Status: **accepted 2026-08-30**

Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5

Dependency tree/leaf: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 13 / G

Ruleset alignment: **dnd2024-compatible presentation of dnd2024-owned records**

Source ID and locator: each included record must retain a citation to
`dnd2024.source.srd-5.2.1`; the exact locator is read from that record and is never synthesized.

Outcome: the Rules tab discovers the active D&D 2024 entity catalog at runtime, reflects newly
registered and revised records without a website allowlist change, and shows each selected record's
bounded summary and source citation. A minimal source-built page asset supplies the same projection
when the runtime activation safely refuses a drifted catalog snapshot.

Exclusions: rule execution or calculation; catalog mechanics, procedures, queries, or raw JSON;
catalog or game-state writes; new D&D IDs, schemas, migrations, or source claims; arbitrary
cross-application browsing.

Allowed files/areas: the generic read-only application catalog route and remote-path gate; its
focused tests; the D&D 2024 Rules reader, view, types, styles, and tests; this plan, owner status,
receipt, production bundle, and existing page publication boundary.

Stop point: the live private `dnd2024-play` page shows the current active entity index, refreshes it
without a page rebuild, lazily shows selected-record detail and attribution, and all stated tests
pass. No catalog or game-state record is changed.

## Confirmed decisions

- The user's request confirms the new public, authenticated, read-only catalog-browse route needed
  by the application page.
- “All registered rules” means all active `entity` records under the D&D 2024 catalog's `entities`
  branch. Internal mechanics, procedures, and queries remain outside the player reference.
- Browse summaries populate the complete index. Exact record content is fetched only for a selected
  entry, avoiding thousands of eager record requests.
- The production build generates a bounded minimal projection from the canonical authored entity
  files. It contains no raw entity JSON or executable mechanics and is used only when the active
  runtime catalog is unavailable.
- A selected detail is accepted only when its active version and current SRD source citation remain
  valid. Records without a presentation summary receive a neutral UI description; the browser does
  not invent rule behavior.

## D&D 5e 2024 alignment

| Concern | Source meaning used | Existing owner | Consequence |
| --- | --- | --- | --- |
| Record identity | Registered active D&D 2024 catalog entity | Activated public application catalog | Index identity, name, path, and fingerprint come from catalog summaries. |
| Readable detail | Optional authored `dnd2024.core.presentation.summary` | Exact entity JSON | Use it when present; otherwise show a neutral reference description. |
| Attribution | Exact current SRD citation and locator | `dnd2024.core.source` | Selected detail fails closed unless a current source citation is present. |
| Revision | Active positive `dnd2024.core.version.revision` | Exact entity JSON | Revised records are accepted; revision 1 is not hard-coded. |

## External implementation reference

Foundry dnd5e review is not applicable: this slice defines no rule, formula, data transition, or
sheet mechanic. It is a read-only catalog presentation and copies no Foundry code, data, or assets.

## Prerequisite evidence

- Slice 1 receipt proves the searchable reference interaction, strict exact-record reader, and
  source attribution seam.
- The activated application catalog already owns bounded browse summaries and exact record reads.
- The D&D 2024 public catalog materializer classifies authored entity JSON as `entity` and keeps
  mechanics, procedures, and queries as distinct kinds.

## Runtime artifacts

- Add `GET /api/applications/{applicationId}/catalog/browse`, protected by the existing web security
  filter and read rate limit. It delegates to the existing generic catalog explorer.
- Replace the Rules ID allowlist with bounded, paginated traversal of the `entities` branch and a
  generated source-catalog fallback asset.
- Add lazy exact-record detail loading, dynamic category filters, refresh, loading, and failure UI.
- No new permanent catalog ID, schema, migration, database record, effect, or transaction is added.

## Authoritative state and closed input

The application ID is fixed to `dnd2024`, collection is fixed to `dnd2024`, and the browse root is
fixed to `entities`. Callers may supply only the same-origin server URL and a catalog-provided index
entry to the detail loader. Category labels derive from bounded catalog paths. The browser never
supplies rule content, source locators, status, revisions, or outcomes.

## Behavior, result, and typed effects

Traverse bounded catalog pages and nodes with repeat detection. Keep active `entity` summaries in
the `entities` branch, sort category/title/ID deterministically, and derive category labels from the
first path segment. On selection, inspect the exact record, verify identity/status/version/source,
and replace the neutral index description with its authored presentation summary when available.
If runtime activation rejects catalog drift, fetch the build-generated minimal projection whose
entries already passed those identity/status/version/source checks. Opening Rules refreshes the
index; a visible refresh action repeats the read. All operations are read-only and produce no typed
effect or transaction.

## Failure, replay, and rollback contract

Invalid origin/application, malformed pages, repeated cursors/nodes, exceeded bounds, unavailable
routes, or invalid record detail fail closed. Existing loaded entries remain visible when a refresh
fails. An invalid selected detail shows an unavailable detail state without exposing raw JSON.
Repeated reads are idempotent. Rollback is ordinary source/page revision rollback; no database or
catalog rollback is required.

## Implementation sequence

1. Add and test the generic secured browse route and remote allow boundary.
2. Replace the fixed reader with bounded dynamic index and exact detail readers.
3. Update types, validation, filtering, view refresh/detail behavior, and focused tests.
4. Run focused/full tests and the production build.
5. Restart the private host, publish the bundle, smoke-test the live Rules tab, and record evidence.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Positive | Dynamically discovered actions, spells, classes, creatures, equipment, and other entity families appear. |
| New record | A new browse summary appears without adding a website ID. |
| Revised record | Positive revision and changed authored summary/source are accepted on refresh/detail load. |
| Negative kind | Mechanic, procedure, and query summaries never enter the index. |
| Fidelity | Wrong identity, inactive version, or missing current SRD citation cannot populate detail. |
| Boundary | Page/cursor/node/record bounds and repeat detection fail closed. |
| Refresh | Opening Rules and explicit refresh re-read the catalog while preserving prior data on failure. |
| Surface | Public route is GET-only, secured, read-rate-limited, and accepted by the remote web path gate. |
| Compatibility | Player and DM see the same rules index; no game state changes. |

## Verification commands

- `node --test test/rules-reference.test.js test/web-state.test.js`
- `npm test`
- `npm run build:server`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter WebInterfaceTests`
- `dotnet test DantesRoleplay.slnx`
- Desktop and mobile live-browser smoke of `/ui/dnd2024-play` after publication.

Catalog validation is not required because this slice changes no catalog file. Protocol walk is not
required because no MCP surface or dependency registration changes.

## Completion receipt and exit gate

Receipt: `web/evidence/dnd2024/DND2024-RULES-REFERENCE-DYNAMIC-CATALOG-SLICE-2-RECEIPT.md`.
Rich executable rule text, rule editing, and catalog-authoring UI remain future work.
