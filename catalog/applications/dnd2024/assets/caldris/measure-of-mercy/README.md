# Measure of Mercy starting-area content packet

This directory is a retained, reviewable deployment packet for campaign
`campaign.caldris.measure-of-mercy`. It does not mutate the live database or publish a new
application generation by being present in the catalog.

`asset-import-manifest.json` is the deployment authority for eleven content-addressed PNG files,
their map ownership and normalized anchors, six previous active map revisions, fourteen new child
locations, and the required compare-and-swap operations. `prepared-situation.json` contains future
play material. It reuses three established clues without writing their records or relationships,
adds two distinct unrevealed GM clues, keeps its three scenes unused, and requires an explicit
knowledge admission for every player-orientation candidate to
`actor.caldris.ganji`. The full situation also has a canonical `game.core.world.secret` payload,
linked to the campaign, chapter, and world so the authorized knowledge owner can retrieve it.
`image-prompts.json` records the selected image-generation requests and output identities. The
selected atlas is `caldris-atlas-clean-v2.png`; it revises only the western Eredane landmass
against the retained Eredane regional reference. The superseded `caldris-atlas-clean-v1.png`
remains unchanged for review and recovery. Scope maps are independent north-up detail frames,
with the direct-parent terrain correspondences recorded in the manifest.

## Import boundary

1. Verify the live `dnd2024-main` state-space fingerprint and every existing component revision,
   schema hash, and old blob hash against the manifest and the preserved export named there.
2. Back up the database and blob store. Upload each PNG to content-addressed storage, then verify
   its SHA-256, byte length, MIME type, and dimensions before creating references.
3. Land the map owner's reviewed support for a map-owning `site` or `interior` parent. Resolve the
   `game.core.world.map.visual` schema mismatch described below before importing any map binding.
4. Create the fourteen location envelopes with expected absence. Apply their containment and
   `game.core.world.map.anchor` components through the existing typed ECS transaction owner.
5. Replace the six existing and add the two new map bindings using the manifest's exact component
   expectations. Player and DM variants use the same clean terrain master; labels and markers come
   from retained overlay records.
6. Preflight-read each illustration owner's `game.core.media.visual` component. The preserved
   baseline observed component absence at owner entity revision 1. If still absent, add the
   manifest's complete value with expected revision 0. If present, preserve its status and every
   attachment, append the new illustration once by SHA-256, and write with the freshly read
   component revision. A stale CAS must restart the read/merge. These are ordinary prepared
   location illustrations; attaching them records no active scene or played event.
7. Verify and reuse the three declared `clue.caldris.q01.*` records and their existing links without
   writing them. Import only the two distinct clue entities under `location.caldris.atlas` /
   `opening-clues`; their support links target the canonical
   `secret.caldris.quest.q01.the-thirteenth-bell`. Import the prepared-situation secret under
   `location.caldris.atlas` / `knowledge`. Use these exact reviewed containment values; the
   synchronization adapter must not infer a container. Do not admit knowledge, move a character,
   create a session or encounter, resolve a quest, or award a reward.
8. Read back blobs, entities, components, containments, relationships, map projections, and Player
   and DM audience results before selecting the new application generation.

If activation must be reversed, restore the six previous map values through new version-checked
operations. Remove only newly created, still-unreferenced records through supported operations.
Keep retained blobs and operation history.

## Contract dependency

The live registered `game.core.world.map.visual` schema identified in the preservation export
contains content-addressed image metadata. The current authored catalog schema contains only
`assetKey` and `alt`. The coordinator must select one contract and perform its reviewed migration
or adapter change before applying these map values. This packet records the live-compatible value
template but does not redefine or migrate the shared schema.
