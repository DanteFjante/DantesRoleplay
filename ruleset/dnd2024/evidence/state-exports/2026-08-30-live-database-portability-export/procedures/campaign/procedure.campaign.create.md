---
id: procedure.campaign.create
category: campaign
name: Validate and create an existing-world campaign
governs: commit(kind: "campaign") validating or creating one existing-world campaign blueprint
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Campaign creation is a two-stage path. Validation is read-only and returns a fingerprint. Creation
may accept only the same valid blueprint and matching fingerprint, then atomically creates one
campaign root with directed world and reference links.

## Instructions
1. Submit the closed `CampaignBlueprint` with `operation: "validate"`. It resolves only existing
   active world records and returns a deterministic result without world changes.
2. Use only a valid returned fingerprint for `operation: "create"`. The creator revalidates it;
   a changed lifecycle, scope, or fingerprint rejects without partial campaign state.
3. A created campaign has one `game.core.campaign.root` component, one empty-data
   `game.core.campaign.in-world` link, and canonical role/audience-data
   `game.core.campaign.references` links. Existing world records are never copied or changed.

## Constraints
- No caller supplies effects, component data, relationships, audit data, child ids, SQL, or script.
- This feature creates no chapter, arc, quest, character, item, session, clock, world, or access
  policy. Those are separately owned later features.
