# Web control center Slice 14 implementation — changed-content preview repair

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [control-center dependency plan, Slice 14](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md#slice-14-packet--site-editor-draft-preview-repair)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Let the Site Editor show the currently edited HTML in the existing isolated preview by explicitly appending an inactive draft first.  
Exclusions: `srcdoc` preview, mutation of old revisions, automatic publication, new API routes, migrations, asset editing, external connections, and authority changes.  
Allowed files/areas: control-center page bundle, its focused web tests, and Feature 2 plan/roadmap/receipt.  
Stop point: Save-and-preview creates one inactive revision and frames that returned exact revision; existing saved-revision preview remains available.

## Confirmed decisions

- The user reported that preview does not work for changed current pages.
- Previewing unsaved text uses the already-confirmed append-only draft owner rather than introducing a less isolated client-side document preview.
- The action label makes persistence explicit: previewing changed content saves an inactive draft and does not publish it.

## Prerequisite evidence

- [Slice 4 implementation](WEB-CONTROL-CENTER-SLICE-4-IMPLEMENTATION.md): immutable draft append, exact-revision isolated preview, and no active-pointer move.
- Current `SiteEditorPanel`: its Preview button points at the selected saved revision and ignores textarea content.

## Authoritative state and behavior

The browser supplies edited bounded HTML and existing optimistic tokens through the existing draft endpoint. `IWebPageStore` remains authoritative for the next immutable revision, copied assets, and active pointer. On explicit Save & preview, append the inactive draft, read the returned revision, then frame its existing preview URL with `sandbox="allow-scripts"`. Normal saved-revision preview remains a read-only action.

## Failure and no-change contract

Draft validation, stale revision, authorization, and persistence failures retain existing error behavior and open no preview. Success never changes the active pointer. The preview continues to use the existing no-store CSP and opaque iframe, and no request can preview arbitrary HTML without first passing the owner draft bounds.

## Acceptance and verification

- Source/UI tests prove the action uses the returned draft revision, labels persistence clearly, and retains the opaque iframe sandbox.
- Existing draft/pointer/preview route tests continue to pass.
- Run focused `WebInterfaceTests`, build the MCP host, live HTTP/source check, `git diff --check`, and record a receipt.

## Completion receipt and exit gate

Write `WEB-CONTROL-CENTER-SLICE-14-RECEIPT.md`, update plan status, and stop. No broader site-editor capability is included.
