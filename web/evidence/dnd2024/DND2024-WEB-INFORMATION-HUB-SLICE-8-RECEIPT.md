# DND2024 web information hub Slice 8 receipt — Campaign pursuits and knowledge

Status: **accepted 2026-08-28**

Implementation document: `DND2024-WEB-INFORMATION-HUB-SLICE-8-IMPLEMENTATION.md`

Dependency leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 2F

Published source revision: `e91998886fd3ee7ef864bdc4921081f90aa8133e`

Private Sites version: 10

Deployment: <https://dantes-roleplay-dnd2024-table.dantecavallin.chatgpt.site/>

## Delivered

- Added independent Campaign **Quests**, **Open Threads**, and **Clues** views, including native filters/search, empty states, and overview links.
- Extended the closed Campaign read model and fixture source with authored continuity data and safe links back to the existing projected World data.
- Player projection returns only party-known content: 3 quests, 3 open threads, and 4 clues. DM projection additionally includes behind-the-screen content: 4 quests, 4 open threads, and 5 clues.
- The server-side audience projector removes Player-hidden records and objectives, private World links, and DM-only context, truths, reveals, and connections before browser transport.
- Added focused validation for Campaign navigation, filtering, DM Player-preview parity, and the new secret-exclusion cases.

## Verification evidence

- `node --test test/web-prototype-state.test.js test/web-audience-envelope.test.js`: 36 passing tests.
- Exact reviewed revision: `npm test` passed 67 of 68 tests; the sole failure is the pre-existing unrelated catalog record-inventory check for missing `catalog/applications/dnd2024/components/abilities/dnd2024.abilities.schema.json`.
- Exact reviewed revision: `npm run build` passed.
- Exact local DM and Player routes returned HTTP 200. The Player response omitted every reviewed hidden Campaign canary and all DM-only fields, while DM returned the expected expanded counts.
- The emitted client bundle contained none of the Campaign secret canaries or model configuration values.
- Private deployment completed successfully while the site retained owner-only custom access: one allowed owner, no external visitors, and no group access.

## Deliberate exclusions

This is fixture-backed, read-only presentation only. It adds no campaign authority, editing, persistence, database/catalog integration, rules calculations, thread clock, clue inference, automatic advancement, model call, or map work. The planned live Campaign and scoped-map leaves remain separate future work.
