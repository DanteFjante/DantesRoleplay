# D&D code-adoption Slice 12C receipt — attribution and pinned-upstream review

Date: 2026-08-27  
Status: **accepted**

## Accepted boundary

- `New-UpstreamDiffReport.ps1` compares exact pinned/candidate commits in verified unique
  OS-temporary repositories without a checkout, submodules, donor execution, import, or activation.
- `Test-UpstreamDiffWorkflow.ps1` proves changed and unchanged results, deterministic reports,
  exact-commit rejection, lock-overwrite rejection, and donor-lock immutability using a local Git
  fixture with no network dependency.
- `Test-AdoptionAttribution.ps1` proves exact nonfloating pins, safe lock policy, required SRD/CC BY
  and MIT notice text, and the Foundry reference-only provenance boundary.
- The current review artifact is `adoption/evidence/upstream-diff-2026-08-27.json`.

## Verification and current upstream state

- Offline workflow: passed; **2** negative cases; repeated report bytes were identical; lock did not
  change.
- Attribution: passed for **2** locked sources and **3** provenance files; donor lock SHA-256
  `43A3980EC299D57501135B48DEDB70B7B8A77FEC7716DE20E8A566A14CB9F468`.
- `dnd-srd-engine`: branch `main` still resolves to the exact pinned commit
  `ead852b19b9e45f54f43e193caf4f10aad91a91b`; status `unchanged`; **0** changed files.
- Foundry dnd5e: branch `6.0.x` resolves to
  `0d081cd457c2197c312fd62051c226d1cbbc0335`; status `review-required`; **42** changed files; the
  five configured license/readme/package/system fingerprints remain unchanged.
- The report confirms `automaticActivation: false`, `lockChanged: false`, and
  `runtimeWrites: "none"`; its lock hash matches the current lock byte-for-byte.

## Review disposition

Foundry remains reference-only. Its changed branch is maintenance information for a future explicit
review, not a request to update the pin or import behavior. The unchanged primary donor requires no
new adoption work.
