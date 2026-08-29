# Campaign Feature 10 R3 — cross-root composition ratification

Status: **Ratified 2026-08-21; planning boundary only.**
Authority: User continuation after review of the C10 execution plan and W17 draft.

## Ratified architecture

C10 uses a Campaign-owned `CampaignWorldCompositionCoordinator` in DataAccess. It is an internal composition service, not an MCP tool. It coordinates two effect-free children: the World-owned W17 composer and a Campaign-owned C2 composition adapter. Neither child opens or commits a transaction, applies effects, records an audit, routes events/notifications, or exposes transport.

`CampaignBootstrapper` cannot be called as a child because its existing public C2 path owns a transaction and audit. The new C2 adapter validates/materialises campaign-only effects instead.

## Closed outer blueprint and identifiers

The later typed `NewWorldCampaignBlueprint` contains the C1 campaign title/premise/goals/tone, ruleset, initial chapter/arc, and one W17 authored World record. It has no caller-provided `existingWorldId`, permanent World ID, local key, relationship, reference, raw effect, namespace, fingerprint, audit, event, transaction control, script, SQL, or extension data.

`campaignId` remains C1's permanent `campaign.<suffix>` ID. The exact substring after `campaign.` becomes the World namespace `world.c10.<suffix>`. W17 derives every World ID as `world.c10.<suffix>.<fixed-local-key>`; the World root is `world.c10.<suffix>.world`. The Campaign child receives only that derived root after W17 succeeds.

W17's fixed graph remains 14 entities, 20 components, four containment links, and 20 World relationships. The campaign child emits exactly 13 campaign effects: campaign entity/root, one in-world link, and ten references in this canonical order:

1. `location.gate` / start / party
2. `actor.one` / npc / party
3. `actor.two` / npc / party
4. `faction` / faction-stake / party
5. `knowledge.fact` / knowledge / party
6. `knowledge.rumour` / knowledge / party
7. `knowledge.clue.one` / knowledge / gm
8. `knowledge.clue.three` / knowledge / gm
9. `knowledge.clue.two` / knowledge / gm
10. `knowledge.secret` / knowledge / gm

W17 injects the corresponding fixed visibility/status values: root/region/gate/market public; observatory/faction/motives party; fact public; rumour party; secret and unrevealed clues GM.

## Child contracts

```text
W17.ComposeAsync(closedWorldBlueprint, derivedWorldNamespace)
  => valid/invalid: virtual staged World, derived root, ordered local-key map,
     visibility review, 58 World effects, counts, ordered problems

C2Adapter.ComposeAsync(newWorldCampaignBlueprint, validW17Result)
  => valid/invalid: derived CampaignBlueprint, resolved references,
     13 Campaign effects, counts, ordered problems
```

W17 validates the fixed graph, typed content, definitions, ID collisions, and staged dry-run. The C2 adapter runs C1 semantics against W17's virtual read-only World. A child failure returns no effects to the coordinator. Both child results are canonical and zero-write.

## Public operations and fingerprint

R6/R7 extend the existing `commit(kind: "campaign")` surface only:

- `operation: "compose-preview"` — read-only combined review.
- `operation: "compose-create"` — same closed blueprint plus review fingerprint.

No new commit kind is allowed. Preview reveals mapping, 58/13 owner-separated counts, visibility review, canonical references, warnings/problems, and fingerprint, but never raw effects or a staged World. It reserves nothing.

The fingerprint is lowercase SHA-256 over UTF-8 canonical JSON logically prefixed by `c10.compose.v1`: version; declared-order closed blueprint preserving validated array order; campaign ID plus derived namespace/root; W17 local-key/visibility review in rank order; then the ten canonical references. Objects use declared member order, no whitespace, and `{}` for empty objects. Invalid child results have no fingerprint. Identical state/input produces byte-identical review output and fingerprint.

## Single transaction and failure contract

R7's coordinator is the sole transaction owner. It validates the envelope, starts one transaction, recomposes both children against current state, rechecks the canonical fingerprint and collisions, then dry-runs and applies one exact merged list: all 58 W17 effects followed by all 13 Campaign effects. It allocates one root operation ID before real application and passes it to the existing effect applier.

All structural events, guards, reactions, and notifications join that transaction/correlation. On success the coordinator records exactly one C10 success audit under the same operation ID and commits once. The audit contains affected IDs/counts and fingerprint, never raw GM-only prose or effects. Any error rolls back World/Campaign rows, events, reactions, notifications, and success audit. After rollback and tracker clearing, at most one separate `success: false` failure audit may be recorded; it cannot claim creation succeeded. Cancellation rolls back and propagates. A post-commit read error never retries creation.

| Stage | Stable result |
| --- | --- |
| Closed envelope invalid | `INVALID_COMPOSITION_REQUEST`; no transaction/effects/success audit. |
| W17/C2 validation fails | Ordered child problems; no effect application/success audit. |
| Fingerprint missing/mismatched/stale | `STALE_COMPOSITION_PREVIEW`; no effects/success audit. |
| Collision or merged dry-run fails | `COMPOSITION_ID_CONFLICT` or `COMPOSITION_EFFECTS_INVALID`; no partial state. |
| Effect, guard, event, reaction, notification fails | `COMPOSITION_APPLY_FAILED`; full rollback. |
| Success audit or commit fails | `COMPOSITION_CREATE_FAILED`; full rollback. |
| Existing committed campaign ID | `COMPOSITION_REPLAYED`; no second success, audit, or effects. |

The first successful create is deliberately not idempotent. A later caller reads the campaign; there is no reservation, repair, successful replay, or alternate fixed graph.

## Consequences

R3 activates W17 Slice 1 and unblocks the C2 effect-free adapter design. It does not authorise either public C10 operation. The order remains R4 World child, R5 Campaign child, R6 preview, R7 atomic create; each is a separate reviewed slice and receipt.

No runtime, catalog, migration, fixture, or public-surface artifact changed in R3.
