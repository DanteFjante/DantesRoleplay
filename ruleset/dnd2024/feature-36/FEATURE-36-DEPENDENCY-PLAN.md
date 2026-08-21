# Feature 36 dependency plan — experience and governed level advancement

Status: **Slice 1 implemented; Campaign C14 policy/authorization, Feature 27 class-level resolution, and Character CH9 remain required before any campaign award or level-up can be implemented.**
Last updated: 2026-08-21

## Execution rule

This is a repository planning artifact under `AGENTS.md`, `procedure.system.create-feature`, the
Terra planning guide, [CAMPAIGN_CREATION_PLAN.md](../../../CAMPAIGN_CREATION_PLAN.md), and
[CHARACTER_CREATION_PLAN.md](../../../CHARACTER_CREATION_PLAN.md). It creates no runtime game artifact
or persistent database state. A later pass implements one verified slice, runs disposable catalog
validation and focused tests, records a receipt, and stops; persistent import needs separate
integration-play/release authority.

## Target capability

In a campaign that has explicitly chosen XP or milestone advancement, an active character can become
eligible for exactly its next total level, then complete one source-backed CH9 level-up only when
its campaign authorization and all class/feature owners succeed atomically.

### Included

- D&D 2024 XP total recording, trusted campaign XP awards, and a read-only next-level eligibility
  result for the XP policy.
- Campaign C14's shared exact-next-level authorization seam for both XP and milestone policies.
- CH9's future one-at-a-time class/total-level transaction through Feature 27 class, Hit Point,
  feature, grant, and choice owners.
- A first bounded level-1-to-2, single-class, non-spellcasting acceptance fixture.

### Excluded

- Automatic level-up, XP loss/spending, encounter/monster XP calculation, party split formulas,
  quest/chapter completion rewards, treasure rewards, post-level-20 feats, respec/downgrade,
  class/source content expansion, multiclassing, subclasses, feats/ASIs, spellcasting, rest/Hit
  Dice recovery, browser workflow, player authorization, or a new transport/tool.

## Official source basis

The registered `source.dnd2024.srd-5.2.1` is the source identity. The official 2024 Basic Rules,
*Character Creation > Level Advancement*, establish that XP is a character total, level thresholds
are based on **total** character level, and reaching a threshold makes the character capable of a
level. The same source assigns class choice, Hit Point/Hit Die adjustment, features, and derived
proficiency changes to the level-gain sequence. Feature 36 uses the threshold table as a derived
eligibility reader; it does not copy derived proficiency, class level, HP, or feature state into
XP data. [Official source](https://www.dndbeyond.com/sources/dnd/br-2024/creating-a-character/).

The threshold table is fixed by source for this ruleset: 0 at level 1, 300 at 2, 900 at 3, 2,700 at
4, 6,500 at 5, 14,000 at 6, 23,000 at 7, 34,000 at 8, 48,000 at 9, 64,000 at 10, 85,000 at 11,
100,000 at 12, 120,000 at 13, 140,000 at 14, 165,000 at 15, 195,000 at 16, 225,000 at 17,
265,000 at 18, 305,000 at 19, and 355,000 at 20. XP never decreases on gaining a level.

## Verified dependencies and overlap result

| Dependency | Evidence and boundary |
| --- | --- |
| Total level / Proficiency Bonus | `dnd2024.character-level` is total level 1–20; PB is derived. Its recorder is administrative only and expressly excludes XP/level-up. |
| Character level-up root | Character CH9 is a detailed planned `1→2` transaction. It requires campaign authorization, Feature 27 class/HP owners, immutable declarations, and receipts; it excludes XP/milestone policy. |
| Campaign continuity | C2/C3 own campaign roots, transactions, chapters/arcs, and audit patterns. They do not own characters or advancement policy. |
| Campaign authorization | C14 is newly planned in [its companion plan](../../../campaign/feature-14/CAMPAIGN-FEATURE-14-ADVANCEMENT-AUTHORIZATION-PLAN.md). C15 Slice 1/2 now supplies its campaign-bound active-character scope; C14 still owns policy and one-time authorization, never XP math or class effects. |
| Class/level semantics | Roadmap Feature 27 is planned and has no verified class-level/HP/feature resolver contract yet. This is a hard blocker. |
| Character content/lifecycle | CH4 gives level-one membership; CH5/CH6/CH7/CH13 provide planned actor attachment/lifecycle/evidence prerequisites. Their actual current scope must be re-read before writing. |
| Source registry | `procedure.mechanic.dnd2024.source-registry` already owns the SRD 5.2.1 identity and locator format. No new source entity is needed for this CC-BY source. |

## Recursive dependency analysis

```text
Feature 36: XP/milestone eligibility and governed level advancement       [blocked parent]
├─ official XP thresholds and level-gain order                             [implemented source basis]
├─ total-level component / derived PB                                      [implemented]
├─ C14 policy + one-time authorization                                    [missing campaign leaf]
│  └─ campaign-bound active character lifecycle                           [missing character evidence]
├─ XP total, writer, award receipt, and threshold reader                  [missing Feature 36 Slice 1]
├─ Feature 27 class-level / HP / feature resolver                         [missing ruleset leaf]
├─ CH9 source declaration, receipt, and atomic character-level root       [blocked character parent]
│  ├─ C14 atomic consumption seam                                          [blocked campaign leaf]
│  └─ played CH6/CH7 evidence                                              [missing character evidence]
└─ level 1→2 first fixture                                                 [blocked acceptance parent]
```

## Dependency and ownership decisions

1. **XP total belongs to the character.** Proposed `dnd2024.character-experience` contains only
   nonnegative safe-integer `total` and fixed source reference. It has no campaign id, class level,
   threshold, eligibility, award history, spend/remaining amount, or policy field. Missing means
   XP is unknown/unconfigured; zero is an explicit level-one total.
2. **XP awards and milestones are campaign facts.** C14 validates campaign scope and records the
   award/authorization evidence. A campaign never copies a character's XP total, and an actor never
   carries a campaign policy or authorization list.
3. **Eligibility is derived, never stored.** Feature 36 reads total XP plus authoritative total
   level and returns the exact next threshold/result. It does not change level, mint authorizations,
   or cache `eligible: true`.
4. **Level-up is CH9 only.** CH9 receives one available C14 authorization for exact `N→N+1` and
   invokes Feature 27/other actual owners. It must not call the administrative level recorder with
   a caller-supplied replacement level.
5. **No automatic transition.** A threshold/read result and C14 authorization merely enable a
   deliberate CH9 validate/advance request. The player’s class/feature choice stays inside the
   supported CH9 source declaration; no reward or XP action selects it.
6. **One policy at a time.** `xp` campaigns reject milestone issuance; milestone campaigns have no
   XP award/eligibility route. Changing policy requires a later migration that explains active
   authorizations and existing XP, rather than a normal mutable setting.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 0 | C14 semantic confirmation | C2/C3 and CH5/CH6 attachment/transaction evidence re-read | Campaign policy/authorization vocabulary, actor scope, atomic consume seam, and policy-switch rule are confirmed; no runtime artifact. |
| 1 | Character XP state and eligibility reader | Existing total-level and source-registry contracts plus the exact source locator confirmed | Closed XP record/correct/read paths derive threshold/next-level eligibility with zero effects; no campaign award or level change. This foundational slice does not depend on campaign-character attachment. |
| 2 | Campaign XP award to authorization bridge | C14 Slice 1 and Feature 36 Slice 1 verified | One atomic campaign award updates character XP through its owner and produces at most one exact C14 authorization; below-threshold/duplicate/replay paths do not. |
| 3 | Milestone authorization | C14 Slice 1 verified | A milestone campaign issues/revokes the same one-time exact authorization with zero XP and zero character level effects. |
| 4 | One governed 1→2 level-up | Feature 27, CH9, and Slice 2 or 3 verified | One supported active character completes CH9's source-backed 1→2 transition; authorization consumption and every owner effect roll back together. |
| 5 | Expansion | Slice 4 accepted | One additional source-backed level/class/choice only through an amended Feature 27/CH9 plan. |

## Slice 1 — character XP state and eligibility reader

Implementation evidence is recorded in [the Slice 1 receipt](FEATURE-36-SLICE-1-RECEIPT.md).
The focused tests and disposable catalog validation pass. The attempted complete-suite run exposed
an unrelated, intermittent exact-cancellation-subclass assertion in `SessionFeature1Tests`; its
isolated rerun passed. Do not use that result to begin a dependent slice before the shared suite
has a clean baseline.

### Runtime artifacts proposed for confirmation

- `procedure.mechanic.dnd2024.character-experience`, under
  `ruleset.dnd2024.core.advancement.experience`, governing XP state and its read/write mechanics.
- `dnd2024.character-experience` closed component/schema.
- `mechanic.dnd2024.character-experience.write` for administrative record/correct only, and
  `mechanic.dnd2024.character-experience.read` for effect-free diagnostics/eligibility.

These are permanent IDs and schema meanings. The approved Feature 36 implementation boundary
authorizes this independent foundational slice. It must remain unable to award XP, mint a campaign
authorization, or replace a character level; those integration callers remain blocked on C14 and
CH9.

### Data/input and algorithm

The writer accepts exactly `{ mode: "record" | "correct", total: nonnegative safe integer }` on a
character role. `record` requires absence; `correct` requires valid existing state. It fixes
`{ sourceId: "source.dnd2024.srd-5.2.1", locator: "Character Creation > Level Advancement" }`.
It accepts no award delta, campaign id, policy, threshold, target level, class, source payload,
reason, effects, or authorization.

The reader accepts `{}` and reports `present`, `valid`, `total`, current total level, and a derived
status: `below-next-threshold`, `eligible-for-next-level`, or `at-level-cap`. For a valid level
`N < 20`, it derives only the fixed threshold for `N+1`; `eligible` means `total >= threshold`.
For level 20 it returns cap status with no next threshold/level. Missing/malformed XP or level state
is diagnostic invalid/unknown, never zero/default/eligible. It applies zero effects and uses no
randomness.

### Acceptance matrix

| Case | Assertion |
| --- | --- |
| Record/correct | 0 and 300 record canonical source-backed state; correct replaces exactly one valid state. |
| Threshold boundaries | At levels 1, 4, 5, 19, and 20, prove one XP below threshold, exact threshold, and above threshold produce the correct next-level/cap result. |
| Differential | Identical level-one characters at 299 and 300 XP differ only in eligibility for level 2. |
| Closed input/state | Negative/fractional/unsafe/string/null/extra values, caller threshold/level/campaign/effects, duplicate record, absent correct, and corrupt stored XP/level reject or diagnose without effects. |
| Source and cap | Wrong source locator fails; level 20 with 355,000 or higher is cap, not an authorization or level 21. |
| Determinism/routing | Equivalent reads are byte-identical and effect-free; administrative phrases do not capture campaign award or CH9 level-up phrases. |
| Integrity | Record/correct changes only XP; all failed operations leave exact actor bytes/revision unchanged; disposable corrupt fixtures are deleted/restored. |

### Slice 1 exit gate

XP can be safely recorded and inspected as character state, while every level/class/campaign fact
remains unchanged. Catalog validation, focused tests, full suite, and a receipt pass. Stop before
campaign award, authorization creation, or level-up.

## Slice 2–4 integration rules

**Slice 2** uses a C14 governed campaign award action with a positive award amount and a closed
recipient set resolved from campaign attachment. It must call the XP owner exactly once per
recipient, reject policy/actor/state drift before any effect, retain XP awards above a threshold,
and ask C14—not Feature 36—to create/reuse one authorization. The source does not prescribe monster
XP or party split arithmetic, so the first campaign award amount is trusted-host policy input and
must be separately auditable.

**Slice 3** proves the milestone branch without an XP component/effect. Milestone issuance is a
trusted-host campaign decision, not a hidden outcome of a quest, chapter, world clock, or AI.

**Slice 4** reuses CH9's exact `validate`/`advance` input and C14's atomic consume. The supported
class resolver derives class level, HP/Hit Die choice, features, grants, and total-level transition;
the XP reader/C14 authorization never supplies those values. One child failure rolls back XP award
when it shares the root, authorization consumption, all character effects, grant receipts, CH9
receipt, events, and success audit according to the confirmed root design.

## Plan-quality audit

- One player outcome with explicit policy, XP, and level-up boundaries: **yes**.
- Official source/version/locator and exact threshold table: **yes**.
- Existing total-level, CH9, campaign, source-registry, and character-plan owners searched: **yes**.
- Every missing owner expanded: **yes** — C14, XP state/reader, Feature 27, CH9, and character attachment evidence.
- State/derived/transient ownership and no-duplicate invariants: **yes**.
- One lowest runtime slice after dependency re-check: **yes — Slice 1 XP state and eligibility reader**.
- Closed inputs, threshold boundaries, replay, cap, policy routing, atomicity, restoration, and repository gates: **yes**.
- Runtime artifacts created by this planning pass: **none**.

## Plan-change rule

Revise before implementation if Feature 27 chooses different class-level/HP ownership, CH9 changes
its authorization seam, campaign membership is not a stable actor relation, the official source
changes thresholds, or a campaign needs party-level/shared XP. Do not work around those changes with
an actor campaign field, stored eligibility, a copied threshold table in campaign state, a free-text
milestone reason, or a direct total-level replacement.
