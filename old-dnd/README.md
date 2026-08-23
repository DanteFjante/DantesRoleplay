# Old D&D archive

This directory contains the retired D&D 2024 implementation that previously lived in active
repository paths. It is intentionally outside `catalog/`, `ruleset/`, and `src/game-adapters/` so
catalog discovery and source globs do not treat it as current system authority.

Original relative paths are preserved below this directory. For example:

- `ruleset/dnd2024/...` is archived at `old-dnd/ruleset/dnd2024/...`;
- `catalog/components/dnd2024.*` is archived at `old-dnd/catalog/components/dnd2024.*`;
- the quarantined compiled game layer is archived at
  `old-dnd/src/game-adapters/dantes-roleplay/...`; and
- focused legacy tests retain their original paths below `old-dnd/DantesRoleplay.Tests/`.

`catalog-manifest.pre-archive.json` is the untouched catalog manifest from immediately before the
move. The active `catalog/manifest.json` keeps the pre-existing non-D&D records and omits only paths
moved into this archive.

This archive is evidence and recovery material, not an authored catalog or build input. Do not edit
or import it in place. Restore a coherent slice to its original path, review it against the current
architecture, and synchronize it explicitly if any part is needed again.

Generic kernel code, architectural ratchets, and cross-subsystem plans that merely mention D&D as
an example or historical dependency remain in their active owner paths.
