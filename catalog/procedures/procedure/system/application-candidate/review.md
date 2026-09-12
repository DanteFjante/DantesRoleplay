---
id: procedure.system.application-candidate.review
category: system
name: Application candidate reuse review
governs: System capabilities system.application-candidate.review-submit, system.application-candidate.review-read, system.application-candidate.review-cancel
status: active
createdBy: "system"
changeNote: "Documents the durable application-candidate reuse-review gateway."
---

## Description
Reuse review submits one exact retained application candidate to the durable validation-task owner. Submit requires the candidate identity and fingerprint together with its authoring operation receipt and causal command; the host supplies the trusted principal, current application generation, current Read and Validate grants, immutable reviewer profile, provider policy, budget, and task command identity. A successful submit returns a Pending task handle. Read requires the current Read grant and returns the owner's actual lifecycle data, including a bounded retained result only after completion and accounting reconciliation. Cancel requires current Read and Validate grants and asks the task owner to cancel that exact handle.

A task handle, Pending state, worker judgment, completed result, or cancellation is not candidate validation, publication, or execution authority. The authoring owner independently checks the retained review proof, exact authoring causation, current grants, runtime evidence, and compatible publication case. Foreign handles, changed causation, revoked grants, and unavailable providers remain denied or unavailable as reported.

## Instructions
1. Preserve the exact candidate ID, revision, content fingerprint, authoring operation ID, causal command ID, task ID, and task command ID. Do not substitute current values.
2. Use a stable idempotency key for submit and cancel. Poll the returned task handle with review-read; do not retry an uncertain write under a new identity.
3. Keep principal, application generation, grants, reviewer profile, provider selection, tool policy, and budgets host supplied. Never place them in capability input.
4. Report Pending, completed, cancelled, denied, and unavailable results distinctly. Continue to candidate validation and activation only through their registered owner capabilities.
