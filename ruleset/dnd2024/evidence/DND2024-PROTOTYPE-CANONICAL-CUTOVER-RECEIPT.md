# D&D 2024 prototype canonical cutover receipt

Status: accepted
Date: 2026-08-29
Ruleset alignment: dnd2024-compatible
Boundary: repository D&D source replacement only

## Delivered

`catalog/applications/dnd2024` is now generated from the reviewed prototype model. It contains:

- 154 ECS component descriptors and bounded component schemas;
- 71 archetype schema artifacts; and
- 2,329 authored prototype entity records.

The active source intentionally contains no legacy mechanics or procedures. The deterministic
builder is `prototype/dnd2024/tools/build-canonical-catalog.js`; it resolves prototype external
schema references into local fragments, removes prototype-only annotations, and applies the
kernel's maximum string-length bound where an authored regular expression leaves repetition
unbounded.

The generic C# schema profile was extended without D&D IDs, vocabulary, formulas, or branching. It
now accepts bounded recursive local references, `propertyNames`, `if`/`then`/`else`, and the
restricted bounded regular-expression forms used by the transported schemas. External references
remain rejected.

## Preservation and stop point

The former active source was moved recoverably to:

`old-dnd/prototype-cutover-archive/2026-08-29-dnd2024-pre-cutover`

Two intermediate generated source revisions are also retained under the same archive root. The
earlier verified export remains at `old-dnd/exports/2026-08-28-current-dnd`.

No SQLite database, campaign, world, server process, application registration, or web deployment
was changed. No import or activation was performed. Existing inventory planning references are
explicitly non-authoritative until a separate prototype-data migration reconciles them.

## Verification

| Check | Result |
| --- | --- |
| `node --test test/canonical-catalog-cutover.test.js` | passed: builder emits all prototype source and no mechanics |
| Focused schema C# tests | passed: 35/35, including compilation of all 154 active schemas |
| `npm test` in `prototype/dnd2024` | passed: 123/123 |
| `npm run build` in `prototype/dnd2024` | passed |
| `./roleplay.cmd validate catalog` | passed: 144 records; 21 pre-existing advisory warnings; no live data touched |

## Deliberate exclusions

- no migration of current campaign or world records;
- no attempt to import, adapt, or retain archived JavaScript mechanics;
- no claim that the live running application is now playable on the prototype source; and
- no live database synchronization.

The next slice must define and confirm the campaign/world migration, including a fresh live export,
mapping contract, rollback path, and runtime activation procedure.
