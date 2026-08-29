# Trail Game TG4 confirmation — Northstar Passage content contract

Status: **awaiting confirmation**
Requested: **2026-08-25**
Ruleset alignment: **ruleset-neutral**
Dependency plan: [TG4 starter-scenario dependency tree](TG4-STARTER-SCENARIO-DEPENDENCY-PLAN.md)

## Product and ownership decision

Confirm **Northstar Passage** as the first original Trail Survival scenario. Mechanical data remains
in `trail-survival.scenario`; human-readable titles, summaries, prompts, and labels live in a new
immutable `trail-survival.scenario-presentation` component. No franchise text, historical claim,
external code, or external asset is reused.

## Permanent top-level IDs

- Component: `trail-survival.scenario-presentation`
- Scenario entity and scenario ID: `scenario.northstar-passage`
- Rules profile: `rules.trail-survival.v1`
- Route: `route.northstar-passage`
- Conveyance kind: `conveyance.wayfarer-wagon`

## Route and policy IDs

- Landmarks: `landmark.cinderbrook`, `landmark.pinewatch`, `landmark.glasswater-ford`,
  `landmark.redstone-ridge`, `landmark.moonfen`, `landmark.windcarved-gate`,
  `landmark.starfall-basin`, `landmark.aurora-haven`.
- Legs: `leg.cinderbrook-pinewatch`, `leg.pinewatch-glasswater`,
  `leg.pinewatch-redstone`, `leg.glasswater-moonfen`, `leg.redstone-moonfen`,
  `leg.moonfen-windcarved`, `leg.windcarved-starfall`, `leg.starfall-aurora`.
- Paces: `pace.careful`, `pace.steady`, `pace.urgent`.
- Rations: `ration.full`, `ration.standard`, `ration.lean`.
- Suggested roles: `role.navigator`, `role.medic`, `role.mechanic`,
  `role.quartermaster`.

## Economy IDs

`resource.scrip`, `resource.provisions`, `resource.spare-parts`, `resource.medicine`,
`resource.canvas`, `resource.fuel`, and `resource.keepsake`.

## Event and choice IDs

Every choice is namespaced under its event slug.

| Family | Event | Choices |
| --- | --- | --- |
| Weather | `event.silver-rain` | `choice.silver-rain.shelter`, `choice.silver-rain.press-on` |
| Weather | `event.ember-wind` | `choice.ember-wind.cover-load`, `choice.ember-wind.press-on` |
| Weather | `event.whiteout` | `choice.whiteout.wait`, `choice.whiteout.follow-stars` |
| Weather | `event.sudden-thaw` | `choice.sudden-thaw.detour`, `choice.sudden-thaw.cross` |
| Health | `event.trail-fever` | `choice.trail-fever.use-medicine`, `choice.trail-fever.endure` |
| Health | `event.twisted-ankle` | `choice.twisted-ankle.treat`, `choice.twisted-ankle.push-on` |
| Health | `event.restful-grove` | `choice.restful-grove.rest`, `choice.restful-grove.continue` |
| Health | `event.clean-spring` | `choice.clean-spring.refill`, `choice.clean-spring.continue` |
| Breakdown | `event.cracked-axle` | `choice.cracked-axle.repair`, `choice.cracked-axle.improvise` |
| Breakdown | `event.torn-canvas` | `choice.torn-canvas.patch`, `choice.torn-canvas.ride-open` |
| Breakdown | `event.loose-wheel` | `choice.loose-wheel.repair`, `choice.loose-wheel.slow-down` |
| Breakdown | `event.worn-harness` | `choice.worn-harness.replace`, `choice.worn-harness.press-on` |
| Supply | `event.spoiled-provisions` | `choice.spoiled-provisions.discard`, `choice.spoiled-provisions.sort` |
| Supply | `event.hidden-cache` | `choice.hidden-cache.take`, `choice.hidden-cache.leave` |
| Supply | `event.dropped-crate` | `choice.dropped-crate.retrieve`, `choice.dropped-crate.continue` |
| Supply | `event.medicine-shortage` | `choice.medicine-shortage.trade`, `choice.medicine-shortage.endure` |
| Trade | `event.ridge-peddler` | `choice.ridge-peddler.buy-parts`, `choice.ridge-peddler.pass` |
| Trade | `event.repair-camp` | `choice.repair-camp.pay`, `choice.repair-camp.continue` |
| Trade | `event.provision-exchange` | `choice.provision-exchange.trade`, `choice.provision-exchange.decline` |
| Trade | `event.fuel-seller` | `choice.fuel-seller.buy`, `choice.fuel-seller.decline` |
| Terrain | `event.flooded-ford` | `choice.flooded-ford.wait`, `choice.flooded-ford.cross` |
| Terrain | `event.moonfen-lights` | `choice.moonfen-lights.ignore`, `choice.moonfen-lights.follow` |
| Terrain | `event.falling-rocks` | `choice.falling-rocks.shelter`, `choice.falling-rocks.dash` |
| Terrain | `event.forked-trail` | `choice.forked-trail.scout`, `choice.forked-trail.guess` |

## Outcome IDs

- `outcome.aurora-haven-reached`
- `outcome.party-lost`
- `outcome.wagon-lost`
- `outcome.exposure`
- `outcome.swept-away`
- `outcome.lost-in-moonfen`

## Hash and content boundary

Confirm uppercase SHA-256 over canonical minified mechanical scenario JSON with
`scenarioContentHash` omitted. Version 1 pins that hash. Presentation content has its own component
schema/version hash and does not affect deterministic mechanics.

Confirmation authorizes the exact IDs and meanings above for TG4 only. It does not authorize a
public API, browser surface, migration, startup registration, automatic scenario seeding, or live
database write.
