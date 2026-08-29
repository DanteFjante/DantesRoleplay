---
id: procedure.mechanic.dnd2024.source-registry
category: ruleset.dnd2024.core.governance.sources
name: Govern the D&D 2024 SRD source identity
governs: source.dnd2024.srd-5.2.1 citation convention and dnd2024 application source registration
status: active
---

## Description

Adapts the archived ECS source record to the current generic application source registry and stable
catalog citation policy without creating duplicate runtime authority.

## Instructions

Register the authored D&D catalog as immutable application source `dnd2024-core`; preserve its scan
and activation fingerprints. Rules-bearing artifacts cite `source.dnd2024.srd-5.2.1` plus an exact
locator and retain the CC-BY-4.0 attribution governed by the adoption policy.

Register the existing `game` application before `dnd2024` and declare it as an ordered D&D base
application whenever D&D mechanics consume generic world-owned components such as
`game.core.world.root` or `game.core.world.clock`. Map those exact base component type versions;
never copy their schemas or values into a D&D-owned parallel component and never accept their
state as caller input.

Keep optional, compatibility, homebrew, and third-party packages outside the core source glob under
`catalog/extensions/dnd2024/<package>/`. Each package has a closed `extension-package.json`, is
registered as its own immutable application source, declares `dnd2024-core` in
`requiredSourceIds`, and remains disabled unless its exact source ID is selected before campaign
creation. The initial package source is `dnd2024-extension.legacy-equipment`, registered from
`catalog/extensions/dnd2024/legacy-equipment/**/*` with precedence 100 and the same logical
identity as its source ID.

## Constraints

The archived `dnd2024.source` campaign component is replaced and must not be revived. Application
source registration authenticates files; this procedure owns citation meaning. Neither stores rule
prose, campaign state, interpretations, or executable behavior. An extension manifest is packaging
metadata, not permission to treat its future content as SRD or to activate it without a separate
content-family review.
