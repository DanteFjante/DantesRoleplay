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

## Matches
activate a validated candidate
publish runtime authored definitions
make a candidate current

## Instructions
### Input and result
The closed input is `applicationId`, exact `candidateId`, positive `revision`, uppercase
`contentFingerprint`, and `validationOperationId`.

```json
{"applicationId":"example","candidateId":"0123456789abcdef0123456789abcdef","revision":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","validationOperationId":"validation-operation-1"}
```

Success is `committed` with `operationId` and `requestFingerprint`. Treat the receipt as publication
evidence for that exact generation. Refresh application and feature discovery before invoking new work;
already running work remains pinned to its selected revision.

### Operating steps
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.

## Constraints
- Activation never converts stored data or rolls back effects committed by older definitions.
- On stale validation or publication conflict, inspect the candidate and current application, then
  revalidate or recover explicitly. Never retry with a different candidate under the same idempotency key.
