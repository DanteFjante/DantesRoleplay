# Web local read-rate limit repair Slice 1

Status: **accepted 2026-08-30**

Owner: `web/WEB-INTERFACE-ROADMAP.md`, Feature 1 / accepted quota boundary

Ruleset alignment: **ruleset-neutral host policy**

## Outcome and boundary

Raise the shared authenticated web-read allowance from 240 to 2,000 requests per minute so one
normal D&D 2024 campaign load plus the bounded, paginated registered-Rules refresh does not reject
itself with `WEB_RATE_LIMITED`.

The user's request confirms this quota revision. Uploads remain limited to 10 per minute, SSE
remains limited to four concurrent streams, rejected reads remain stable 429 JSON, and all existing
loopback/private-remote authentication and security headers remain unchanged.

Allowed files: the generic web security quota constants/composition, its focused tests and usage
documentation, this plan/receipt, and the rebuilt/restarted local host.

Forbidden work: disabling rate limiting, changing upload/stream quotas, bypassing authentication,
adding a route, changing catalog/game state, or adding D&D-specific server behavior.

## Acceptance

- The generic read limiter uses exactly 2,000 permits per one-minute fixed window with no queue.
- Upload and stream limits retain their accepted values.
- Focused web tests and a live host smoke pass after rebuild/restart.
- Repeated Rules refreshes load the complete registered index without a 429 response.

Stop point: the local website and complete Rules index load under the revised bounded read quota;
no page revision or live game record changes.

Completion receipt: `web/evidence/WEB-LOCAL-READ-RATE-LIMIT-REPAIR-SLICE-1-RECEIPT.md`.
