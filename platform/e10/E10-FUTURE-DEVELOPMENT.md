# E10 future development — deferred feedback and identity work

Status: **Deferred intentionally for the playable-prototype phase.**
Last updated: 2026-08-21

## Current prototype boundary

The local durable-feedback loop is complete through E10 Slice 3A:

- an LLM can submit and query bounded system-feedback reports through the existing MCP surface;
- a local developer can list, inspect, triage, export, hold, archive, and restore reports;
- reports stay in the local SQLite database and archive is reversible;
- feedback is evidence for reviewed improvements, never an instruction to automatically alter game
  state, rules, catalog records, or source code.

This is sufficient for the current goal: make the game playable, exercise it with LLMs, and use
their observations to prioritize reviewed fixes. No identity provider, remote-user account, web
login, remote administrator workflow, external issue tracker, scheduler, or deletion feature is
needed for that loop.

## Explicitly deferred

### E9 — trusted principal context and authorization

E9 is not a prerequisite for local play or local feedback. It becomes necessary only when a
deployment must distinguish separate remote people/services and enforce who may perform an action.
The detailed plan remains at [E9-DEPENDENCY-PLAN.md](../e9/E9-DEPENDENCY-PLAN.md).

If it is resumed, the intended low-complexity direction is app-hosted ASP.NET Core Identity with
local username/password accounts. The application must still authenticate at a login boundary and
pass only a verified session or short-lived token into MCP/API requests; a username, password, or
`isGm` value inside a tool payload is never trusted identity.

Before E9 implementation, confirm the concrete choices below in one decision record:

1. Bootstrap administrator and account-creation/recovery process.
2. Browser, MCP/API, CLI, test, and internal-call transport behavior.
3. Session/token lifetime, revocation, logout, password-reset, and outage behavior.
4. Canonical opaque principal identifier and minimum audit evidence.
5. Initial capability ids, scopes, and the owners of campaign/GM/player-control policy data.
6. Anonymous-read policy; default remains deny for any privileged operation.

Implement E9 only in its existing order: shared trusted context and deny-by-default hook, all
transport parity, then one separately planned consumer policy. Do not build a generic role editor,
account-management product, or game authorization policy as part of E9.

### E10 Slice 3B — authorized remote feedback submit/read

Start only after accepted E9 context, authorization, privacy-audit, and transport-parity receipts.
Then route feedback submission and reads through the shared E9 hook using the capability and scope
contract in [E10 Slice 3 decision proposal](E10-SLICE-3-DECISION-PROPOSAL.md). Remote anonymous
access remains denied.

### E10 Slice 3C — authorized remote feedback triage/export

Start only after Slice 3B. It adds administrator-only remote triage/export while preserving the
existing redaction rules. Local commands remain a separate trusted-filesystem boundary.

## Intentionally outside the current roadmap

The following are not merely postponed implementation details; each needs a new decision record
and explicit approval if it is ever wanted:

- hard deletion/purge, automatic expiry, retention-policy editing, or bulk retention operations;
- issue-tracker, email, chat, webhook, or other external feedback delivery;
- automatic code/catalog/rule changes based on feedback;
- attachments, screenshots, transcripts, semantic scoring, clustering, or remediation;
- multi-tenant identity, distributed rate limiting, or a generic roles/permissions product.

## Re-entry checklist

Resume this work only when one of these concrete needs appears:

- a real remote user needs an account;
- a remote player/GM must be distinguished from another player/GM;
- the feedback database must be safely readable or administrable outside the local trusted host;
- a reviewed external delivery destination has been selected; or
- deletion is required and a tested backup/restore and approval process exists.

Until then, prioritize gameplay mechanics, playable loops, and using the existing local feedback
evidence during integration testing.
