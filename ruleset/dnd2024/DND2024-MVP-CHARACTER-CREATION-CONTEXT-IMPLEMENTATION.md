# D&D 2024 MVP character-creation context implementation

Status: accepted 2026-08-29
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `DND2024-MVP-CHARACTER-CREATION-CONTEXT-DEPENDENCY-TREE.md` / lowest ready leaf
Ruleset alignment: dnd2024-compatible
Source ID and locator: not applicable; no D&D rule meaning changes.

## Outcome and boundary

Extend the existing `system.audience-context` query with `status: "character-creation-required"`
only when the configured actor entity does not exist. The existing character-creation mechanic then
receives that server-reserved `characterId`; after it commits, the DM must reread a bound context.

Allowed areas: the generic knowledge participation result, the existing MCP context adapter and
tests, and D&D chat procedure/playbook. Excluded: a new identity model, rules changes, web work,
an alternate action path, and making invalid existing participation creatable.

## Authoritative state and behavior

The host configuration selects the application, campaign, and reserved actor ID. The application
binding proves the campaign scope. The participation verifier distinguishes only three states:
active, known-missing actor, and every other denial. The context adapter maps these to bound,
creation-required, and denied respectively.

The Player supplies character choices but never an identity. Creation uses the context-provided
`characterCreation.characterId` exactly; normal play requires the subsequent bound actor role hint.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Active participation | bound context with actor role hint |
| Missing actor | creation-required context with reserved character ID and no actor role hint |
| Existing inactive/invalid actor | generic denial |
| Policy/binding failure | generic denial before later reads |
| Chat procedure | reloads context after creation and never trusts a user-supplied actor ID |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~SystemAudienceContextToolsTests|FullyQualifiedName~KnowledgeCoreTests|FullyQualifiedName~GuardTests"
./roleplay validate catalog
```

## Completion receipt and exit gate

Recorded in `evidence/DND2024-MVP-CHARACTER-CREATION-CONTEXT-RECEIPT.md`. The next capability is
ordinary player intent resolution using the already-bound actor; it remains on the existing action
pipeline.
