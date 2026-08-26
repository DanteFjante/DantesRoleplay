# Trail Game TG0 confirmation — product, identity, and first-release boundary

Status: **confirmed**
Confirmed: **2026-08-25**
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree: [Trail Game dependency plan](TRAIL-GAME-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**

## Confirmation authority

The user instructed: “Work in the way you think is best to finish TG0 and continue TG1.” This
confirms the previously recommended TG0 defaults and delegates the bounded identity/placement
choices needed to begin TG1. It does not pre-accept TG1 implementation or later feature acceptance.

## Confirmed decisions

| Concern | Confirmed decision |
| --- | --- |
| Permanent application ID | `trail-survival` |
| Application display name | `Trail Survival` |
| Initial milestone | First playable, followed by MVP and customizable v1 |
| Product type | Original, deterministic, single-player trail-survival browser application |
| Starter setting | Original fictional journey; no historical-accuracy claim |
| First-release customization | Data-only scenario packs; multiple packs may coexist and each run pins one version |
| Advanced customization | Trusted sandboxed JavaScript is deferred to a later separately confirmed plan |
| AI boundary | AI is optional assistance/narration and never required simulation authority |
| Distribution boundary | Local/private web application; public hosting, accounts, cloud saves, stores, and multiplayer are excluded |
| Authored source placement | `catalog/applications/trail-survival/` inside the single repository catalog tree |
| Initial source identity | `trail-survival-core`, trusted, rooted at the existing configured workspace boundary |
| Base applications | None; `trail-survival` is isolated from `dnd2024` |
| External reuse | Reference implementations may be studied; direct code/assets require exact compatible-license provenance and notices |
| Branding/content | No Oregon Trail franchise name, logos, assets, prose, audio, maps, balance tables, or presentation are copied |

`Trail Survival` is an internal/application display name, not a claim of trademark clearance for a
future commercial release. Commercial naming review remains a release concern and does not change
the stable opaque application ID.

## TG1 authorization boundary

TG1 may now create and verify only the minimal independent application package using:

- application ID `trail-survival`;
- display name `Trail Survival`;
- source ID `trail-survival-core`;
- source path/glob `catalog/applications/trail-survival/**/*`;
- one descriptive procedure ID `procedure.trail-survival.about` in category
  `trail-survival.application`; and
- disposable test-only registration, activation, and state-space records.

TG1 is not authorized to add simulation schemas, mechanics, state fixtures, migrations, startup
auto-registration, live-database records, UI routes, public protocol kinds, or `dnd2024` changes.

## Exit evidence

TG0 is complete because the application identity, source placement, audience/distribution,
milestone, fictional setting, customization tier, AI boundary, and external-material policy are
closed. Runtime meaning begins only in separately active TG1/TG2 implementation documents.

