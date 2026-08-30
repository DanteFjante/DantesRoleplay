# DND2024 live Party Slice 2 receipt — canonical stored state and direct inventory

Status: **accepted 2026-08-30**

Implementation document: `web/DND2024-LIVE-PARTY-CANONICAL-READ-SLICE-2-IMPLEMENTATION.md`

Dependency tree: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 10 / E2–E3

Local page revision: **13**

## Delivered

- Extended the accepted Party roster with read-only canonical character state for the exact
  authorized selected actor. The adapter discovers existing component types first, then reads only
  exact stored identity, origin, class-membership, creature, experience, and proficiency records.
- Added a bounded direct-inventory projection of at most 24 exact contained entities with stored
  definition link, quantity, placement, and equipment slots. A successful empty read remains an
  authoritative empty inventory; an unavailable read is not presented as empty.
- Canonical sections replace provisional Sheet, Origin, Backstory, or Inventory entries only when
  their authoritative records exist. Malformed optional records are omitted independently.
- Added explicit canonical/provisional presentation statuses and canonical inventory empty-state
  language without changing the six-section dossier interaction.
- Kept the browser calculation-free: it displays stored values and does not derive modifiers,
  Proficiency Bonus, Armor Class, total level, carrying capacity, or equipment consequences.
- Preserved Orban as a **Provisional character record** because the live actor has no approved
  canonical character components. The review-required migration draft was not promoted or edited.
- Added no catalog record, permanent ID, schema, migration, public route, game-state write, or D&D
  rule implementation.

## Evidence

- Focused canonical projection test: **1 passing, 0 failing**.
- Focused canonical adapter/component/inventory test: **1 passing, 0 failing**.
- Full React companion suite after integration: **125 passing, 0 failing**.
- Production server-bundle build passed and emitted the revised script and stylesheet.
- Revision 12 was exported before activation as the rollback boundary. Published
  `dnd2024-play` revision **13**; control readback reported active revision 13 and latest revision
  13. The page, script, and stylesheet each returned HTTP 200.
- Live browser verification opened Party Overview, Sheet, and Inventory. Orban remained clearly
  provisional, the accepted narrative records remained readable, and the page reported no browser
  warnings or errors. The Party overview was left open for the user.

## Deliberate exclusions

- executing the existing character-sheet mechanic or projecting its derived aggregate;
- deriving modifiers, bonuses, Armor Class, total character level, or other rule outcomes;
- nested containers, inventory totals, currency/value/weight calculations, equipment consequences,
  item actions, or inventory mutation;
- approving or importing Orban's provisional mechanical draft;
- character creation/editing, participation writes, knowledge grants, or any game-state mutation;
- entity-owned portraits or generated character art; and
- broader shared-roster detail for Player perspective without an authoritative audience contract.

## Next Party boundary

Project the existing derived character-sheet aggregate for an authorized canonical character and
define the deeper inventory read boundary, while keeping stored state and derived values visibly
distinct. Orban's canonical mechanical sheet remains blocked on the separate review and approval of
his provisional draft.
