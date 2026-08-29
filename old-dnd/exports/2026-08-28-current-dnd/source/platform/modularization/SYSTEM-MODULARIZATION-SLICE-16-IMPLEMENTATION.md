# System modularization Slice 16 implementation — Campaign adapter quarantine

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization game-code eviction](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **dnd2024-compatible**  
Source ID and locator: **not applicable; no D&D rule is implemented or changed**  
Outcome: Move all compiled Campaign contracts/workflows and their focused tests out of legacy
kernel project directories into the explicit Campaign game-adapter quarantine.  
Exclusions: Behavior/rule rewrites, catalog mechanics, APIs/namespaces/assemblies, protocol
adapters, DbContext/migrations, and local AI.  
Allowed files/areas: Campaign domain/persistence/tests, game-adapter compile conventions, literal
inventory paths, and planning evidence.  
Stop point: Campaign/guard tests and build pass with old Campaign source paths absent.

## Confirmed decisions

Quarantine makes dependency direction honest but does not make C# authoritative for rules. The
Quarantine makes dependency direction honest but does not make C# authoritative for rules. The
four existing Campaign `dnd2024` literal occurrences remain baselined for later removal.

## D&D 5e 2024 alignment

No rule meaning changes. Campaign consumers remain compatible with existing D&D state while the
generic kernel remains ruleset-neutral.

## External implementation reference

No Foundry reference is relevant to physical relocation.

## Prerequisite evidence

- [Slice 15 receipt](SYSTEM-MODULARIZATION-SLICE-15-RECEIPT.md).
- Existing Campaign feature tests cover the moved contracts/workflows.

## Runtime artifacts

None; types retain existing namespaces and assemblies through compile links.

## Authoritative state and closed input

Existing Campaign state/catalog owners and requests remain unchanged.

## Behavior, result, and typed effects

Physical placement only; creation, continuity, session, composition, participation, effects, and
transactions remain unchanged.

## Failure, replay, and rollback contract

Existing Campaign tests retain negative/no-change/rollback coverage; exact ruleset literal counts
move paths without growth.

## Implementation sequence

Add game-adapter compile conventions; move Campaign domain/persistence/tests; update literal paths;
verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Campaign feature suites pass. |
| Boundary | Sources live only under Campaign adapter quarantine. |
| Compatibility | Same assemblies/namespaces/registration/effects. |
| Literal ratchet | Same four occurrences at new paths, no growth. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~CampaignFeature|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 16 receipt](SYSTEM-MODULARIZATION-SLICE-16-RECEIPT.md). Stop before another game feature.
