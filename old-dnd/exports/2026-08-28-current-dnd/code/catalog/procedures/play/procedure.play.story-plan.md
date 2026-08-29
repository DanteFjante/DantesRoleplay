---
id: procedure.play.story-plan
category: play
name: Execute a bounded story plan
governs: commit(kind: "story-plan") start and cancel; query(kind: "story-plan")
status: active
---

## Description

Accept one small remote-story-model plan, then let the trusted backend resolve each semantic step
serially. The backend alone retrieves the current procedure contracts, answers authorized knowledge,
selects and validates mechanics, commits action effects, and returns a bounded handoff for narration.

## Instructions

1. Start only a linear plan of one to six ordered steps. Each step is exactly `campaign-context`,
   `knowledge`, or `action`; a context step is optional but, when present, is first and unique.
2. Treat every supplied intent as a semantic request, not a command. The caller never supplies a
   mechanic, procedure, effect, database query, tool call, workflow, branch, binding, or retry.
3. Before a context or knowledge step, retrieve its fixed current governing procedure and use its
   typed backend owner. Before an action, retrieve route candidates, selected full procedures, and
   validate the unchanged proposal through the local procedure verifier before the normal action
   transaction runs.
4. Process exactly one step at a time and persist its bounded result. Poll the named story plan
   with its returned revision; do not assume a background step completed without querying it.
5. A successful action is normal played history. If a later step blocks, fails, or is cancelled,
   preserve every earlier action/event/audit/receipt and mark remaining steps skipped.
6. At a terminal plan, read `procedure.play.storytelling` and narrate only its context findings,
   perspective-safe knowledge findings, verified mechanic narration, affected IDs, and unresolved
   work. Do not turn orchestration summaries into new mechanics or fictionally invent results.

## Constraints

- Version 1 is limited to the configured development GM audience. It has no production background
  authentication, actor execution, remote-model credentials, resume, plan generation, branching,
  loops, retries, output binding, parallelism, generic reads, raw effects, or arbitrary commits.
- Cancellation is checked at step boundaries. An action and its story-step receipt commit together,
  or both roll back; no result may claim one without the other.
- Do not expose raw effects, projections, procedure bodies, candidate lists, model prompts/replies,
  source hashes, hidden knowledge IDs, policy/lease data, or stack traces in a story-plan result.
