---
id: procedure.system.hierarchical-catalogs
category: system
name: Browse hierarchical catalogs
governs: query(kind: "categories"), query(kind: "procedures", category: "..."), query(kind: "mechanics", category: "...")
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
How to navigate procedure contracts and mechanics through their stable primary category paths.
Categories are derived from stored record paths; they are navigation metadata, not world state,
ruleset identity, or an action-selection mechanism.

## Matches

## Instructions
1. Start at one catalog root with query(kind: "categories", catalog: "procedures") or
   query(kind: "categories", catalog: "mechanics").
2. Open one returned child with the same call and its full category path. The response contains
   only direct children of that branch, so repeat this step until the branch is specific enough.
3. Read direct as records exactly on the opened path and subtree as records on that path or
   anywhere below it. A branch may have direct: 0 and still contain useful descendants.
4. List the records below a branch with query(kind: "procedures", category: "...") or
   query(kind: "mechanics", category: "..."). A category filter always includes the named node
   and descendants; combine it with query text to narrow the result further.
5. For mechanics, use scope separately when selecting a ruleset preference. Category describes
   primary purpose; scope identifies the ruleset or campaign context.
6. Use includeInactive: true only when historical archived records must contribute to category
   counts or record listings.

## Constraints
- A category is one lowercase dot-delimited path. Do not infer a taxonomy from similar names or
  widen matching by raw text prefix: ruleset.dnd2024.play never includes
  ruleset.dnd2024.player.
- Keep exactly one primary category per procedure or mechanic. Do not add placeholder category
  records, a category table, aliases, tags, or multiple categories through this navigation path.
- Categories do not isolate rulesets and do not select a mechanic for an action. Use mechanic
  scope and normal action retrieval for those responsibilities.
- query(kind: "categories") is read-only. It never creates categories or changes catalog, world,
  or operation state beyond the ordinary read audit.
