# D&D code adoption Slice 0B receipt — adoption and provenance contract

Status: **accepted**
Completed: 2026-08-25
Implementation: [Slice 0B](../../DND-CODE-ADOPTION-SLICE-0B-IMPLEMENTATION.md)
Policy: [Selective adoption policy](../ADOPTION-POLICY.md)

## Delivered

- Fixed current catalog, `old-dnd`, standalone donor, Foundry, and official SRD roles with
  non-destructive reuse precedence and no automatic import/activation.
- Defined closed provenance rows covering exact source commit/path/symbol/hash, target,
  license/disposition/notices/attribution/change indication, SRD alignment/locator, owners/conflicts,
  transformation, dependencies, tests, reviews, and lifecycle.
- Defined the four-way coverage-matrix contract for current/archive/donor/SRD/Foundry evidence,
  conflicts, dependencies, tests, disposition, and exact GPT-5.6 model/reasoning assignments.
- Blocked mixed/unknown licenses, assets, premium/non-SRD material, missing official locators,
  unsupported models, unresolved accepted conflicts, premature acceptance, and Foundry
  reference-only import.
- Added a development-only contract validator. No runtime package or production dependency was
  added.

## Verification

- Six contract/schema/example JSON documents parsed.
- Two Draft 2020-12 schemas compiled with independent `ajv-cli@5.0.0` validation.
- Two positive examples validated.
- Nine declared negative cases were rejected: prohibited asset, missing CC BY/SRD evidence, unknown
  license, premature acceptance, Foundry reference-only acceptance, unsupported model, missing D&D
  locator, ready row with conflicts/no passing tests, and donor disposition without a donor candidate.
- Slice 0A lock SHA-256 remains
  `43A3980EC299D57501135B48DEDB70B7B8A77FEC7716DE20E8A566A14CB9F468` and matches its durable
  baseline evidence.

## Deliberate exclusions

The real coverage matrix is not populated. No archived or donor candidate is classified, imported,
rewritten, registered, executed, activated, or accepted. No D&D rule, catalog/runtime file,
application source, database, migration, public operation, or `old-dnd` file changed. Slice 1A is
the next planned leaf.
