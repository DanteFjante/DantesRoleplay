# DND2024 Thalorien world expansion Slice 3 receipt — map fidelity and political powers

Status: **accepted 2026-08-30**

## Delivered boundary

Crownmere and Merrowgate now use detailed 1254×1254 hand-painted top-down city art with dense
European-style streets, walls, gates, plazas, building blocks, and surrounding farmland. The art
contains no labels, named internals, grid, tactical scale, or canonical geometry. The earlier SVGs
remain unused rollback evidence.

The DM Factions page now presents all eight existing political actors available without creating
new canonical records: Aldros, Evandos, Merceros, Minevros, Rhiannos, Valeros, and Waylos appear as
`Sovereign power` cards derived from their exact public kingdom Region summaries, while The Gilded
Concord appears as an `Organization` with its exact active GM-only summary, three goals, three
methods, three assets, and ready agenda. Each sovereign power links back to its canonical Region.

The live review found that local DM Player-preview had been reusing the DM-authorized knowledge
notebook, allowing the Concord's secret activity to reappear through Lore even though the Factions
directory itself was absent. Player-preview now fails closed on knowledge unless a genuine
actor-authorized projection exists. It contains no World directory, faction cards, Concord name,
agenda, assets, or secret manipulation text.

No application-ECS record, permanent ID, schema, migration, D&D mechanic, or live database state
was added or changed.

## Evidence

- Full prototype suite: 165 passed, 0 failed.
- Production prototype build: passed.
- Focused World-directory, Player-preview, faction, and map tests: passed.
- Source-resolution visual inspection confirmed both PNG maps are detailed, readable, top-down,
  unlabelled, and free of tactical or secret annotation.
- Disposable live-database review returned one exact active GM-only Gilded Concord component with
  three goals, three methods, three assets, and a ready agenda.
- The same live DM projection produced eight Factions-page actors: seven exact kingdom Regions as
  sovereign powers plus the Gilded Concord as an organization.
- Live Player-preview returned `knowledge.status: unavailable`, no World directory, zero factions,
  and no Concord name, agenda, asset, or secret-text bytes.

## Deliberate exclusions

Map streets and buildings remain illustrative and unnamed. No new country government, faction,
city, location, lore, secret, or clue was made canonical. Elaris, Kharad Veyr, new cities, and new
secret/clue chains remain blocked until the application-ECS world-authoring transaction can create
and update their records atomically.
