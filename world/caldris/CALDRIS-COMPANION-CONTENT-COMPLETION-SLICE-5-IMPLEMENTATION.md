# Caldris Slice 5 implementation — companion content completion

Status: **accepted**
Owner/roadmap: Caldris implementation map
Dependency tree: `CALDRIS-COMPANION-CONTENT-COMPLETION-DEPENDENCY.md`
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: not applicable
Outcome: import a large reviewed reference layer for play
Exclusions: D&D numbers, mechanics, media work, quest lifecycle, player characters, and played outcomes
Allowed areas: this plan/receipt, additive Caldris runtime manifests and builder, live reviewed sync
Stop point: all manifests commit and read back; no excluded owner changes

## Contract

Use only existing location, chronology, fact, secret, classification, containment, and knowledge
relationship owners. SQLite remains live authority. Each bounded manifest is parsed, previewed, then
committed byte-for-byte with the same payload. Invalid, stale, oversized, or cross-root batches fail
without change. No direct SQL is permitted.

## Content set

- remaining Bramblebridge sites and first-ring named places;
- polity dossiers, everyday law/work/trade, faith, waters, calendar, eras, and low-magic impact;
- campaign threads, volume guides, boss ladders, adventure bridges, consequence guidance, and cozy
  returns as GM-only reference.

## Acceptance

- every new ID is unique and every summary fits the existing schema;
- each location terminates beneath an existing Caldris container;
- each fact/secret has classification, containment, in-world, and about records;
- every dry run succeeds before any commit;
- live readback returns every added record and the website remains healthy;
- a short receipt records counts, operations, and deliberate exclusions.
