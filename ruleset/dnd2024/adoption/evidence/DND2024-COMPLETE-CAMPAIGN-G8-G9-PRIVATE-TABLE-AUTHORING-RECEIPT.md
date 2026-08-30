# D&D 2024 complete-campaign G8/G9 private-table authoring receipt

Status: **accepted**
Date: 2026-08-30

## Delivered boundary

The local private-table seat is now an immutable process snapshot containing principal, role,
application, exact source-id profile, campaign, and optional actor. Configuration reload cannot
silently change that selection. Restart with the same configuration reconstructs the same values.

The authorized binding now requires the exact active source profile and rejects a state space whose
manifest fingerprint differs from the activation that supplied the fingerprinted knowledge
metadata. Loopback, campaign/application equality, role/actor shape, unique active campaign root,
and exact component evidence continue to fail closed.

With G4, G7N, and G8 verified, the existing G9 `system.world-state.sync` implementation is accepted:
private-operator authorization, bounded root scope, expected revisions, registered component and
relationship owners, typed effects, one transaction, replay/conflict behavior, rollback, and audit
evidence remain intact.

No live campaign, source registration, activation, state space, World record, or database row was
changed.

## Evidence

- [Implementation contract](../../DND2024-G8-G9-PRIVATE-TABLE-AUTHORING-IMPLEMENTATION.md)
  records the stable-seat, exact-profile, exact-activation, and authoring boundaries.
- `LocalKnowledgeSeatLifecycleTests` proves refresh stability, restart reconstruction, loopback
  Game Master access, and campaign/source-profile/remote denial.
- `KnowledgeCoreTests.Activated_binding_resolves_only_the_exact_active_campaign_space` proves the
  exact source profile and state-space activation fingerprint, including mismatch denial.
- `SystemAudienceContextToolsTests` and `WorldChronologyWebEndpointTests` prove Actor and Game
  Master seat behavior without caller-selected identity.
- `ApplicationWorldAuthoringSynchronizerTests` and `WorldStateProtocolTests` retain G9's dry-run,
  commit, replay, conflict, stale revision, root-scope, rollback, closed-input, and protocol
  authorization evidence.
- The combined lifecycle, audience, activated-document, knowledge, authoring, and protocol filter
  passed 41 of 41 tests in isolated configuration `CodexG8`.

## Deliberate exclusions and next gate

Acceptance makes the transaction available to later reviewed features; it is not permission to
perform a live write in this receipt. Immutable backup/migration gates still apply to prototype
rewrites. The next campaign-tab slice is trusted replay-safe session/arc closure and record capture.
