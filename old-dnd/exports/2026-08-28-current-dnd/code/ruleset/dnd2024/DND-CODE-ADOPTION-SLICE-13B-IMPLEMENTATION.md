# D&D code-adoption Slice 13B implementation — retain archive, remove nothing

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), archive-maintenance lane  
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 13B  
Ruleset alignment: `ruleset-neutral` disposition  
Source ID and locator: not applicable; no rule behavior changes  
Outcome: make the exact archive disposition explicit from the accepted 13A inventory.  
Exclusions: deletion, moves, edits under `old-dnd/`, replacement provenance, runtime/catalog
changes, and restoration into active paths.  
Allowed files/areas: this document and Slice 13B receipt.  
Stop point: every archive file has one retained disposition and the removal set is empty.

## Decision

Retain all 737 tracked archive files. Remove none.

This follows the existing explicit user decision to keep the old D&D implementation and the 13A
evidence that:

- production projects, active catalog, and compiled production source have zero archive consumers;
- accepted transformations still validate 43 exact archive source hashes;
- adoption classification tools and a compiled provenance/packaging test still read the archive;
- durable evidence and fixtures still cite original archive paths; and
- removing only unreferenced files would fragment coherent historical feature families without a
  runtime, storage, or maintenance benefit.

## Confirmation boundary

Because the removal set is empty, the destructive confirmation gate is not exercised. Any future
proposal to remove or supersede archive paths must author a new exact target list, replace every
consumer, demonstrate equivalent recovery evidence, and obtain separate confirmation. “Slice 13B
accepted” is not reusable deletion authority.

## Receipt

The disposition is recorded in `adoption/evidence/DND-CODE-ADOPTION-SLICE-13B-RECEIPT.md`.
