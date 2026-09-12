---
id: procedure.system.application-candidate.inspect
category: system
name: Application candidate inspect
governs: System capability system.application-candidate.inspect
status: active
createdBy: "system"
changeNote: "Documents the registered generic capability."
---

## Description
Inspect uses a selected current application and a standing Read grant. Select an exact candidate id and revision, or the exact source operation identity supported by the capability schema. It returns retained evidence only when that retained revision remains available; missing context, grants, ambiguous lookup, or retained bytes produce the owner's failed or unavailable result and never select a substitute current candidate.

## Instructions
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.
