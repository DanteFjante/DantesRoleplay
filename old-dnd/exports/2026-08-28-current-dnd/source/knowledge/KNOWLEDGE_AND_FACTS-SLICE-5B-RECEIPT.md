# Knowledge and facts — Slice 5B receipt

Completed: 2026-08-21

## Delivered

- An optional host-only Ollama completion adapter for the configured `qwen3:8b` standard profile.
  It checks exact model availability and completion capability, uses non-streaming JSON Schema
  output, disables thinking, fixes temperature at zero, supplies no tools, and enforces prompt,
  response, token, timeout, concurrency, and task-class bounds.
- Interactive requests take priority over queued background completions. The adapter records the
  provider, configured model, installed-model digest, named profile, elapsed time, and Ollama token
  counts in its bounded result; failures use stable codes and do not expose a reasoning trace.
- Trusted-GM Mode A fact answering over bounded hybrid-search results. The coordinator hydrates
  canonical candidates before the model call and accepts only candidate IDs, preserved epistemic
  kinds, and candidate-backed citations. Invented IDs, recategorized facts, malformed output,
  unavailable Ollama, and empty retrieval all fail closed to deterministic candidates and `unknown`.
- Separate bounded in-memory queues for embedding synchronization and review-only knowledge
  proposals. Jobs have bounded retention, cancellation, retry eligibility, safe status text, model
  audit fields, and isolated capacity so embedding work cannot consume the proposal queue.
- `qwen3:8b` may propose aliases, lowercase tags, possible duplicate pairs, and possible
  contradiction pairs for at most eight supplied canonical IDs. The host validates every ID and
  pair, rereads all inputs, and discards output when the source fingerprint changed.
- A hosted processor services each queue independently. Proposal output is advisory and ephemeral;
  it never mutates facts, tags, aliases, relationships, search rows, or campaign state.

## Runtime configuration

The feature remains disabled unless explicitly configured:

```text
Knowledge__Completion__Enabled=true
Knowledge__Completion__Endpoint=http://localhost:11434
Knowledge__Completion__Model=qwen3:8b
Knowledge__Completion__Profile=standard
Knowledge__Completion__MaxPromptCharacters=30000
Knowledge__Completion__MaxOutputTokens=1024
Knowledge__Completion__MaxConcurrentRequests=1
```

`DANTESROLEPLAY_OLLAMA_COMPLETION=1` also enables the configured completion profile and gates the
live integration tests. The host never starts Ollama, downloads a model, or silently selects a
different installed model.

## Verification

- Focused tests cover disabled/unavailable operation, exact model verification, schema mismatch,
  no-tools/non-thinking requests, interactive priority, citation enforcement, invented IDs, kind
  preservation, deterministic fallback, isolated queue capacity, cancellation, stale input, and
  unsupported proposal IDs.
- Live local tests exercise `qwen3:8b` through Ollama for direct schema-bound completion, full
  hybrid-retrieval fact answering, and review-only proposals.
- The 16-test focused Slice 5B matrix passed both with local completion disabled and with live
  Ollama integration enabled. The six-test MCP protocol walk passed after dependency registration.
- The complete repository suite passed 695/695 in 1 minute 20 seconds. The full solution compiled
  without an error; Slice 5B adds no analyzer warning.
- No catalog record or canonical database schema changed; catalog validation and migration are not
  applicable.

## Explicit exclusions retained

Slice 5B adds no model tool calling, read-agent loop, public/MCP knowledge endpoint, route proposal,
workflow execution, canonical knowledge write, or player-facing authorization. Pending jobs and
proposal results intentionally do not survive a process restart; both are safe derived work and may
be re-enqueued. Tool-bounded Mode B and route-only Mode C remain Slice 5C.
