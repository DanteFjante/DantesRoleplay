---
id: procedure.event.define
category: event
name: Define an event type
governs: registering versioned event payload contracts
status: active
---

## Description
Define or revise an event type before any event can use it.

## Instructions
1. Read existing event types and reuse one when its payload contract fits.
2. Dry-run `commit(kind: "event-type")` before writing.
3. Give the type a permanent lower dotted id and an object-root JSON Schema Draft 2020-12 payload schema.
4. Use a nonempty change note when revising an existing id.

## Constraints
- `world.*` types are reserved structural contracts and arrive from the catalog only.
- This contract registers schemas only. It does not create an event ledger, routing, subscriptions, chains, or notifications.
