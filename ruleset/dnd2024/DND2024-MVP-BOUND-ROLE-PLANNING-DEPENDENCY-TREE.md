# Bound-role planning dependency tree

Status: lowest leaf ready
Ruleset alignment: ruleset-neutral
Source: not applicable; this is generic interaction authorization, not a D&D rule.

## Outcome and non-goals

When the host supplies a role reference in a closed interaction intent, a proposed step cannot
bind that same role to a different entity. This protects the player character supplied by the
host-authorized audience context during both AI-planned and submitted plans.

This does not select roles for an ordinary caller, create a new MCP operation, alter D&D mechanics,
or make the player character a persistent identity model.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Host-selected actor | `system.audience-context` | verified | `DND2024-MVP-AUDIENCE-CONTEXT-RECEIPT.md` |
| Closed intent and role-hint parsing | `InteractionIntent` | verified | `InteractionAuthorityContracts.cs` |
| AI and submitted-proposal verification | `InteractionProposalVerifier` | verified | `InteractionProposalVerifier.cs` |
| Character-creation routing | context adapter + catalog mechanic | verified | `DND2024-MVP-CHARACTER-CREATION-CONTEXT-RECEIPT.md` |

## Dependency tree

```text
Bound player character cannot be impersonated in a plan [ready]
├─ host emits an exact actor role hint [verified]
└─ proposal verification compares supplied role bindings to the exact host hint [ready]
   ├─ AI planner path uses the verifier [verified]
   └─ caller-submitted path uses the verifier [verified]
```

## Confirmed decision

The user's 2026-08-29 instruction to continue authorizes the protected planning follow-up: a
host-supplied role hint is a constraint for that same declared role, not a caller override. A plan
may omit a role where the selected contract permits it; it may never substitute a different entity
for a supplied role.

## Lowest ready leaf

Add a generic verifier check before missing-role resolution. A static binding that conflicts with
an intent role hint is unsafe. A result binding may not replace a hinted role. The check changes no
state and applies identically to AI-planned and submitted proposals.

## Planning receipt

- Runtime artifacts created: none.
