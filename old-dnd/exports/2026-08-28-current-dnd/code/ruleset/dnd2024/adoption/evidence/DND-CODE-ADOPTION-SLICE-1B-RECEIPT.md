# D&D code-adoption Slice 1B corrective receipt — exact external evidence

Status: **accepted after corrective review 2026-08-25**
Implementation: [Slice 1B](../../DND-CODE-ADOPTION-SLICE-1B-IMPLEMENTATION.md)
Matrix: [coverage-matrix-1b.json](coverage-matrix-1b.json)
Source inventory: [slice-1b-source-inventory.json](slice-1b-source-inventory.json)

The corrected enrichment removed every broad/default donor and Foundry assignment. It records 23
exact donor-file candidates and 10 exact Foundry-file references whose explicit match tokens occur
in capability IDs. Each source-inventory entry records the pinned repository, commit, tree, exact
file, and Git blob. All other archive capabilities remain unmatched; Foundry remains `adopted: false`.

Matrix SHA-256: `2B07ED18F7C55FE116171DD4A70A2746D2B1542B71F15A192598C15418B5666B`.
Source-inventory SHA-256: `8301337667723A81F5250712C1B7C7B0B5D446557EC68E44E892E7167C5156F8`.
Two consecutive generations were identical and the enriched matrix passed the accepted schema.
No external code was executed or installed, no assets were imported, and no runtime state changed.
