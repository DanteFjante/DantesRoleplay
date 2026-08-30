# Caldris companion content completion dependency tree

Status: **accepted**
Ruleset alignment: **dnd2024-compatible**
Source: not applicable; this slice adds setting and GM reference content, not D&D rules

## Outcome and boundary

The companion and local DM can retrieve the remaining high-value authored Caldris places, social
systems, cultures, and campaign guidance without reading planning Markdown during play. Numerical
mechanics, media projection, mutable quest lifecycle, and simulated consequences are excluded.

## Owners and readiness

| Dependency | Owner | State |
| --- | --- | --- |
| Existing Caldris hierarchy and campaign | `dnd2024-main` SQLite state | verified by Slices 1–4 |
| Additional places | `game.core.world.location` + containment | ready |
| Public setting reference | fact + knowledge classification/relationships | ready |
| GM campaign reference | secret + knowledge classification/relationships | ready |
| Reviewed import | `system.world-state.sync` | verified |
| General media projection | separate Slice 23 worktree changes | excluded; do not touch |

## Dependency tree

```text
Companion-readable Caldris reference [accepted]
├─ existing World/campaign [verified]
├─ additive locations beneath existing regions [verified]
├─ additive facts/secrets beneath existing containers [verified]
└─ dry-run-first reviewed sync and live readback [verified]
```

## Confirmation

The user's instruction to continue the remaining plan with maximum content confirms additive
permanent Caldris content IDs within existing schemas. No schema meaning or public operation changes.
