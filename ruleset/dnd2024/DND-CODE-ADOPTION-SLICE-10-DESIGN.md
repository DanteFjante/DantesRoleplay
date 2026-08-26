# D&D code-adoption Slice 10 design — static SRD content cohorts

Status: **active**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), breadth lane  
Dependency source: [D&D code-adoption dependency plan](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 10  
Ruleset alignment: `dnd2024-owned`

## Parent outcome

Import static SRD 5.2.1 content as reviewable application-owned records. Each cohort must have one
schema family, one exact official locator policy, one deterministic transformation, and independent
acceptance. Static records contain data only; complex eligibility, timing, effects, and outcomes
remain Slice 11 work.

The user's instruction to implement Slice 10 confirms reuse of permanent IDs already present in the
quarantined `old-dnd` catalog. It does not authorize invented aliases, schema-meaning changes,
automatic live-state migration, archive deletion, or importing donor campaign state.

The 2026-08-26 extension decision now confirms new IDs and schemas when core content remains an
SRD-faithful `dnd2024-core` source and every alteration or non-SRD addition is isolated in an
explicit optional source selected before campaign creation. It does not authorize blending optional
meaning into core records or silently changing an existing non-empty campaign's source profile.

## Dependency tree

```text
Parent 10 — static SRD content breadth
├── 10A currency definitions [implemented; acceptance pending]
│   ├── existing dnd2024.item-definition schema [accepted prerequisite]
│   ├── archived five-record source cohort [hash locked]
│   ├── official SRD Coins / Coin Values verification [verified]
│   ├── deterministic transform and change indication
│   └── activated-source, schema, collision, and mechanic-consumption tests
├── 10B mundane equipment [active]
│   ├── 10B1A schema-faithful adventuring gear [implemented; acceptance pending]
│   ├── 10B1B rope/quiver representation gaps [deferred; requires a schema decision]
│   ├── 10B2 armor and shields [active]
│   │   ├── 10B2A light armor [implemented; acceptance pending]
│   │   └── 10B2B–D medium, heavy, and Shield [implemented; acceptance pending]
│   ├── 10B3A reduced weapon profiles [implemented; acceptance pending]
│   ├── 10B3B archived weapon item links [implemented; acceptance pending]
│   └── missing weapon links, ammunition, and tools [gated by new IDs and tool representation]
├── 10C spells [pending]
│   └── requires a separately confirmed static spell-definition schema
├── 10D monsters [pending]
│   └── requires a separately confirmed static monster-definition schema
├── 10E magic items [pending]
│   └── static identity may import here; complex behavior remains Parent 11
└── 10F Fighter levels 1–2 progression identities [implemented; acceptance pending]
    ├── existing character content-definition and class-progression schemas [accepted prerequisites]
    ├── archived Fighter class plus five feature identities [hash locked]
    ├── official SRD Fighter traits and feature table verification [verified]
    └── deterministic transform, closed references, and progression-reader tests
```

The exact remaining gates are recorded in
[`adoption/evidence/DND-CODE-ADOPTION-SLICE-10-REMAINING-STATIC-CONTENT-GATES.md`](adoption/evidence/DND-CODE-ADOPTION-SLICE-10-REMAINING-STATIC-CONTENT-GATES.md).

## Content authority and runtime boundary

The authored records live below `catalog/applications/dnd2024/content/entities/`. Application source
preview and activation retain their exact hashes as immutable winners. Each record uses the existing
catalog entity envelope so its component data can be validated with the same component schema and
later materialized through a separately approved application-content installation boundary.

The current generic kernel deliberately creates empty state spaces and has no partial static-content
installer. Parent 10 does not silently weaken that contract or use the full-legacy-world adoption
operation, which would copy unrelated legacy state. A cohort is production-source-ready when it is
activated and schema-valid; state-space materialization remains an explicit downstream boundary.

## Cohort gates

Each leaf must prove exact source hashes, official SRD values and locators, required attribution,
indicated changes, deterministic output, unique IDs/paths, schema validity, application source
preview/activation, and compatibility with the mechanics that consume the record shape. No cohort
may batch another content family merely because the records share a donor package.

## Parent stop point

Parent 10 remains active until every selected static family has its own accepted leaf or an explicit
defer decision. Parent 10 acceptance additionally requires a separately confirmed materialization
policy if the records are to be installed into non-empty campaign state automatically.
