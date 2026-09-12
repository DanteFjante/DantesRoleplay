---
id: procedure.system.web-composition
category: system
name: Author and operate composed web pages
governs: retained web page content, composition-v1 components, exact query and action bindings, publication, serving, progress presentation, and recovery
status: active
createdBy: "system"
changeNote: "Documents the implemented composition and publication boundary."
---

## Description
The website stores versioned page content and assets while the application publication state space
stores the exact selected content reference. `composition-v1` is a closed page-local JSON format.
Component IDs and revisions exist only inside one document; they are not a global component registry.

The MCP operating manual can discover this contract and the application candidate/query/action
contracts. Page bundle retention and operator publication use the existing authenticated web
administration routes. There is no separate MCP page-write verb.

## Matches
author a composed website page
bind a page to an application query
add an application action button
publish or recover a web page revision
show durable worker progress on a page
theme a website page

## Instructions
### Composition input
A retained bundle contains exactly one root `composition.json` or legacy `index.html`, plus optional
assets. The composition document has `formatVersion: 1`, an authored `generation` label, up to 16
queries, up to 16 actions, page-local component definitions, and one root node. The generation label
is not an activation proof; publication pins retained bytes, content hash, revision, and asset
inventory fingerprint.

```json
{"formatVersion":1,"generation":"records-v1","queries":[{"name":"records","query":"example.query.records","input":{}}],"actions":[{"name":"refresh","mechanic":"example.mechanic.refresh"}],"components":[{"id":"refresh-button","revision":"1","template":{"kind":"element","tag":"button","action":"refresh","children":[{"kind":"text","text":"Refresh"}]}}],"root":{"kind":"element","tag":"main","children":[{"kind":"each","items":"records","as":"record","children":[{"kind":"value","path":"record.name"}]},{"kind":"component","id":"refresh-button","revision":"1"}]}}
```

The renderer escapes text and values and supports allowlisted elements, conditions, loops, required
props, and caller-scoped slots. It rejects unknown fields, duplicate IDs, reference cycles, unsafe
URLs, undeclared bindings, missing query data, and output beyond its declared bounds.

### Query and action bindings
Query names are local aliases. The server resolves each qualified query to its exact current
projection/version/content hash and canonical schemas, then executes it with the audience's current
state-space Read authority. The reviewed authoring path updates an existing query only when the
canonical contract remains unchanged and the projection change is compatible; it does not create an
arbitrary new query.

Pure actions use their application-scoped path. A stateful page action supports zero or one simple
root role, bound by the server to the selected route entity, and executes with the Atomic profile.
Multiple roles, object/snapshot roles, graph snapshots, child mechanics, authorized context, event
requirements, and service calls are unsupported from a page. Browser JSON never selects principal,
grant, application generation, state revision, command authority, budgets, or deadlines.

### Publication and serving
Retain a draft first, validate its exact candidate and binding contracts, then publish the selected
revision through the existing web and application-publication owners. Serving reselects the live ECS
publication pin, rechecks Application-scoped page Read authority, and uses the immutable asset base
`/ui/{slug}/content/{contentPageId}/revisions/{revision}/`. Audience queries separately require the
exact StateSpace Read grant.

Invocation presentation preserves `tag`, `code`, data/read evidence, proposal, Pending task handle,
current receipt, prior commits, completion evidence, and recovery identity. A commit refreshes
authoritative data. Pending remains pending. An uncertain action stays fenced until its original
receipt is reconciled; the browser does not retry it automatically.

### Recovery
If candidate validation or publication fails, the previous ECS pin and its exact retained assets stay
selected; the newer draft remains inert. If the web compatibility pointer disagrees, inspect both
revisions and reconcile explicitly. A transaction does not span the content and ECS databases.
Wrong-audience or revoked-grant reads return no page content and do not broaden authority.

Theme authoring has no published capability contract. The shared presentation-only browser asset
`/components/system-theme.js` provides `initializeSystemTheme`, `getSystemTheme`, `setSystemTheme`,
`onSystemThemeChange`, and `SystemThemeToggle`. It persists the key
`dantes.system-theme.v1` with `green-wood` (`Green & Wood`), `system`, `light`, or `dark`. Green & Wood
is the default when storage is absent, invalid, cleared, or unavailable and resolves to the browser's
dark color scheme; explicit system, light, and dark preferences retain their existing behavior. The
asset reflects selection through
`documentElement.dataset.systemTheme`, binds a native select marked `system-theme-toggle`, and emits
the `system-theme-change` window event with `{preference, resolvedTheme}`. Its semantic CSS variables include
`--system-color-canvas`, `--system-color-surface`, `--system-color-surface-raised`,
`--system-color-text`, `--system-color-muted`, `--system-color-border`, `--system-color-accent`,
`--system-color-accent-contrast`, and `--system-color-danger`. This seam is presentation only and
grants no authority. Legacy surfaces with hard-coded dark colors still require migration to these
tokens before every control follows the selected theme. Treat CSS/assets as ordinary retained content;
do not invent a theme tool, runtime-authored payload, or receipt. Source availability does not mean
the asset is published to a live website.

## Constraints
- Composition JSON and each source/canonical document are bounded; exact limits come from the current
  retained page contract. Loops and expanded render nodes are also bounded.
- The old mutable active-asset route is not generation-pinned; composed pages use exact revision assets.
- Page selection evidence is not a permission token. The web-page standing grant owner rechecks the
  retained resource on every use.
- No application-specific or game-specific page behavior belongs in the generic host.
