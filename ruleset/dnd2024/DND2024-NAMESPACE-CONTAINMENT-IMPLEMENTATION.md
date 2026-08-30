# D&D 2024 G7N implementation — application namespace containment

Status: **implemented; feature acceptance pending unrelated D&D suite repair**
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `DND2024-COMPLETE-CAMPAIGN-DEPENDENCY-GRAPH.md`, G7N
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: not applicable; this changes identity placement, not D&D rule meaning
Outcome: every identity authored by or explicitly consumed inside the D&D 2024 application begins
with the single `dnd2024.` namespace.
Exclusions: D&D rule behavior, schema meaning, generic catalog IDs outside the application, live
database import/activation, state-space/world migration, compatibility aliases, and UI redesign.
Allowed files/areas: `catalog/applications/dnd2024`, current D&D runtime/test references, the source
identity fixture where present, namespace guard tests, the complete-campaign graph/roadmap, and the
pre-cutover export/receipt.
Stop point: the authored D&D catalog, filenames, executable references, current tests, and public
application catalog expose only `dnd2024.*` identities; catalog validation and focused tests pass;
the live database remains unchanged and its old immutable records remain available in an export.

## Confirmed decisions

- The user's 2026-08-30 request confirms the permanent identity cutover and supersedes G7's earlier
  allowance for inverted application-local keys such as `mechanic.dnd2024.*`,
  `procedure.mechanic.dnd2024.*`, and `ruleset.dnd2024.*`.
- Canonical forms are `dnd2024.mechanic.*`, `dnd2024.procedure.*`, `dnd2024.ruleset.*`,
  `dnd2024.source.*`, `dnd2024.content.*`, `dnd2024.currency.*`, and `dnd2024.item.*`.
- Generic `game.core.*` contracts remain generic outside the D&D application. Any reference inside
  the D&D application uses its exact installed identity, `dnd2024.game.core.*`.
- Old identities are retired, not retained as aliases. Historical receipts and immutable database
  operation history are evidence and are not rewritten.

## Alignment and external reference

No SRD rule or Foundry dnd5e behavior applies. The change is namespace/ownership containment only;
JavaScript calculations, inputs, outputs, and effects remain equivalent apart from identity strings.

## Prerequisite evidence

- All 2,546 current JSON records under `catalog/applications/dnd2024` already have top-level IDs
  beginning `dnd2024.`.
- All 126 mechanic/procedure Markdown records currently violate that invariant through inverted
  IDs; their application-catalog qualification can consequently repeat `dnd2024`.
- The live-database drift check reports application mechanics, procedures, components, and the D&D
  source entity that were never exported. A pre-cutover export is therefore required before edits.
- G7 already proves that `dnd2024.game.core.*` is the only live world/campaign component owner.

## Identity rewrite

| Current prefix | Canonical prefix |
| --- | --- |
| `mechanic.dnd2024.` | `dnd2024.mechanic.` |
| `procedure.mechanic.dnd2024.` | `dnd2024.procedure.mechanic.` |
| `procedure.play.dnd2024.` | `dnd2024.procedure.play.` |
| `ruleset.dnd2024.` | `dnd2024.ruleset.` |
| `source.dnd2024.` | `dnd2024.source.` |
| `content.dnd2024.` | `dnd2024.content.` |
| `currency.dnd2024.` | `dnd2024.currency.` |
| `item.dnd2024.` | `dnd2024.item.` |
| application-local `game.core.` | `dnd2024.game.core.` |

Mechanic `.md`/`.js` and procedure `.md` basenames move with their IDs. All child-mechanic,
procedure, source citation, content, schema-constant, current code, and focused-test references move
in the same slice. Historical evidence exports retain the identity that was true when captured.

## Failure and compatibility contract

Validation fails when a D&D application record ID/category or a D&D-owned reference uses an
inverted `*.dnd2024.*` form, an unqualified `game.core.*` form, a duplicated
`dnd2024.dnd2024.*` form, or a filename that does not match its mechanic/procedure ID. No alias or
fallback remaps an old ID. The cutover never imports into or rewrites the live database.

## Implementation sequence

1. Export the current live catalog/world to immutable pre-cutover evidence.
2. Rewrite the closed prefix map and move mechanic/procedure sidecars without changing behavior.
3. Update current runtime/test references and add one deterministic namespace guard.
4. Validate the catalog, run focused and full tests, record exclusions/failures, and write receipt.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| JSON/Markdown authored IDs | every current D&D application ID begins `dnd2024.` |
| categories and references | no inverted D&D prefix or unqualified application `game.core.*` remains |
| mechanic sidecars | Markdown and JavaScript share the new basename and mechanic ID |
| application catalog | qualification produces one leading `dnd2024`, never a repeated namespace |
| mechanics | child references and effects resolve through exact new/current IDs |
| compatibility | old IDs are absent from current catalog/code and receive no aliases |
| live state | unchanged; pre-cutover export preserves old immutable records |

## Verification and receipt

- deterministic namespace guard and affected D&D mechanic/application tests
- `roleplay validate catalog`
- full `dotnet test` in a repository-local isolated output path
- no protocol walk because the public verb/kind surface does not change

The implementation evidence is recorded in `DND2024-NAMESPACE-CONTAINMENT-RECEIPT.md`. Stop before
live activation/import/migration.
