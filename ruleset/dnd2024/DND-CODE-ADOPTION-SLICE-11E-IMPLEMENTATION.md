# D&D code-adoption Slice 11E implementation — Temporary HP and healing decision

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), Temporary HP/healing 11E  
Ruleset alignment: `dnd2024-owned`  
Source ID and locators: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing > Healing`
(PDF p. 17) and `Temporary Hit Points` (PDF p. 18)  
Outcome: fix the owners, dependency graph, archive reuse, and transaction boundary for the next
dependency-ready complex family.  
Exclusions: Long Rest orchestration, dying/death state, conditions, healing sources, damage events,
non-weapon damage, concentration, migrations, public operations, and production C#.  
Allowed files/areas: this document, Parent 11 design/status, source/reference evidence, and receipt.  
Stop point: accepted semantic boundary only; runtime records begin in 11F.

## Rule and owner decision

| Rule concern | SRD 5.2.1 meaning | Current/recovered owner decision |
| --- | --- | --- |
| Healing | add a positive healing amount to current HP, capped at maximum | adapt archived `mechanic.dnd2024.healing.apply`; keep `dnd2024.hit-points` authoritative |
| Temporary HP state | a separate positive buffer; absence means none | recover archived `dnd2024.temporary-hit-points` schema and writer under the current application source |
| Non-stacking choice | the recipient chooses the existing or incoming buffer, even when the incoming amount is lower | keep the archived explicit `onExisting: keep|replace` transition; never derive the choice |
| Damage absorption | lose Temporary HP before actual HP; carry the remainder into HP | revise the existing root `mechanic.dnd2024.weapon-damage.apply` after mitigation |
| Duration | depleted or Long Rest | removal on depletion is in this family; generic Long Rest expiry is deferred to the rest family |
| Separation | Temporary HP is neither HP nor healing and cannot revive a creature at 0 HP | healing never reads/writes Temporary HP; granting Temporary HP never reads/writes HP |

The application action runner rejects application event/notification output. The archived
`dnd2024.healing.received` event is therefore not recovered in this family. Equivalent requested,
applied, discarded, before, and after values remain in the mechanic result and effect audit. This is
a host-capability adaptation, not an intentional difference from the SRD rule.

## Dependency and transaction graph

~~~text
dnd2024.temporary-hit-points
        -> mechanic.dnd2024.temporary-hit-points.write          (one add/set/remove or no change)

dnd2024.hit-points
        -> mechanic.dnd2024.healing.apply                       (one bounded HP set)

weapon damage roll + mitigation profile
        + optional dnd2024.temporary-hit-points on target
        -> mechanic.dnd2024.weapon-damage.apply                 (buffer effect, then optional HP set)
        -> one generic application action transaction/replay/audit
~~~

No state, RNG, event, persistence, or transaction owner is added. The target role declares both HP
and optional Temporary HP; component absence is an expected projected state, not an undeclared read.

## Archive reuse decision

- **Keep/adapt:** the archived component ID/schema and writer encode the SRD 5.2.1 positive-buffer,
  explicit-choice, and absence-at-zero invariants and use only supported component effects.
- **Adapt:** the archived healing mechanic retains its bounded arithmetic and result fields but emits
  no unsupported event.
- **Adapt:** the archived damage absorption vectors are composed after the already accepted current
  mitigation child rather than recovering the archive's older whole damage implementation.
- **Drop from runtime boundary:** archived event types, reducers, old world APIs, and campaign fixtures.

## External implementation reference

Pinned Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/documents/actor/actor.mjs` lines 742–807, calculates damage/healing, consumes Temporary HP
before HP, clamps HP, and only replaces Temporary HP when the incoming value is greater. The adopted
engineering lesson is to calculate the full change before one mutation boundary. DantesRoleplay
keeps the SRD-required recipient choice explicit rather than adopting Foundry's UI/default choice.
No Foundry code, hook, actor state, UI, asset, or runtime dependency is used.

## Acceptance evidence

- official SRD PDF: 364 pages, SHA-256
  `8974902D109D6E63672D7C490BDE9CCF052410503D9CFA768237154FBC5E3D87`;
- exact PDF p. 17 Healing and p. 18 Temporary Hit Points text inspected;
- archived Feature 16 receipts, component/schema, writer, healing mechanic, and focused tests reviewed;
- current HP, damage resolver, weapon-damage root, effect transaction, replay, and rollback owners
  inspected; and
- Foundry reference path/lines inspected at the pinned commit.

The user's standing approval for SRD-faithful core changes confirms reuse of the archived permanent
component/mechanic IDs. No live-state migration or campaign profile change is authorized.
