---
id: procedure.snapshot.package
category: snapshot
name: Stage and verify immutable snapshot packages
governs: internal immutable snapshot package staging and byte-free verification
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines the generic storage boundary for one producer-owned immutable snapshot package. It has no
public MCP operation: a named domain coordinator may call the typed in-process store only within
its own approved root transaction.

## Instructions
1. Ask the registered scope producer for one closed `SnapshotCaptureProposal`; never accept bytes,
   a digest, a storage id, a path, URI, credentials, domain list, raw effect, or restore option
   from an MCP caller.
2. Stage the proposal only after the owning root has opened its transaction. The package store
   joins that transaction and never starts, commits, rolls back, or audits a root itself.
3. The store generates the `snapshot.*` identity, copies the content, calculates its byte count and
   SHA-256 digest, records private provenance, and returns only a byte-free reference.
4. The coordinator may report success only after its complete outer root commits. If it rolls back,
   it must not expose a package reference or substitute current state for the package.
5. Verify a package only from an exact expected reference. Recompute its byte count and digest and
   fail closed for missing, unavailable, mismatched, or corrupt content.

## Constraints
- A package has exactly one producer-owned scope contract/version, producer/version, boundary
  fingerprint, canonical content digest, byte count, capture time, and availability state.
- Generic storage never interprets, selects, copies by guessing, restores, forks, lists, downloads,
  or returns package content. A later admitted in-process consumer requires its own contract.
- Package content and provenance are immutable. SP1 supports `available` only; retention,
  retirement, deletion, correction, encryption, replication, external storage, and migration are
  separate features.
- A failed/cancelled/replayed root must not leave a visible package. No chat, event/audit history,
  recap, current world state, or database/file copy is a substitute for a verified package.

