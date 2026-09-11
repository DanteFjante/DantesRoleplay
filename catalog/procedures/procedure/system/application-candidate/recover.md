---
id: procedure.system.application-candidate.recover
category: system
name: Application candidate recover
governs: System capability system.application-candidate.recover
status: active
createdBy: "system"
changeNote: "Documents the registered generic capability."
---

## Description
Recover copies one exact retained application activation revision into a fresh inert candidate. Supply the activation revision and the expected current active fingerprint so a changed application cannot be silently overwritten. Recovery does not activate the copied content, grant authority, retry an uncertain command under a new identity, or infer success from missing retained evidence.

## Instructions
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.
