---
id: procedure.system.standing-grant.admin
category: system
name: Standing grant administration
governs: System capability system.standing-grant.admin
status: active
createdBy: "system"
changeNote: "Documents installation-operator standing-grant mutation."
---

## Description
Standing grant administration issues, replaces, or revokes one immutable grant revision. The capability is a private operator adapter: the standing-grant owner independently rechecks current installation-operator membership inside its transaction, compares the exact current revision, and records the new revision, current pointer, and operation receipt atomically.

## Matches
grant an AI permission to read or act
replace a standing grant
revoke worker or application access
set operation and effect limits

## Instructions
### Input and result
Use the discoverable `system.standing-grant.admin` schema. The closed command selects `issue`,
`replace`, or `revoke` and binds one grant family to an exact grantee, application, Application or
StateSpace scope, definition allowance, capability set, allowed effect kinds, maximum operations,
and expiry. The private host supplies issuer identity, confirmation, and idempotency context.

Success returns the committed operation ID and request fingerprint. Preserve the current grant
revision from the owner for replacement or revocation; never reconstruct it from an earlier receipt.

```json
{"mutation":"issue","grantId":"example-authors","expectedCurrentRevision":0,"principalReference":"principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","applicationId":"example","scope":"application","stateSpaceId":null,"capabilities":["author","validate","activate","read"],"definitions":{"mode":"exactIds","exactIds":["procedure.example.sample"],"applicationOwnedNamespaces":[]},"effectKinds":[],"maximumOperations":16,"expiresAtUtc":"2026-12-31T23:59:59Z"}
```

### Operating steps
1. Select the exact grantee principal, installed application, scope, definition allowance, capabilities, effect kinds, operation ceiling, and UTC expiry. Empty selectors never mean unrestricted access.
2. Use `issue` with expected revision 0 for a new grant family. Use `replace` or `revoke` with the exact positive current revision. Revocation preserves the current binding and appends a terminal revoked revision.
3. Review the preflight, confirm through the trusted operator host, and retain the same idempotency key for every retry of the same logical mutation. Reusing that key with changed input is a conflict.
4. Treat the returned operation ID and request fingerprint as the committed receipt. Authentication, application authoring permissions, or possession of a standing grant never grant issuer authority.
5. If target ownership is unavailable, repair or reselect the exact current owner before issuing access. The grant record does not make a nonexistent or stale target valid.

## Constraints
- Empty selectors are never unrestricted access. Grant only the minimum exact definitions,
  capabilities, effects, operation ceiling, scope, and lifetime needed.
- A current grant is rechecked at sensitive execution, commit, task readback, and cancellation. Its
  possession cannot authorize grant administration.
- Revocation prevents new authorized work but does not erase history or roll back committed effects.
