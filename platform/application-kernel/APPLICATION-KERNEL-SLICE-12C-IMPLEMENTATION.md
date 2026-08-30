# Application Kernel Slice 12C implementation — compatible populated-binding revalidation

Status: **active**
Owner/roadmap: application activation and state-space administration
Dependency tree/leaf: verified state-space binding administration; stale active-overlay recovery
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; no game rule is implemented
Outcome: let a populated state space rebind to a newer active overlay only when its persisted
application state remains exactly compatible with the currently registered component contracts.
Exclusions: application activation, catalog/rules changes, caller-supplied mappings or overrides,
data transformation, entity/component/edge mutation, and character creation.
Allowed files/areas: state-space administration contracts/service/tests, its governing procedure,
this document, and one completion receipt.
Stop point: the generic rebind is verified and the existing D&D state space can pass its normal
mechanic readiness check; creation of Ganji is a separate application-owned action.

## Confirmed decisions

The user's 2026-08-30 request to fix the unavailable Party flow authorizes the required safe
migration. The repair is deliberately generic: it may never name a ruleset, campaign, actor, or
content ID. A non-empty state space is eligible only when it belongs to the same immutable
application revision/fingerprint and every stored component is owned by that application, has the
same registered version and schema hash, and validates against that schema.

## Prerequisite evidence

- The `dnd2024-main` state space has a stale activation binding while the registered application
  revision/fingerprint remains current.
- `ApplicationActionRunner` rejects the Party mechanic before invocation with
  `STATE_SPACE_ACTIVATION_STALE`.
- Component registrations already retain immutable owner, version, normalized schema, and hash;
  application ECS rows retain the corresponding qualified type, version, schema hash, and data.
- Existing state-space upgrades already require exact active/binding evidence, dry run, replay,
  atomic operation audit, and immutable binding history.

## Authoritative state and behavior

`StateSpaceAdministrationService` owns this one binding transaction. It first proves the target
active overlay matches the registered application. For a populated space it additionally loads all
application ECS components in that state space, resolves each exact registered component type,
checks ownership/version/schema hash, and validates the stored JSON. Any mismatch remains
`MIGRATION_REQUIRED`; no rows or binding history are changed. An empty space retains the existing
`empty-state-compatible` evidence; a verified populated space reports
`populated-state-compatible-rebind`.

The request shape is unchanged. The current active fingerprint and current binding fingerprint are
both caller evidence, not caller authority. Dry run fingerprints the derived compatibility result;
commit repeats the proof inside its transaction and rejects any drift.

## Failure, replay, and rollback

Unknown/foreign/missing/version-or-schema-drifted/invalid stored components reject with
`MIGRATION_REQUIRED`. Stale active or binding evidence rejects as today. Audit failure rolls back
the binding and history. Equal-token retries return retained immutable evidence; no branch permits
partial conversion or data mutation.

## Implementation sequence

1. Inject the existing generic component registry and schema validator into the binding owner.
2. Add exact populated-state compatibility proof without altering request or effect shapes.
3. Update focused fixtures/tests for valid and invalid populated rebinding and rollback.
4. Update the governing procedure to describe the strictly compatible rebind.
5. Run focused tests, catalog validation, relevant protocol tests, and live dry-run/commit/readback.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| empty space | existing upgrade behavior and evidence remain unchanged |
| valid stored registered component | rebinds only after dry run and retains component data |
| unknown, foreign, hash/version-drifted, or invalid component | `MIGRATION_REQUIRED`, no binding change |
| activation or binding changes after dry run | stale rejection, no binding change |
| audit failure | binding and immutable history roll back |
| replay | retained compatibility evidence is returned |

## Verification and receipt

Run focused state-space administration and protocol tests, `roleplay validate catalog`, and
`git diff --check`; then dry-run and commit the exact live rebind and confirm the Party mechanic is
available. Record the result in `APPLICATION-KERNEL-SLICE-12C-RECEIPT.md` and stop before
character creation.
