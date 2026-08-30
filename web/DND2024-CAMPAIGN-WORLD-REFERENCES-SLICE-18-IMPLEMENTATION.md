# D&D 2024 Campaign World references — Slice 18 implementation

Status: **accepted**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `web/DND2024-CAMPAIGN-RECORD-WORLD-LINKS-DEPENDENCY-TREE.md`, Leaves 2, 4, and 5 read side
Ruleset alignment: **dnd2024-compatible**; no D&D rule calculation
Source: authorized application ECS and authorized knowledge notebook
Outcome: project explicit campaign-record World references and authorized evidence subjects into the existing Campaign links.
Exclusions: creating live relationships, restoring retired campaign adapters, changing recap bytes, inferring links, visits, migration, deployment, and browser writes.
Allowed areas: authorized-knowledge contracts/reader/HTTP projection/tests; the D&D 2024 live adapter, read model, and focused tests; this dependency tree and one receipt.
Stop point: links render from explicit relationships and admitted clue subjects, all unknown or unauthorized targets fail closed, and no write surface exists.

## Confirmed boundary

- The permanent live relationship kind is
  `dnd2024.game.core.campaign.record.references-world-entity`.
- The source is an ended session, closed chapter, or resolved/abandoned arc in the selected campaign.
- The target is an exact entity already present in the same audience-projected World location,
  person/creature, or faction directory.
- Relationship data is empty. The association is narrative only and never means “visited.”
- Recap component bytes remain immutable.
- Non-familiar admitted knowledge may carry its exact `knowledge.about` subject. Familiarity carries
  no subject. The final web projection still omits subjects absent from the projected World.
- Capture at closure remains deliberately unreachable until the replacement W10/G9 transaction owner
  exists; this slice does not revive the retired adapter or create an arbitrary annotation endpoint.

## Projection contract

1. The authorized notebook entry optionally emits `{ id, name }` for the exact hydrated subject only
   after the entry passes audience, effective-state, revision, archive, validity, and kind filtering.
2. Familiar entries emit no subject in any list, including grouped locations.
3. The live adapter reads only the exact record-reference relationship kind for eligible records and
   treats malformed, duplicate, oversized, or failed relationship pages as empty.
4. The connected Hub converts IDs to existing `CampaignEntityLinks` only by exact membership in the
   already-projected location, person, and faction directories.
5. Player output never receives DM-only record references. Evidence subjects may navigate only when
   their target is independently present in the Player World projection.

## Failure, compatibility, and no-change behavior

- Older knowledge servers without subjects remain valid and produce empty clue links.
- Missing relationship endpoints, unknown IDs, cross-world IDs, wrong kinds, and malformed values
  produce no links and no partial guessed result.
- Duplicate exact IDs collapse deterministically.
- No relationship, component, campaign record, or live database row is written by this slice.

## Acceptance matrix

| Case | Expected |
| --- | --- |
| ended session or terminal arc with explicit known targets | exact location/person/faction links |
| active record or wrong relationship kind | no links |
| reference target absent from projected World | omitted |
| admitted evidence with projected subject | one exact clue link |
| familiar evidence or subject absent from projected World | no subject/link |
| older server response without subject | compatible empty link |

## Verification

- focused authorized-knowledge tests
- focused D&D web live-adapter and connected-envelope tests
- full D&D web tests and production build
- `roleplay validate catalog` only if catalog records change (not expected in this slice)
- full solution acceptance is not claimed; the currently running MCP server locks its normal build output

## Completion evidence

Acceptance will be recorded in
`web/evidence/dnd2024/DND2024-CAMPAIGN-WORLD-REFERENCES-SLICE-18-RECEIPT.md`.
