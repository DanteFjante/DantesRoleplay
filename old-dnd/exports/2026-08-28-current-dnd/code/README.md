# DantesRoleplay

DantesRoleplay is a C# host for a data-driven roleplaying runtime. The host stores state, runs
sandboxed catalog JavaScript, applies typed effects atomically, and exposes the system through the
three MCP verbs `orient`, `query`, and `commit`.

## Read this first

An implementation agent should read only:

1. [AGENTS.md](AGENTS.md) for repository rules;
2. [the implementation-document reading protocol](docs/IMPLEMENTATION_DOCUMENT_READING.md);
3. the relevant catalog procedure and existing owner records;
4. one active subsystem roadmap or feature plan; and
5. the receipt for a prerequisite only when its proof matters.

Do not preload every roadmap, feature plan, handoff, or receipt. Catalog Markdown files are runtime
contracts, not general project documentation, and should be retrieved by owner or ID.

## Document map

| Need | Owner |
| --- | --- |
| Architecture and code/data boundary | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Current repository summary | [STATUS.md](STATUS.md) |
| Reproducible blockers only | [KNOWN_ISSUES.md](KNOWN_ISSUES.md) |
| First playable-story order | [STORY_FIRST_ROADMAP.md](STORY_FIRST_ROADMAP.md) |
| D&D feature index | [ruleset/dnd2024/ROADMAP.md](ruleset/dnd2024/ROADMAP.md) |
| Knowledge/retrieval roadmap | [Knowledge and facts](knowledge/KNOWLEDGE_AND_FACTS_PLAN.md) |
| Platform enabling features | [Platform roadmap](platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md) |
| Cross-owner status remediation | [Remediation plan](docs/status-remediation/STATUS_REMEDIATION_IMPLEMENTATION_PLAN.md) |
| Author a dependency tree | [DEPENDENCY_TREE_AUTHORING.md](docs/DEPENDENCY_TREE_AUTHORING.md) |
| Author a feature implementation document | [FEATURE_IMPLEMENTATION_AUTHORING.md](docs/FEATURE_IMPLEMENTATION_AUTHORING.md) |
| Read implementation documents | [IMPLEMENTATION_DOCUMENT_READING.md](docs/IMPLEMENTATION_DOCUMENT_READING.md) |
| World, campaign, quest, session, character, items | The matching top-level `*_PLAN.md` roadmap |
| One feature's next slice | Its feature directory's active dependency plan |
| Completed proof | `*RECEIPT.md`, `*CONFIRMATION.md`, `*VALIDATION.md`, or `*RATIFICATION.md` |
| Connecting an MCP client | [CONNECTING.md](CONNECTING.md) |
| Catalog synchronization | [CATALOG_HANDOVER.md](CATALOG_HANDOVER.md) |

## Essential commands

```powershell
dotnet build
dotnet test
.\roleplay validate catalog
```

Run catalog validation after catalog edits. Import into the persistent database only at an explicit
integration or release boundary.
