# World Feature 2 — Slice 1 receipt

Status: **Verified**
Completed: 2026-08-20

## Delivered

Slice 1 adds a generic, declared relationship projection for mechanics. A role can set
`includeRelationships: true` and receive a frozen, canonically ordered list of the incoming and
outgoing relationship records that touch it. A role that does not opt in has no relationship field
in the JavaScript projection.

The extension is generic: it knows no world, location, movement, route, combat, or D&D vocabulary.
It exposes only `fromEntityId`, `toEntityId`, `kind`, and raw JSON-object `data`; it does not expose
the opposite endpoint's name, components, containment, or relationships. Relationships with a
soft-deleted endpoint are excluded.

## Evidence

| Check | Result |
| --- | --- |
| Focused resolver/sandbox suite | `dotnet test DantesRoleplay.slnx --no-restore --filter FullyQualifiedName~ProjectionResolverTests` — **27/27 passed**. |
| Catalog validation | `roleplay validate catalog` — **99 records valid**; three non-blocking near-duplicate warnings, including the revised existing projection procedure. No live data touched. |
| Full regression suite | `dotnet test DantesRoleplay.slnx --no-restore` — **382/382 passed**. |
| Whitespace validation | `git diff --check` passed; Git reported only working-copy line-ending conversion warnings. |

## Handoff

World Feature 2 Slice 2 remains separate: it adds the traveller component, fixture traveller,
travel procedure, and adjacent-movement mechanic. This slice does not allow any entity to move yet.
