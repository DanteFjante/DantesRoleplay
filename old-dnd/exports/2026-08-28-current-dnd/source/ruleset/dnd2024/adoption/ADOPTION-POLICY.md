# D&D 2024 selective code-adoption policy

Status: **accepted development policy**
Effective: 2026-08-25
Owner: [D&D code-adoption dependency plan](../DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Pinned sources: [donor lock](donor-lock.json)
Reproduced evidence: [Slice 0A baseline](evidence/donor-baseline-2026-08-25.json)

## Purpose and authority

This policy governs development-time recovery or adaptation of `old-dnd`, `dnd-srd-engine`, and
Foundry dnd5e material into the DantesRoleplay D&D 2024 application. It is an engineering gate, not
legal advice and not runtime/game-state authority.

Rule meaning comes only from the official SRD 5.2.1 record and an exact heading/page locator.
Repository code/tests are implementation evidence. The accepted application kernel, canonical
components, catalog JavaScript, typed effects, SQLite transaction, replay, and audit owners are not
replaced by donor architecture.

## Fixed source roles

| Source | Fixed role | Default disposition |
| --- | --- | --- |
| Current authored catalog/application owners | canonical native owner | retain |
| `old-dnd/` | first-party historical recovery evidence | candidate until exact revalidation |
| `dnd-srd-engine` at the donor lock | primary external engineering donor | candidate per exact file/symbol/content item |
| Foundry dnd5e at the donor lock | mature engineering reference | reference-only |
| `source.dnd2024.srd-5.2.1` | D&D 2024 rule/content authority | required exact locator for D&D-owned work |

No branch, tag, package version range, registry alias, latest release, or unpinned submodule can
replace the exact donor lock. A new upstream commit creates a new review; it never updates or
activates material automatically.

## Reuse precedence

1. Retain a current accepted owner.
2. Recover a compatible first-party archived implementation with its receipts/tests.
3. Adapt a pinned donor's independently portable pure code, content encoding, or test vector for a
   verified gap.
4. Use Foundry to inspect data flow and edge cases.
5. Implement natively from SRD 5.2.1 when no safe reusable candidate exists.

An external implementation's completeness, popularity, or passing tests never override an existing
owner or the SRD. The Slice 0A donor suite has recorded failures and dependency vulnerabilities; no
donor package is approved as a production dependency.

## License and attribution dispositions

Every candidate has one closed disposition in the
[provenance schema](contracts/provenance-ledger.schema.json):

| Disposition | Meaning | May become accepted? |
| --- | --- | --- |
| `first-party-recovery` | Exact `old-dnd` source with repository provenance | yes, after semantic/current-kernel revalidation |
| `approved-mit-software` | Exact source path/symbol is inside verified MIT software scope | yes, with notice preservation and transformation evidence |
| `approved-cc-by-srd-content` | Exact content item is independently verified in SRD 5.2.1 | yes, content data only, with attribution/change indication |
| `reference-only` | May inform design/tests but its bytes are not copied | no imported target bytes |
| `blocked-asset` | Artwork, icon, font, token, audio, video, or other separately licensed asset | no |
| `blocked-mixed-or-unknown` | License scope cannot be established per exact bytes | no |
| `blocked-non-srd-or-premium` | 2014-only, PHB/DMG/MM-only, premium-module, or otherwise outside confirmed SRD 5.2.1 | no |
| `rejected` | Duplicate, obsolete, unsafe, conflicting, or deliberately unused | no |

### Pinned standalone donor

The pinned `dnd-srd-engine` [LICENSE](https://github.com/greghcarr/dnd-srd-engine/blob/ead852b19b9e45f54f43e193caf4f10aad91a91b/LICENSE)
and [NOTICE](https://github.com/greghcarr/dnd-srd-engine/blob/ead852b19b9e45f54f43e193caf4f10aad91a91b/NOTICE)
separate MIT engine code from the CC BY 4.0 starter content pack and SRD reference submodule.

- MIT reuse requires exact path/symbol/hash, the MIT notice/copyright preservation record, and a
  transformation/target hash.
- Starter-pack content is not approved as a batch. Each item requires independent SRD 5.2.1
  verification, exact locator, required attribution, and change indication.
- An item the donor labels uncertain, non-SRD, PHB/DMG/MM-only, or user-supplied is blocked.
- Reference SRD Markdown may be used for source checking under CC BY; copying long rule prose into
  runtime code/data is not an implementation shortcut.

### Pinned Foundry reference

The pinned Foundry [LICENSE](https://github.com/foundryvtt/dnd5e/blob/275bed0be4ccfa15e6b3347acccb8da8784726d9/LICENSE.txt)
covers software under MIT, while its [README](https://github.com/foundryvtt/dnd5e/blob/275bed0be4ccfa15e6b3347acccb8da8784726d9/README.md)
identifies SRD content under CC BY and assets under separate terms.

- Foundry remains `reference-only` by default.
- Direct code reuse requires a later per-symbol review proving MIT scope, no Foundry-global/runtime
  dependency, preserved notice, and a better outcome than native/donor adaptation.
- `fonts/`, `icons/`, `tokens/`, artwork, media, premium compendium material, and any nested/mixed
  asset license are `blocked-asset`.
- Foundry's 2014 compatibility behavior is not D&D 2024 authority.

## Required provenance

Every recovered, generated, adapted, or rejected candidate records:

- candidate and intended target path/kind;
- exact repository/source key, 40-character commit, source path/symbol, SHA-256, and pinned lock;
- license classification/disposition, notice files, preservation, attribution, and change indication;
- alignment, edition, official SRD verification, source ID, and exact locator when rule/content owned;
- existing owner evidence, conflicts, dependencies, and reverse-impact expectations;
- transformation kind/description/tool/mapping hash and resulting target hash;
- executable tests/evidence hashes and semantic/license/Foundry-independence reviews; and
- lifecycle state: `candidate`, `blocked`, `reviewed`, `accepted`, or `rejected`.

Missing evidence blocks. A model cannot fill an unknown with an inference. Generated bytes remain a
candidate even when deterministic.

## Coverage matrix contract

Slice 1 will generate one row per capability using the
[coverage schema](contracts/coverage-matrix.schema.json). Each row joins current owner, archive
candidates, standalone donor candidates, exact SRD locator state, Foundry references, conflicts,
dependencies, tests, disposition, and an exact model/reasoning assignment.

The matrix is a planning inventory. It does not register IDs, decide rule meaning, or activate
sources. A missing row is not evidence that no owner exists.

## Prohibited transformations and targets

Reject or block:

- whole donor `CampaignState`, reducers, event log, persistence, transaction, RNG, ID, or handler
  closure as runtime authority;
- Node/npm/Zod/Immer/ULID or Foundry runtime dependencies in production;
- direct output to `catalog/` without a later active D&D-owned implementation slice;
- ruleset-specific C#, database columns, public protocol branches, or generic-kernel D&D IDs;
- opaque minified/bundled output whose source symbol/license/transformation cannot be traced;
- floating refs, missing hashes, missing source locator, caller-supplied derived authority, or hidden
  component/database reads;
- 2014 rule blending, optional/house rules without confirmation, or non-SRD/premium content; and
- automatic registration, precedence change, migration, activation, acceptance, or deletion.

## Review and activation gates

A candidate may be `reviewed` only after schema validation and required reviews/tests. It may be
`accepted` only in its own confirmed implementation slice after:

1. exact owner/source/license/SRD evidence;
2. closed input, result, effects, transaction, failures, replay, rollback, and compatibility;
3. Foundry review for D&D-owned behavior;
4. focused positive/negative/boundary/deterministic tests;
5. catalog validation and full-suite acceptance when catalog/runtime changes occur;
6. preserved notices/attribution in the delivered target; and
7. explicit confirmation for permanent IDs, schema meaning, migrations, public surface,
   cross-owner semantics, intentional differences, or feature completion.

Source-overlay precedence must be explicit and non-destructive. Removing an override may expose a
lower source, but no fallback source becomes effective without current activation/review evidence.

## Model assignments

- `gpt-5.6-luna` medium: deterministic inventories, frozen-schema transforms, fixtures, and
  homogeneous content candidates.
- `gpt-5.6-terra` high: ordinary adapters, wrappers, conformance tooling, and approved mappings.
- `gpt-5.6-sol` high/xhigh: licensing/authority, rule meaning, conflicts, complex composition,
  migrations, destructive work, and acceptance.

Luna/Terra output remains candidate material until the prescribed Sol/repository review gate. Model
assignment never grants authority to accept or activate.

