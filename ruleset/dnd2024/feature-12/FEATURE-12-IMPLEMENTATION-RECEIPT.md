# Feature 12 implementation receipt — turn action economy

Completed: 2026-08-20

Feature 12 now provides one closed, participant-owned budget for Action, Bonus Action, Reaction,
free object interaction, and movement. Administrative record/correct is separate from the
effect-free reader, turn-start restoration, and normal spending path. The Feature 11 lifecycle
restores only the newly active participant, while a Reaction remains consumable by another roster
participant between that participant's turns.

The implementation is bounded deliberately: it records and consumes explicit resources but does
not decide what any other rule costs. The next dependent work is Feature 13, which revises this
single spend path for relevant conditions rather than creating a parallel action-economy rule.
