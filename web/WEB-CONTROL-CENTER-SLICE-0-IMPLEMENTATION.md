# Web Interface Feature 2 Slice 0 implementation — control authorization and API conventions

Status: **accepted with recorded repository-level build/test exceptions — delivered by [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md)**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Operator control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Establish one reusable, fail-closed authorization and same-origin boundary for future
`/api/control/*` endpoints without implementing a control-center page or any panel data.  
Exclusions: Control-center HTML, status/effect/ECS/settings/page-editor/conversation/Codex endpoints,
database changes, migrations, persisted grants, accounts, secrets, MCP changes, Tailscale Serve
configuration, catalog changes, game-state writes, and changes to existing page-upload semantics.  
Allowed files/areas: `src/system/authorization/`, `src/system/web-interface/`, focused shared tests,
`web/WEB-CONTROL-CENTER-*`, and the web roadmap. No host composition, database, catalog, MCP, or
user-authored page edit.  
Stop point: The capability contract, control route helpers, JSON/same-origin mutation guard, remote
route inclusion, focused/full tests, receipt, and planning-status updates are complete. Do not map a
runtime `/api/control/*` endpoint.

## Confirmed decisions

- On 2026-08-24 the user asked to implement Slice 0 after reviewing the dependency plan and model
  routing. This confirms page ID `control-center`, route family `/api/control/*`, the five proposed
  custom-element IDs, the five capability names below, and the local-plus-exact-Tailscale operator
  mapping for this security foundation.
- The permanent capability evidence names are `control.read`, `control.pages.write`,
  `control.settings.write`, `control.ai.message`, and `control.codex.approve`.
- The closed single-operator policy remains: authenticated local access or the exact configured and
  allowed Tailscale identity is the grant. No grant database or browser-supplied role is added.
- Control reads use GET. Control mutations use only POST or PUT with `application/json` and one
  serialized same-origin `Origin` header. DELETE and simple-form mutations are not mapped.
- Local control mutations require a loopback Host (`localhost`, IPv4 loopback, or IPv6 loopback).
  Their Origin scheme/host/effective port must equal the request. Tailscale control mutations require
  HTTPS Origin with the exact already-authorized Tailscale Host and effective port; the loopback
  backend request scheme is not trusted as the public scheme.
- Existing `/api/pages/*` mutation behavior is deliberately unchanged. The stricter rule applies
  only through the new control endpoint convention.

## D&D 5e 2024 alignment

No D&D rule, vocabulary, formula, eligibility, outcome, or catalog content is involved. The slice is
generic host authorization and HTTP request validation.

## External implementation reference

No Foundry dnd5e review is relevant to ruleset-neutral HTTP authorization. Existing ASP.NET minimal
API endpoint-filter and route-mapping facilities remain the only framework dependency.

## Prerequisite evidence

- [Feature 1 Slice 4 receipt](WEB-INTERFACE-SLICE-4-RECEIPT.md) verifies loopback access, trusted
  same-origin authored pages, security headers, and request limits.
- [Feature 1 Slice 5 receipt](WEB-INTERFACE-SLICE-5-RECEIPT.md) verifies exact Tailscale Host/login
  identity, authenticated principals, and the web-only remote route boundary.
- `src/system/authorization/domain/PrivateOperatorAuthorization.cs` owns trusted principals,
  private-host scope, capability evaluation, and safe evidence.
- `WebPrivateOperatorGuard` already derives identity from the trusted HTTP adapter and maps legacy
  read/modify requests without accepting caller authority.

## Runtime artifacts

- Revise `PrivateOperatorCapability` with five closed control values and one canonical audit-name
  mapping. Existing `Read` and `Modify` values and evidence names remain compatible.
- Revise `WebPrivateOperatorGuard.Evaluate` to accept an optional server-selected capability while
  preserving its current method-derived default for existing endpoints.
- Add web-owned `WebControlEndpointConventions` with route prefix `/api/control` and public mapping
  helpers `MapDantesRoleplayControlGet`, `MapDantesRoleplayControlPost`, and
  `MapDantesRoleplayControlPut`. Patterns are relative, bounded route literals; the helpers select
  capability, filter, and existing rate-limit policy server-side.
- Add `WebControlRequestGuard`, `WebControlRequestDecision`, and `WebControlRequestFilter` for exact
  capability validation, identity authorization, JSON enforcement, local/Tailscale Host/Origin
  comparison, stable errors, security headers, and denial before handler invocation.
- Add `/api/control` to the existing remote web route allowlist. This exposes no runtime endpoint by
  itself.
- Revise authorization/web component manifests to record the capability and control-boundary owners.
- No route, table, migration, schema, setting, page revision, or durable authorization record.

## Authoritative state and closed input

- The endpoint mapping helper supplies the capability. Headers, query parameters, and JSON bodies
  cannot nominate or widen it.
- `WebAccessPolicy` supplies local versus Tailscale mode and principal identity from TCP peer, exact
  configured Host, and Tailscale's verified login header under the existing Slice 5 boundary.
- ASP.NET supplies request method, Host, Origin, Content-Type, scheme, port, and trace identifier.
- The caller may supply only the future endpoint's declared route/query/JSON data. It may never
  supply principal ID, access mode, capability, private-host scope, expected public scheme, evidence,
  or a trusted forwarded-host/proto value.
- Control route helpers accept only a relative route beginning with `/`, with a maximum length of
  160, no query/fragment, no `..`, and no `/api` prefix. Invalid mapping fails at startup.

## Behavior, result, and typed effects

1. The helper maps the handler under `/api/control`, fixes GET/POST/PUT, fixes the capability, and
   selects the existing read or upload limiter.
2. The filter applies the existing security headers and asks `WebPrivateOperatorGuard` to authorize
   the server-selected control capability.
3. Identity/capability denial returns stable 403 JSON and never invokes the handler.
4. GET with `control.read` proceeds without Origin or Content-Type checks.
5. POST/PUT first require one of the four write/message/approval capabilities, then require
   `application/json`, an approved Host, and exactly matching serialized Origin.
6. JSON failure returns 415; missing/invalid/mismatched Host or Origin returns 403. No failure invokes
   the handler or owner.
7. An accepted request receives the existing authenticated principal and invokes the handler once.

The slice persists no state and owns no transaction or typed effect.

## Failure, replay, and rollback contract

- Missing/non-loopback peer, disabled/wrong remote Host, absent/disallowed Tailscale login, or an
  authorization denial retains the existing access error and makes no change.
- A non-control capability passed to a control helper/guard is rejected; it never silently falls
  back to generic `Modify`.
- Missing/multiple/`null`/non-absolute/path-bearing Origin, non-loopback local Host, scheme/host/port
  mismatch, or caller-supplied forwarded headers returns 403 before the handler.
- Missing or non-JSON mutation Content-Type returns 415 before body/handler processing.
- Unsupported control methods and invalid relative route patterns fail during mapping, not at first
  stateful owner invocation.
- Repeating an accepted request has only the future endpoint's declared replay behavior. Slice 0
  itself writes nothing. Repeating a rejected request remains no-change.
- Rollback is removal of the new convention/capability values; no data migration or persisted state
  exists.

## Implementation sequence

1. Extend the authorization capability/evidence contract compatibly and add focused policy tests.
2. Add control Host/Origin/JSON evaluation and handler-invocation tests.
3. Add the route mapping helpers, DI registration, remote path inclusion, and mapping tests.
4. Update manifests, run focused tests and full verification, write the receipt, mark Slice 0
   accepted, and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Capability | All five control values produce their exact dotted audit names; existing read/modify evidence is unchanged. |
| Local positive | GET control read succeeds; JSON POST/PUT from loopback Host and exact HTTP Origin succeeds with the mapped capability. |
| Tailscale positive | Exact configured Host/login plus exact HTTPS Origin succeeds even though the backend peer/scheme is loopback HTTP. |
| Identity negative | Non-loopback, wrong Host, missing login, and denied login fail before the handler. |
| Origin negative | Missing, multiple, null, malformed, path-bearing, wrong scheme, wrong host, wrong port, or non-loopback local Host fails before the handler. |
| Input negative | Missing/non-JSON mutation Content-Type and invalid mapping patterns fail before the handler. |
| Injection | Capability headers/query/body and forwarded host/proto headers cannot alter the mapped capability or expected origin. |
| Replay/no change | Every rejected request invokes no handler and persists no state. |
| Compatibility | Existing web routes, page uploads, local/Tailscale identity, MCP surface, and full suite remain unchanged. |

## Verification commands

- Focused authorization tests filtered to `PrivateOperatorAuthorizationTests`.
- Focused web tests filtered to `WebInterfaceTests`.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- Full solution tests.
- Existing protocol/manifest-guard tests only if shared registration/surface checks require them; no
  MCP protocol walk is expected because the MCP surface and host composition do not change.
- `git diff --check` on Slice 0 files.

## Completion receipt and exit gate

Delivered behavior and the unrelated full-solution verification exceptions are recorded in
[`WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md`](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md). Slice 1 is the next
ready leaf; it remains unimplemented.
