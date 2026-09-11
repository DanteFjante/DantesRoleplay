---
id: procedure.system.application-candidate.validate
category: system
name: Application candidate validate
governs: System capability system.application-candidate.validate
status: active
createdBy: "system"
changeNote: "Documents the registered generic capability."
---

## Description
Validate names one exact candidate revision and zero or more definition-pinned samples. The invocation requires two operations plus one per sample and must fit the shared ceiling of 16 operations, so at most 14 samples can run. Validation records bounded evidence and may record an unavailable outcome when runtime dependencies are absent. A committed validation receipt does not by itself mean the outcome was valid, does not activate content, and does not waive current permissions.

## Instructions
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.
