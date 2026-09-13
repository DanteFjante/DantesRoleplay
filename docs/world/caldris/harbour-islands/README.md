# Caldris harbour islands

Authored maps and live world content for Tidecross and the Halfway Light.

- [Tidecross](Tidecross.md) is a major port of call between Eredane and Solasca: twelve mapped places, five lore entries and four historical milestones.
- [The Halfway Light](Halfway-Light.md) remains the jointly maintained lighthouse island in Morrow Strait: eight mapped locations and nine walking connections.
- Tidecross's world manifest also corrects the Lantern Sea atlas anchor from (510, 570) to (570, 560), placing it in water. Tidecross's atlas anchor is (645, 755).

The PNG files are the exact bytes admitted to the live game's content-addressed blob store. The matching text files retain the final built-in image-generation prompts. Each guide links its image and prompt.

## Stored world content

`*-world-manifest.json` preserves the reviewed, successfully applied world-state synchronization request. `*-live-records.json` preserves the resulting entity/component values and registered schema versions read back from the server. Every requested component value matched the corresponding manifest. The manifest relationships were applied atomically with the entities.

These are bounded authored-content captures, not a replacement for the complete database export. They contain no upload credentials, server configuration, browser sessions, or unrelated game records. The running SQLite database remains authoritative.

To install this content elsewhere, first inspect that world's existing entities, binding, schemas and revisions. Admit the PNG bytes through the normal blob-upload API and verify their SHA-256 values. Adapt the manifests to that installation's exact revisions and use a new request token, preview through `system.world-state.sync`, then apply the identical reviewed payload. Do not blindly replay an original manifest against an existing world. Names, coordinates, text and media references are authored content; the original expected revisions and request tokens describe the completed synchronization.

Both maps were verified on the public Player website. Tidecross displayed twelve places and its market detail; all four historical entries appeared in History. The Halfway Light displayed eight places, its beacon detail, and the complete map at phone width.
