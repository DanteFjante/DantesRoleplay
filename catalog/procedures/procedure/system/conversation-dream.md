---
id: procedure.system.conversation-dream
category: system
name: Derive a private conversation-memory candidate
governs: System capability system.conversation-memory
status: active
createdBy: "system"
changeNote: "Documents source-pinned INNER consolidation without gameplay mutation."
---

## Description
Conversation dreaming submits a bounded INNER procedure that can read only an exact current revision and explicit message IDs from one authorized private gameplay-session journal. Its structured result is retained as a candidate memory with the durable task handle, completion evidence, source revision, source-message provenance, and private audience.

A candidate is not played state, a world fact, or permission to disclose a secret. Producing one performs no ECS or gameplay write. A later explicit operation may promote reviewed content through ordinary application permissions and typed effects.

## Matches
summarize remembered gameplay discussion
dream over selected conversation messages
derive a memory candidate

## Instructions
1. Query the journal state and select at most 64 exact visible source-message IDs from one current revision.
2. Following this manual, submit the selected application's exact active `<application>.procedure.conversation-dream` procedure through `system.inner-worker.submit` with a closed result schema requiring `sourceRevision`, the ordered `sourceMessageIds`, and the derived memory content. Durable worker procedures are application-owned; this system manual governs the operation but is not the task's executable definition. Provider, model, tools, grant, budget, and deadline remain host-owned.
3. During execution, use only the host-selected read-only `system.conversation-memory` tool. A revision change, missing message, archive, or revoked authority stops the read.
4. After durable completion, call the conversation-memory `derive` commit with the task handle and exact source selection. The host freshly authorizes task readback, obtains the persisted result, output-schema fingerprint, and completion evidence from the task owner, verifies the echoed source selection, and retains that structured result as a private candidate. The caller cannot supply replacement provider output or completion evidence. Read or archive the candidate through conversation-memory management.

## Constraints
- Never infer played events or commit world state from conversation text.
- Never widen the source audience or mix journals, sessions, applications, state spaces, principals, or revisions.
- Never let a derived result authorize or promote itself.
- Provider fixtures establish bounded integration only; a real provider run requires explicit configured availability.
