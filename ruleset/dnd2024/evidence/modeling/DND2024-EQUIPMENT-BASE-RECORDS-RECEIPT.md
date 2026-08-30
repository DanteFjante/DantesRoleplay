# DND2024-EQUIPMENT-BASE-RECORDS V1 completion receipt

Status: **complete**
Implementation document: `DND2024-EQUIPMENT-BASE-RECORDS-IMPLEMENTATION.md`
Dependency tree/leaf: `DND2024-SRD-RECORD-INVENTORY-DEPENDENCY-TREE.md`, leaf 5
Source: `source.dnd2024.srd-5.2.1`

Materialized 123 equipment entries with candidate archetype `dnd2024.archetype.item-definition` as
one deterministic JSON entity per file under `prototype/dnd2024/records/equipment/base/`. Every
record carries source citation, active revision metadata, and preserved inventory notes where
available. Specialized equipment components remain a separate extraction slice.

Verification: `node tools/convert-equipment-base-inventory.js --write` succeeded and `npm test`
passed 22/22.
