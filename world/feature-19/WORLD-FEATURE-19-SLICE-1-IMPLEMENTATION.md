# World Feature 19 Slice 1 implementation — dedicated dated chronology records

Status: **awaiting confirmation**
Owner/roadmap: `WORLD_AND_LORE_PLAN.md`, proposed W19
Dependency tree/leaf: `web/DND2024-WORLD-TAB-COMPLETION-DEPENDENCY-TREE.md`, C1 / ordered leaf 4
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**; this feature defines setting chronology, not a D&D rule
Outcome: introduce one closed, reusable World chronology record whose explicit in-world date,
audience classification, World scope, and optional subjects can later drive the World History screen
Exclusions: Thalorien entries, website projection, player authorization, campaign recaps/outcomes,
knowledge conversion, event-ledger conversion, automatic record creation, calendar formatting,
date arithmetic, mechanics, migrations, maps, media, NPC profiles, and completed World-tab acceptance
Allowed files/areas after confirmation: this document; generic and D&D 2024 materialized component
definitions/schemas; one governing World procedure; the trusted-GM World read recipe; fixture records
and relationships; focused catalog tests; disposable catalog validation; receipt and owning-plan status
Stop point: stop when the chronology owner validates, imports, reads in stable date order, rejects
wrong-world/malformed records and links, and has no mechanic or UI consumer

## Confirmation still required

The user's 2026-08-30 World-tab request confirms the outcome: History must read dedicated dated
World records rather than reinterpret authorized knowledge. Repository policy still requires exact
confirmation of these new permanent IDs and schema meanings before runtime/catalog implementation:

- component `game.core.world.chronology`, materialized as
  `dnd2024.game.core.world.chronology`;
- relationships `game.core.world.chronology.in-world` and
  `game.core.world.chronology.about`; and
- the complete record shape proposed below, including signed calendar minutes and an authored date
  label.

No catalog, SQLite, route, response, or live Thalorien record changes while this gate is open.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| D&D rules | None | Catalog mechanics remain authoritative | No SRD source or Foundry reference applies. |
| World time | One root-owned calendar/minute coordinate | `game.core.world.clock` | A chronology date uses the same calendar identity but does not mutate or derive the clock. |
| Campaign history | Campaign-specific recaps and outcomes | Campaign owners | A chronology record captures durable setting history only and never copies a recap automatically. |
| Knowledge | Facts, rumours, secrets, and clues | World knowledge owners | Chronology is not inferred from prose, reveal state, or the structural event ledger. |

## External implementation reference

No Foundry dnd5e implementation is relevant because this feature defines no D&D mechanic or rules
content.

## Prerequisite evidence

- W5 verifies one root-owned `calendarId` and monotonic `currentMinute`; its contract explicitly
  excludes mutable history from the clock component.
- W7 verifies bounded trusted-GM World graph reads and explicitly says its current output is not a
  player-safe view.
- The World-tab completion tree records that the present History UI interprets authorized knowledge
  because no dated chronology owner exists.
- Campaign session recaps already own campaign milestones and must remain a separate history.

## Proposed runtime artifacts

### Closed chronology component

`game.core.world.chronology` is a complete object with exactly:

| Field | Meaning |
| --- | --- |
| `status` | `active` or `archived`; archived records remain durable but are omitted from ordinary reads. |
| `title` | Trimmed nonempty display title, at most 160 Unicode scalar values. |
| `summary` | Trimmed nonempty narrative account, at most 1,000 Unicode scalar values. |
| `calendarId` | Trimmed nonempty calendar identity, at most 100 scalar values; it must equal the scoped root clock. |
| `occurredAtMinute` | Signed safe integer from -1,000,000,000 through 1,000,000,000 used only for chronology order. |
| `precision` | `exact`, `approximate`, or `era`; it describes how literally the minute coordinate should be read. |
| `dateLabel` | Authored, trimmed, nonempty display date, at most 100 scalar values; no host formatter invents it. |
| `visibility` | Descriptive `public`, `party`, or `gm`; it is not authorization until a later audience owner admits it. |

The signed coordinate permits history before the root clock's zero epoch. `dateLabel` is required
because the repository has no calendar-formatting owner. It is presentation text, while
`occurredAtMinute` supplies deterministic order; neither is derived from the other.

### Relationships and procedure

- Every chronology entity has exactly one empty-data `game.core.world.chronology.in-world` link to
  one active World root carrying the matching clock/calendar.
- It may have zero through ten empty-data `game.core.world.chronology.about` links to entities proven
  to belong to that same World. These links support later exact navigation without embedding IDs in
  the component.
- New `procedure.game.core.world.chronology` governs declaration, reviewed effects authoring,
  complete replacement, scoping, stable read order, and archival behavior.
- `procedure.game.core.world.read` gains one trusted-GM chronology recipe, ordered by
  `occurredAtMinute`, then permanent entity ID, capped at 100 records/200 edges.

No mechanic, semantic event, subscription, notification, migration, or new query kind is created.

## Authoritative state and closed input

Catalog/SQLite component and relationship records are authoritative. Entity identity supplies the
record ID; the component supplies only closed chronology data; relationships supply World scope and
subjects. A caller never supplies a derived current/relative age, campaign ID, UI route, asset,
event-ledger ID, or authorization result.

The root clock is read to validate calendar identity only. Creating or replacing chronology never
changes the clock, and advancing the clock never creates or changes chronology.

## Behavior, result, and typed effects

Reviewed setup creates the chronology entity, adds one complete component, creates its one World
scope link, then optional subject links in stable ID order. A correction replaces the entire closed
component. Archival is a complete replacement retaining date/title/summary/scope.

The trusted-GM recipe returns active records from exactly one requested root in ascending signed
minute order and then ID order, with their same-root subject links. It does not merge records at the
same minute, infer causal order, or generate prose.

## Failure, replay, and rollback contract

- Missing/extra fields, malformed text, unknown enums, unsafe/fractional minutes, duplicate subject
  links, nonempty link data, missing/duplicate/reversed scope, absent/mismatched root clock, or
  cross-world subjects fail without writes.
- Equal minutes are valid and ordered by permanent ID. Replaying an inspected identical effects
  manifest is handled by the existing effects/audit contracts; no chronology-specific idempotency
  token is introduced.
- Deleting history is not part of the feature. Archive by complete replacement.
- Rollback removes the new fixture/component/relationship declarations before import, or restores a
  prior reviewed complete component after import. No clock, campaign, knowledge, or event row is
  rewritten.

## Implementation sequence after confirmation

1. Add generic component definition/schema and D&D 2024 materialized definition/schema.
2. Add the governing procedure and trusted-GM read recipe extension.
3. Add a small generic fixture with pre-epoch, same-minute, and archived records plus scope/subject
   links; add focused schema/scope/order/failure tests.
4. Run focused tests and `roleplay validate catalog`; run the full suite at Slice 1 acceptance.
5. Write the receipt, mark W19 Slice 1 verified, and stop before live Thalorien data or web work.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Valid record | One active record with matching calendar/root and optional same-world subjects validates and imports. |
| Historical date | A negative minute is accepted and sorts before minute zero. |
| Stable ties | Equal-minute records sort by permanent ID without merging. |
| Archived | Record remains stored but ordinary chronology reads omit it. |
| Calendar mismatch | Record whose calendar differs from its scoped root clock rejects. |
| Scope | Missing, duplicate, reversed, cross-world, or nonempty `in-world` rejects. |
| Subjects | Cross-world, duplicate, self, nonempty, or more than ten `about` links reject. |
| Separation | No campaign recap, knowledge record, event row, or clock advance creates chronology. |
| Compatibility | Existing W1–W18 catalog validation and tests remain green. |

## Verification commands

- focused `CatalogWorldFeature19Tests`
- `roleplay validate catalog`
- full repository test suite at Slice 1 acceptance

The protocol walk is not required because this slice adds no MCP surface or dependency registration.

## Completion receipt and exit gate

After confirmation and implementation, write
`world/feature-19/WORLD-FEATURE-19-SLICE-1-RECEIPT.md`, update this document and the two owning plans
once, and stop. Live Thalorien chronology entries, audience-safe projection, History-screen
consumption, and final World-tab acceptance remain separate confirmed slices.
