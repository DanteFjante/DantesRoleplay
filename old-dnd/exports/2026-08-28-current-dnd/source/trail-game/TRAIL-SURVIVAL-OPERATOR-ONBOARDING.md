# Trail Survival operator onboarding

Status: **reviewed sequence; do not apply to a normal database without an explicit installation boundary**
Application: `trail-survival`
Source: `trail-survival-core`
Owner: [TG1 Slice 2](TG1-SLICE-2-IMPLEMENTATION.md)

## Purpose and boundary

This is the exact private-operator sequence for registering the authored Trail Survival source,
activating its current revision, and creating one empty state space through the existing three MCP
verbs. It creates application administration/state-space records in the connected database. It does
not import a scenario, create game entities, publish a player catalog, or enable automatic startup
registration.

Use a disposable database while developing. Applying this sequence to the normal host is a later
explicit synchronization/install decision.

## Host prerequisites

- The host exposes exactly `orient`, `query`, and `commit`.
- The private operator is authenticated for system modification.
- Host configuration maps the opaque allowed-root ID `repository` to this repository root.
- The authored file
  `catalog/applications/trail-survival/procedures/application/procedure.trail-survival.about.md`
  exists and has been reviewed.

The protocol never receives the absolute repository path.

## Sequence

Use a new 32-character lowercase hexadecimal `requestToken` for each distinct mutation. For each
mutation, submit the exact same payload first with `dryRun: true`, then without `dryRun`. Do not edit
the payload between those two calls.

### 1. Inspect capabilities

Call `query` with:

```json
{ "kind": "capabilities" }
```

Confirm the existing application/source/activation/state-space kinds are present. No Trail
Survival-specific system kind is expected.

### 2. Register the application

The `commit` arguments are:

```json
{
  "kind": "system.application.register",
  "payload": "{\"requestToken\":\"<application-token>\",\"applicationId\":\"trail-survival\",\"displayName\":\"Trail Survival\",\"description\":\"Original customizable single-player trail-survival application.\",\"baseApplications\":[],\"expectedFingerprint\":null}",
  "dryRun": true,
  "intent": "Validate the Trail Survival application registration.",
  "proceduresUsed": ["procedure.system.use"]
}
```

Repeat the exact call with `dryRun` omitted or false.

### 3. Register the authored source

```json
{
  "kind": "system.source.register",
  "payload": "{\"requestToken\":\"<source-token>\",\"applicationId\":\"trail-survival\",\"sourceId\":\"trail-survival-core\",\"allowedRootId\":\"repository\",\"relativePathOrGlob\":\"catalog/applications/trail-survival/**/*\",\"trust\":\"trusted\",\"precedence\":0,\"logicalIdentity\":\"trail-survival-core-catalog\",\"expectedFingerprint\":null}",
  "dryRun": true,
  "intent": "Validate the Trail Survival authored source registration.",
  "proceduresUsed": ["procedure.system.use"]
}
```

Repeat the exact call with `dryRun` omitted or false.

### 4. Preview the application

Call `query` with:

```json
{
  "kind": "system.application-preview",
  "applicationId": "trail-survival",
  "limit": 10
}
```

Require `isValid: true`, zero problems, and exactly one winner at the authored procedure path. Copy
the returned `previewFingerprint`; do not calculate or type a replacement value.

### 5. Activate the exact preview

```json
{
  "kind": "system.application.activate",
  "payload": "{\"requestToken\":\"<activation-token>\",\"applicationId\":\"trail-survival\",\"previewFingerprint\":\"<server-preview-fingerprint>\",\"expectedActiveFingerprint\":null}",
  "dryRun": true,
  "intent": "Validate the exact Trail Survival source activation.",
  "proceduresUsed": ["procedure.system.use"]
}
```

Repeat the exact call with `dryRun` omitted or false. Retain the returned
`activation.activationFingerprint`. Repeating the same commit is an idempotent replay and must
return the original operation/result.

### 6. Create an empty state space

Choose a bounded state-space ID. The development proof uses `trail-survival-onboarding`:

```json
{
  "kind": "system.state-space.create",
  "payload": "{\"requestToken\":\"<state-space-token>\",\"stateSpaceId\":\"trail-survival-onboarding\",\"applicationId\":\"trail-survival\",\"activeFingerprint\":\"<server-active-fingerprint>\",\"expectedFingerprint\":null}",
  "dryRun": true,
  "intent": "Validate an empty Trail Survival state space.",
  "proceduresUsed": ["procedure.system.use"]
}
```

Repeat the exact call with `dryRun` omitted or false. Repeating the same commit must return the
original operation/result and must not create a second binding.

### 7. Read back evidence

Use authenticated `query` calls:

```json
{ "kind": "system.applications", "applicationId": "trail-survival" }
```

```json
{ "kind": "system.sources", "applicationId": "trail-survival", "id": "trail-survival-core" }
```

The application result must name the active fingerprint and the one empty state space. Source
results must retain the opaque `repository` root ID and safe relative glob without revealing an
absolute path.

## Recovery

- `DRY_RUN_REQUIRED`: dry-run the exact payload, then retry it unchanged.
- `PREVIEW_STALE` or `DRY_RUN_STALE`: query a fresh preview and repeat the activation dry run.
- `ACTIVATION_STALE`: query current application evidence and decide explicitly whether to activate
  against that current fingerprint.
- `SOURCE_ROOT_UNKNOWN`: correct host configuration; never replace the root ID with an absolute path
  in protocol input.
- `REQUEST_TOKEN_CONFLICT`: use a new token only for a genuinely different mutation. An exact retry
  must keep its original token.
- `STATE_SPACE_EXISTS`: read the existing binding; do not overwrite or recreate it.

## Stop point

An empty bound state space is the end of onboarding. Do not create entities or component types until
the TG2 domain model and its permanent IDs/schema meanings are confirmed.

