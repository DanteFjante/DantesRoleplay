# Web Interface Feature 2 Slice 10 receipt — persistent application workspace

Status: **accepted**  
Accepted boundary: [Slice 10 implementation document](WEB-CONTROL-CENTER-SLICE-10-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Replaced the long scrolling control-center page with a persistent desktop sidebar and one main
  workspace. Narrow layouts retain a compact horizontally scrollable navigation band, visible
  focus, active-page state, and a non-expanding content column.
- Added the closed client routes `#/settings`, `#/effects`, `#/assistants`, `#/applications`,
  `#/applications/{encodedApplicationId}`, and `#/site-editor`. Empty, unknown, malformed,
  slash-bearing, control-character, or oversized application routes fall back to Settings without
  selecting an arbitrary server path.
- Preserved the five existing custom elements and their IDs exactly. Workspaces are hidden rather
  than reconstructed, so switching functions retains their local UI state and all existing API,
  authorization, settings, effects, assistant/Codex approval, ECS/catalog, and editor contracts.
- Routed registered application choices into the existing `<ecs-explorer>`. Direct application
  hashes wait for bounded discovery and display application, state-space, entity, component,
  schema, and public-catalog evidence inside the main workspace while navigation remains mounted.

## Verification evidence

- Focused `WebInterfaceTests`: **65 passed**, 0 failed. New coverage asserts the persistent shell,
  five closed routes, one instance of each stable panel, active-page accessibility, mobile
  breakpoint, bounded application decoding, route-aware selection/list restoration, and the
  absence of application iframe or `postMessage` authority.
- Embedded script syntax validation: **passed**.
- Clean solution build: **passed**, 0 warnings and 0 errors.
- Full solution run: local AI **20/20** and shared tests **665/666**. The sole failure is the existing,
  unrelated `GuardTests.Both_dispatchers_name_every_kind_in_the_description_a_client_reads`, where
  concurrent MCP dispatcher work serves ten kinds not named in `GenericCommitTool.cs`. It does not
  exercise the page bundle, hash router, structure explorer, or this presentation refactor.
- `git diff --check`: no whitespace errors; working-copy line-ending warnings only.

## Deliberate exclusions

No application website was embedded or launched. There is no application-to-page mapping, iframe
or `postMessage` bridge, new HTTP route/capability/schema/record/migration, new frontend dependency,
backend write, catalog or game-state change, remote publish, or live model call. Browser automation
was not requested and was deliberately omitted; the responsive and navigation contracts are
covered by focused source assertions and existing endpoint tests.
