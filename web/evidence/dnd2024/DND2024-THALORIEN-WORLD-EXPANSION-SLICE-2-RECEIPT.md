# DND2024 Thalorien world expansion Slice 2 receipt — existing-city maps

Status: **accepted 2026-08-30**

## Delivered boundary

Crownmere and Merrowgate now have explicit city-map scopes beneath their exact Aldros and Merceros
Region maps. Each Region link uses the existing canonical settlement marker. The new SVGs are
unlabelled, audience-shared illustrations with no feature points, so their visual street patterns
cannot become districts, landmarks, routes, distances, exits, or tactical geometry.

The scopes appear only when the projected audience can already see the exact settlement and its
expected containing Region. No canonical state, location, knowledge, schema, route, or D&D mechanic
changed.

## Evidence

- Focused connected-envelope/live-map tests: 13 passed, 0 failed.
- Full prototype suite: 162 passed, 0 failed.
- Production prototype build: passed.
- Visual inspection at 800×800 confirmed that both SVGs render cleanly, remain unlabelled, and
  contain no secret annotation.
- Live `GET http://localhost:5173/api/hub?perspective=dm` returned
  `map.live.city.crownmere` and `map.live.city.merrowgate`, both assets, and both exact Region child
  links in a ready envelope.
- Actor-shaped test input that knows only Brackenford contained neither city name nor city asset.
  A Crownmere record under a wrong parent produced no city scope.

## Deliberate exclusions

The maps do not establish named city internals. Elaris and Kharad Veyr remain lore-only settlements;
no new city, secret, or clue was created. Those canonical additions remain blocked until the
application-ECS world-authoring transaction exists.
