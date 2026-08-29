# Generic information dependency tree — grounded AI without game scope

Status: **lowest leaf ready**
Ruleset alignment: **ruleset-neutral**
Source: Not applicable; this is generic kernel/retrieval infrastructure.

## Outcome and non-goals

The host can store user-defined information in a generic namespace and produce a bounded, cited
answer without a campaign, world, ruleset, or game component. It can expose declared action
contracts to host-enabled executors. Campaign knowledge remains a separate compatibility adapter.
This tree does not add web/file ingestion, autonomous model-authored writes, external
authentication, arbitrary tool execution, or a campaign migration.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Transactions, audit, MCP transport | Generic C# kernel | verified | `ARCHITECTURE.md` |
| Local structured completion | Retrieval owner | verified | `DantesRoleplay/Retrieval/LocalStructuredModels.cs` |
| Existing campaign knowledge answer | World/campaign adapter | verified | `AuthorizedKnowledgeCandidateResolver.cs` |
| Generic information persistence | New DataAccess owner | ready | User confirmation, 2026-08-22 |
| Generic scope policy | New host seam | ready | User confirmation, 2026-08-22 |

## Dependency tree

```text
generic grounded answer
├─ generic source and record persistence [ready]
├─ fixed development information-scope policy [ready]
├─ bounded candidate retrieval [ready]
├─ citation-validated local answer [ready]
├─ reviewed query/commit kinds [ready]
├─ hierarchical namespace selectors [ready]
├─ declared action contracts and executor seam [ready]
├─ existing generic action-runner adapter [ready]
└─ campaign/world adapter [planned, excluded]
```

## Lowest ready leaf

One local-host information source may contain bounded text records plus opaque JSON metadata. A
fixed configured generic scope policy authorizes reads. `information-answer` receives a scope and
question, obtains only that scope's candidates, and validates citations locally. `information-source`
and `information-record` create or revise the data. No game-specific input or state is read.

## Confirmation gates

Confirmed by the user on 2026-08-22: new neutral tables, generic source/record and answer surface,
fixed local development policy, campaign compatibility deferred. The user additionally confirmed
hierarchical namespaces and declared action contracts. The permanent IDs and public kinds for this
leaf are `information-source`, `information-record`, `information-answer`,
`information-action-contract`, `information-actions`, and `information-action`.

## Planning receipt

- Runtime artifacts created: none.
