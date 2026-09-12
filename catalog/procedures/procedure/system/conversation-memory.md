---
id: procedure.system.conversation-memory
category: system
name: Manage scoped conversation memory
governs: MCP query and commit kind system.conversation-memory
status: active
createdBy: "system"
changeNote: "Documents private gameplay-session conversation capture and recovery."
---

## Description
Conversation memory retains actual user and user-visible outer-assistant messages from one explicitly linked Codex thread. It is private to the host-selected principal and exact application, state space, and gameplay session. It is conversation evidence, not proof that an in-world action happened, and it never enters the played-message or planning-history tables automatically.

The existing `query` verb reads `state`, bounded `messages`, or derived candidates. The existing `commit` verb connects a scoped Codex source, appends one completed visible-message batch, retries capture, disconnects, archives, or requests deletion. The host supplies principal and authority. A caller supplies only the exact application/state/session target and source identities allowed by the closed operation.

## Matches
remember this gameplay conversation
connect Codex conversation capture
inspect or clear conversation memory

## Instructions
1. Connect only an explicitly chosen gameplay task to the exact repository project and gameplay session. Never bind an engineering task or all account conversations.
2. Capture through the supported hook correlation and non-resuming app-server adapter. It verifies the linked thread with metadata-only `thread/read`, locates the exact turn through bounded `thread/turns/list` pages, and loads its visible items through bounded `thread/items/list` pages. Preserve each visible `userMessage` and `agentMessage` item in source order with its exact item and turn IDs. Do not parse transcript files.
   The pinned hook target is `roleplay capture-memory`; configure its principal/application/state/gameplay-session/project/repository/thread/checkpoint options once for the linked session. Run that exact command once with `--connect` and empty stdin, then omit `--connect` when piping hook JSON. Disconnect remains effective until another explicit `--connect`. The JSON can correlate only `event_name`, `session_id`, `cwd`, `turn_id`, and prompt metadata; it cannot change the configured scope. Configure the server's matching `ConversationMemoryCapture` section to enable MCP management for that same immutable binding.
3. Check `state` after connection or failure. The authorized `retry` management operation re-enables only a retry-pending journal; then run the same pinned helper with `--retry` and empty stdin to deliver one saved queue item without a new user turn. A disconnected journal requires an explicit `connect`; retry never reconnects it. Disconnect to stop capture; archive when the journal must remain available without appearing in ordinary reads.
4. Use deletion only for the exact scoped journal or message. If a derived memory still cites source messages, retain the source evidence and report those dependent references instead of claiming deletion.

## Constraints
- Hidden reasoning, tool items, approval details, credentials, and unrelated tasks are never ingested.
- Replaying the same delivery returns the same receipt. Reusing its token or source identity with changed content is a conflict.
- Bounds fail explicitly. Text and item identities are never silently truncated or concatenated.
- The caller cannot select a principal, audience, grant, or direct-message recipient.
