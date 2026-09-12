---
id: procedure.system.application-lifecycle
category: system
name: Operate application definitions and state spaces
governs: registered applications, sources, component schemas, activation generations, state spaces, dependency inspection, readiness, and recoverable cache refresh
status: active
createdBy: "system"
changeNote: "Documents the implemented application lifecycle and recovery boundary."
---

## Description
Use this contract after `orient()` identifies the application boundary. It joins the existing
application registry, source overlay, component-type registry, activation, state-space, dependency,
and readiness owners into one operating sequence. It creates no second registry and does not make
catalog files or cache entries authoritative.

## Matches
register or activate an application
author a component schema
inspect application readiness
upgrade or recover a state space
refresh application caches after activation

## Instructions
### Discover current state
1. Read `query(kind: "system.applications")`; select the exact application revision, active
   fingerprint, and state-space binding fingerprint returned there.
2. Read `system.sources` and `system.application-preview` before changing the source overlay. Preview
   resolves only registered host-relative sources; caller input never names an absolute path.
3. Read `system.dependencies` before changing a component or projection. Its coverage explicitly
   names indexed kinds and may be incomplete; never infer missing edges from JavaScript or filenames.
4. Read `system.application-readiness` after activation or recovery. Treat registration, active
   catalog, query, web-page, and audience checks as separate owner results.
5. Use `query(kind: "capabilities")` for the current closed schema of every capability below.

### Registered write sequence
The implemented lifecycle uses these existing capability IDs:

- `system.application.register` creates immutable application metadata.
- `system.source.register` records one configured-root-relative source; it neither creates nor scans
  files.
- `system.extension.register` records reviewed extension membership, namespaces, precedence,
  dependencies, and conflicts.
- `system.component-type.register` registers one immutable versioned qualified component schema.
- `system.application.activate` selects an exact valid preview fingerprint.
- `system.state-space.create` creates one empty runtime or application-publication space pinned to an
  exact active fingerprint.
- `system.state-space.upgrade` rebinds a compatible space to the current activation.
- `system.state-space.adopt-legacy` copies a complete or explicitly closed legacy graph into a new
  state space with exact mappings; the source remains unchanged.

Each is a current registered capability invoked through `commit`, requires the host's authenticated
operator context, confirmation, and a 32-character lowercase hexadecimal request token. Caller JSON
does not carry a principal or grant.

### Example inputs
Register a component schema only after the application exists:

```json
{"requestToken":"0123456789abcdef0123456789abcdef","applicationId":"example","qualifiedTypeId":"example.note","schemaJson":"{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"text\"],\"properties\":{\"text\":{\"type\":\"string\"}}}","expectedSchemaHash":null}
```

Create an empty runtime state space from an exact activation:

```json
{"requestToken":"1123456789abcdef0123456789abcdef","stateSpaceId":"example-space","applicationId":"example","scope":"runtime-state-space","activeFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","expectedFingerprint":null}
```

Upgrade only with the current binding fingerprint:

```json
{"requestToken":"2123456789abcdef0123456789abcdef","stateSpaceId":"example-space","applicationId":"example","activeFingerprint":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB","expectedBindingFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}
```

### Results, freshness, and recovery
Dry-run the identical payload where the descriptor supports dry run, read every preflight check, then
commit with the same input. Success returns an owner operation receipt or registered result; read back
the application, schema, state space, or readiness owner before reporting completion. Equal request
replay is safe; changed input under one token conflicts.

Activation publishes one immutable generation. New invocations resolve the new exact generation;
in-flight work remains pinned. Catalog snapshots, prepared programs, and retrieval indexes are derived
and rebuildable. If refresh is missing or stale, keep the authoritative activation, use lexical/manual
fallback where offered, and rebuild the derived generation. Never roll back the active pointer merely
to repair a cache.

An incompatible populated state space returns `MIGRATION_REQUIRED`; it does not accept caller-supplied
conversion. Preserve the existing state and use a separately reviewed migration or a new state space.
An interrupted activation or stale preflight requires a fresh preview and current fingerprints. Do
not reuse a request token for a changed plan.

## Constraints
- Catalog files are authored development inputs; SQLite is authoritative for a running installation.
- Schema registration versions contracts. It does not convert existing data or invent a query.
- Activation does not import source files, execute JavaScript, or prove every dependency kind is indexed.
- Never claim a source-integrated migration has been applied to a live database without deployment
  evidence.
