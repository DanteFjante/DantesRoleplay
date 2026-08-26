# D&D 2024 application roadmap

Status: **Active; Slices 0–8 accepted; Slices 9 and implemented Parent 10 leaves await confirmation**
Last updated: 2026-08-26

## Outcome

Deliver a D&D 5e 2024 application on the generic application kernel without rebuilding every rule
from scratch and without introducing a second state, transaction, or rules authority.

The preferred sequence is:

1. recover compatible, already-tested DantesRoleplay D&D catalog work from `old-dnd/`;
2. adapt licensed external implementations only where they close a verified gap or improve tests;
3. implement from SRD 5.2.1 only when neither existing source is safe to reuse; and
4. retain canonical component state, catalog JavaScript rules, typed effects, SQLite transactions,
   deterministic replay, and application registration as the only runtime architecture.

## Current boundary

- The generic application kernel is accepted through its first delivery and supports registered
  applications, source overlays, application-owned schemas, ECS state, structural projections,
  exact JavaScript execution, typed effects, state adoption, and reverse dependency impact.
- The current authored `catalog/` contains the accepted D&D check, proficiency, combat, encounter,
  and standalone base-Speed families, the verified Slice 9 stateless character-sheet reader, and
  the verified five-record Slice 10A currency cohort, the verified nine-record Slice 10B1A
  adventuring-gear cohort, the verified complete thirteen-record Slice 10B2 Armor-table cohort, and
  six verified reduced Slice 10B3A weapon profiles plus four verified Slice 10B3B weapon item links
  and the verified Slice 10F Fighter levels 1–2 progression identity cohort under the registered
  `dnd2024` application source.
- `old-dnd/` retains the previous D&D implementation and tests by explicit user decision. It is
  uncompiled and non-authoritative until a reviewed application slice adopts an exact subset.
- The official rule authority remains `source.dnd2024.srd-5.2.1`. Donor repositories are
  implementation evidence, never rule authority.
- `dnd2024-core` remains SRD-faithful. Homebrew, compatibility content, and non-SRD additions must
  be separate registered sources explicitly selected before a campaign is created; an exact source
  profile is frozen into the campaign binding.
- The generic kernel's
  [exact source-profile selection](../../platform/application-kernel/APPLICATION-SOURCE-PROFILES-DEPENDENCY-PLAN.md)
  is accepted, so core-only and core-plus-extension campaigns can now be activated independently
  without a database migration or a silent change to an existing campaign.
- The first optional package, `dnd2024-extension.legacy-equipment`, is accepted outside the core
  catalog glob. It is compatibility-classified, requires `dnd2024-core`, is disabled by default,
  and contains one hash-locked, non-SRD hempen-rope definition. Core-only campaigns exclude it;
  campaigns selecting the exact two-source profile can consume it through existing item mechanics.

## Active dependency plan

The cross-owner import/recovery work is owned by the
[D&D code-adoption dependency plan](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md). It records the reuse
ladder, donor boundaries, effort forecast, small-model assignments, acceleration tooling, and
confirmation gates.

## Delivery lanes

| Lane | Outcome | State |
| --- | --- | --- |
| Adoption foundation | Pinned donors, license/provenance ledger, four-way coverage inventory, test-only seam proof, reusable conformance tooling, staged content transformation, candidate dependency mapping/result-effect allowlisting, and generic impact/replay/rollback proof | Slices 0–6 and 7A1–7A2 accepted; 7A3–7D verified after Sol review |
| Native recovery | Re-adopt compatible archived components, mechanics, procedures, and tests in bounded feature families | Slice 8 accepted: exact 51-mechanic, 26-component-disposition, and 39-procedure matrix closure |
| Donor gap filling | Adapt pure derivations, planners, SRD content encodings, and golden tests only for uncovered behavior | Slice 9 character calculations implemented; parent acceptance awaits a clean repository-wide test gate |
| Playable vertical | Ability/D20, proficiency, AC/HP, weapons, damage, turn flow, and one fresh-host replayable encounter | planned |
| Breadth | Character progression, conditions, equipment, spells, monsters, and magic items in independent cohorts | Currency, schema-faithful adventuring gear, the complete Armor table, six reduced weapon profiles, four archived weapon item links, and Fighter levels 1–2 progression identities are implemented and verified; every remaining archive family is mapped to an explicit schema or permanent-ID gate |
| Maintenance | Pinned upstream-diff reports, attribution, conformance regression, and optional archive retirement | planned |

## Historical evidence

The [archived D&D roadmap](../../old-dnd/ruleset/dnd2024/ROADMAP.md) and its receipts preserve the
previous implementation evidence. They are inputs to the coverage inventory, not an active plan and
not permission to copy the archive wholesale.

## Rules

- Each implementation slice owns one mechanic family or one adoption seam.
- A D&D-owned slice cites an exact SRD 5.2.1 heading/page locator and records the relevant Foundry
  dnd5e review before code changes.
- Existing verified native behavior wins over donor behavior unless an explicit semantic change is
  confirmed.
- Generated mappings and converted tests are candidates until reviewed, validated, and activated.
- No plan or donor may place D&D formulas, IDs, eligibility, or outcome branches in generic C#.
- Update this roadmap only when a delivery lane changes state; receipts own completed evidence.
