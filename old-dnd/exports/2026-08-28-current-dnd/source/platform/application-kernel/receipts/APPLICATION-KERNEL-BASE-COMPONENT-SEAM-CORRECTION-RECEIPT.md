# Application kernel base-component seam correction receipt

Status: **accepted**
Completed: 2026-08-27
Triggering implementation: [D&D 2024 CC2F authenticated rest start](../../../ruleset/dnd2024/DND2024-CHARACTER-CREATION-CC2F-IMPLEMENTATION.md)

## Corrected invariant

The accepted application kernel already records ordered base applications on immutable revisions
and permits exact base-owned component projection. A derived state space could not previously store
those base-owned component values because the ECS write guard admitted only the primary owner.
Automatic action mapping also returned during its primary-owner attempt when a requirement used a
fully qualified base component ID, before checking the declared base owner.

The generic correction now:

- admits component writes only when the type owner is the primary application or an exact direct
  base recorded on that state space's bound immutable application revision;
- continues rejecting unregistered, stale, transitive-but-undeclared, or unrelated owners;
- resolves a fully qualified component ID directly against the matching owner in the allowed
  primary/direct-base set; and
- preserves deterministic unqualified primary-then-base lookup.

No migration, protocol kind, application-specific branch, implicit cross-application dependency,
or unrestricted component owner was added.

## Evidence

- Focused application-scoped ECS and application-mechanic execution classes: 11 passed.
- D&D rest-start integration: 12 passed and proves exact base root/clock projection plus atomic
  derived-application state/relationship effects.
- Full shared suite: 1,204 passed; Local AI suite: 21 passed.
