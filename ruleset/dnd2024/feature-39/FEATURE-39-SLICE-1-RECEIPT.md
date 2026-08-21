# Feature 39 Slice 1 receipt — Heroic Inspiration presence state

Date: 2026-08-21  
Status: **Implemented and accepted in scope; stop after Slice 1**

## Delivered boundary

- `dnd2024.heroic-inspiration` is a closed empty presence component. Its presence means an
  already identified player character holds exactly one available Heroic Inspiration instance;
  absence means none.
- `procedure.mechanic.dnd2024.heroic-inspiration` governs the state and its narrow normal grant
  path.
- `mechanic.dnd2024.heroic-inspiration.grant` accepts only `{}` and one valid
  `dnd2024.character.profile` subject. It returns one `component.add` effect only when the
  presence state is absent.
- Duplicate grants, invalid/corrupt profile state, corrupt Heroic Inspiration state, non-character
  subjects, and all extra or malformed input reject without repairing or changing state.

The component intentionally stores no count, Boolean, provenance, source trait, rest result,
recipient, die, roll, outcome, expiry, or history.

## Explicitly deferred

This slice does **not** consume or transfer Heroic Inspiration, reroll any die, change a D20 Test,
grant Human Resourceful on a Long Rest, choose an overflow recipient, create a character, or grant
an Origin Feat. Those are the later Feature 39 and named owner boundaries in the dependency plan.

## Verification

- `CatalogFeature39Tests`: **3 passed**. It proves one profile-gated add, duplicate/ineligible and
  closed-input rejection with byte-stable unrelated state, corrupt-state rejection without repair,
  and the empty-object component schema.
- `roleplay validate catalog`: **valid** — 390 records (94 mechanics, 110 procedures, 77
  components, 12 event types, 5 subscriptions, 92 entities), 72 existing warning-level
  near-duplicate findings, and no live data touched.
- Full suite: **779 passed, 1 failed**. The only failure is the existing
  `CatalogFeature10Tests.Imported_catalog_replays_the_feature_10_vertical_session_in_two_fresh_databases`
  transcript expectation: the imported fixture now has `dnd2024.encounter-sides`, while its
  historical assertion expects no such delta. No Feature 39 test or artifact is implicated.
- `git diff --check` passes, with only repository-wide pre-existing line-ending notices.

No persistent catalog import was performed.
