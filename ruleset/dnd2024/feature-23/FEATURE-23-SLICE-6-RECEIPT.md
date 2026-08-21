# Feature 23 Slice 6 receipt — creature Size and carrying capacity

Completed: 2026-08-20

Added the closed `dnd2024.creature-size` component, one-time Size recorder, and read-only carrying
resolver. It composes the burden reader and derives SRD 5.2.1 carry and drag/lift/push values from
Strength and Size. It creates no encumbrance speed state.

`CatalogFeature23Slice6Tests` passes all six Size categories at Strength 10 (Tiny 75/150 lb,
Small/Medium 150/300 lb, then doubling through Gargantuan), plus duplicate/missing-Size refusal.
Catalog validation passes with advisory near-duplicate warnings only; no live data was touched.

Slice 7 transfer and capacity admission is next.
