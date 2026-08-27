# D&D code-adoption Slice 13C implementation — clean build and retained recovery

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), archive-maintenance lane  
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 13C  
Ruleset alignment: `ruleset-neutral` acceptance  
Source ID and locator: not applicable; no rule behavior changes  
Outcome: prove the active application builds and validates independently of archive runtime code,
while accepted transformation/provenance/recovery checks still succeed against retained bytes.  
Exclusions: archive mutation, runtime/catalog changes, pin updates, migration, live data, and public
surface changes.  
Allowed files/areas: this document; read-only archive and application verification; one 13C
machine report and receipt.  
Stop point: release build, disposable catalog, adoption/recovery tooling, shared tests, and Local AI
tests pass against the same retained worktree.

## Prerequisites and behavior

- 13A proves zero runtime consumers and records the archive aggregate hash.
- 13B retains all archive paths and approves no deletion.
- Slice 12's fail-fast acceptance runner owns the complete build/catalog/adoption/test sequence and
  is reused rather than duplicated.

The run must begin and end with the same 13A archive aggregate hash. Catalog validation uses a
disposable database. Transformation tools must re-read and hash their archived sources. The
complete shared suite includes the optional-extension provenance test that reads the archived Rope
record. No protocol walk is required by repository policy because no MCP registration changed, but
the reused acceptance runner may execute it as additional evidence.

## Failure contract and acceptance

Missing or changed archive bytes, failed transformation hashes, accidental project/catalog
inclusion, build/test failure, live-data access, or aggregate-hash drift blocks acceptance. The
successful report is recorded as `adoption/evidence/slice13-retained-acceptance-2026-08-27.json`,
with the accepted receipt at `adoption/evidence/DND-CODE-ADOPTION-SLICE-13C-RECEIPT.md`.
