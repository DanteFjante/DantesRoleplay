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

## Matches
inspect an application candidate
read retained candidate evidence
find a candidate by authoring operation

## Instructions
### Input and result
The closed input is `applicationId`, nullable `candidateId`, nonnegative `revision`, and nullable
`sourceOperationId`. Use either an exact candidate/revision or the source operation form required by
the current capability schema; do not combine identities speculatively.

```json
{"applicationId":"example","candidateId":"0123456789abcdef0123456789abcdef","revision":1,"sourceOperationId":null}
```

Success is `completed` with `code`, `message`, bounded `dataJson`, and an `evidenceReference`.
Parse `dataJson` as retained candidate data, then preserve the returned identity and fingerprint for
review, validation, activation, or recovery. A read result is not a write receipt or execution grant.

### Operating steps
1. Use exact retained candidate revisions and fingerprints from the capability result; never replace them with a current revision.
2. Keep principal, selected application generation, grant, confirmation, and command identity host supplied. A manual does not grant any of them.
3. Report failed, denied, pending, and unavailable results as returned. Do not claim recovery, validation, or activation succeeded without completed evidence.

## Constraints
- Refresh the selected application and candidate reference after a stale result; never replace the
  requested revision with the latest one silently.
- Retry an unavailable read only after following its structured recovery action. Reads are safe to
  repeat, but absence of evidence is not proof that a write failed.
