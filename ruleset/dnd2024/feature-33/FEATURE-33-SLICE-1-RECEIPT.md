# Feature 33 Slice 1 receipt — immutable standard-rest policy

Date: 2026-08-21  
Status: **Implemented and accepted in scope; stop after Slice 1**

## Delivered boundary

- `dnd2024.rest-policy` defines the immutable standard-rest policy vocabulary.
- `procedure.mechanic.dnd2024.rest-policy` governs its catalog-only definition.
- `content.dnd2024.rest-policy.standard.v1` records source-cited Short and Long Rest timing,
  activity, interruption, and ordered consequence-handoff facts from SRD 5.2.1.

The policy declares Short Rest at 60 minutes and Long Rest at 480 minutes, including the standard
Long Rest sleep/activity, restart, partial-credit, and interruption-extension facts. Its benefit
labels are handoffs to existing or future state owners, not executable recovery effects.

## Explicitly deferred

No actor can start, interrupt, resume, or finish a rest. The slice creates no rest episode, clock
advance, scheduler, event/subscription, Hit Die, HP recovery, Temporary-HP expiry, Exhaustion
recovery, resource/spell-slot reset, Human Resourceful grant, or Heroic Inspiration transition.

## Verification

- `CatalogFeature33Tests`: **2 passed**. It proves fresh import/readback of the one exact static
  policy; its distinct Short/Long facts; source citation; and rejection of altered duration,
  noncanonical interruption order, elapsed-time state, and executable-effect fields.
- `roleplay validate catalog`: **valid** — 393 records (94 mechanics, 111 procedures, 78
  components, 12 event types, 5 subscriptions, 93 entities), 73 warning-level near-duplicate
  findings, and no live data touched.
- Full suite: **787 passed, 1 failed**. The sole failure is the existing
  `CatalogFeature10Tests.Imported_catalog_replays_the_feature_10_vertical_session_in_two_fresh_databases`
  transcript expectation for the imported `dnd2024.encounter-sides` fixture delta; no Feature 33
  test or artifact is implicated.
- `git diff --check` passes, with only repository-wide pre-existing line-ending notices.

No persistent catalog import was performed.
