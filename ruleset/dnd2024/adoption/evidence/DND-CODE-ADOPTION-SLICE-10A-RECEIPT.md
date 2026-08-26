# D&D code-adoption Slice 10A receipt — SRD currency definitions

Date: 2026-08-26  
Status: **implemented and verified; acceptance pending user confirmation**  
Boundary: Parent 10 / first homogeneous static-content cohort

## Delivered

- Recovered the five existing permanent currency definition IDs for CP, SP, EP, GP, and PP under
  the activated D&D application source.
- Preserved the existing `dnd2024.item-definition` state shape, exact `1/50` pound mass, fungible
  stack policy, and copper values consumed by the accepted currency/burden readers.
- Added a hash-locked deterministic transform check over the archived inputs. It rejects shape,
  value, source, ID, path, and target drift.
- Corrected the archived generic locator to `Equipment > Coins > Coin Values (PDF p. 89)` and
  recorded the exact SRD 5.2.1 CC BY attribution and change indication.
- Added activated-source, bounded-schema, and runtime-consumption evidence without changing the
  generic kernel, public protocol, component schema, live database, or archive.

## Verification

- Currency cohort transform — passed, 5/5 hash-locked deterministic targets.
- Focused activated content/schema/currency/burden test — passed, 1/1.
- Full activated D&D suite — passed, 76/76.
- Existing content-transformation regression suite — passed at Stage 5C.
- Adoption contract suite — passed: 2 positive examples and 9 required rejections.
- Conformance tooling suite — passed at Stage 4C.
- Core catalog validation — passed, 144 records with the existing 21 advisory warnings; no live
  data was touched. Application-source preview/activation includes all five content records.
- Solution build — passed with 0 warnings and 0 errors.
- Full repository suite — passed, 1,087/1,087 plus 20/20 local-AI tests.
- `git diff --check` — passed; only existing line-ending conversion notices were emitted.

## Target hashes

| ID | SHA-256 |
| --- | --- |
| `currency.dnd2024.copper-piece.v1` | `95153514D15044ADE632C8CE2B8EB530811D2CE5E306F26A976FFFC78BA7E566` |
| `currency.dnd2024.silver-piece.v1` | `E23F9DEFF866AB800962CD00EAED310718DAD86FD8E531047CE4E46DC8CA0221` |
| `currency.dnd2024.electrum-piece.v1` | `510A4D3115E6B1F7BFCC8D6B20F36EE5CB8896A62FF1AE0EE3C1EEEC22AE72C6` |
| `currency.dnd2024.gold-piece.v1` | `AD6737C91EE47DCE72D3383E78ED19C1D738E24AD789D2AD3B7848FB448BAE5D` |
| `currency.dnd2024.platinum-piece.v1` | `908A4CC8D69F22B099523A4BBD2CFCE1C77890A4AD8277BB82AE8C28FD63B095` |

## Deliberate exclusions and next gate

This receipt does not claim automatic installation into campaign state. The accepted application
kernel intentionally creates empty state spaces, and its full-legacy adoption operation cannot be
used for a partial content cohort without copying unrelated state. A generic partial static-content
materialization boundary therefore needs its own cross-owner decision before automatic installation.

Mundane equipment, spells, monsters, and magic items remain separate Parent 10 cohorts. They may
not reuse this receipt because their schemas and official locators differ; complex behavior remains
Parent 11 work. Final Slice 10A acceptance requires user confirmation.
