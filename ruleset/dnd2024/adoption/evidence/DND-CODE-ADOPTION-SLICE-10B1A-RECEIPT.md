# D&D code-adoption Slice 10B1A receipt — schema-faithful adventuring gear

Date: 2026-08-26  
Status: **implemented and verified; acceptance pending user confirmation**  
Boundary: Parent 10 / first mundane-equipment leaf

## Delivered

- Recovered nine existing permanent adventuring-gear definition IDs under the activated D&D
  application source: Backpack, Caltrops, Crowbar, Oil, Pouch, Rations, Tinderbox, Torch, and
  Waterskin.
- Preserved the accepted `dnd2024.item-definition` shape, exact rational masses, stack policies,
  Backpack/Pouch capacities, and existing inventory/transfer ownership.
- Added a hash-locked deterministic transform that rejects source, shape, ID, value, path, target,
  attribution, and quarantine drift.
- Added item-specific SRD locators and recorded the exact SRD 5.2.1 CC BY attribution/change
  indication. Display names for Oil and Rations now match the current SRD table.
- Proved activated-source retention, schema validity, atomic 30-pound Backpack admission/refusal,
  and nested burden using existing mechanics without changing the generic kernel, component schema,
  public protocol, live database, or archive.

## Correctness exclusions

- The archived 50-foot hempen Rope record remains quarantined because SRD 5.2.1 does not state that
  length or subtype.
- The archived Quiver record remains quarantined because its kind-level capacity would admit every
  ammunition kind, while SRD 5.2.1 permits 20 Arrows.
- Prices and item-specific action behavior remain deliberate later work.

## Verification

- Adventuring-gear transform — passed, 9/9 deterministic targets; 2 representation gaps retained.
- Focused activated content/schema/capacity test — passed, 1/1.
- Full activated D&D suite — passed, 77/77.
- Existing currency cohort regression — passed, 5/5.
- Existing content-transformation regression — passed at Stage 5C.
- Adoption contract suite — passed: 2 positive examples and 9 required rejections.
- Conformance tooling suite — passed at Stage 4C.
- Core catalog validation — passed, 144 records with the existing 21 advisory warnings; no live
  data was touched.
- Main test project — passed, 1,088/1,088 while intentionally suppressing unrelated project builds.
- Local-AI tests — passed, 20/20.
- `git diff --check` — passed; only line-ending conversion notices were emitted.
- Repository solution build — presently blocked by the unrelated untracked
  `ControlSystemCapabilityExplorer.cs`, whose five `StatusCodes` references lack the required web
  namespace. This slice does not edit that concurrently owned file.

## Target hashes

| ID | SHA-256 |
| --- | --- |
| `item.dnd2024.backpack.v1` | `8F5A1BD1AED9C1F530100B0EC2E7EB933526EB12F14DF677ED9973804B80EEBE` |
| `item.dnd2024.caltrops-bag.v1` | `B37F6CEECB9D90A0B614E47A910A641039162F3A20DE26A0FE9041EACE7ADEB3` |
| `item.dnd2024.crowbar.v1` | `E22DAFAA3155EB2C371D00CC9DA7427EA76A3DDADD43F515E89F313C412F9618` |
| `item.dnd2024.oil-flask.v1` | `41B8F5C8A45275FC71F2D049150787E0CF26CF82EF468DA32C426811EA857D28` |
| `item.dnd2024.pouch.v1` | `4BA576B3BB150EB9F6CB6D59BBD03380AECB4423CEF23022EE09C6B1DEB2B45A` |
| `item.dnd2024.rations-one-day.v1` | `E29992B9D458A3585071D3A8687C5BA809BA38911B59AE10F8F615F563533D10` |
| `item.dnd2024.tinderbox.v1` | `2678AD98CA131049EA479675991ED0D9AEDB07B268A730D6499C56DAD8F02248` |
| `item.dnd2024.torch.v1` | `9F782D5242D897B93B86861B451D0118F5C3015083CC4C8033577C9EF65FF220` |
| `item.dnd2024.waterskin.v1` | `B630B9F03B126CD9E5A4F3B18B2199113CB72DF3AA6D72AC2DECB60469738024` |

## Next gate

Parent 10 remains active. Armor/shields, weapons, ammunition, tools, spells, monsters, and magic
items remain independent cohorts. Rope/Quiver need an explicit schema decision before import, and
automatic installation into campaign state still needs the separately approved materialization
boundary described by the Parent 10 design. Final Slice 10B1A acceptance requires user confirmation.
