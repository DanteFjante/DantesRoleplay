# D&D code-adoption Slice 11A implementation — damage-mitigation rule and owner decision

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), damage-mitigation 11A  
Ruleset alignment: `dnd2024-owned`  
Source ID and locators: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing >
Resistance and Vulnerability > No Stacking/Order of Application` and `Immunity` (PDF p. 17), plus
`Rules Glossary > Petrified > Resist Damage` (PDF p. 186)  
Outcome: freeze the exact rule, state, dependency, reuse, and transaction decisions for the first
Slice 11 family.  
Exclusions: runtime catalog edits, HP mutation, temporary HP, healing, damage events, 0-HP behavior,
death saves, concentration, damage adjustments, non-weapon causes, migrations, and public surfaces.  
Allowed files/areas: this document, Parent 11 design/status, source/reference evidence, roadmap,
dependency-plan status, and the 11A receipt.  
Stop point: one accepted decision that makes 11B authorable without guessing; no runtime change.

## Confirmed decisions

The user's standing approval authorizes SRD-faithful core work and requires non-SRD additions to stay
in separately selectable pre-campaign extensions. This leaf reuses the archived permanent IDs
`dnd2024.damage-mitigation`, `mechanic.dnd2024.damage-mitigation.write`, and
`mechanic.dnd2024.damage.resolve`; it does not create a new public operation or change live data.

The broad archived damage locator is corrected to exact SRD 5.2.1 headings/pages. Foundry is
reference-only. No Foundry code, data IDs, assets, hooks, bypass model, UI, or runtime dependency is
adopted.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Damage types | mitigation applies to named damage types | `dnd2024.weapon-profile` and existing combat mechanics | 11B uses the accepted thirteen-type canonical vocabulary; it introduces no generic-kernel enum |
| Resistance | matching damage is halved and rounded down; multiple instances count once | new recovered mitigation profile, later consumed by weapon damage | store membership once; 11C may halve a damage instance at most once |
| Vulnerability | matching damage is doubled; multiple instances count once; it follows Resistance | same | store membership once; 11C applies it after Resistance |
| Immunity | matching damage is not taken | same | 11C short-circuits the final amount to zero before HP mutation |
| Petrified | the condition grants Resistance to all damage | `dnd2024.conditions` through `mechanic.dnd2024.d20-test.state-effects` | resolver composes the current Condition projection and reports Petrified; it does not store a duplicate flag |
| Missing versus empty | no SRD rule; repository authority distinction | application ECS component presence | absent mitigation reports unknown; a present component with three empty lists reports known-empty |
| Cross-list overlap | independent grants can coexist; Immunity still means no matching damage is taken | profile plus later consumer | preserve independently known memberships; later arithmetic resolves Immunity first and never deletes stored state |

## External implementation reference

Pinned Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reviewed at:

- `module/data/actor/templates/traits.mjs` lines 23–36 and 107–132, which keeps separate damage
  resistance, immunity, and vulnerability traits and derives all-damage Resistance from Petrified;
- `module/data/actor/fields/damage-trait-field.mjs`, which provides the shared trait storage field;
  and
- `module/documents/actor/actor.mjs` lines 818–932, which calculates immunity, modifications,
  Resistance with integer truncation, and Vulnerability before applying HP changes.

Useful engineering evidence: keep canonical mitigation state separate from one damage instance,
derive condition-based mitigation before damage application, and finish damage calculation before
the HP mutation owner runs. Foundry's global runtime, hooks, mutable actor model, `ALL` sentinel,
bypasses, caller overrides, healing/temp-HP branches, thresholds, and overlap-cleanup behavior are
not copied.

## Prerequisite evidence

- [Pre-Slice 11 acceptance](adoption/evidence/DND-CODE-ADOPTION-PRE-SLICE-11-ACCEPTANCE.md) proves
  Slices 0–10 and the generic kernel are accepted.
- Current `dnd2024.hit-points`, `dnd2024.conditions`,
  `mechanic.dnd2024.d20-test.state-effects`, `mechanic.dnd2024.weapon-damage.roll`, and
  `mechanic.dnd2024.weapon-damage.apply` are the active owners.
- Application projection requirements, child composition, typed component effects, and the generic
  application action runner already own materialization, transactions, replay, and audit.
- Archived Feature 15 receipts/tests establish first-party recovery evidence; they are not runtime
  authority until the bounded 11B/11C leaves adopt them.

## Runtime artifacts

None in 11A. The next leaf may recover and adapt one component ID, two mechanic IDs, their procedure
owners, and focused tests. No C# production seam is authorized because current generic projection,
composition, JavaScript, effects, and transaction owners are sufficient.

## Authoritative state and closed input

11B will make the mitigation component the only stored base-profile authority. Its administrative
writer will accept a closed complete record/correct request. The resolver will accept exactly `{}`
and obtain Petrified through declared child composition. Callers may not supply source references,
Petrified, damage amount/type, arithmetic results, HP state, effects, or events.

## Behavior, result, and typed effects

11A adds no behavior. It fixes the later ordering as: damage adjustments remain outside this family;
then Immunity, one Resistance halving rounded down, and Vulnerability doubling. 11B stops at an
effect-free profile; 11C owns the single HP effect through the existing weapon-damage action.

## Failure, replay, and rollback contract

11B must reject malformed/noncanonical stored state and closed-input violations without effects.
Its writer must use exactly one `component.add` or `component.set`. 11C must preserve the current
single action transaction, operation-key replay, failed-effect rollback, and unchanged state on all
validation failures. No live-state upgrade is part of this family.

## Implementation sequence

1. Accept this source/owner/reuse decision.
2. Author 11B for storage, writer, dependency-aware profile composition, and focused tests.
3. Accept 11B before authoring 11C HP integration.
4. Run 11D family acceptance only after 11C is accepted.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Official source | exact official SRD 5.2.1 PDF URL, hash, headings, and pages recorded |
| Current owners | mitigation introduces no duplicate HP, Condition, damage roll, effect, transaction, or RNG owner |
| Archived reuse | exact reusable IDs/files named; semantic adaptations and exclusions explicit |
| Foundry | exact pinned paths and useful behavior recorded; no direct code/runtime adoption |
| State boundary | missing/known-empty semantics and Petrified dependency fixed |
| Transaction boundary | 11B effect-free profile; 11C existing action transaction and HP effect |
| Compatibility | no migration, source-profile change, public surface, or existing campaign mutation |

## Verification commands

- inspect the official `SRD_CC_v5.2.1.pdf` and record its SHA-256/page evidence;
- inspect the three pinned Foundry source paths at the exact commit;
- search current/archived component, mechanic, procedure, receipt, and test owners;
- `git diff --check -- ruleset/dnd2024`.

## Completion receipt and exit gate

Evidence is recorded in
`adoption/evidence/DND-CODE-ADOPTION-SLICE-11A-RECEIPT.md`. 11A ends before runtime edits; 11B is the
only ready implementation leaf.

