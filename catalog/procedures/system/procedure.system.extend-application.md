---
id: procedure.system.extend-application
category: system
name: Extend an application with non-core content
governs: catalog/extensions/**, commit(kind: "system.component-type.register"), commit(kind: "system.source.register"), commit(kind: "system.application.activate"), adding homebrew or third-party content to an existing application
status: active
---

## Description
How to add homebrew, third-party or compatibility content to an application that already exists,
so it is usable at play time and never mistaken for the core ruleset it extends. Written after
adding a homebrew species to `dnd2024`, where four of the five things that cost time were
invisible from the surface and only appear once you read the registered schemas.

## Instructions
1. Use the extension seam that already exists; do not invent a source or id convention. An
   application that supports extensions has `catalog/extensions/<applicationId>/` with an
   `extension-package.schema.json`. Author `catalog/extensions/<app>/<name>/extension-package.json`
   against it: `classification` is one of `compatibility`, `homebrew`, `third-party`, and that
   field plus `sourceId` is how the content stays distinguishable. Read an existing package first
   and copy its shape.
2. Give extension records CORE-STYLE ids. Provenance belongs in `sourceRef.sourceId`
   (`<app>-extension.<name>`) and in the record's display name, not in the entity id. An id like
   `...species.half-elf.homebrew-v1` fights every convention around it and buys nothing that
   the source id does not already say.
3. BEFORE authoring anything, read the registered schema of every component the content needs:
   `query(kind: "capabilities")` names the types, and the registered schema is what
   `system.world-state.sync` validates against — the catalog `.schema.json` file is the authored
   copy, not the authority. Expect `sourceRef.sourceId` to be pinned with
   `{"const": "<core source>"}` on content types that were written when only core content existed.
   A const there makes extension provenance literally unrepresentable, and no amount of correct
   authoring gets past it.
4. If a schema must be relaxed, PROVE the relaxation first and register the widening second.
   Extract every existing record of that type — catalog files and live components both — and
   validate them against the proposed schema. A widening that every existing record still
   satisfies is a strict relaxation and is safe; anything else is a redesign and needs its own
   decision. Then `commit(kind: "system.component-type.register")` with the current hash as
   `expectedSchemaHash`; the host derives version, profile and hash itself, so a minified payload
   is fine.
5. Plan the RETYPE in the same pass as the widening. Registering a new type version does not
   migrate anything: the synchronizer always writes the newest registered version, and a stored
   component cannot change its type contract in place, so every component still stored at the old
   version becomes un-updatable the moment the new version is registered. Count the affected rows
   before you register, and know how they will be moved to the new version.
6. Register the content as its own source: `commit(kind: "system.source.register")` with
   `relativePathOrGlob` covering the package's `content/` subtree ONLY, a distinct
   `logicalIdentity`, and `precedence: 0` so it cannot shadow core. Pointing the glob at the whole
   package sweeps the manifest into the scanned document set, which is not catalog data.
7. Preview, then activate. `query(kind: "system.application-preview", applicationId: "...")` —
   pass a small `limit`, the response carries every winner otherwise — and check `isValid`,
   `problems`, `shadows`, and that `winners` rose by exactly the number of records you authored.
   Then `commit(kind: "system.application.activate")` with that exact `previewFingerprint`, the
   current `activationFingerprint` as `expectedActiveFingerprint`, and every source id including
   the new one.
8. Project the content into the live state space with `commit(kind: "system.world-state.sync")`,
   containing it where its peers are contained. Activation publishes to the catalog; it does not
   put entities in a state space, and the two are easy to confuse.
9. To edit an entity that is NOT contained beneath the world root, pass that entity's own id as
   `rootEntityId`. The root only has to exist, and an entity is trivially inside itself, so this
   scopes the manifest to exactly that entity. It does not let you place the entity: a containment
   target must itself be inside the selected root.
10. Verify by reading back through `query`, never from the database file. Confirm the entity, and
    confirm the catalog record carries the extension `sourceId` and its path.

## Constraints
- Never author extension content under the core source's glob. Whatever lands there IS core,
  regardless of what its `sourceRef` claims.
- Never register a new component type version without knowing which stored components it freezes
  and how they will be retyped. This is the single most expensive mistake in this procedure.
- Never widen a schema without validating every existing record of that type against it first.
- Never commit a payload that differs from the one that passed its dry run, and never skip the dry
  run on a kind that supports it.
- Provenance is not decoration: a record that cites the core source when it did not come from the
  core source is a false attribution, and the whole point of the extension seam is to prevent it.
- If extension content appears to require a change to a canonical contract, schema or procedure,
  that is a finding to report before it is work to start.
- Activation and state-space projection are separate steps. Neither implies the other.
