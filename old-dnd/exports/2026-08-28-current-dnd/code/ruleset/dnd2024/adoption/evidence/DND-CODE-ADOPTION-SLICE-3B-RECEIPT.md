# D&D code-adoption Slice 3B receipt — effect-free raw ability-check wrapper

Status: **accepted 2026-08-25**
Implementation: [Slice 3B implementation](../../DND-CODE-ADOPTION-SLICE-3B-IMPLEMENTATION.md)
Parent: [Slice 3 design](../../DND-CODE-ADOPTION-SLICE-3-DESIGN.md)

## Delivered boundary

Added one development-only first-party-recovery JavaScript wrapper, a closed result schema, a
manifest-driven seeded-vector probe, and provenance. It consumes only a serialized operation-view
component, validates that view and the exact `{ ability, dc }` input before its sole random draw,
derives the ability modifier, and returns an explained data object with no proposals.

The narrowed recovery deliberately excludes all archived skill, proficiency, level, condition,
circumstance, Advantage/Disadvantage, second-roll, donor-state, narration, and effect behavior.
Natural 1 and 20 follow ordinary total-versus-DC comparison.

## Artifact fingerprints

| Artifact | SHA-256 |
| --- | --- |
| `adoption/probes/ability-check/ability-check.wrapper.js` | `2FFB48E5AE8E8F93D1CAB4BB3E584D2E426B235AF9CDE1BEBFAA85142B985277` |
| `adoption/probes/ability-check/ability-check.result.schema.json` | `DD11831F12172C2B207943D3E3CA86F8AF6486F86A5CBC2D54D3E94BCAEDAEDE` |
| `adoption/probes/ability-check/ability-check.wrapper.probe.json` | `CF113D8FB60ACFF7D3F43F2D8C982DB48E40F8C742FC36A058983D6AE634243C` |
| `adoption/probes/ability-check/ability-check.wrapper.provenance.json` | `E355DC9E01A5D0838CAAD3AB9CDB1A197A8FD21E0724FC06B3FC9A5D160EEEC3` |
| `src/system/application-execution/tests/ApplicationAdoptionProbeTests.cs` | `F01C4DBBC9ABC24809CED7F4C11B889592A8E81E6462C4DE04D5006732C9DFA7` |

## Evidence

- Focused test: `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter
  FullyQualifiedName~ApplicationAdoptionProbeTests --no-restore` — passed: 2.
- AJV Draft 2020-12 validation passed for the wrapper manifest and provenance ledger row; all D&D
  adoption JSON parsed; local Markdown links passed.
- The focused probe validates its closed result schema, repeats each vector for byte-identical data,
  asserts one log only after the random draw, and asserts invalid input/view paths have no log,
  effects, events, or notifications.
- Catalog validation is blocked by unrelated pending EF model changes in `DantesRoleplayDbContext`.
- The wider test suite is not green due to unrelated trigger-scheduling catalog coverage: the new
  `trigger_fire_work` table and its columns are not classified by `CatalogCoverageTests`.

## Vector evidence

Seed 7 gives roll 1: Dexterity 16 plus modifier 3 totals 4 and succeeds against DC 4. Score 1 with
the same roll and DC 0 totals -4 and fails. Seed 36 gives roll 20: Dexterity 30 totals 30 and fails
against DC 31, proving natural 20 is not an automatic ability-check success.

## Deliberate exclusions and next leaf

No production catalog record, permanent ID, public operation, database state, effect, event,
notification, transaction, or generic C# rule logic was added. Slice 3C is next for neutral
vectors, archive/donor comparison, parity classification, and the final isolation proof.

## Slice 3C correction addendum

Slice 3C found that the result schema capped `dc` at JavaScript's maximum safe integer while the
wrapper accepted larger integers. The wrapper now rejects values above `9007199254740991`, and its
manifest includes that negative case. Current hashes are recorded by the Slice 3C receipt; the table
above remains the exact Slice 3B acceptance snapshot.
