# Web Interface Feature 2 Slice 1 receipt — read-only control-center shell

Status: **accepted with recorded unrelated repository test exception**  
Accepted boundary: [Slice 1 implementation document](WEB-CONTROL-CENTER-SLICE-1-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added `GET /api/control/status` through the Slice 0 `control.read` convention. It reports only the
  authenticated local/Tailscale access mode and a fixed ordered list of the five currently
  unavailable panels. It reads no database, configuration, world, provider, or Codex state.
- Added `Cache-Control: no-store` to the caller-specific status response.
- Added an uploadable one-file `control-center` bundle with the five confirmed browser-native custom
  elements, accessible panel navigation, independent loading/unavailable/forbidden/retry handling,
  and no build toolchain or external asset.
- Added the exact bundle-upload/open instructions to the web README. The host does not automatically
  seed a page into SQLite.
- Updated web ownership and Feature 2 planning/roadmap status.

No effects, ECS/contracts, settings, editor, conversation, local-model, Codex, page-write, database,
migration, catalog, or MCP behavior was introduced.

## Verification evidence

- Focused `WebInterfaceTests`: **44 passed**, 0 failed.
- Web project build: **passed**, 0 warnings and 0 errors.
- Full shared test assembly before the final normal rebuild: **582 passed**, 0 failed.
- Normal solution build: **passed**, 0 warnings and 0 errors.
- Disposable local HTTP/browser walk, using a temporary database on port 6220:
  - `/api/control/status` returned 200, `Cache-Control: no-store`, local access, and the exact five
    unavailable panel records.
  - The supplied bundle uploaded through the existing ZIP endpoint and remained revision-scoped.
  - A fresh browser load rendered all five panels, the navigation and local-boundary state, and no
    browser-console errors. The temporary host was stopped afterward.
- Slice-file `git diff --check`: no whitespace errors; existing working-copy line-ending warnings may
  remain.

The final normal full-suite run passed **583 of 584** tests. Its one failure is outside Slice 1:
`ApplicationPreviewTests.Preview_scans_registered_glob_is_deterministic_redacted_and_read_only`
compares two `ApplicationPreviewResult` values that display identically but are unequal at
`src/system/application-preview/tests/ApplicationPreviewTests.cs:39`. Slice 1 does not reference
application preview, sources, or equality behavior. The focused Slice 1 suite and normal solution
build separate its acceptance evidence from this repository-level test exception.

## Deliberate exclusions and next decision

- The control-center page source remains an operator-uploaded bundle. No live user page was modified
  or seeded by this slice.
- Every panel truthfully stays unavailable until its owning slice is confirmed and implemented.
- Slice 2 (past effects) is not active. It requires explicit confirmation that immutable accepted
  event-ledger entries—not a second effect log—are the authoritative view, along with its bounded
  filtering/detail contract.
