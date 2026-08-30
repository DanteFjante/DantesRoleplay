# Caldris Slice 7 implementation — Ganji narrative integration

Status: **accepted**
Owner/roadmap: Caldris character-linked hooks and application World state
Dependency tree/leaf: Caldris playable opening; character-linked narrative preparation
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: not applicable; no D&D rule is implemented
Outcome: store the approved Tensor Sect, its two Highmead locations, and three Ganji-linked quest
hooks while retaining Ganji's mechanical creation as a separate governed decision
Exclusions: actor creation, campaign participation, species rules, ability scores, class state,
proficiencies, equipment, supernatural prediction, active quest lifecycle, session state, and play
outcomes
Allowed areas: this document, `CALDRIS-GANJI-CHARACTER-INTEGRATION.md`, one additive runtime
manifest, one validation receipt, and one reviewed live `system.world-state.sync`
Stop point: the additive manifest commits and every added entity/component reads back, or the
governed preview proves a platform blocker without changing state

## Confirmed boundary

The user's request to add Ganji's approved background to the game and repository confirms the new
permanent Tensor, location, knowledge, and character-hook IDs in this slice. The request does not
resolve Ganji's mechanical species representation, background, ability assignment, languages,
tool choice, or starting equipment.

`actor.caldris.ganji` is deliberately not created by World authoring. The D&D basic-character
mechanic requires its reserved actor ID to be absent and owns actor creation, derived state,
campaign participation, and rollback. Creating a narrative actor early would block that atomic
path. The reviewed prose remains ready for the character record once the missing player choices are
made.

## Authoritative owners

- `procedure.system.use` owns the reviewed dry-run/identical-commit/readback workflow.
- `procedure.game.core.world.location` owns the two nested Highmead locations.
- `procedure.game.core.world.faction` owns the Tensor Sect state and its World/location links.
- `procedure.game.core.world.knowledge` owns the public sect fact and GM-only character hooks.
- `procedure.character.playtest-bootstrap` and
  `dnd2024.procedure.mechanic.character-basic-create` prove why World authoring must not fabricate
  the playable actor or participation.

## Acceptance

- all new entity IDs are absent and every referenced Caldris endpoint exists before the write;
- the exact repository manifest parses and passes `system.world-state.sync` dry run;
- the byte-identical payload commits atomically;
- every created entity and component reads back at revision 1;
- the Tensor Sect and its locations are public World material, while the personal hooks remain
  GM-only preparation;
- no actor, participation, D&D rule state, quest progress, or played outcome is created.

## Completion evidence

Application Kernel Slice 12B repaired mature-root synchronization without changing the public
manifest or relaxing World scope. The exact manifest then passed dry run and committed atomically.
Live readback verified all eight entities, their components and containments, and all twelve
relationships. Ganji's playable actor remains the separate character-creation boundary described
above.
