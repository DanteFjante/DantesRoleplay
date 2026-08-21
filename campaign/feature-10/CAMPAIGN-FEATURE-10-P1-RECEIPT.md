# Campaign Feature 10 — P1 existing-world continuity proof receipt

Status: **verified**
Date: 2026-08-21

## Proven path

`CampaignFeature10PrerequisiteP1Tests.Existing_world_campaign_session_reopens_from_stored_state_without_a_transcript`
imports the catalog into one disposable database and exercises only existing owners:

1. C1/C2 validate and create the existing-world campaign.
2. C3 initialises the chapter and arc; Q1/Q2 create, offer, accept, and advance the quest; C4
   attaches its context; S1 starts one session.
3. W2 moves the existing fixture traveller to the connected market; W4 reveals one clue while the
   supporting secret remains byte-identical.
4. S3 closes the session while the current chapter remains open, which is the supported P1
   closure path. A repeated end rejects with `STALE_SESSION_STATUS` and adds no structural event.
5. A fresh context over the same database reconstructs the active chapter/arc, active quest and
   completed objective, moved traveller, revealed clue, and immutable session recap. The active
   session reader correctly reports `NO_ACTIVE_SESSION` after closure; the recap reader supplies
   the stored factual continuity record. No assertion uses a transcript.

## Verification

- Focused P1 test passed: **1/1**.
- Full suite: **773 passed, 1 failed, 774 total**.
- The only failure is the known unrelated
  `CatalogFeature10Tests.Imported_catalog_replays_the_feature_10_vertical_session_in_two_fresh_databases` expectation. It still omits the later
  `dnd2024.encounter-sides` fixture component from its expected delta.

## Scope boundary

This evidence adds no production behavior, catalog record, public operation, permanent ID,
schema, migration, or persistent-database import. It unblocks C10’s played-existing-world evidence
gate only. The World-owned small-world composer remains the next dependency.
