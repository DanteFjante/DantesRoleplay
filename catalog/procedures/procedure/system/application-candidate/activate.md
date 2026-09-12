---
id: procedure.system.application-candidate.activate
category: system
name: Application candidate activate
governs: System capability system.application-candidate.activate
status: active
createdBy: "system"
changeNote: "Documents the registered generic capability."
---

## Description
Activate requires an exact candidate revision and fingerprint, its committed validation operation, the current selected application generation, an active standing Activate grant, and a stable command identity. The private operator surface also requires trusted confirmation. It fails closed on stale validation, changed grants, publication failure, or unavailable dependencies and never selects a newer candidate revision.

## Instructions
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.
