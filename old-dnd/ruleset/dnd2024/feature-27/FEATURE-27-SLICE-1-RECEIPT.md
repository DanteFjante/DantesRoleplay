# Feature 27 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Added `dnd2024.class-progression` and its governing procedure for immutable class-content
  progression facts.
- Added the effect-free `mechanic.dnd2024.class-progression.read` reader.
- Extended the existing source-backed Fighter content with d10 Hit Die, fixed HP gain 6 before
  Constitution, and exact level 1/2 feature-entitlement identities.
- Added immutable source-backed feature content identities for Fighting Style, Second Wind, Weapon
  Mastery, Action Surge, and Tactical Mind.

## Explicitly not delivered

No actor class membership, total-level transition, HP or Hit-Die application, feature action,
resource, recovery, choice, proficiency, campaign authorization, or CH9 level-up transaction.
Every returned feature entitlement has `behaviorStatus: "unimplemented"`.

## Evidence

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --filter
  "FullyQualifiedName~CatalogFeature27Tests"` — passed, 3/3.
- `roleplay validate catalog` — valid disposable import, 266 records, 0 warnings. No live data
  was touched.
- Tests prove canonical level-1/2 entitlement order, unsupported level diagnostics, closed input,
  invalid progression diagnostics, source mismatch diagnostics, zero effects, and immutable
  feature-content provenance.
