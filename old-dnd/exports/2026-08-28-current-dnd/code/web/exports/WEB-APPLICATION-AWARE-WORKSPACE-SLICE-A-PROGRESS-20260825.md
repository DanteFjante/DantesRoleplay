# Application-aware workspace Slice A progress evidence — 2026-08-25

Status: **resolved by the completed Slice A implementation; retained as intermediate evidence**  
Owner: [Slice A implementation](../WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-IMPLEMENTATION.md)  
Database: `DantesRoleplay.MCPServer/data/dantesroleplay.db`

## Recovery evidence

- Pre-slice backup: `application-aware-slice-a-before-20260825T194331.zip`
- SHA-256: `38B066A617CCBAF0779F1648302141537185B61A0E7B23E64FB5E397332EE342`
- Size: 1,149,484 bytes.
- The archive contains the database; WAL/SHM companions were absent when the backup was taken.
- Recovery remains an explicit operator action. This slice did not restore or directly mutate the
  database.

## Operational deviation

The current host applied the already accepted pending migration
`20260825162249_TriggerSchedulingRecurring` during startup before the remaining MCP work. This was
outside Slice A's intended no-migration startup boundary. It is retained and disclosed here; the
backup predates it. Current bootstrap contracts were also seeded by normal host startup.

## Trail Survival progress

- Application `trail-survival` registered: operation
  `47b0e5319cc349fc817f55d1467a92b6`, fingerprint
  `899C9DA173F5752AB0E091B356DF25197A35F61020D6544B523BA51589E40535`.
- Source `trail-survival-core` registered: operation
  `b1cb04ed14b146f1864f00efb75e2a67`, root `repository`, glob
  `catalog/applications/trail-survival/**/*`, fingerprint
  `5909759F99F51933464186183E8A949AFFA5E348735C7CB5B2E870673FE302F2`.
- The eleven TG2 component types are registered. The `trail-survival.scenario-pin` registration
  operation is `c687a4965b0e41b38ef1efec6d5c2d83`, version 1, profile v2, schema hash
  `A33CFF26CA503FD7AB10F16E5EF9C6CD2B6F76E61ADED8FE9BD41F8D8955F64E`.
- The source changed while Slice A was running: observed valid previews grew from 24 winners to 27
  and then 37. The latest observed preview fingerprint is
  `CF4E8A52A6365B0ABFC65595EE05778EB321CE6F616E17E7AD6FB2A3F169BF79` with 37 winners and no
  problems. TG3 remains active, so this is not treated as a stable activation boundary.
- No Trail activation or state space was created.

## D&D 2024 progress

- Application `dnd2024` registered: operation `e9dbe127bb794010ae108e70481bdaa2`, fingerprint
  `9837F57732ED2A53AFFF385225C4CE91168352EC4FEB474745F72C6926310A12`.
- Thirty-three accepted current-catalog component types and eleven ratified source globs were
  registered through dry-run/commit pairs.
- The accepted preview had 11 sources, 118 winners, no shadows, and no problems; preview
  fingerprint `BFA64B5BE73FCF35CA654CA99F2C192A6F8C981523B5B9C3866E008965F15232`.
- Activation operation `02a468a4d0be46b7b07b8e77ad85545f` replayed exactly. Activation revision
  1 has fingerprint `1DE742780807C868DA7354C2E0409CDB800955AE0047440ED0B630E1FE708AC0`.
- The live legacy graph uses 24 definitions. Twelve compatibility type registrations were needed
  beyond the 33 current-catalog types. Their validation semantics were preserved while unsupported
  annotations were removed; two surplus legacy closing braces were repaired; one bounded key
  pattern was expressed as an equivalent union; redundant formats were removed only where an exact
  constant already fixed the value.

| Legacy definition | Destination type | Version/profile | Schema hash | Commit operation |
| --- | --- | --- | --- | --- |
| `dnd2024.abilities` | `dnd2024.abilities` | 1 / v1 | `F82A0A3113591EE8DF0FBF8A5F790D94D253CD5CA673ED8304275C9EC1B0F4E2` | `40ded259d51b4704aedd280ec8bad133` |
| `dnd2024.armor-class` | `dnd2024.armor-class` | 1 / v1 | `BE11B11953CDBDBD8491288336605529A536039FC9F5301A975EB616128A1D4E` | `b2a7ab753fcb49fcade0f5af59373d06` |
| `dnd2024.character-level` | `dnd2024.character-level` | 1 / v1 | `A3B9A66F092AECCE9F1D979B6E51FC557AD8D477362CEDC59CB2C0DCE18E4519` | `45c56082240c4a8287ed5ed7b8950f80` |
| `dnd2024.hit-points` | `dnd2024.hit-points` | 1 / v1 | `BD8D1D98E05D3C1A5BBCF68C0E49BE0334D986E9F521B88F9FC4C857888DEEC6` | `16129f1bf69443dea9c9152e6dc40518` |
| `dnd2024.playtest-character-record` | `dnd2024.playtest-character-record` | 1 / v2 | `B0D85524578879362262318BD93BFBD4FE66A86AEE90046F0151FCE5BA1A1805` | `c285c108bf974a9487465d32e977fb9a` |
| `dnd2024.saving-throw-proficiencies` | `dnd2024.saving-throw-proficiencies` | 1 / v1 | `B9982118957B02C9B869CFBF38164A8740B2765B5737EC11A94F758B91B59B9E` | `7dc19550662c49ddb2682e55c6431a25` |
| `dnd2024.skill-proficiencies` | `dnd2024.skill-proficiencies` | 1 / v1 | `213660F564C12B088C632D60901782EEFEA048EB31A49069DC09E9CE450C2363` | `2c913979a8b541e9bad1522e9147f1c2` |
| `dnd2024.source` | `dnd2024.source` | 1 / v1 | `5D73BBDD11E36D506980BEF82E6A341D78660E5193529617823E0D4A0C4F40D4` | `05056d2fb39c4913af892705783763c6` |
| `dnd2024.weapon-proficiencies` | `dnd2024.weapon-proficiencies` | 1 / v1 | `FB4FC2EBE73CE45DB393E16816B4B6310FF2440863816FE76E19D51FBCE39E7C` | `2a915211d89a478a9a1b3abecf75df05` |
| `dnd2024.weapon-profile` | `dnd2024.weapon-profile` | 1 / v1 | `D5B795828BE50EB1A83D2379A2F71B085604D79E6B1598FD6F6D1F61B9C748CD` | `6d23d1c647a54023bf083ec61425498a` |
| `game.core.campaign.root` | `dnd2024.game.core.campaign.root` | 1 / v2 | `823F1A8815BB402AA9D79B09E23479AAA6DF9BED776E6A5E5E0953608A30D978` | `4f7e57e080ea4769a5c0209060f27036` |
| `lock` | `dnd2024.lock` | 1 / v1 | `478776EB27EE7C5623DCC9ACC6BACC4E34AA87D4A2CE427D99D22F6DBDB6F49C` | `c4f6995b7312461abb4b29075add3b70` |

## Typed adoption stop

The complete adoption preview used 24 component mappings and 11 relationship mappings against the
stable legacy inventory of 233 entities, 412 components, 30 containments, and 357 relationships.
It failed without mutation:

- operation `f0ac09fe2a4f4d9da9b8d0c862412392`;
- code `COMPONENT_VALUE_INVALID`;
- first rejected component
  `creature.f4s2-corrupt-level/dnd2024.character-level`;
- value has level `0`, while the accepted type requires levels 1 through 20.

An isolated disposable-database probe then relaxed one failing definition at a time and proved that
all other mapped definitions validate. The complete set of schema-invalid live values is:

| Entity | Definition | Invalid condition |
| --- | --- | --- |
| `creature.f4s2-corrupt-level` | `dnd2024.character-level` | level `0`; allowed range is 1–20 |
| `creature.slice4-fixture-invalid-level` | `dnd2024.character-level` | level `0`; allowed range is 1–20 |
| `f5-bad-extra` | `dnd2024.abilities` | undeclared `extra` property in a closed object |

After relaxing only those two definitions in the disposable copy, the full 233/412/30/357 preview
passed. `creature.f4s2-corrupt-saves` is also an explicitly corrupt disposable fixture: its value
passes JSON Schema but violates the owning writer's canonical save ordering rule. The disposable
probe made no live change.

The live legacy graph was left unchanged. No `dnd2024-main` state space was created. Schemas will
not be weakened and legacy fixtures will not be deleted without explicit operator confirmation.

## Resume conditions

1. Confirm a governed cleanup of the four retained disposable corruption fixtures
   `creature.f4s2-corrupt-level`, `creature.f4s2-corrupt-saves`,
   `creature.slice4-fixture-invalid-level`, and `f5-bad-extra`; or choose to leave D&D activated
   without adopting the legacy graph.
2. Wait for the Trail TG3 package to reach an accepted, stable source boundary, then re-preview,
   register any accepted new type versions, activate the exact preview, and create
   `trail-survival-onboarding`.
3. Re-run readback and replay checks, then write the Slice A completion receipt. Do not begin Slice
   B before that acceptance.

## Resolution

- All four cleanup candidates were already soft-deleted. No destructive effect was necessary.
- The generic adoption reader now excludes soft-deleted entities and their retained rows while
  continuing to reject truly unknown references.
- `dnd2024-main` was adopted at the exact active 212/388/29/357 inventory.
- TG3 reached acceptance; Trail activated its stable 41-winner overlay and created the empty
  `trail-survival-onboarding` binding.
- Final evidence is in
  [the Slice A receipt](../WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-RECEIPT.md).
