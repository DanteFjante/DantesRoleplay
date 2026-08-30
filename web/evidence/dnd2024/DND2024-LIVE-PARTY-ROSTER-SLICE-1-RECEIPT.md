# DND2024 live Party roster Slice 1 receipt — selectable provisional dossier

Status: **accepted 2026-08-30**

Implementation document: `web/DND2024-LIVE-PARTY-ROSTER-SLICE-1-IMPLEMENTATION.md`

Dependency tree: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 10 / E1–E3 presentation seam

Local page revision: **11**

## Delivered

- Replaced the fixed Party preview with a responsive active-roster workspace and selectable character dossier.
- Added Overview, Sheet, Knowledge, Backstory, Origin, and Inventory sections with search for long records and explicit empty states.
- DM perspective resolves only the exact active campaign → participation → actor graph. Withdrawn, malformed, duplicate, cross-campaign, and unreadable shapes are omitted independently.
- A real Player receives only the server-bound actor. A local DM's Player preview receives no unproven roster or actor knowledge.
- Retained playtest entries are separated into Sheet, Backstory, Origin, and Inventory presentation groups. Sheet and Inventory explicitly state that these are provisional records, not derived mechanics or canonical custody.
- Player knowledge can enter a dossier only from the already-authorized actor notebook. DM knowledge is never attached to a character dossier.
- Initials remain the portrait fallback; no portrait or game-state owner was invented.

## Evidence

- Focused Party/adapter/client validation: **68 passing, 0 failing**.
- Full React companion suite: **111 passing, 0 failing**.
- Production server-bundle build passed.
- Live server projection returned one exact active participant, **Orban**, with 13 Sheet entries, 49 Backstory entries, 2 Origin entries, and 3 Inventory notes; no fixture companions were present.
- Published `dnd2024-play` revision **11** with four bounded assets. The page, emitted script, and emitted stylesheet returned HTTP 200, and the active script contained the new Party roster/dossier surface.
- Revisions 9 and 10 were exported before their replacements; revision 10 is the immediate temporary-ZIP rollback boundary.

## Deliberate exclusions

- canonical character-sheet mechanic execution and derived numbers;
- canonical containment-backed inventory, equipment consequences, value, weight, or accessibility;
- character creation/editing, campaign participation writes, knowledge grants, or any game-state mutation;
- entity-owned portraits or generated character art;
- a Player-visible summary of other characters without a server-owned shared-roster audience contract; and
- new catalog records, permanent IDs, schemas, migrations, public routes, D&D rules, or hosted deployment.

## Next Party boundary

Connect the canonical character-sheet aggregate and bounded inventory read for an authorized selected character. Keep the accepted roster and six-section interaction stable, replacing provisional Sheet/Inventory notes only when their exact authoritative projections are available.
