# Web Interface Feature 2 Slice 0 receipt — control authorization and API conventions

Status: **accepted with recorded unrelated repository build/test exceptions**  
Accepted boundary: [Slice 0 implementation document](WEB-CONTROL-CENTER-SLICE-0-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added the confirmed private-operator capabilities `control.read`, `control.pages.write`,
  `control.settings.write`, `control.ai.message`, and `control.codex.approve`, with exact safe audit
  names and fail-closed handling for unknown values. Existing `read` and `modify` evidence is
  unchanged.
- Extended the existing trusted web operator guard so a server-mapped endpoint selects one exact
  capability. Browser headers, query parameters, and bodies cannot select it.
- Added closed GET/POST/PUT mapping helpers that can create routes only below `/api/control`, attach
  the exact capability and existing rate limiter, and reject invalid patterns/capabilities at
  startup.
- Added a control request guard/filter that applies existing security headers, authenticates the
  local or exact allowed Tailscale operator, requires JSON for changes, and rejects wrong Host or
  Origin before handler invocation.
- Local mutations accept only `localhost` or loopback IP Hosts with exact request Origin.
  Tailscale mutations accept only the already-authorized Host with exact HTTPS Origin, independent
  of the loopback backend scheme.
- Reserved `/api/control` in the remote web-only route boundary and updated component ownership and
  usage documentation.

No control route, page, panel, database record, migration, setting, conversation, model call, Codex
process, catalog item, MCP kind, or game-state write was added.

## Verification evidence

- Web project build: **passed**, 0 warnings and 0 errors.
- Initial focused authorization/web run with normal project-reference builds: **51 passed** before
  the final accepted-handler test was added.
- Final focused authorization/web run with project-reference rebuilding disabled: **52 passed**.
  The web/core projects had been rebuilt from the final Slice 0 sources immediately beforehand.
- An initial full shared test-assembly run with project-reference rebuilding disabled passed
  **575 tests**, 0 failed, 0 skipped. After additional unrelated tests/registration changes appeared
  in the moving worktree, the final no-build rerun passed **578 of 579** tests. The one failure is
  `SystemCatalogProtocolTests.Empty_provider_discloses_nothing_and_capabilities_publish_only_read_kinds`:
  its read-only expectation now sees the separately added `system.application.register` and
  `system.source.register` write kinds. Slice 0 changes neither those kinds nor MCP composition.
- Slice-file `git diff --check`: **no whitespace errors**; only existing LF-to-CRLF working-copy
  warnings were reported.
- No MCP protocol walk was run because Slice 0 changes neither host composition nor MCP surface.

The final normal full-solution build was attempted and stopped outside this slice at
`src/system/application-registry/persistence/RegistryAdministrationService.cs:16`: the concurrently
changed file does not resolve `DantesRoleplayDbContext` while compiling `DantesRoleplay.DataAccess`.
That file was present in the dirty worktree, is outside the Slice 0 allowed areas, and was not
modified here. The independent web project build and focused test run separate Slice 0 evidence
from these repository-wide moving-worktree exceptions.

## Deliberate exclusions and next leaf

- Existing `/api/pages/*` uploads retain their earlier security and activation behavior.
- Capability grants remain the closed single-operator local/exact-Tailscale policy; there is no
  user/role database.
- The control-center page, health projection, panels, and every domain read/write remain excluded.
- Feature 2 Slice 1, the read-only shell and health/status presentation, is the next ready leaf and
  is assigned to Terra at medium reasoning. It still requires its own active implementation
  document before code changes.
