# D&D 2024 Exploration Current View Slice 1 receipt — authoritative location scene

Status: **implementation complete; feature acceptance pending 2026-08-30**

Implementation: `DND2024-EXPLORATION-CURRENT-VIEW-SLICE-1-IMPLEMENTATION.md`
Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 11

## Delivered boundary

- The connected adapter reads the ambient actor's exact direct containment only after the actor and
  audience-filtered location directory are established. The edge must name that actor, use slot
  `presence`, and point to an admitted location.
- The ready hub no longer promotes the first projected location to Current View. A missing,
  malformed, wrong-actor, wrong-slot, or nonprojected edge produces an empty current-location ID and
  a friendly unavailable scene.
- The Current View tab now renders a responsive Exploration composition with the authorized
  location description, kind/status, observations, co-present people, known exits, optional
  location-scope image and alt text, and already-filtered DM context.
- Observations, people, exits, image, and DM context each fail independently. The tab performs no
  model request, game-state write, travel inference, or atlas-selection promotion.

## Verification evidence

- The complete canonical D&D 2024 web suite passed: **109 passed, 0 failed**.
- Focused coverage includes exact presence admission, wrong actor, wrong slot, unauthorized
  location, direct endpoint reading, no first-location fallback, and admitted current-location
  projection.
- The canonical React server bundle built successfully with Vite 8.0.13 after transforming
  **1,622 modules**.
- The currently active local page returned HTTP 200 with no browser warnings or errors. It remains
  the prior versioned page-bundle revision; this source build was not uploaded or activated.
- Focused whitespace validation passed; only pre-existing line-ending notices were reported.

## Deliberate exclusions

- Combat, Conversation, encounter selection, current-conversation/current-scene persistence, and
  deterministic multi-mode resolution remain Leaf 12.
- No permanent ID, schema, migration, catalog record, C# route, MCP surface, D&D rule, state write,
  model call, inferred route, or fixture fallback was added.
- No live SQLite page record was exported, uploaded, or activated. Publication remains an explicit
  synchronization boundary after review.

## Acceptance gate

The bounded source implementation is complete. Feature acceptance and live page-bundle publication
remain separate confirmations.
