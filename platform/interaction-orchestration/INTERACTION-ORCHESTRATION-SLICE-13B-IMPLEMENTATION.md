# Interaction orchestration Slice 13B implementation — inner-first typed outer fallback

Status: **accepted 2026-08-25**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Slice 13B](INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

Every actionable application-conversation turn attempts one bounded inner resolution before any
outer planning. An inner `unknown`, `unsupported`, or `unavailable` result is returned once to the
selected outer AI as bounded safe receipt context. The outer AI may then request one server-mediated
outer planning attempt. Both attempts use current trusted search/inspection, the common proposal
verifier, append-only receipts, and distinct idempotency identities. No branch executes without the
existing later player confirmation.

Exclusions: query execution/result binding, task lists, multi-batch continuation, automatic recipe
promotion, automatic execution, new routes/MCP kinds, migrations/durable conversation state,
application rules, and fallback after `needs-input`, `ambiguous`, `unsafe`, or `stale`.

Allowed files/areas: interaction-orchestration outer/planning seams and component registration;
web-interface application conversation coordinator/tests; the MCP host's dedicated local outer
composition/task allowlist; this document/receipt and concise owner status. Stop after a single
correlated inner-to-outer fallback can return an inert proposal or truthful typed non-resolution.

## Confirmed decisions

The user authorized Slice 13B on 2026-08-25 with these closed semantics:

1. Initial outer `respond` remains a non-action response. Initial outer `delegate` and
   `direct-plan` are both actionable and always attempt the inner planner first.
2. Inner `resolved` returns its inert proposal immediately. `needs-input`, `ambiguous`, `unsafe`,
   and `stale` stop without fallback. Only `unknown`, `unsupported`, and `unavailable` are eligible
   for one outer reconsideration, including the required local-AI-disabled path.
3. The reconsideration receives only the player text and a bounded safe prior-resolution object:
   status, code, safe summary/evidence, and opaque receipt reference. It receives no prompt trace,
   contract body, state projection, effects, authorization, operation data, or hidden model output.
4. Only a reconsideration result of `direct-plan` starts outer planning. `respond`, repeated
   `delegate`, or provider unavailability terminates without a loop or execution.
5. The outer planning provider matches the explicitly selected Slice 13A provider. Local outer
   planning uses the dedicated local outer Ollama profile, not the inner local completion profile;
   remote outer planning uses the accepted remote outer role/profile.
6. Inner and outer attempts have distinct deterministic turn-scoped idempotency keys and share one
   host-created delegation correlation. Their receipts remain independent truthful history.
7. No new permanent ID, database/schema field, public route/kind, or browser request field is
   introduced. The existing outer request gains bounded internal prior-resolution context and the
   already-confirmed task classes/schemas remain unchanged.

## Prerequisite evidence

- [Slice 13A validation](receipts/INTERACTION-ORCHESTRATION-SLICE-13A-VALIDATION.md) confirms strict
  local/remote selection, separate local outer profile, and no provider fallback.
- Slice 12E current code owns the local/remote bounded planning loop and common verifier.
- Slice 12F current code owns the process-local application conversation, child delegation,
  explicit execution confirmation, safe narration, and `PriorSafeResultCode` seam.
- Slice 12D/current receipt store proves resolution idempotency is scoped by principal,
  application, state space, and idempotency key, so inner/outer attempts require distinct keys.

## Runtime artifacts and authoritative input

- `InteractionOuterPriorResolution` is an internal model-observation projection containing only
  existing safe receipt fields. It grants no read or execution authority.
- The dedicated local outer structured-completion seam is shared by outer turn, narration, and
  outer-role planning. The host fixes its model/profile/bounds and task allowlist.
- The application conversation coordinator derives delegation ID, attempt identities, AI role,
  planner preference, receipt context, and fallback eligibility. Player/model input cannot set
  them.

No state transaction is added. Planning may append its existing resolution receipt; application
mutation remains owned by the later explicit execution endpoint and exact action/ECS transaction.

## State machine

1. Validate and retain the player message, then obtain one outer classification.
2. `respond` returns normally. For either actionable decision, derive one delegation ID and an
   `.inner` intent identity and call `PlanAsync` with inner role/local preference.
3. If inner resolves, retain that proposal for confirmation and stop. If it is not fallback
   eligible, expose its safe summary/receipt reference and stop in `needs-attention`.
4. For eligible non-resolution, expose the inner receipt, call the selected outer provider once
   with the safe prior-resolution projection, and forbid another delegation loop.
5. Only `direct-plan` derives an `.outer` intent identity and calls `PlanAsync` with outer role,
   selected local/remote preference, and the same delegation correlation.
6. Retain only a resolved outer proposal for explicit confirmation. Otherwise expose the outer
   result and stop. Neither attempt executes, learns automatically, or hides earlier receipts.

## Failure, replay, and no-change contract

| Condition | Result | No-change guarantee |
| --- | --- | --- |
| Inner resolved | Awaiting confirmation | No outer reconsideration/planning and no execution. |
| Inner needs-input/ambiguous/unsafe/stale | Needs attention with inner receipt | No outer call and no execution. |
| Inner unknown/unsupported/unavailable; outer unavailable/responds/delegates | Needs attention with truthful inner/outer messages | No loop, outer planning, or execution. |
| Eligible inner result; outer direct-plan resolves | Awaiting confirmation with outer proposal | Both resolution receipts retained; no execution. |
| Outer planning non-resolution | Needs attention with both safe results | No proposal retained and no execution. |
| Equal request replay | Existing receipt behavior applies per distinct attempt identity | No duplicate receipt/action. |
| Cancellation/exception before mutation | Existing typed/cancellation behavior | No provider switch or application mutation. |

## Implementation sequence and acceptance

1. Add bounded prior-resolution input and the dedicated local outer planning seam.
2. Make local planning choose the inner or dedicated outer completion by immutable role.
3. Implement the bounded conversation fallback state machine with unique attempt identities.
4. Add focused tests for inner success, each stop class, eligible fallback, selected provider
   parity, no loop, receipt visibility, distinct identities, and no execution.
5. Run focused tests, solution build, and full solution tests; write the receipt and update owners.

Verification:

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~ApplicationConversationTests|FullyQualifiedName~InteractionPlanningTests|FullyQualifiedName~InteractionOuterProvider"
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.slnx --no-restore
```

No catalog validation or protocol walk is required because this slice changes neither catalog
records nor MCP kinds/tool registration.

## Receipt and stop

Completion evidence: [Slice 13B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13B-RECEIPT.md).
Stop before 13C.
