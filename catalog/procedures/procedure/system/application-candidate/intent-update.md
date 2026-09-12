---
id: procedure.system.application-candidate.intent-update
category: system
name: Application candidate intent update
governs: System capability system.application-candidate.intent-update
status: active
createdBy: "system"
changeNote: "Documents the bounded intent-association authoring capability."
---

## Description
Intent update stages a new inert application candidate that changes only the explicit `Matches` phrases of one exact current procedure or mechanic. Supply the exact target revision and content fingerprint, a stable idempotency key, and the current candidate identity when revising an existing candidate. An empty phrase list disables the alternate association. The selected current standing grant must carry both Read and Author authority.

## Instructions
1. Choose a current procedure or mechanic returned by authorized discovery and preserve its exact ID, kind, revision, and content fingerprint.
2. Supply the complete desired phrase set. The operation replaces that target's alternate phrases; it does not add another implementation or publish the candidate.
3. Inspect the returned candidate, then use the existing validate and activate capabilities with their exact retained references. Only successful activation changes discovery.
4. If the target or candidate is stale, refresh both references before retrying. Reuse the same idempotency key only for the same logical update.
