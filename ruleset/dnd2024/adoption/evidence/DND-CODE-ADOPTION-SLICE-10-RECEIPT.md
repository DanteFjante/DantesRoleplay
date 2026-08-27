# D&D code-adoption Parent 10 receipt — static SRD content breadth

Date: 2026-08-26
Status: **accepted**
Boundary: selected production-source-ready static content cohorts

## Accepted cohorts

- Five currency definitions.
- Nine schema-faithful adventuring-gear definitions.
- All thirteen SRD Armor-table entries, including Shield.
- Six reduced weapon profiles and four immutable weapon item links.
- Fighter levels 1–2 progression identity plus five referenced feature identities.
- One non-SRD hempen-rope definition only in the separately selected, disabled-by-default legacy
  equipment extension; it is not Parent 10 core content.

Each core cohort retains its exact SRD 5.2.1 locator, archived input hashes, deterministic target
transformation, attribution, schema validation, activated-source path, and existing-mechanic
consumption evidence. The user accepted all implemented remaining leaves on 2026-08-26.

## Parent closure

Spells, monsters, magic items, missing weapon/ammunition/tool IDs, tool representation, and Quiver
have explicit defer gates. They are not counted as delivered. Complex behavior belongs to later
feature families. Static records remain activated application-source content and are not
automatically installed into existing campaign state.

## Current acceptance evidence

- D&D plus optional packaging: 85/85 passed.
- Shared suite: 1,106/1,106 passed; local-AI: 21/21 passed.
- Release build: 0 warnings, 0 errors.
- Catalog validation: 144 valid core records and 21 unchanged advisories; no live data touched.
- `git diff --check`: passed with existing line-ending notices only.

Parent 10 is complete for its selected static-content scope.
