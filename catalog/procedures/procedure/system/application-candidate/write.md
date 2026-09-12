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

## Matches
stage runtime authored definitions
write an application candidate
revise an inert candidate

## Instructions
### Input and result
The closed input contains `applicationId`, nullable `candidateId`, `expectedCandidateRevision`, nullable
`expectedActiveFingerprint`, `origin: "runtime"`, null `synchronizationEvidenceReference`, a nonempty
`newImplementationReason`, and 1–16 replacement `documents`. Each document carries
`logicalIdentity`, `sourceId`, `relativePath`, `mediaType`, and bounded `text`.

```json
{"applicationId":"example","candidateId":null,"expectedCandidateRevision":0,"expectedActiveFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","origin":"runtime","synchronizationEvidenceReference":null,"newImplementationReason":"No current definition satisfies the reviewed contract.","documents":[{"logicalIdentity":"procedure.example.sample","sourceId":"runtime","relativePath":"procedures/procedure/example/sample.md","mediaType":"text/markdown","text":"---\nid: procedure.example.sample\ncategory: example\nname: Sample procedure\ngoverns: example sample operation\nstatus: active\ncreatedBy: runtime\nchangeNote: Initial reviewed candidate.\n---\n\n## Description\nOperate one reviewed sample.\n\n## Matches\noperate the sample\n\n## Instructions\nRead the exact current capability schema, then invoke it with its required input.\n\n## Constraints\nDo not infer authority from this procedure.\n"}]}
```

Success is `committed` with an `operationId` and `requestFingerprint`. Retain both and inspect the
exact returned candidate before review or validation. Equal idempotent replay returns the same
receipt; changed input under the same key conflicts.

### Operating steps
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.

## Constraints
- Candidate documents are inert. Writing does not publish, execute, import files, or mutate state.
- A new candidate uses null `candidateId` with expected revision 0. A revision uses the exact retained
  candidate ID and current revision. On stale active or candidate evidence, inspect and rebuild the
  request instead of retrying blind.
