# D&D code-adoption Slice 12 design — acceptance and maintenance

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), adoption acceptance lane  
Dependency tree: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 12  
Ruleset alignment: `ruleset-neutral`; it verifies accepted `dnd2024-compatible` and
`dnd2024-owned` behavior without changing that behavior  
Outcome: make the accepted adoption boundary reproducible on a fresh host, prove its full
regression/protocol surface, and detect upstream or attribution drift without automatic adoption.  
Exclusions: new game rules, runtime IDs, schema meaning, migrations, public operations, live-data
changes, donor lock updates, and automatic upstream activation.

## Leaf schedule

| Leaf | Boundary | Exit evidence |
| --- | --- | --- |
| 12A | fresh-host play, deterministic replay, no-change rejection, and rollback proof | accepted |
| 12B | repeatable full validation and protocol evidence | accepted |
| 12C | attribution and pinned-upstream diff workflow | accepted |
| 12D | parent acceptance | accepted |

Each leaf is independently stoppable. Slice 12 reuses the activated catalog, generic action runner,
effect transaction, replay ledger, adoption contracts, and donor lock. It does not create a second
acceptance or update authority.

## Dependency and transaction boundary

~~~text
accepted catalog + source activation
        -> fresh SQLite action/replay tests
        -> existing injected-failure rollback proof
        -> full catalog/build/test/protocol runner
        -> attribution + pinned/candidate upstream report
        -> human review only (never lock or runtime mutation)
~~~

Runtime actions continue to own exactly one generic transaction. Development scripts may inspect
the checkout, temporary donor repositories, and command results, but may write only requested
evidence files. Temporary repositories must be created below the operating system temporary
directory, pinned revisions must be verified, and cleanup must validate the exact resolved target.

## Parent acceptance

Parent 12 is accepted. Leaves 12A–12C have durable receipts, the same worktree passes catalog
validation and the full regression suite, the protocol walk is recorded, attribution requirements
pass, and the upstream comparison reports review-required changes without editing `donor-lock.json`
or any runtime artifact.
