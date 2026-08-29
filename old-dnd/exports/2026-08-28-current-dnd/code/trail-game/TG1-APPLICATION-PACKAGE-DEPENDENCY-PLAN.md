# Trail Game TG1 dependency tree — independent application package

Status: **accepted 2026-08-25**; [final receipt](TG1-SLICE-3-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Parent tree: [Trail Game dependency plan](TRAIL-GAME-DEPENDENCY-PLAN.md), TG1
Ruleset alignment: **ruleset-neutral**
Source: **not applicable; no D&D rule or external implementation is used**
Confirmation: [TG0 product contract](TG0-PRODUCT-CONTRACT-CONFIRMATION.md)

## Outcome and non-goals

Prove that the existing generic application kernel can host a separately authored
`trail-survival` application catalog, activate its exact source revision, expose its descriptive
catalog record, and bind one fresh isolated state space without application-specific production C#.

TG1 does not define the run domain, simulation rules, scenario records, state fixtures, player UI,
browser action bridge, live installation, or migration behavior.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Opaque application registration | `application-registry` | verified | Accepted generic application kernel and current `ApplicationIdentifier`/registry tests. |
| Allowed-root path/glob scanning and overlays | `source-registry` | verified | Current registered-source scanner and preview tests. |
| Deterministic preview | `application-preview` | verified | Current preview service and accepted kernel receipt. |
| Exact activation and replay | `application-activation` | verified | Current dry-run/commit/replay tests and accepted kernel receipt. |
| Navigable active procedure catalog | `catalog-navigation` | verified | Active materializer parses exact procedure winners and qualifies application records. |
| Isolated state-space binding | application-scoped ECS | verified | Current immutable state-space registry and isolation tests. |
| Trail Survival authored application source | `trail-survival` application | verified foundation | [Slice 1 receipt](TG1-SLICE-1-RECEIPT.md). |

## Dependency tree

```text
TG1 independent application package                                      [accepted]
├─ TG1.1 Minimal authored source and disposable seam proof                [accepted]
│  ├─ one descriptive procedure record                                   [verified]
│  ├─ register exact application/source in disposable SQLite             [verified]
│  ├─ deterministic preview and exact activation/replay                  [verified]
│  ├─ materialize and inspect qualified active catalog                   [verified]
│  └─ create/read one empty isolated state space                         [verified]
├─ TG1.2 Operator onboarding contract                                    [accepted; depends TG1.1]
│  ├─ exact existing system protocol sequence                            [verified]
│  ├─ allowed-root host configuration evidence                           [verified]
│  └─ no startup auto-registration or live mutation                      [verified]
└─ TG1.3 Package/isolation acceptance                                    [accepted; depends TG1.2]
   ├─ fresh-host protocol walk                                           [verified by Slice 2]
   ├─ zero-app and dnd2024 coexistence                                   [verified]
   └─ durable TG1 completion receipt                                     [recorded]
```

## Conflicts and decisions

- The legacy central catalog manifest is synchronization history for the `dnd2024` catalog. The
  new application source remains under the single authored `catalog/` tree but outside legacy
  `catalog/procedures`, `components`, `mechanics`, and world paths.
- The active application materializer requires at least one supported public record. Slice 1 uses
  one descriptive procedure, not a component schema, state fixture, mechanic, or action.
- The procedure describes how to identify the application boundary. It must not imply implemented
  play behavior.
- Disposable tests may register and activate exact identities. Production startup and the user's
  normal database remain unchanged.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | [TG1 Slice 1](TG1-SLICE-1-IMPLEMENTATION.md) | TG0 | **Accepted**; [receipt](TG1-SLICE-1-RECEIPT.md). |
| 2 | [TG1 Slice 2 — operator onboarding](TG1-SLICE-2-IMPLEMENTATION.md) | Slice 1 receipt | **Accepted**; [receipt](TG1-SLICE-2-RECEIPT.md). |
| 3 | [TG1 Slice 3 — package acceptance](TG1-SLICE-3-IMPLEMENTATION.md) | Slice 2 receipt | **Accepted**; [receipt](TG1-SLICE-3-RECEIPT.md). |

## Completion state

TG1 has no remaining leaf. The three receipts preserve the source/internal seam, exact
existing-protocol onboarding, and final zero-app/two-application isolation evidence. TG2 planning
is the next roadmap boundary but is not active under this plan.

## Confirmation gates

- Slice 1 is accepted through tests asserting the same bounded invariants.
- Any component schema, mechanic, action, query kind, public route, startup registration, migration,
  or live installation belongs to a later confirmed plan.
- TG1 completion confirmation is supplied by the automated evidence recorded in the Slice 3
  receipt.

## Planning receipt

- Runtime artifacts created by this dependency plan: none.
- Active implementation owner: none; TG1 is complete.
- Exact stop observed: no TG2 schema, ID, state, or mechanic work began.
