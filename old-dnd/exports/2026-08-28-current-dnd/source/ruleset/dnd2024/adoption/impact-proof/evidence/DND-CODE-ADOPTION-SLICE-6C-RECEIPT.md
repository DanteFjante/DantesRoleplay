# D&D code-adoption Slice 6C receipt — candidate impact, replay, and rollback proof

Status: **accepted 2026-08-25**
Implementation: [Slice 6C implementation](../../../DND-CODE-ADOPTION-SLICE-6C-IMPLEMENTATION.md)
Parent: [D&D code-adoption dependency tree](../../../DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)

## Delivered boundary

Added a ruleset-neutral, read-only proof that pins the accepted Slice 6A mapping, Slice 6B
allowlist, and candidate result by exact relative path and SHA-256. It rejects stale references,
mismatched candidate keys, mismatched mapping links, and mismatched reverse-impact roots before
executing the existing generic kernel tests for declared impact, replay, and rollback.

The proof produces only a deterministic report. It neither registers/activates the candidate,
materializes game state, invokes a candidate as a live mechanic, applies an effect, or writes catalog,
database, operation, or MCP-host state.

## Artifact fingerprints

| Artifact | SHA-256 |
| --- | --- |
| `adoption/impact-proof/contracts/impact-replay-rollback-proof.schema.json` | `C3A3C92353CF1E7994325167602D9A145C2BCA0BB6937648870E30E19871CAA0` |
| `adoption/impact-proof/fixtures/impact-replay-rollback-proof.valid.json` | `E1F6187818AC2CE9910A54C1DCFC87073DEEF95BCA67FA54EFE4115AAE51BE3B` |
| `adoption/impact-proof/tools/Test-ImpactReplayRollbackProof.ps1` | `5265EA2E5111206510739531C3BD89A83304940A264F564315C78F17B2A0DC97` |

## Evidence

- Focused proof: `pwsh -NoProfile -File ruleset/dnd2024/adoption/impact-proof/tools/Test-ImpactReplayRollbackProof.ps1` — passed. It compiled/validated the closed proof schema; rejected unexpected properties, stale mapping hash, and mismatched impact roots; then ran focused `ApplicationAdoptionProbeTests` and `ApplicationEcsEffectApplierTests` with no build.
- The report records the exact mapping (`E734…4767`), allowlist (`8FBA…4EF9`), and result (`9667…671`) hashes; both declared impact roots; one completed focused proof run; and `writes: none`.
- Full current no-build suite evidence remains 926/928 passed. The two failures are confined to untracked `TrailSurvivalRunDomainTests`, outside this slice and its owners.
- Catalog validation was attempted after implementation but is currently blocked before catalog checks by a pending EF model migration from the concurrent worktree. Slice 6C changes no catalog records or schema/migration owner.

## Chain and generic-host result

The allowlist's candidate key, mapping path, and mapping hash exactly match the pinned Slice 6A
mapping. Its mapping's two canonical roots exactly match the proof manifest. The generic focused
tests then prove the kernel's declared component-field reverse impact, at-most-once execution identity
replay without a second mutation, and rollback of an earlier effect when a later effect is stale.
No second state authority or candidate transaction mechanism is introduced.

## Deliberate exclusions and next leaf

Slice 6 is now complete as reusable candidate tooling. It adds no D&D-owned behavior, source
locator, Foundry review, permanent runtime ID, catalog registration, activation, public operation,
migration, or live campaign data. Slice 7A is next: recover the first bounded archived gameplay
cohort only after its D&D-owned source, owner, and confirmation boundaries are authored.
