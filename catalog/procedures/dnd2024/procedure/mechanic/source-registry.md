---
id: dnd2024.procedure.mechanic.source-registry
category: ruleset.dnd2024.core.governance.sources
name: Register an SRD source
governs: commit(kind: "component") declaring the source-registry definition; commit(kind: "effects") registering SRD document identity and licensing metadata
status: active
createdBy: "llm"
changeNote: "Created as Feature 2 Slice 1, the lowest dependency for source-cited character level and skill proficiency."
---

## Description
Defines the centralized, immutable source identity and CC-BY attribution record used by D&D 2024 rules-bearing data. Non-goals: rules execution, copied rules prose, actor state, and campaign content.

## Instructions
Source and attribution
- System Reference Document 5.2.1, Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd.
- Licensed under Creative Commons Attribution 4.0 International, https://creativecommons.org/licenses/by/4.0/legalcode.
- Required attribution: This work includes material from the System Reference Document 5.2 (“SRD 5.2”) by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

Purpose and explanation
This component gives every D&D rules-bearing record one stable source identity. A source reference names this entity plus a section locator, so corrections never silently change what an old ruling cited. It stores identity, licensing and locator policy only. It does not store rules prose, actor state, mechanics, campaign data or an interpretation of the SRD.

Dependencies
- The kernel entity/component model governed by procedure.world.model and procedure.world.change.
- The parent ruleset contract dnd2024.procedure.mechanic.ruleset.
- No other D&D game component.

Creation and data
1. Declare exactly one component definition, dnd2024.source.
2. Create permanent entity dnd2024.source.srd-5.2.1 and attach dnd2024.source in the same atomic effects commit.
3. Its component data is exactly: system, document, version, publisher, canonicalUrl, documentUrl, publishedOn, license, and locatorFormat. license is exactly id, url, and attribution.
4. Use these fixed values: system dnd2024; document System Reference Document; version 5.2.1; publisher Wizards of the Coast LLC; canonicalUrl https://www.dndbeyond.com/srd; documentUrl https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf; publishedOn 2025-05-01; license id CC-BY-4.0; license URL https://creativecommons.org/licenses/by/4.0/legalcode; and the attribution above.
5. locatorFormat is "section heading plus PDF page(s) when stable". A consumer cites it as {"sourceId":"dnd2024.source.srd-5.2.1","locator":"<section heading and optional PDF pages>"}.
6. This registry has no action input or output and no mechanic. It is read with query(kind: "entities", id: "dnd2024.source.srd-5.2.1") or discovered with withDefinitionId: "dnd2024.source".

Deterministic verification
- Query by permanent entity id and by component definition; both return one identical active entity.
- Assert every fixed field above, the exact attribution, and that no extra top-level or license fields exist.
- A duplicate entity.create dry run must fail and leave the stored component unchanged.
- The component must contain no rule text, actor field, dice expression, mechanic source, or campaign identifier.

Revision and retirement
The entity id and version meaning are permanent. A new SRD release gets a new source entity rather than changing this one. Correcting erroneous metadata requires reading the current entity, revising this contract, using component.merge or component.set deliberately, querying it back, and recording the operation id. Never retire a source that is still referenced by stored rules or audits.

## Constraints
- dnd2024.source has one responsibility: source identity, licensing, URLs and locator policy.
- dnd2024.source.srd-5.2.1 always means SRD 5.2.1; it is never repointed to a later document.
- Component data is a closed object with the nine named top-level fields and three named license fields.
- The attribution and license URL must be stored exactly as specified by the official SRD.
- Do not copy rules prose into this component or use it as an executable mechanic.
- Do not store campaign state, actor data, interpretations, derived values or non-SRD material here.
- Unknown source ids never fall back to this entity implicitly; consumers must fail validation.
- No repository payload is authoritative for this game component; the live database contract, component definition and entity are the runtime source of truth.
