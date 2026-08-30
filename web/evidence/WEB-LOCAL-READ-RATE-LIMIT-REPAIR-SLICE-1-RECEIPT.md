# Web local read-rate limit repair Slice 1 receipt

Status: **accepted 2026-08-30**

Implementation: `web/WEB-LOCAL-READ-RATE-LIMIT-REPAIR-SLICE-1-IMPLEMENTATION.md`

## Delivered boundary

- Increased the shared authenticated web-read fixed-window quota from 240 to **2,000 requests per
  minute**, with the existing zero-request queue and stable 429 response retained.
- Kept uploads at **10 per minute** and SSE at **four concurrent streams**.
- Kept loopback/private-remote authorization, security headers, route policies, and every read/write
  boundary unchanged.
- Rebuilt and restarted the local host; no page publication was required.

No route, schema, catalog record, D&D rule, live campaign record, application activation,
state-space binding, page revision, typed effect, or transaction changed.

## Evidence

- Focused `WebInterfaceTests`: **90 passed, 0 failed**.
- Live fixed-window check: **300 consecutive requests returned HTTP 200**, exceeding the former
  240-request ceiling without a rejection.
- Live audience binding returned HTTP 200 for D&D 2024, `dnd2024-main`, and the Brackenford campaign.
- Live in-app browser check opened Rules after the 300-request walk, loaded **2,380 references**,
  reported no `WEB_RATE_LIMITED` response, and had no console errors.
- Diff whitespace check is part of final handoff review.

## Rollback

Restore `WebInterfaceSecurity.ReadRequestsPerMinute` to 240 and rebuild/restart the host. Upload and
stream quotas require no rollback because they did not change.
