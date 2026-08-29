# Knowledge and facts — Slice 6 core receipt

Status: **Complete for 6A–6C; a local development-only 6D bridge is available**  
Completed: 2026-08-21

## Delivered

- Added the host-supplied, fail-closed `IAuthenticatedCampaignAudiencePolicy` contract. Its only
  method accepts a campaign ID; a request cannot supply a principal, role, or actor ID.
- Added separate authorized request/result and candidate contracts. The result never contains a
  canonical knowledge ID, sensitivity, source kind, candidate list, or policy revision.
- Added a campaign-bound resolver: policy first, active campaign/world scope next, active actor
  participation as defense in depth, then effective actor knowledge state.
- Added FTS `AllowedKnowledgeIds`, applied in SQLite with `json_each` before ranking and `LIMIT`.
  Actor retrieval is lexical only; the existing vector path remains trusted-GM-only until it can
  prove filtering before nearest-neighbour selection.
- Added a bounded no-tools Ollama completion task (`knowledge.authorized-answer`) with internal
  citation validation. Candidate texts omit document-id lines; actor output preserves only stance
  plus `statement`, `rumour`, or `evidence` presentation. Secrets map to `statement`.
- Re-resolves policy and candidate revisions after inference, restarting once and returning a
  generic stale result on repeated change. Familiar-only matches provide recognition without the
  proposition; unknown records are excluded.

## Verification

- Focused authorization-answer and lexical retrieval tests pass (8 tests).
- The full test suite was run after implementation; it completed successfully with the existing
  unrelated xUnit analyzer warning in `KnowledgeAcquisitionCoordinatorTests`.

## Deliberately not delivered

No real authentication implementation, party scope, remote deployment path, or permissive
request-selected identity was added. Production still requires a real
`IAuthenticatedCampaignAudiencePolicy` before publishing or sharing the host.

## Temporary local bridge

The MCP `query(kind: "knowledge-answer")` route is now available through an explicitly enabled,
loopback-only development policy. It has one fixed configured GM or actor seat for the whole host,
never accepts an actor/role/principal from a request, and is disabled by default. Setup and removal
steps are in [development knowledge access](../DEVELOPMENT_KNOWLEDGE_ACCESS.md).
