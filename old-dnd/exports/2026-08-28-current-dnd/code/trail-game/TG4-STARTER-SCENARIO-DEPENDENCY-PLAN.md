# Trail Game TG4 dependency tree — Northstar Passage starter scenario

Status: **planning complete; awaiting permanent-content confirmation**
Ruleset alignment: **ruleset-neutral**
Source: **not applicable; original Trail Survival content**

## Outcome and non-goals

Provide one original, deliberately compact scenario pack that activates the accepted TG3 loop from
setup through victory or defeat and supplies human-readable presentation data for later TG6 views.
TG4 does not add a public scenario-discovery/selection API, browser UI, automatic fresh-state
seeding, new mechanic, migration, startup registration, or live database mutation.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Scenario rule shape | `trail-survival.scenario` component schema | verified | closed identity, route, economy, policy, event, and outcome data contract |
| Simulation | seven Trail catalog mechanics | verified | [TG3 hardening receipt](TG3-SLICE-5-RECEIPT.md) |
| Scenario entity state | application-scoped ECS | verified | exact component version/schema and state-space isolation tests |
| Human-readable scenario content | no existing owner | missing | TG4 proposes one immutable `trail-survival.scenario-presentation` component |
| Authored fixture/provenance | application source catalog JSON/Markdown | ready | source registration already scans `catalog/applications/trail-survival/**/*` |
| Browser discovery/control | TG5/TG6 | planned | deliberately not a TG4 prerequisite |

## Dependency tree

```text
TG4 Northstar Passage starter scenario                                  [awaiting confirmation]
├─ TG4.1 authored fixture, presentation, and provenance                 [awaiting confirmation]
│  ├─ permanent scenario/rules/route/content IDs                        [awaiting confirmation]
│  ├─ existing mechanical scenario component                           [ready]
│  ├─ new presentation component and schema                            [awaiting confirmation]
│  └─ original-content provenance ledger                               [ready]
├─ TG4.2 deterministic play/balance coverage                            [planned; depends TG4.1]
│  ├─ schema and cross-reference validation                             [ready]
│  ├─ known-seed complete victory and defeat paths                      [ready]
│  └─ event-family, market, rest, forage, replay, and no-change matrix  [ready]
└─ TG4.3 full catalog and compatibility acceptance                      [planned; depends TG4.1/2]
```

## Conflicts and decisions

- The existing scenario component is intentionally mechanical and cannot carry labels or prose.
  TG4 proposes a second immutable presentation component rather than adding non-rule fields to the
  accepted TG3 schema or putting presentation into C#.
- The accepted market schema supports several market landmarks sharing one offer schedule, not
  location-specific prices. TG4 uses four service stops with that shared schedule and does not
  change the schema.
- Setup role IDs are presentation suggestions because TG3 accepts bounded caller-authored party
  members. They do not become rule authority.
- The fixture is authored catalog data and test-materialized into disposable application state.
  Automatic scenario installation/discovery remains a TG5 boundary.
- `scenarioContentHash` is the uppercase SHA-256 of canonical minified mechanical scenario JSON
  with the `scenarioContentHash` property omitted. Tests recompute it and pin it unchanged.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | TG4.1 fixture and presentation | TG3 | Exact confirmed IDs, closed schemas, valid cross-references, and provenance are authored. |
| 2 | TG4.2 balance/coverage | TG4.1 | Known seeds exercise every mechanic/event family and reach victory plus representative defeats deterministically. |
| 3 | TG4.3 acceptance | TG4.1/2 | Fresh catalog validation, focused/full tests, and isolated build evidence pass. |

## Lowest ready leaf

TG4.1 is structurally ready but blocked on the permanent IDs, content-hash convention, and new
presentation-component meaning recorded in
[TG4 starter-scenario confirmation](TG4-STARTER-SCENARIO-CONFIRMATION.md). Once confirmed, its
[implementation document](TG4-SLICE-1-IMPLEMENTATION.md) becomes active without changing scope.

## Confirmation gates

- Confirm the permanent component, scenario, route, landmark, leg, resource, policy, role, event,
  choice, conveyance, and outcome IDs listed in the confirmation record.
- Confirm the content-hash convention and that presentation is a separate immutable component.
- TG4 completion acceptance requires a second confirmation after full evidence is available.

## Planning receipt

- Runtime artifacts created: none.
- External implementation/code/assets: none; all scenario text and data will be original.
- C# and generic kernel changes: excluded.
- Public surface, migration, startup, and live data: excluded.
