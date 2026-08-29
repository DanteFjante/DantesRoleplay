# D&D code-adoption Slice 3A receipt — dependency-aware operation view

Status: **accepted 2026-08-25**
Implementation: [Slice 3A implementation](../../DND-CODE-ADOPTION-SLICE-3A-IMPLEMENTATION.md)
Parent: [Slice 3 design](../../DND-CODE-ADOPTION-SLICE-3-DESIGN.md)

## Delivered boundary

Added a disposable, manifest-driven proof that a two-projection operation view can materialize from
one exact application component and declare a reverse-impact path. The fixture maps the six score
fields independently into the first view, then gives the leaf view only that dependency output.
It neither calculates modifiers nor performs a check, roll, DC comparison, JavaScript execution,
effect, transaction, catalog registration, or runtime activation.

The generic harness includes malformed-manifest, binding/scope, stale-reference, invalid-mapping,
unknown-dependency, cycle, invalid-output, determinism, isolation, and no-change assertions. An
unrelated component is deliberately added to the same subject and never appears in the result.

## Artifact fingerprints

| Artifact | SHA-256 |
| --- | --- |
| `adoption/probes/ability-check/operation-view.probe.json` | `70FEB8FD147289FD97DE2927DC95AE99F8F0909AF8B8678547C8C53A9941CC32` |
| `adoption/probes/ability-check/operation-view.probe.schema.json` | `76FAABAA8E39F27D6B6C39A0A85C65DB34F4B42B0AC9FCA0C585600EFA2A9565` |
| `src/system/application-execution/tests/ApplicationAdoptionProbeTests.cs` | `242A73AAE98D5FCB0A0206D2035F79B47F0238B4AF9751698E7A00C5093D9A27` |

## Evidence

- Focused test: `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter
  FullyQualifiedName~ApplicationAdoptionProbeTests --no-restore` — passed: 1.
- Manifest schema: AJV Draft 2020-12 validation — passed.
- Adoption contract checker — passed: 6 JSON files parsed, 2 schemas compiled, 2 positive examples
  accepted, 9 negative examples rejected.
- D&D ruleset JSON parsing and local Markdown-link checks — passed.
- `roleplay validate catalog` — valid: 144 records; 21 pre-existing near-duplicate warnings; no live
  data touched.
- Full solution suite: Slice 3A compiles and its focused test passes, but the suite is not green due
  to unrelated web-interface work: `WebInterfaceTests.Application_conversation_surface_is_exact_and_component_has_no_control_authority`
  sees an unlisted `POST /api/applications/{applicationId}/observations` endpoint.

## Graph and no-change result

The expected leaf output is exactly
`{"scores":{"str":12,"dex":16,"con":14,"int":10,"wis":13,"cha":8}}`.
From the exact `dex` source field, reverse impact reports the first projection at depth 1 and the
leaf projection at depth 2. Repeated materialization produces byte-identical output and source
evidence. ECS component content/revision and projection-definition count are unchanged after
materialization.

## Deliberate exclusions and next leaf

No D&D rule logic exists in C#, and no permanent ID, production registration, public surface,
catalog record, migration, effect, event, or JavaScript wrapper was added. Slice 3B is next: it may
introduce the effect-free catalog-JavaScript raw ability-check calculation behind this accepted view.
