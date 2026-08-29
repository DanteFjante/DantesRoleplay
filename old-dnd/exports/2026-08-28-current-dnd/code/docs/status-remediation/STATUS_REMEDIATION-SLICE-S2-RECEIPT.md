# Status remediation Slice S2 receipt — action recovery

Status: **Verified**  
Date: 2026-08-21

## Delivered

`commit(kind: "action")` now accepts a closed payload with required `intent` and optional
`roleEntityIds`, `input`, `scope`, and `seed` only. Unknown fields now reject instead of being
silently ignored.

Malformed JSON, non-object payloads, missing intent, and unknown fields return `INVALID_PAYLOAD`
with the expected action shape and a literal callable recovery. A valid payload that fails action
projection retains its `PROJECTION_FAILED` explanation and callable recovery. None emit the stale
generic “the rule is broken, not your arguments” text.

## Evidence

- `ProtocolWalkTests`: **6 passed, 0 failed**.
- The isolated MCP-server and test-project builds succeeded using existing dependency outputs.

## External build note

The normal dependency build is currently blocked by unrelated concurrent Campaign Quest code:
`CampaignQuestContextRunner.cs` lacks the `IDbContextTransaction` namespace/type resolution. This
slice did not modify that owner.
