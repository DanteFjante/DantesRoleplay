# D&D 2024 application roadmap

Status: **Active; code-adoption Slices 0–13 accepted selected scope**
Last updated: 2026-08-27

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
  and standalone base-Speed families, the accepted Slice 9 stateless character-sheet reader, and
  the accepted five-record Slice 10A currency cohort, the accepted nine-record Slice 10B1A
  adventuring-gear cohort, the accepted complete thirteen-record Slice 10B2 Armor-table cohort, and
  six accepted reduced Slice 10B3A weapon profiles plus four accepted Slice 10B3B weapon item links
  and the accepted Slice 10F Fighter levels 1–2 progression identity cohort under the registered
  `dnd2024` application source.
- Character creation CC1, CC2A–CC2H2, and the all-class basic-playable MVP are accepted. Pure role-bound JavaScript resolves Standard
  Array plus Soldier increases, plans any of nine species with canonical Size/base Speed, turns
  Human Skillful into a skill contribution, and resolves the recommended Versatile/Skilled feat
  into three skill/tool contributions. Four Origin-feat identities are active without implying
  undeveloped benefits. A shared one-instance Heroic Inspiration state and guarded normal grant
  are active; immutable source-corrected rest policy supplies lifecycle values; and authenticated
  Short/Long Rest starts bind exact HP, policy, active base world, and authoritative clock state into
  one atomic episode/membership transaction. Stateless progress now classifies each authoritative
  clock interval as sleep/light activity, records every exact source interruption, adds Long Rest
  hours, stops interrupted Short Rests atomically, and reaches duration-ready without benefits.
  The MVP now composes a Soldier actor using any of twelve source-bound SRD level-1 class models,
  class-specific core state, an explicit pending ledger, and campaign participation in one replayable transaction. Automatic event adapters,
  finish/recovery, Resourceful, executable background/class grants, and source-complete creation
  remain later independent leaves.
- `old-dnd/` retains the previous D&D implementation and tests by explicit user decision. The
  accepted [Slice 13 inventory](adoption/evidence/retained-archive-inventory-13a.json) fingerprints
  all 737 files and proves zero runtime/build/catalog/production-source consumers. It remains
  non-authoritative recovery/provenance material; 43 accepted transformation sources and other
  development evidence still depend on its exact bytes.
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
- The [pre-Slice 11 acceptance](adoption/evidence/DND-CODE-ADOPTION-PRE-SLICE-11-ACCEPTANCE.md)
  revalidated every delivered adoption/tooling/content/runtime boundary through Slice 10. The
  [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md) is accepted selected scope: damage mitigation is
  accepted through 11A–11D, and Temporary Hit Points/healing are accepted through 11E–11H. Weapon
  damage now composes mitigation, optional buffer absorption, and HP in one atomic root.
- The accepted families add no C# rule logic, migration, live-state mutation, or automatic campaign
  upgrade. Long Rest expiry, damage events, 0-HP consequences, death saves, concentration, and
  non-weapon damage remain separately gated.
- The accepted [Parent 11 remaining-family gate map](adoption/evidence/DND-CODE-ADOPTION-SLICE-11-REMAINING-COMPLEX-FAMILY-GATES.md)
  gives every incomplete combat, progression, rest, spell, monster, magic-item, and Inspiration
  candidate an executable prerequisite. Those are independent product features, not pending import
  rows.
- The accepted [Parent 12 receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-12-RECEIPT.md) adds a
  repeatable full acceptance runner, fresh-host/replay/rollback evidence, attribution auditing, and
  a review-only pinned-upstream diff. The primary donor is unchanged; Foundry's reference-only
  branch has a 42-file review report and remains inactive. Slice 13 archive retirement is still
  separately gated.
- The accepted [Parent 13 receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-13-RECEIPT.md) closes
  archive maintenance in retained scope: retain all 737 files, remove none, keep zero runtime
  consumers, and preserve reproducible transformation/recovery evidence. The numbered adoption
  plan has no remaining implementation leaf.

## Active dependency plan

The cross-owner import/recovery work is owned by the
[D&D code-adoption dependency plan](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md). It records the reuse
ladder, donor boundaries, effort forecast, small-model assignments, acceleration tooling, and
confirmation gates.

New playable-character work is owned by the
[D&D 2024 character-creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md). Its
first species subslices are accepted: source-bound ability generation/background increases,
species-definition/selection planning, Human Skillful, recommended Versatile/Skilled, the Heroic
Inspiration presence/grant foundation, immutable standard-rest policy content, authenticated rest
start, and clock-derived activity/interruption progress through no-benefit duration readiness. No
later leaf is active, and CC2 remains open on automatic interruption adapters, finish/recovery,
Resourceful triggering, and final trait composition.
For a token-constrained first delivery, the
[basic character-creation MVP plan](DND2024-CHARACTER-CREATION-MVP-PLAN.md) provides one 5-8 EP
vertical slice to an explicitly `basic-playable` actor with unresolved entitlements recorded rather
than silently granted. That slice is accepted; it does not close the full-resolution plan.

## Delivery lanes

| Lane | Outcome | State |
| --- | --- | --- |
| Adoption foundation | Pinned donors, license/provenance ledger, four-way coverage inventory, test-only seam proof, reusable conformance tooling, staged content transformation, candidate dependency mapping/result-effect allowlisting, and generic impact/replay/rollback proof | Slices 0–7 accepted after Sol review and user acceptance |
| Native recovery | Re-adopt compatible archived components, mechanics, procedures, and tests in bounded feature families | Slice 8 accepted: exact 51-mechanic, 26-component-disposition, and 39-procedure matrix closure |
| Donor gap filling | Adapt pure derivations, planners, SRD content encodings, and golden tests only for uncovered behavior | Slice 9 accepted; all 17 candidate groups have closed dispositions |
| Playable vertical | Ability/D20, proficiency, AC/HP, weapons, damage, turn flow, and one fresh-host replayable encounter | accepted through Slice 7D |
| Breadth | Character progression, conditions, equipment, spells, monsters, and magic items in independent cohorts | Parents 10–11 accepted selected scope; every incomplete family has an explicit independent-feature gate |
| Maintenance | Pinned upstream-diff reports, attribution, conformance regression, and retained archive recovery | Slices 12–13 accepted; archive removal remains a separately confirmed future proposal |
| Character creation | Stateless, source-bound creation choices composed into one actor transaction | Basic-playable Soldier creation with any of twelve SRD level-1 classes is accepted with class-specific core state, campaign participation, replay/rollback, and an explicit no-behavior pending ledger; source-complete spell/exertion, finish/recovery, Resourceful, equipment, and remaining grants stay planned |

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
