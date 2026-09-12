---
id: procedure.system.application-candidate.write
category: system
name: Application candidate write
governs: System capability system.application-candidate.write
status: active
createdBy: "system"
changeNote: "Documents the registered generic capability."
---

## Description
Write creates or revises a candidate through the selected application authoring boundary. Use a stable idempotency key and supply the exact expected candidate revision and active application fingerprint required by the contract. The private operator surface requires its trusted confirmation; the application surface uses the caller's current standing Author grant. A committed write is not validation, activation, or authority to execute content.

## Instructions
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.
