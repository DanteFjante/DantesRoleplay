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

## Matches
validate an application candidate
run candidate samples
check authored JavaScript before activation

## Instructions
### Input and result
The closed input is `applicationId`, exact `candidateId`, positive `revision`, 64-character uppercase
`contentFingerprint`, and `samples`. Each sample pins `definitionId`, `kind`, positive definition
revision and fingerprint, plus JSON strings `inputJson` and `expectedDataJson`. Stateful samples also
provide `stateSpaceId`, `stateRevision`, role entity IDs, and `expectedEffectsJson` as required by the
candidate grammar.

```json
{"applicationId":"example","candidateId":"0123456789abcdef0123456789abcdef","revision":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","samples":[]}
```

Success is a committed operation receipt. Inspect the exact candidate afterward to read the retained
validation outcome and evidence; the transport commit tag alone does not mean the candidate passed.

### Operating steps
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.

## Constraints
- Use the current capability schema to determine which stateful fields are required. Do not invent a
  sample, expected effect, service dependency, or schema conversion.
- Failed validation leaves the candidate inert. Fix the candidate or its exact dependencies, write a
  new candidate revision, and validate that revision; do not reuse stale evidence.
