# D&D 2024 character creation CC2A completion receipt

Status: **accepted**
Completed: 2026-08-27
Implementation: [CC2A species definitions and selection planning](../DND2024-CHARACTER-CREATION-CC2A-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Character Origins > Character Species > Parts of a Species /
Species Descriptions* (PDF pp. 83–86)

## Delivered boundary

- Re-adopted the immutable `dnd2024.species-profile` family and all nine versioned SRD species
  definitions under the current application catalog.
- Re-adopted `dnd2024.selected-species` as the minimal future actor-side reference, without adding
  a writer or storing a copy of species facts.
- Replaced the archived C# selection resolver design with
  `mechanic.dnd2024.species-selection.resolve`, a pure role-bound JavaScript planner. The bound
  content definition supplies identity, allowed Sizes, base Speed, and entitlements.
- Fixed-Size species accept `{}` and derive Size. Human and Tiefling require exactly one declared
  Small-or-Medium choice. The resolver emits canonical selected-species, Size, and Speed data with
  zero effects, events, or notifications.
- Every special trait and species choice family remains explicitly unresolved. The plan reports
  `blocked-unimplemented-traits` and cannot be mistaken for an atomically creatable character.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reference-reviewed for
  singleton species content, configured Size choices, staged actor updates, and its implicit
  Medium fallback. No Foundry code/data/assets were adopted, and the fallback was deliberately not
  adopted because missing Size is unknown in this repository.

## Acceptance evidence

| Check | Result |
| --- | --- |
| Focused species cases | 7 passed: nine-profile inventory, Human Small/Medium, fixed Size, Goliath Speed, invalid/derived input, source drift, canonical definition binding, seed independence, zero-effect replay, and explicit trait blockers |
| Full D&D regression class | 112 passed |
| Catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full solution | 1,136 shared tests passed and 21 Local AI tests passed; one unrelated architecture guard failed on separately in-progress web-interface D&D literals in `WebInterfaceEndpoints.cs` and the untracked `Dnd2024WorkspaceElement.cs` |
| Whitespace | `git diff --check` reported no whitespace errors; only existing line-ending notices |

The full-suite exception is outside CC2A's allowed files and behavior. The species slice adds no C#
runtime literal, web endpoint, browser component, public surface, dependency registration, or MCP
kind. Its focused and complete D&D regression boundaries are green.

## Deliberate exclusions

CC2A does not grant Darkvision, resistance, Inspiration, skills, feats, spells, rest behavior, or
any other species trait. It does not persist selected species, Size, or Speed, create an actor,
attach campaign participation, or change the public interface. CC2's next coherent subslice must
give one species' full entitlement set named owners before the atomic creation root can consume
this plan.
