# D&D code-adoption dependency tree — recover and import licensed rules work

Status: **planning with accepted deliveries; Slices 0–6, 7A1–7A2, and 8 accepted; 7A3–7D verified after Sol review**
Ruleset alignment: **dnd2024-compatible adoption pipeline; every rule-bearing child slice is
dnd2024-owned**
Source: **not applicable to the pipeline itself**. Every rule-bearing child must cite
`source.dnd2024.srd-5.2.1` with an exact section/page locator before it becomes ready.
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)

## Outcome and non-goals

Build a repeatable, reviewable path that recovers compatible DantesRoleplay D&D code and selectively
adapts licensed external JavaScript/TypeScript, content encodings, and tests. The delivered D&D
application must run through the accepted application kernel and remain dynamically extensible
through registered components, declared dependencies, projections, catalog mechanics, and typed
effects.

The target is not a whole-engine port. It is a selective transplant:

~~~text
canonical application components
        -> declared mechanic projection / derived projection
        -> adapted catalog JavaScript planner
        -> DantesRoleplay result + typed effects
        -> one generic SQLite transaction and audit trail
~~~

Non-goals:

- do not run or persist a donor `CampaignState` as a second authority;
- do not adopt donor reducers, event log, persistence, IDs, RNG ownership, or transaction root;
- do not add Node.js, npm package resolution, Zod, Immer, or ULID as production runtime
  requirements for the sandbox;
- do not make Foundry VTT a runtime dependency or import Foundry-bound UI/runtime code;
- do not import artwork, icons, premium compendiums, non-SRD rulebook text, or content whose license
  is unclear;
- do not bulk-copy `old-dnd/`, silently reactivate every archived record, or treat archived files as
  current authority;
- do not replace an accepted native owner merely because a donor has a differently shaped model;
- do not blend 2014 behavior, optional rules, or house rules into the 2024 application; and
- do not delete the retained archive or migrate live state in this plan.

## Reuse decision

Use this precedence for every capability:

1. **Retain active native work.** Current application-kernel/catalog owners remain unchanged.
2. **Recover verified archived native work.** Prefer an accepted `old-dnd` mechanic and its tests
   when its SRD meaning, component schema, and effect contract still match.
3. **Adapt the standalone donor.** Use `dnd-srd-engine` for uncovered pure derivations, planners,
   schema ideas, SRD content encodings, and test scenarios.
4. **Consult the mature reference.** Use Foundry dnd5e to find edge cases and established data-flow
   patterns, not as executable code unless a tiny Foundry-independent MIT portion is separately
   justified.
5. **Implement from the SRD.** Write native catalog JavaScript/data only for gaps that cannot be
   safely recovered or adapted.

This preserves our old implementation. “Keep” means revalidate and adopt an exact subset into the
current application, not keep the archive on the runtime path.

## Donor assessment snapshot

| Candidate | Intended role | Evidence | Decision |
| --- | --- | --- | --- |
| Repository `old-dnd/` | First-party recovery source | 737 tracked files, including 86 catalog mechanic scripts, 48 component definitions, 52 schemas, 67 procedures, and about 90 C# test files; its archived roadmap records broad verified/accepted feature evidence | preferred when semantics still match; inventory and revalidation required |
| [`greghcarr/dnd-srd-engine`](https://github.com/greghcarr/dnd-srd-engine) | Primary external engineering donor | standalone event-sourced TypeScript engine; engine MIT, starter content CC BY 4.0; not published to a registry; assessed at commit `ead852b19b9e45f54f43e193caf4f10aad91a91b`, version `0.11.0-alpha.0` | pin exact commit; adapt selected code/data/tests, not the runtime architecture |
| [`foundryvtt/dnd5e`](https://github.com/foundryvtt/dnd5e) | Required mature engineering reference | MIT software and CC BY 4.0 SRD content, but much of the JavaScript depends on Foundry globals and assets have mixed licenses; proposed review pin is 6.0.x commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` | inspect per D&D-owned slice; no direct runtime dependency and no asset import |
| `source.dnd2024.srd-5.2.1` | Rule and content authority | archived source record names SRD 5.2.1, its official URL/PDF, publication date, CC BY 4.0 attribution, and heading-plus-page locator format | restore/reuse the existing record through a reviewed application source; every adopted rule gets an exact locator |

The 2026-08-23 local donor assessment ran the pinned standalone donor suite with 4,633 passing,
2 failing, and 173 skipped tests. Both observed failures were repository portability/documentation
problems rather than core rule failures, but the donor remains alpha software and its output is not
accepted as rule truth. Re-run the pinned suite in the first adoption slice and store durable
evidence before relying on it.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Application registration, exact source overlays, activation, trust, and hashes | `application-registry`, `application-source-registry`, `application-activation` | verified | [Application-kernel completion receipt](../../platform/application-kernel/receipts/APPLICATION-KERNEL-COMPLETION-RECEIPT.md) |
| Application component schemas and canonical state | `component-type-administration`, application ECS, SQLite | verified | Completion receipt; exact immutable type versions and schema hashes guard writes |
| Structural automapping and reverse impact | `projection-materialization` | verified | Versioned exact field/projection dependency graph, cycle rejection, bounded materialization, reverse-impact evidence |
| Mechanic-visible component projection | `procedure.mechanic.projection` and mechanic `requirements.roles` | verified | [Slice 11I receipt](../../platform/application-kernel/receipts/APPLICATION-KERNEL-SLICE-11I-RECEIPT.md) proves the current ratified mechanics need no compatibility projection |
| Exact application JavaScript execution | `application-execution` and bounded sandbox | verified | Completion receipt and Slice 11J parity evidence |
| Effects, transaction, replay, and audit | application ECS effects plus generic application action runner | verified | Completion receipt; canonical state is never owned by JavaScript or a model |
| Current game/world application sources | authored `catalog/`, ratified to `dnd2024` | verified but deliberately narrow | 33 accepted game component schemas and 14 mechanics are covered by catalog/application-kernel tests |
| D&D rule mechanics and content | application-owned catalog JavaScript/data | missing from the active catalog | the prior ruleset tree is retained only under `old-dnd/` |
| Prior D&D implementation | `old-dnd/catalog` plus archived receipts/tests | planned recovery source | uncompiled and intentionally retained; exact files require keep/adapt/replace/drop classification |
| External donor semantics | pinned donor commit and per-symbol provenance ledger | planned | donor owns whole-state/event-sourced shapes that conflict with our authority if imported unchanged |
| Official 2024 rule meaning | `source.dnd2024.srd-5.2.1` and exact locators | planned re-adoption | source record exists in the archive; per-rule locator review is still required |
| Foundry edge-case review | relevant Foundry paths at a pinned commit/branch | planned per rule cohort | required by D&D implementation protocol; it never replaces the SRD locator |
| Live application-state migration | explicit operator adoption/upgrade boundary | blocked and out of scope | non-empty upgrades require separate compatibility evidence; this plan changes no live database |

## Dependency tree

~~~text
Safely recover/import D&D 2024 code                                      [planned]
├─ A. Freeze authority and legal boundary                               [planned]
│  ├─ A1. Confirm selective-transplant policy and donor roles            [accepted]
│  ├─ A2. Pin exact donor commits/submodules and record licenses         [0A accepted]
│  └─ A3. Define per-file/symbol/content provenance + attribution ledger [0B accepted]
├─ B. Produce one four-way coverage matrix                              [source review remains]
│  ├─ B1. Active catalog/application owner inventory                     [1A accepted]
│  ├─ B2. Archived native keep/adapt/replace/drop inventory              [2A–2C classified and first cohort selected]
│  ├─ B3. Standalone donor capability/test/content inventory             [1B accepted]
│  └─ B4. SRD + Foundry evidence and 2014/non-SRD exclusion columns      [selected ability-check source reviewed; remaining rows planned]
├─ C. Prove the adapter seam without production activation              [Slice 3 accepted]
│  ├─ C1. Materialize an operation-specific donor-compatible view       [3A accepted]
│  ├─ C2. Run one pure donor/native rule with kernel-owned seeded RNG    [3B accepted]
│  ├─ C3. Normalize the effect-free result and prove parity              [3C accepted]
│  └─ C4. Prove no donor state/reducer/persistence/undeclared reads      [3C accepted]
├─ D. Build reusable adoption tooling                                   [partial]
│  ├─ D1. Deterministic schema/content transformer with dry-run diff     [5A–5C accepted]
│  ├─ D2. TypeScript-AST wrapper generator for sandbox-compatible JS     [planned]
│  ├─ D3. Test-vector converter and native/donor/SRD conformance runner  [4A–4C accepted]
│  └─ D4. Dependency/mapping manifest and reverse-impact checks          [planned]
├─ E. Recover already-proven native gameplay                            [blocked by C–D]
│  ├─ E1. Ability, D20, proficiency, saves, and Initiative cohort        [planned]
│  ├─ E2. HP, AC, weapons, damage, and one encounter cohort              [planned]
│  └─ E3. Turn flow, conditions, mitigation, healing, and inventory      [planned]
├─ F. Fill verified gaps from donor/SRD                                 [blocked by D–E]
│  ├─ F1. Pure derivations and character-sheet calculations              [planned]
│  ├─ F2. Character origin, class, feat, and advancement cohorts         [planned]
│  ├─ F3. Equipment, magic-item, and bestiary content cohorts            [planned]
│  ├─ F4. Combat timing, reactions, movement, and condition cohorts      [planned]
│  └─ F5. Spellcasting resources and spell-resolution cohorts            [planned]
└─ G. Accept and maintain                                               [blocked by E/F]
   ├─ G1. Fresh-host vertical play, replay, rollback, and parity          [planned]
   ├─ G2. Upstream pinned-diff report; never automatic activation         [planned]
   └─ G3. Optional archive retirement after independent acceptance        [blocked/destructive]
~~~

## State and bridge contract

The bridge is not a generic “pass campaign state in and take campaign state back” adapter. It is a
bounded operation adapter:

1. the mechanic declares exact roles, component fields, child projections, and derived projections;
2. the kernel materializes only that declared view from canonical application state;
3. a wrapper converts the view to the minimum donor function input and provides kernel-owned seeded
   randomness/content lookup;
4. adapted JavaScript returns a normalized result and proposed effects only;
5. the generic verifier rejects unknown results/effects, stale authority, undeclared access, or an
   unsupported donor event; and
6. one application action transaction applies typed effects, audits, and replays.

Components may depend on other projections for automapping. Exact dependency edges are registered
and cycle-checked. Changing a source schema or mapping must produce reverse-impact evidence naming
every affected projection/mechanic before activation. Generated output may be cached only as a
disposable revision-keyed optimization; canonical component state is always recomputed authority.

## Old implementation recovery rules

Each archived record gets exactly one disposition:

| Disposition | Use when | Required evidence |
| --- | --- | --- |
| Keep | rule meaning, ID, schema, projection, output, and effects already match the current kernel/SRD contract | archived receipt/tests plus fresh catalog, parity, and boundary validation |
| Adapt | D&D behavior is reusable but its state access, envelope, dependency declaration, or effect shape is legacy | exact semantic diff, transformed code, negative/no-change tests, and no duplicate owner |
| Replace | donor/native reimplementation is demonstrably safer or more complete | SRD locator, Foundry review, parity or intentional-difference confirmation, migration impact |
| Drop | duplicate, obsolete, 2014-only, non-SRD, unsafe C#, or no longer part of the product boundary | explicit reason; destructive deletion remains a separate confirmed slice |

Do not judge an archived feature only by filename or old roadmap status. Search its current owner,
read its receipt and tests, compare the exact component/effect contract, then decide. C# game-adapter
logic is evidence to translate, never rule code to recompile.

## Acceleration shortcuts

These shortcuts reduce repetition without transferring authority to generated code:

- Generate the four-way coverage matrix from manifests, exports, schemas, mechanic requirements,
  test names, and donor public exports. Humans review only conflicts and unmatched rows.
- Convert donor Zod/TypeScript shapes into candidate JSON Schemas and projection mappings, then
  validate/diff them against existing owners. Do not auto-register or auto-activate them.
- Use the TypeScript compiler AST and a pinned build to extract/wrap pure functions. Avoid manual
  copy/paste and regex rewrites; ship reviewed plain sandbox JavaScript, not a production Node
  dependency.
- Convert donor test cases into a neutral scenario format first. Run the same inputs/seeds through
  archived native, adapted donor, and final catalog mechanics; require either parity or an exact
  SRD-backed intentional-difference record.
- Use deterministic source-overlay precedence so reviewed native adaptations override staged donor
  candidates. Removing an override can reveal the lower source without deleting history.
- Import static SRD content in homogeneous cohorts with deterministic IDs, hashes, attribution, and
  dry-run diffs. Reject an entire cohort on one unknown license or source locator.
- Generate dependency edges from reviewed mapping manifests so reverse-impact queries expose the
  components/mechanics affected by a schema change.
- Keep a pinned upstream comparison report. A new upstream commit opens review work; it never
  changes production content automatically.
- Start with the archived Features 1–10 encounter vertical. It already has first-party behavior and
  tests, so it is a lower-risk proof than adapting a complex spell or whole character builder.

## Exact model assignment policy

Use the currently available GPT-5.6 family explicitly. OpenAI's
[model guidance](https://developers.openai.com/api/docs/models) describes
[`gpt-5.6-sol`](https://developers.openai.com/api/docs/models/gpt-5.6-sol) as the frontier choice for
complex reasoning/coding,
[`gpt-5.6-terra`](https://developers.openai.com/api/docs/models/gpt-5.6-terra) as the balance of
intelligence and cost, and
[`gpt-5.6-luna`](https://developers.openai.com/api/docs/models/gpt-5.6-luna) as the cost-sensitive,
high-volume choice. In this plan those roles become concrete assignments rather than quality labels.

| Model assignment | Reasoning effort | Use in this plan | Must not decide alone |
| --- | --- | --- | --- |
| `gpt-5.6-luna` | medium; high only for a failing deterministic task | inventories, generated coverage rows, frozen-schema transforms, fixture generation, repetitive golden-test conversion, and homogeneous static-content cohorts | rule meaning, schema semantics, owner conflicts, effect mapping, licensing exceptions, or acceptance |
| `gpt-5.6-terra` | high by default | ordinary implementation subslices, pure-function wrappers, archived JavaScript adaptation, conformance tooling, projection mappings after approval, and focused debugging | intentional SRD differences, cross-owner semantics, complex timing, migrations, destructive work, or final acceptance |
| `gpt-5.6-sol` | high for review; xhigh for cross-owner/complex rule design | donor policy, source/licensing review, rule and owner mapping, first seam design, complex combat/reaction/spell families, migration decisions, acceptance review, and resolving disagreements found by Luna/Terra | nothing is auto-approved; repository confirmation gates still apply |

Every Luna or Terra assignment receives one closed subslice, exact allowed files, frozen input/output
contracts, generated diffs where useful, and executable acceptance tests. Sol authors or reviews the
contract first when rule meaning, authority, transaction behavior, or licensing is involved.

The imported code does not automatically make every task Luna-safe. It makes repetitive work
Luna/Terra-safe after Sol has made the semantic boundary explicit.

## Effort scale and forecast

One effort point (EP) is one focused engineering/review block for a prepared slice, roughly two to
four hours of human-equivalent work. It includes implementation, focused tests, and concise evidence;
it is not elapsed model runtime or a delivery promise. Ranges expand when source behavior conflicts.

| Order | Leaf/slice | Depends on | EP | Primary model / review | Exit gate |
| ---: | --- | --- | ---: | --- | --- |
| 0 | Adoption policy, donor pins, licenses, and ledger format | existing kernel | 2–3 | `gpt-5.6-sol` high | **accepted** — exact allowed donor roles, commits, paths, licenses, and prohibited material confirmed; no runtime code |
| 1 | Four-way active/archive/donor/SRD coverage matrix | 0 | 3–5 | `gpt-5.6-luna` medium; Sol reviews conflicts | every capability has owner, status, source locator state, reuse disposition, tests, and dependencies |
| 2 | Archived native recovery classification | 1 | 3–5 | Luna inventories; `gpt-5.6-terra` high classifies; Sol resolves conflicts | Features/records selected for first vertical; no record activated or deleted |
| 3 | Test-only adapter seam on one simple D&D rule | 0–2 | 5–8 | `gpt-5.6-terra` high; Sol xhigh designs/reviews | same seed/input gives explained output; donor state and persistence stay absent; no production registration |
| 4 | Conformance/test-vector converter | 3 | 4–6 | Luna converts fixtures; Terra high builds runner | archived, donor, and adapted result can be compared with normalized intentional differences |
| 5 | Deterministic schema/content transformer and provenance manifest | 3 | 5–8 | Luna transforms frozen rows; Terra high builds tooling; Sol reviews semantics/licenses | dry run is stable; unknown license/source/ID collision rejects the whole batch |
| 6 | Projection/effect mapping template with impact evidence | 3–5 | 5–8 | Terra high implements; Sol xhigh approves boundary | **accepted** — exact candidate inputs/dependencies, static impact roots, closed proposal/effect template, and test-only generic impact/replay/rollback proof |
| 7 | Recover archived Features 1–10 playable encounter vertical | 4–6 | 12–20 | Luna handles fixtures; Terra high handles each mechanic; Sol reviews each family and acceptance | fresh application activation, valid/invalid action, transaction, replay, and fresh-host state proof |
| 8 | Recover later accepted native mechanics | 7 | 4–8 per family | Terra high; Luna for mechanical transforms; Sol reviews intentional differences | family-specific current SRD, owner, parity, and catalog acceptance |
| 9 | Import pure derivations/character calculations for verified gaps | 4–7 | 5–8 per cohort | Terra high; Luna for cases/fixtures; Sol reviews rule mapping | no stored derived authority; donor/native/SRD conformance passes |
| 10 | Import static SRD equipment/monster/spell/item records | 5–7 | 3–6 per homogeneous cohort | Luna medium; Terra reviews transforms; Sol only for source/license conflicts | exact CC BY attribution/locator, deterministic transform, schema/catalog validation |
| 11 | Adapt complex combat, progression, or spell behavior | 6–10 | 8–13 per mechanic family | `gpt-5.6-sol` xhigh; Terra high may implement frozen child tasks | rule-specific dependency tree, Foundry review, typed effects, timing, replay, rollback, compatibility |
| 12 | Full acceptance and pinned-upstream maintenance workflow | selected cohorts | 5–8 | Terra high runs evidence; Sol high accepts/resolves failures | fresh-host play, full suite, catalog validation, protocol walk when applicable, attribution audit |
| 13 | Optional archive retirement | replacement acceptance | 3–5 | `gpt-5.6-sol` high only | separately confirmed deletion with durable receipts and recovery path |

Forecasts before the coverage matrix:

- adoption foundation through reusable tooling: **24–38 EP**;
- minimum recovered playable encounter: **12–20 EP** after the foundation;
- each simple content/derivation cohort: **3–8 EP**;
- each complex mechanic family: **8–13 EP**, sometimes more when composition prerequisites are
  missing; and
- broad SRD breadth beyond the playable vertical: provisionally **45–80 EP**, driven mostly by
  spells, reactions, character progression, and effect wiring rather than data copying.

The planning hypothesis is a **35–60% reduction in remaining D&D-rules work versus rebuilding from
scratch**, provided the coverage matrix confirms that archived tests and donor pure functions are
compatible. Slice 3 must replace this hypothesis with measured conversion effort before broad
import is approved.

## Subslice structure

Yes: the numbered rows above are parent slices. Before implementation, each parent gets one or more
feature-implementation documents. A subslice must still have one owner, one alignment class, one
closed artifact/effect boundary, one transaction owner when it writes state, executable acceptance,
and an exact stop point. Do not divide work merely to create more documents; split whenever a row
contains more than one semantic decision or runtime artifact.

| Parent | Planned subslices | Default model |
| ---: | --- | --- |
| 0 | **0A accepted** — donor pins/build baseline; **0B accepted** — license/provenance policy and confirmation | Sol high |
| 1 | 1A active/archive inventory; 1B donor/SRD/Foundry inventory; 1C conflict and gap report | Luna medium; Sol reviews 1C |
| 2 | **2A–2C accepted** — Features 1–10 and later accepted-feature classification; first test-only cohort selection | Luna/Terra; Sol review at cohort activation |
| 3 | **accepted** — 3A operation-view mapping; 3B test-only pure-rule wrapper; 3C normalized parity and boundary proof | [3C receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-3C-RECEIPT.md) |
| 4 | **accepted** — 4A neutral scenario schema; 4B archive/donor/adapted converters with provenance; 4C deterministic conformance runner and intentional-difference gate | [4C receipt](adoption/conformance/evidence/DND-CODE-ADOPTION-SLICE-4C-RECEIPT.md) |
| 5 | **accepted** — 5A provenance/content manifest; 5B schema/content transformer; 5C dry-run collision/license rejection | [Sol-approved review packet](adoption/transformation/review/SOL-SLICE-5-REVIEW.md) |
| 6 | **accepted** — 6A candidate projection/dependency mapping/static root closure; 6B result/effect allowlist; 6C impact, replay, and rollback proof | Terra high; Sol xhigh review packet recorded for 6B |
| 7 | 7A1 raw ability-score/fixed-DC check; 7A2 proficiency/skills; 7A3 Advantage/Disadvantage; 7A4 saves; 7B Initiative/turn flow; 7C HP/AC/weapons/damage; 7D fresh-host encounter acceptance | Terra high; Luna fixtures; Sol reviews family gates and 7D |
| 8 | **accepted** — all 51 mechanics, 26 component dispositions, and 39 procedures in the accepted native-recovery matrix are resolved; see the [Parent 8 receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-8-RECEIPT.md) | Terra high implementation and full acceptance evidence |
| 9 | one derivation cohort per parent, split into mapping, wrapper, and conformance subslices when effects or multiple owners appear | Terra high; Luna cases; Sol reviews rule mapping |
| 10 | one homogeneous content cohort per parent, split only when schema, attribution, or source locators differ | Luna medium; Terra review |
| 11 | one complex mechanic family per parent: 11A rule/dependency decision, 11B primitive/effect seam, 11C behavior cohort, 11D family acceptance | Sol xhigh; Terra only after each contract freezes |
| 12 | 12A fresh-host play/replay; 12B full validation/protocol evidence; 12C attribution/upstream-diff workflow | Terra high; Sol acceptance |
| 13 | 13A retained-use inventory; 13B separately confirmed removal; 13C clean-build/recovery evidence | Sol high |

Rows 0–2 may remain small documentation/tooling subslices. Rows 3 and 6 must be divided because
mapping, execution/effects, and parity are different semantic boundaries. Rows 7–11 must never be
given to one model as a monolithic assignment; their cohort/family rows are scheduling containers.

## Conflicts and decisions

| Conflict | Required decision |
| --- | --- |
| Donor whole `CampaignState` vs canonical components | construct a minimum operation view; never persist or round-trip donor state |
| Donor events/reducers vs typed effects/SQLite | translate only a closed allowlist of events/results to existing typed effects; unsupported events fail |
| Donor RNG/content/handler closure vs kernel services | inject kernel-owned seeded RNG and reviewed immutable content through the wrapper |
| Donor IDs/schema vs existing owners | mapping manifest must prefer existing IDs and flag semantic collisions; no automatic alias |
| Archived mechanics vs current component qualification | keep only after exact type/version/projection/effect compatibility; otherwise adapt explicitly |
| Foundry globals and mixed assets | reference code paths/edge cases only; import no assets or Foundry-dependent module graph |
| Donor alpha/update drift | pin commits and hashes; updates create a diff report, never a floating dependency |
| SRD vs donor disagreement | SRD 5.2.1 wins; record an intentional-difference test and do not silently follow donor output |
| 2014/2024 dual behavior | 2024 only unless a separately confirmed compatibility feature owns the distinction |
| Deleting old functionality | defer until replacement acceptance and a separate destructive confirmation; source overlays allow non-destructive supersession first |

## Ordered leaves

The effort table is the delivery order. Leaves 0–6 create one reusable lane; Leaves 7–11 then repeat
by bounded feature family. A family may start only when its row in the coverage matrix names one
owner, one exact SRD locator, the applicable Foundry evidence, closed inputs, outputs/effects,
transaction owner, failures, replay, rollback, and compatibility expectations.

Do not batch unrelated families because they share a donor package. A spell cohort, for example,
may share a verified effect primitive, but it does not share one acceptance transaction with
character advancement or monster bootstrap.

## Lowest ready leaf

Slice 6 is accepted. Its three closed subslices add a candidate-only projection/dependency mapping,
a schema-pinned proposal/effect allowlist, and a read-only proof binding their exact hashes to the
existing generic reverse-impact, replay, and rollback behavior. It registers no production candidate
or D&D content. The [Slice 7 design](DND-CODE-ADOPTION-SLICE-7-DESIGN.md) separates the broad
7A label: 7A1 raw ability scores/fixed-DC checks is accepted; its
[receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-7A1-RECEIPT.md) records the catalog source,
activated-path verification, and user acceptance. 7A2 proficiency/skills is accepted. 7A3 explicit
Advantage/Disadvantage, saving throws, Initiative/turn flow, combat state, and fresh-host encounter
acceptance are verified after Sol review. Their exact delivered boundary and verification
are retained in the Slice 7B–7D implementation documents and receipt.

Slice 1 was accepted after corrective review. Its manifest-keyed matrix contains 271 capabilities:
127 exact active/archive matches and 144 archive-only gaps. It preserves historical tests,
dependencies, and archived SRD locator evidence; attaches only exact matched donor/Foundry files;
and leaves unmatched or unverified evidence explicit. Slices 2A–2B classified the historical
vertical and later accepted families as blocked archive-recovery candidates with explicit dependency
closures. Slice 2C selected Feature 1's ability-score/fixed-DC seam as the first test-only recovery
cohort, explicitly deferring the other candidates. The [Slice 3 design](DND-CODE-ADOPTION-SLICE-3-DESIGN.md)
divides the proof into 3A mapping, 3B wrapper, and 3C parity/isolation, with executable stop points.

The selected seam is an ability-score fixed-DC check with closed inputs of ability state, fixed DC,
and kernel-owned seeded RNG. The probe excludes skill proficiency, character level, conditions,
whole donor campaign state, and donor persistence, events, and reducers. Exact SRD 5.2.1 locators
are now verified at `Playing the Game > The Six Abilities > Ability Scores/Ability Modifiers` (PDF
pp. 5–6), `Playing the Game > D20 Tests > Ability Checks/Difficulty Class` (PDF p. 6), and the
attack-only `Rolling 20 or 1` rule (PDF p. 7). Foundry's pinned `module/dice/d20-roll.mjs` was
reviewed as reference-only. [Slice 3A](DND-CODE-ADOPTION-SLICE-3A-IMPLEMENTATION.md) is accepted;
its [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-3A-RECEIPT.md) records the mapping proof and
stops before JavaScript or any production registration.

## Confirmation gates

Confirmation is required before:

1. adopting the selective-transplant policy and exact donor commits/licenses;
2. creating a permanent vendor/staging directory, source registration, projection/mapping ID, or
   provenance schema;
3. changing an existing component schema meaning, mechanic output/effects, permanent ID, source
   precedence, or application manifest;
4. accepting any intentional difference between archived, donor, Foundry, and SRD behavior;
5. activating the first production D&D cohort or migrating non-empty application state;
6. adding/changing a public operation or protocol shape;
7. completing a feature family or the playable vertical; and
8. deleting/superseding archived functionality or removing compatibility records.

Routine generated transformations inside a confirmed mapping and active implementation slice do
not need repeated confirmation, but the generated output remains reviewable authored catalog
material.

## Planning and Slice 0 receipt

- Runtime artifacts created: none.
- Catalog/application/database/archive state changed: none.
- Permanent runtime IDs, schemas, mappings, sources, projections, migrations, public operations,
  and donor dependencies created: none.
- Development artifacts: exact donor lock/verifier/baseline, selective-adoption policy, provenance
  and coverage schemas/examples/validator, and [Slice 0 receipts](adoption/evidence/).
- Current worktree changes outside `ruleset/dnd2024/` were not modified.
