# Campaign Feature 7 dependency plan — schema-bound AI campaign proposal and review

Status: **Planned; blocked by verified C1 and model/profile/proposal-boundary confirmation.**
Last updated: 2026-08-20

## Target and execution rule

C7 is repository-mode work governed by AGENTS.md, procedure.system.create-feature,
procedure.system.modify, and procedure.mcp.add-tool. The model is an untrusted text producer, not
game authority: it cannot read database/filesystem/MCP context, call tools, write catalog/game
state, choose permanent IDs, return effects, or approve itself.

A host supplies a closed brief and bounded approved reference options. C7 requests one
schema-bound proposal from an approved profile, presents assumptions/questions/diff, and requires
explicit approval before the exact resulting blueprint enters C1 validation. It never invokes C2
creation or writes campaign/world/quest state.

## Boundary and ownership

Included: one profile allowlist and adapter; bounded context; alias-only reference selection; strict
parse/normalization; provenance/assumptions/questions/diff/fingerprint; approval; C1-equivalence
and no-write tests.

Excluded: autonomous creation, retrieval beyond supplied context, hidden prompt memory, tool or
store access, mechanics/quests/items/characters, generated rules/effects/IDs/state, automatic
approval, durable unapproved prose, player authorization, and external-model fallback.

| Owner | C7 consumes | C7 owns |
| --- | --- | --- |
| C0/C1 | Brief constraints, blueprint grammar, validator/fingerprint. | Proposal/review wrapper only. |
| C2 | Nothing callable. | C7 never creates campaign records. |
| World/C3/C4 | Host-approved bounded summaries. | No source retrieval or lifecycle. |
| Model-profile owner | Approved identity, endpoint/timeout/privacy/redaction policy. | No profile, secret, or credential policy. |

## Required confirmation

Confirm one approved profile; no-network/no-tool rule; request/response/text/token/time limits;
provider failure envelope; redaction/retention policy; template version; and request-scoped
proposal policy. C7 begins with no durable proposal, prompt, or model output game state.

| Artifact | Proposed meaning |
| --- | --- |
| Campaign proposal operation | Read-like campaign operation: ProposalRequest returns ProposalReview with zero effects. |
| Proposal approval operation | Exact blueprint plus proposal fingerprint and approved true; recomputes identity, then invokes C1 validation only. |
| procedure.campaign.propose | Context allowlist, profile, grammar, redaction, review, approval, recovery. |
| ProposalContext | Trusted service-built model input with aliases instead of permanent IDs. |

If safe model invocation does not fit the campaign surface, revise before implementation. Do not add
a fourth tool, background job, generic prompt endpoint, or undocumented direct route.

## Closed proposal and approval flow

ProposalRequest contains host-selected campaign ID; title/premise/goals/tone fields within C1
limits; ruleset dnd2024; one world alias, one start alias, two-to-three NPC aliases, one faction
alias, zero-to-eight knowledge aliases, and optional style direction. A trusted service resolves
only these approved records to aliases, role, authored safe summary, and descriptive visibility.
The model never receives IDs, GM-only data, unselected records, procedures, operations, secrets,
or raw component JSON.

Model output contains only title, premise, goals, tone/boundaries, selected aliases, initial
chapter title/question/optional GM context, initial arc title/stake/optional GM context, optional
future quest-shaped GM problem, assumptions, and open questions. IDs, effects, component/link
data, ruleset override, status, script, SQL, URL, credential, or executable instruction reject.

The adapter validates strict output, maps aliases to host-supplied IDs, constructs ordinary C1
CampaignBlueprint, and returns ProposalReview: blueprint; ordered assumptions/questions; field diff;
template/model versions; context fingerprint; proposal fingerprint. Proposal fingerprint is SHA-256
over canonical blueprint, alias context, template and profile version. It is not a reservation.

Approval sends approved true, full returned blueprint, and proposal fingerprint. It recomputes
identity; altered/expired context/template/profile/blueprint rejects. A match calls unmodified C1
and returns its validation report plus provenance. Approval writes zero state. Only afterward may
the host call C2 with C1-valid blueprint/fingerprint; C7 cannot batch, bypass, or call C2.

## Algorithm and one slice

1. Validate closed host request, alias set, sizes, profile policy, and safe context.
2. Invoke one approved profile using fixed template/version and bounded context.
3. Parse data only; reject non-grammar material; canonicalize aliases, blueprint, review, and
   fingerprint.
4. On approval, recompute identity and invoke C1. C1 errors remain C1 errors; C7 never repairs or
   conceals them.
5. Propose/approve have zero structural effects. Timeout, unavailable profile, transport,
   redaction, or schema failure returns a stable recovery result only.

~~~text
C7 reviewed AI proposal
├─ C1 closed validator                                      [must be verified]
├─ model profile/template/privacy policy                    [missing semantic leaf]
├─ confirmed review/approval public boundary                [semantic leaf]
│  └─ Slice 1: isolated adapter, review, approval to C1
└─ C2 normal create                                         [unchanged consumer]
~~~

Slice 1 adds only confirmed procedure/public descriptions, a model adapter isolated from game
stores, canonicalizer/fingerprint service, and focused tests. Handlers remain thin. Run catalog
validation, public-surface guards/protocol walk, full suite at acceptance, and diff check.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Valid proposal | C1-shaped blueprint with aliases only from supplied context, assumptions/questions, stable diff/fingerprint, zero writes. |
| Manual equivalence | Equivalent manual/approved proposal obtains byte-identical C1 validation result. |
| Bad output | Extra field, ID, effect, state, script, SQL, URL, unsafe/oversize text, alias, or unmarked assertion rejects without game write. |
| Provider failure | Timeout, unavailable/wrong profile, malformed transport/redaction failure returns stable recovery; no fallback model. |
| Approval integrity | Missing/false/stale approval or changed blueprint/context/template/profile/fingerprint rejects before valid C1 result. |
| No direct create | All propose/approve paths leave game rows/events/notifications/campaign success audits unchanged and make no C2 call. |
| Privacy | No request/log/diagnostic has credential, unselected ID, GM-only text, hidden JSON, or model chain-of-thought. |

## Exit gate and change rule

C7 is verified only when an approved profile produces a bounded review that reaches C1 validation
only after explicit host approval, and every malformed/unavailable/stale/rejected case makes no
durable game change. C2 remains sole creator.

Revise if model needs live retrieval, permanent IDs, another ruleset, non-structured output,
proposal retention, or immediate creation. Never solve those with tool grants, invisible retrieval,
stored unapproved content, caller effects, prompt randomness, or automatic C2 invocation.
