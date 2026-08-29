# Web Interface Feature 2 Slice 4 receipt — in-browser site editing

Status: **accepted**  
Accepted boundary: [Slice 4 implementation document](WEB-CONTROL-CENTER-SLICE-4-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Extended the web-page owner with stable bounded page/revision discovery, exact immutable revision
  reads, inactive draft append, and optimistic exact-revision activation. Drafts copy the selected
  revision's stored asset paths, media types, hashes, and bytes while changing only HTML; they do not
  move the active pointer or its update timestamp.
- Added `ControlPageEditor` and nine `/api/control/pages*` routes for summaries, revision detail,
  exact ZIP export, isolated preview, draft save, publish, and rollback. Read results are no-store;
  writes require `control.pages.write`, bounded JSON, same-origin Host/Origin checks, and expected
  latest/active revision tokens.
- Added preview responses that deny external connections and forms, frame only from the same site,
  and run inside an iframe with `sandbox="allow-scripts"` and no same-origin identity. Preview and
  export always address one immutable revision and never activate it.
- Replaced the Site editor placeholder with existing-page selection, immutable history, exact HTML
  editing, inactive save, preview, ZIP download, explicit publish, and older-revision rollback.
  The panel states the direct HTML/bundle upload recovery path before allowing self-editing of
  `control-center`.
- Preserved the existing direct upload routes and page schema. Direct HTML validation now enforces
  the same 1 MiB HTML ceiling already used by bundle and draft inputs.

No page/revision/asset deletion or mutation, new-page editor, asset editor, migration, filesystem
editor, settings, assistant/Codex bridge, MCP surface, catalog/game-state write, or ruleset-specific
behavior was added.

## Verification evidence

- Focused web-interface tests: **53 passed**, 0 failed. They cover exact asset copying without
  activation, stable bounded discovery, stale and replay conflicts, exact publish/rollback,
  already-active rejection, injected transaction failures, revision JSON/ZIP projection, preview
  headers, route metadata, recovery compatibility, and the Site editor source boundary.
- Solution build: **passed**, 0 warnings and 0 errors.
- Full solution tests: **19/19** local-AI tests and **617/617** shared tests passed.
- Catalog validation: **passed**, 144 records validated; 17 existing near-duplicate warnings and no
  live-data change.
- Disposable local HTTP/browser walk:
  - uploaded the source `control-center` page to a disposable database;
  - selected its active revision, edited the HTML, and saved revision 2 through the browser panel;
  - confirmed revision 1 remained active while revision 2 was listed as draft/history;
  - previewed revision 2 in the sandbox and observed its exact `Browser draft` heading;
  - activated revision 2 and then reactivated revision 1 against the same local server with the
    required same-origin boundary, leaving revision 1 active; and
  - stopped the disposable host after the walkthrough.
- `git diff --check`: no whitespace errors; only existing line-ending warnings for tracked files.

The ordinary output directory was locked by the already-running local MCP server, so final build and
test commands used the repository's ignored `.tmp/slice4-artifacts` output tree. This avoided
interrupting the user's server while compiling and testing the same source graph.

## Deliberate exclusions and next gate

The editor intentionally manages existing pages and HTML only. Creating the first revision and
recovering a broken `control-center` remain direct HTML/ZIP upload operations; individual asset
editing remains excluded. Slice 5 is the next ordered control-center leaf and remains blocked on a
Sol handoff that confirms the host-owned setting-definition allowlist, sources, sensitivity,
mutability, restart metadata, and redacted response contract.
