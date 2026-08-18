# Hierarchical catalog categories

## Goal

Make procedures and mechanics browsable through stable category paths while retaining the
current free-text intent search. A client should be able to list one or more category branches,
optionally include every descendant, and combine that filter with an intent or name search.

This is a navigation feature. It does not add game rules, alter action selection, or change world
state semantics.

## Category model

- Keep one primary category per procedure or mechanic, using a dot-delimited path such as
  `ruleset.dnd2024.gameplay.checks.ability`.
- Treat each path segment as a catalog level. A category tree is derived from stored paths; no
  separate category table or migration is needed for the first release.
- Preserve existing flat category values as root-level categories. New paths must use lowercase
  segments containing letters, digits, and hyphens, with no leading, trailing, or repeated dots.
- Do not use categories for ruleset isolation: mechanics continue to use `scope` for that purpose.
  For example, a D&D ability-check rule can have category
  `ruleset.dnd2024.gameplay.checks.ability` and scope `dnd2024-srd-5.2.1`.
- Keep one category path per item. If an item eventually needs multiple independent classifications,
  add a separate tag feature rather than making the category tree ambiguous.

## D&D taxonomy to adopt now

The current category field already accepts strings, so new D&D procedures and mechanics can use
the path convention before recursive queries exist. Until that feature lands, clients use an exact
category match or text search; they must not assume that prefix matching works.

Use this root and these branches:

```text
ruleset.dnd2024.governance
ruleset.dnd2024.play
ruleset.dnd2024.host
ruleset.dnd2024.data.<component>
ruleset.dnd2024.gameplay.<area>.<rule>
ruleset.dnd2024.combat.<area>.<rule>
ruleset.dnd2024.magic.<area>.<rule>
ruleset.dnd2024.advancement.<area>.<rule>
ruleset.dnd2024.content.<area>.<rule>
```

- Use a category for the rule or contract's primary purpose, not its source version. The separate
  mechanic scope remains `dnd2024-srd-5.2.1`.
- Use lowercase, hyphen-delimited words inside dot-delimited path segments. For example,
  `ruleset.dnd2024.gameplay.ability-checks.fixed-dc`.
- Do not create placeholder leaves. A category path first appears when its first real contract or
  mechanic is committed.
- The initial contract mapping is `procedure.mechanic.dnd2024.ruleset` →
  `ruleset.dnd2024.governance` and `procedure.mechanic.dnd2024.play` →
  `ruleset.dnd2024.play`. The later host contract uses `ruleset.dnd2024.host`.

## MCP interface changes

Extend `query(kind: "procedures")` and `query(kind: "mechanics")` with:

```json
{
  "categories": ["ruleset.dnd2024.play", "ruleset.dnd2024.gameplay"],
  "recursive": true,
  "query": "stealth"
}
```

- `category` remains supported as the existing exact, single-category filter.
- `categories` is an OR filter: a record matches any requested path.
- With `recursive: false` (the default), a path matches only itself.
- With `recursive: true`, a path also matches descendants whose category starts with that path plus
  a dot. `ruleset.dnd2024.play` does not match `ruleset.dnd2024.player`.
- When both `category` and `categories` are supplied, combine them as one OR category filter.
- Combine category filtering with existing text search as AND: a returned item must be in the
  requested branch and match the existing ID/name/description/intent search.
- Add `query(kind: "categories")` as a read-only catalog operation. It accepts `catalog`
  (`procedures` or `mechanics`), optional `prefix`, and optional `recursive`; it returns category
  paths, direct-child paths, and item counts. This is the client-facing category browser.
- `commit(kind: "procedure")` and `commit(kind: "mechanic")` continue to use their existing
  `category` field, now validated as either a legacy flat category or a valid hierarchical path.
  Do not add category fields to `effects` or `action`; those are operations, not catalog entries.

## Implementation sequence

1. Adopt the D&D taxonomy immediately by revising its existing procedure categories and requiring
   every future D&D contract or mechanic to select one leaf path.
2. Create the feature contract `procedure.system.hierarchical-catalogs`, covering only this API
   and retrieval behavior. Dry-run and verify it before code changes.
3. Introduce a shared category-path parser and matcher, with exact and descendant matching.
4. Extend procedure and mechanic stores to accept multiple category paths in reads, preserving the
   existing single `category` behavior.
5. Add the `categories` query kind, include it in the closed query-kind registry and MCP tool
   schema, and update capability descriptions.
6. Add unit and protocol tests, then update `procedure.mcp.add-tool`, `procedure.system.inspect`,
   `orient`, and relevant D&D contracts to describe the new catalog behavior.
7. Gradually move new D&D contracts and mechanics to the `ruleset.dnd2024.*` hierarchy; do not
   rewrite unrelated historical categories.

## Acceptance tests

- Exact category lookup returns only items in the requested category.
- Recursive lookup returns the requested node and descendants, but no similarly prefixed sibling.
- Multiple requested categories return their union without duplicates.
- Text and category filtering combine as an intersection.
- Empty and malformed category paths are rejected on commit with a corrective payload.
- Existing flat categories and single `category` queries behave exactly as they do today.
- `query(kind: "categories")` returns stable child nodes and correct direct/recursive counts.
- MCP protocol tests prove that all new arguments and the new query kind are discoverable through
  `query(kind: "capabilities")`.

## Scope boundaries

This is deliberately after the current MVP. It should not be implemented until the team has
evidence that flat category or intent search makes a real ruleset difficult to navigate.
