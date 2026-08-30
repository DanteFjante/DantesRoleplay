# DND2024 Rules reference pilot Slice 1 receipt

Status: **accepted 2026-08-30**

Implementation document: `web/DND2024-RULES-REFERENCE-PILOT-SLICE-1-IMPLEMENTATION.md`

Dependency tree/leaf: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 13 pilot

Ruleset alignment: **dnd2024-compatible presentation of dnd2024-owned records**

## Delivered boundary

- Replaced the Rules placeholder with a responsive searchable reference workspace containing an
  All/Action/Reaction filter, selectable index, readable detail, exact source attribution, result
  count, and friendly unavailable/no-result states.
- Added a fixed allowlist for the fourteen accepted shared activity records. The browser reads each
  exact record through the existing authorized catalog-record endpoint and preserves allowlist
  order.
- Added a strict fidelity gate that accepts only the expected activity ID and archetype, active
  revision 1, Action or Reaction economy, bounded presentation summary, and an exact
  `source.dnd2024.srd-5.2.1` citation. Invalid or unavailable records are omitted without invented
  fallback content.
- Passed the same closed rules array through the live connected envelope for DM and Player. Rules
  remain read-only and perspective-independent.
- Tightened ready-envelope validation and retained source-cited fixture coverage for the existing
  audience projection tests.

No catalog record, game mechanic, API route, database state, permanent ID, schema, migration,
effect, transaction, LLM request, or deployment changed.

## Evidence

| Check | Result |
| --- | --- |
| `npm test` | Passed: 111 tests, 0 failures. |
| `npm run build:server` | Passed: production React bundle emitted successfully. |
| Focused Rules reader/filter tests | Passed: 4 tests, including deterministic allowlist and fidelity-failing exclusion. |
| Focused connected/validation/audience tests | Passed: Rules projection, closed-envelope validation, and Player-preview equality. |
| Desktop browser smoke | Passed: 14 references, index/detail layout, visible SRD source, and Reaction filter resolving Opportunity Attack. |
| Mobile browser smoke | Passed at 390 × 844: controls stack, detail remains readable, and the filtered result is usable. |
| Slice-scoped whitespace review | Passed. |

The repository TypeScript diagnostic command remains outside this slice's acceptance evidence
because the pre-existing web tree has unrelated diagnostics in map-list typing, server-host JS
inference, and two existing explicit `.ts` imports. This slice added no remaining diagnostic of its
own; the production build and complete test suite pass.

## Deliberate exclusions

- Challenge Rating, Telepathy, rest policy, spells, conditions, character options, and broader
  curated rule families;
- full executable rule text or browser-owned outcomes/calculations;
- catalog search/browse exposure beyond the fourteen exact record reads;
- new reference-entry schema, API route, catalog publication policy, or live-state owner;
- runtime activation/deployment and final Feature 5 acceptance.

## Rollback

Ordinary source control can remove the Rules reader, connected projection, React view, styles, and
tests. The change wrote no persistent state and requires no catalog or database rollback.
