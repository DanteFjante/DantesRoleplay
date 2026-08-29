---
id: procedure.mechanic.dnd2024.item-definition
category: ruleset.dnd2024.core.data.item-definition
name: Author immutable D&D 2024 item definitions
governs: dnd2024.item-definition
status: active
---

## Description

Owns immutable source-cited physical facts and definition-level eligibility referenced by campaign
item instances.

## Instructions

Author each definition on a permanent versioned entity and have instances reference that exact ID.
Definitions in `dnd2024-core` must cite `source.dnd2024.srd-5.2.1`. Definitions supplied by an
optional source must cite that exact selected source ID and must never claim SRD provenance.

## Constraints

A correction creates a new versioned definition entity. Definitions store no custody, quantity,
current equipment state, price transaction, derived AC/burden, or combat result. Selecting an
optional definition source never changes an existing campaign's source profile or schema version.
