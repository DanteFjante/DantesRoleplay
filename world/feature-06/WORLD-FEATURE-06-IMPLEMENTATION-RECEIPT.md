# World Feature 6 — implementation receipt

**Status:** Feature 6 verified  
**Date:** 2026-08-20  
**Scope:** one active fixed-role reaction from the Feature 3 fixture agenda to the Feature 4 Oren
letter clue. No persistent catalog import was performed.

## Delivered artifacts

- `procedure.game.core.world.reactive`
- `mechanic.game.core.world.clue.reveal-on-faction-agenda`
- `subscription.game.core.world.clue.reveal-on-faction-agenda`
- Fixed role binding: `clue.feature-04.oren-letter`
- Generic fixed-role validation repair in `SubscriptionStore`
- Focused fresh-import and reaction-chain coverage in `CatalogWorldFeature6Tests`

## Verified behavior

| Case | Evidence |
| --- | --- |
| Fresh catalog import | The exact fixed role registers with the unquoted entity ID and persists as `{"clue":"clue.feature-04.oren-letter"}`. |
| Missing target | A missing fixed entity reports `Missing entities: clue.feature-04.missing.` and leaves no subscription row after import rollback. |
| Accepted agenda action | The Feature 3 fixture changes `agenda.state` from `ready` to `advanced`; the reaction changes only Oren's clue from `unrevealed/gm` to `revealed/party`. |
| Event chain | One root faction replacement and one derived clue replacement are committed at depths 0 and 1, with one reaction execution. |
| No duplicate | Repeating the agenda action fails without another clue replacement; a previously revealed clue produces a zero-effect reaction. |
| Nonmatch and rollback | A clock replacement does not route the subscription. A corrupt fixed clue aborts the source agenda advance and leaves no event or execution row. |

## Validation record

- Focused Feature 6 plus subscription-store tests: 9 passed.
- `roleplay validate catalog`: valid, 129 records; 13 non-blocking near-duplicate warnings; no live data touched.
- Full test suite: 401 passed, 0 failed, 0 skipped.
- `git diff --check`: no whitespace errors (line-ending notices only).

## Remaining boundary

Feature 6 is accepted. General reaction authoring, dynamic subscriptions,
additional factions or clues, quests, notifications, scheduling, and persistent integration import
remain out of scope. No later world feature was started.
