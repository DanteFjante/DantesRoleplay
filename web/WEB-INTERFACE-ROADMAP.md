# Web interface roadmap

Status: **Slice 1 accepted with the recorded repository-level test exception**
Last updated: 2026-08-23

## Outcome

Provide a small local web interface whose pages are ordinary user-authored HTML documents. A page
may contain CSS and JavaScript, use browser-native composition, fetch arbitrary JSON component data,
and later subscribe to committed changes. Updating a page must not require compiling or restarting
the host.

## Confirmed boundaries

- `DantesRoleplay.Web` is a ruleset-neutral system project hosted by the existing ASP.NET process.
- SQLite stores append-only HTML page revisions and the active revision pointer.
- Uploaded HTML is trusted executable content. Authentication, isolation, CSP hardening, and
  sandboxing are deliberately deferred.
- A dynamic read endpoint accepts a data type and entity ID. The reserved `entity` type returns a
  generic entity envelope; every other type is an existing component-definition ID and returns
  that component's JSON object without a compile-time response model.
- The web layer reads state through `IWorldStore`. It never reads tables directly and never owns
  game rules or game-state writes.
- HTML itself owns layout and nesting. There is no page-layout JSON vocabulary, SPA framework,
  Node toolchain, frontend build, or server-side component renderer.

## Ordered delivery

| Slice | State | Capability |
| --- | --- | --- |
| 1 | accepted | Versioned HTML upload/serving plus dynamic entity/component JSON reads. |
| 2 | planned | Separate uploaded assets or ZIP bundles, only after a real page needs them. |
| 3 | planned | SSE invalidation and optional live page-revision notification. |
| 4 | planned | Authentication, trust policy, isolation, CSP, quotas, and remote deployment. |

## Current implementation owner

[Slice 1 receipt](WEB-INTERFACE-SLICE-1-RECEIPT.md) records the delivered foundation. There is no
active later slice; later work must begin with a separately confirmed implementation document.
