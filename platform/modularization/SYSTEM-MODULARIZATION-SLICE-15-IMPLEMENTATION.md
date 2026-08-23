# System modularization Slice 15 implementation — catalog-tools physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate the developer CLI and catalog operations under catalog-tools while retaining the
existing `roleplay` executable project/assembly.  
Exclusions: Command semantics, project/public CLI name, catalog content, feedback owner semantics,
database/migrations, MCP, game code, and local AI.  
Allowed files/areas: DantesRoleplay.Tools C# source, tools project compile link, manifest/evidence.  
Stop point: Tools build and catalog validation command succeeds from component-owned source.

## Confirmed decisions

The existing Tools project remains the executable shell. Feedback is a consumer command in this
CLI; its domain/service authority remains the feedback component.

## D&D 5e 2024 alignment

Not applicable; developer tools are generic.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 14 receipt](SYSTEM-MODULARIZATION-SLICE-14-RECEIPT.md) verifies catalog runtime placement.
- Solution build and `roleplay validate catalog` exercise the CLI composition.

## Runtime artifacts

None; executable name, assembly, commands, and arguments remain unchanged.

## Authoritative state and closed input

Existing CLI arguments and catalog/database paths remain unchanged.

## Behavior, result, and typed effects

Physical placement only. No command changes or catalog/live database writes beyond the explicit
existing command invoked during validation.

## Failure, replay, and rollback contract

Build/validation reject missing source or command drift. Catalog validation uses its existing fresh
disposable database boundary.

## Implementation sequence

Add tooling compile convention; move all Tools production C#; update manifest; build and validate;
receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Tools project builds and fresh catalog validation succeeds. |
| Boundary | Domain/services stay in their system components. |
| Compatibility | Same executable, assembly, commands, and arguments. |

## Verification commands

- `dotnet build DantesRoleplay.slnx --no-restore`
- `./roleplay.cmd validate catalog`
- focused `GuardTests`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 15 receipt](SYSTEM-MODULARIZATION-SLICE-15-RECEIPT.md). Stop before another move.
