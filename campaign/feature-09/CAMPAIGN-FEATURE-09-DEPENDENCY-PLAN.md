# Campaign Feature 9 dependency plan — controlled expansion selection

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; blocked by a played C8 campaign and one host-selected expansion.**
Last updated: 2026-08-20

## Target capability

After a played campaign has evidence, a host can select exactly one proven next expansion and receive
a new dependency plan that preserves campaign ownership, review, determinism, and rollback.

C9 is a controlled planning gate, not one runtime feature. The existing roadmap intentionally lists
alternatives that cannot be safely bundled.

## Included and excluded

Included: played-evidence review; selection criteria; owner/dependency search; a decision record;
and one follow-up feature plan.

Excluded: implementing templates, cloning, time events, rewards, faction clocks, multiple worlds,
website wizard, map integration, or more than one expansion in the same pass.

## Existing alternatives

- campaign templates or cloning;
- time/clock events;
- quest rewards;
- faction clocks;
- multiple campaign worlds;
- website creation wizard; or
- interactive map integration.

Each candidate has different state, transaction, and consumer ownership. None is a default.

## Dependencies and stop gate

~~~text
C9 controlled expansion
├─ C8 played session/read-only evidence                       [must be verified]
├─ host-selected one expansion                                [missing leaf]
│  └─ Slice 1: create one dedicated dependency plan
└─ implementation                                             [blocked until that plan]
~~~

## Selection criteria

Choose the smallest demonstrated pain point, identify the authoritative owner, name all new
permanent/public/transaction boundaries, and show why the change cannot fit an existing plan.
Reject a selection that only adds speculative content or combines multiple alternatives.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Evidence-based choice | C8 receipt identifies the concrete problem the selected expansion solves. |
| One expansion | Exactly one candidate is selected; all others remain explicitly deferred. |
| Ownership | Search shows an existing owner or a justified new one without copied state. |
| Dependency plan | The selected expansion receives its own target, non-goals, leaves, slices, tests, and exit gate. |
| No runtime change | C9 creates no game/catalog/database state and begins no implementation slice. |

## Exit gate

C9 completes when one, and only one, post-play expansion has a separately approved plan. Stop before
implementing it.
