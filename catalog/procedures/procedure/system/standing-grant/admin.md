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

## Instructions
1. Select the exact grantee principal, installed application, scope, definition allowance, capabilities, effect kinds, operation ceiling, and UTC expiry. Empty selectors never mean unrestricted access.
2. Use `issue` with expected revision 0 for a new grant family. Use `replace` or `revoke` with the exact positive current revision. Revocation preserves the current binding and appends a terminal revoked revision.
3. Review the preflight, confirm through the trusted operator host, and retain the same idempotency key for every retry of the same logical mutation. Reusing that key with changed input is a conflict.
4. Treat the returned operation ID and request fingerprint as the committed receipt. Authentication, application authoring permissions, or possession of a standing grant never grant issuer authority.
5. If target ownership is unavailable, repair or reselect the exact current owner before issuing access. The grant record does not make a nonexistent or stale target valid.
