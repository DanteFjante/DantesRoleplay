---
id: procedure.system.application-candidate.catalog-compare
category: system
name: Application candidate catalog compare
governs: System capability system.application-candidate.catalog-compare
status: active
createdBy: "system"
changeNote: "Documents the registered generic capability."
---

## Description
Catalog compare uses a selected current application and one standing grant with both Read and Author capabilities. Select no more than sixteen exact mechanic or procedure identities under one opaque configured source root. It records and returns the bounded file, database, and common-ancestor comparison without importing, exporting, overwriting, or resolving either side.

## Matches
compare catalog files with the application database
check whether live catalog data needs export
prepare a catalog synchronization candidate

## Instructions
### Input and result
The closed input is `applicationId`, opaque `allowedRootId`, nullable `expectedActiveFingerprint`, and
1–16 `records`, each exactly `{kind: "mechanic"|"procedure", id}`.

```json
{"applicationId":"example","allowedRootId":"workspace","expectedActiveFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","records":[{"kind":"procedure","id":"example.procedure.sample"}]}
```

Success is `completed` with bounded comparison `dataJson` and an `evidenceReference`. Preserve that
reference only for the exact compared bytes and activation. Comparison is read-only and creates no
candidate by itself.

### Operating steps
1. Supply only the configured root identity, expected active fingerprint, and exact typed record identities accepted by the capability schema. Never supply or infer a host filesystem path.
2. Keep principal, selected application generation, grant, command identity, deadline, and operation budget host supplied. A catalog file or retained comparison receipt grants none of them.
3. Treat conflict, needs-export, and incomplete as inspectable outcomes that cannot admit a catalog-sync candidate. Preserve both divergent versions for explicit review.

## Constraints
- Export live database changes before editing the same authored records in files.
- Import is a separate explicit synchronization boundary. Comparison never chooses a winner.
- If activation or file bytes drift, compare again; do not reuse old evidence or change record scope
  under the same operation identity.
