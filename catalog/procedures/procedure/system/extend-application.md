---
id: procedure.system.extend-application
category: system
name: Extend an application with non-core content
governs: catalog/extensions/**; system.extension.register; system.application.activate with extensionIds; extension-owned namespaces; effective application content
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
How to add homebrew, third-party or compatibility content to an application that already exists,
so it is usable at play time and never mistaken for the core ruleset it extends. Written after
adding a homebrew species to `dnd2024`, where four of the five things that cost time were
invisible from the surface and only appear once you read the registered schemas.

## Instructions
1. Use the extension seam that already exists; do not invent a source or ID convention. An
   application that supports extensions has `catalog/extensions/<applicationId>/` with an
   `extension-package.schema.json`. Author `catalog/extensions/<app>/<name>/extension-package.json`
   against it. The schema maps directly to runtime registration: a runtime-safe `extensionId`,
   display metadata and classification, `sourceIds`, contributed `namespaceIds`, dependencies,
   conflicts, precedence edges, and whether it overrides the base application.
2. Put every extension record under one of its registered namespace roots, such as
   `dnd2024.extension.caldris.*`. The resolver strips the owning root and compares the remaining
   suffix together with record kind. Use the same suffix as a base record only for a deliberate
   override; use a unique suffix for additive content. Provenance also remains in the exact
   `sourceRef.sourceId`.
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
6. Register each declared source, then register the extension with
   `commit(kind: "system.extension.register")` using the exact package fields. Source precedence
   resolves duplicate files; extension precedence resolves logical catalog identities, so do not
   use source precedence as an extension override mechanism.
7. Preview, then activate the selected extension IDs. `query(kind: "system.application-preview", applicationId: "...")` —
   pass a small `limit`, the response carries every winner otherwise — and check `isValid`,
   `problems`, `shadows`, and that `winners` rose by exactly the number of records you authored.
   Then `commit(kind: "system.application.activate")` with that exact `previewFingerprint`, the
   current `activationFingerprint` as `expectedActiveFingerprint`, and the reviewed closed
   extension set. The host derives its effective sources and resolution fingerprint.
8. Project the content into the live state space with `commit(kind: "system.world-state.sync")`,
   containing it where its peers are contained. Activation publishes to the catalog; it does not
   put entities in a state space, and the two are easy to confuse.
9. Know the difference between a DEFINITION and an INSTANCE, and create both when a character owns
   something. A definition says what a kind of thing IS — `dnd2024.item-definition` on
   `dnd2024.item.<name>.v1`, contained with its peers under the world root. An instance says a
   particular one EXISTS and who holds it: its own entity carrying `dnd2024.core.definition-link`
   (plus `dnd2024.item.quantity` for items), contained under its owner in the `carried` slot, with
   an owner-scoped id such as `item.caldris.ganji.quarterstaff`. A definition on its own puts a
   thing in the world's catalogue and gives it to nobody.
10. To edit an entity that is NOT contained beneath the world root, pass that entity's own id as
   `rootEntityId`. The root only has to exist, and an entity is trivially inside itself, so this
   scopes the manifest to exactly that entity. It does not let you place the entity: a containment
   target must itself be inside the selected root.
11. Verify through catalog browse, effective application content, and exact record inspection,
    never from the database file. Confirm the extension badge and provenance, confirm overrides
    win by default, and confirm additive records remain visible in effective content.

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
- Ordinary search, website, MCP, and AI callers never select an extension or overlay. They use the
  host-bound effective application context. `includeShadowed` is an operator diagnostic only.
- A definition is not ownership. Never report that a character has something until an instance of it
  exists beneath them.
