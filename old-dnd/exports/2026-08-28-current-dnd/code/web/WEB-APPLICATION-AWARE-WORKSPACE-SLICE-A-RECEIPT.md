# Application-aware workspace Slice A receipt — reviewed application onboarding

Status: **accepted by user instruction to continue on 2026-08-25**  
Completed: **2026-08-25**  
Implementation: [Slice A](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-IMPLEMENTATION.md)  
Parent: [Application-aware workspace dependency plan](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral installation and generic adoption correction**

## Delivered boundary

- Registered and activated the reviewed `dnd2024` and `trail-survival` applications through the
  existing private three-verb MCP surface.
- Registered their reviewed sources and immutable component contracts without adding a protocol,
  route, schema migration, startup auto-registration path, or game rule to C#.
- Adopted the complete active D&D legacy graph into `dnd2024-main` with explicit mappings and exact
  type versions/hashes.
- Created the empty `trail-survival-onboarding` state space against the accepted TG3 activation.
- Corrected the generic legacy-adoption reader to honor the legacy world's established soft-delete
  boundary. Deleted entities and retained rows attached to deleted endpoints remain legacy
  tombstone evidence and cannot be resurrected in an application state space.
- Proved both applications are returned by the normal control application's bounded registry
  discovery, making them available to later shared navigation work.

## Recovery and operational notes

- Pre-slice backup:
  `web/exports/application-aware-slice-a-before-20260825T194331.zip`.
- Backup SHA-256:
  `38B066A617CCBAF0779F1648302141537185B61A0E7B23E64FB5E397332EE342`.
- Normal startup applied already accepted concurrent trigger migrations
  `20260825162249_TriggerSchedulingRecurring` and
  `20260825175551_TriggerSchedulingConditional`. They are outside Slice A and are disclosed rather
  than attributed to it; the backup predates both.
- The user confirmed deletion of four corrupt fixtures. Readback proved all four were already
  soft-deleted, so no destructive effect was sent. The adoption correction excluded them through
  the same active-state semantics already used by `WorldStore`.
- Detailed partial registration and compatibility-schema evidence remains in the
  [progress record](exports/WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-PROGRESS-20260825.md).

## D&D 2024 evidence

| Evidence | Result |
| --- | --- |
| Application | revision 1; fingerprint `9837F57732ED2A53AFFF385225C4CE91168352EC4FEB474745F72C6926310A12` |
| Sources/catalog | 11 sources; 118 winners; no shadows or problems |
| Component registry | 45 current qualified types: 33 accepted current-catalog contracts plus 12 semantically equivalent legacy compatibility contracts |
| Activation | operation `02a468a4d0be46b7b07b8e77ad85545f`; fingerprint `1DE742780807C868DA7354C2E0409CDB800955AE0047440ED0B630E1FE708AC0` |
| Adoption preview | operation `2db9878759d14e62812c1aa6750e7dd5`; outcome `would-adopt` |
| Adoption/replay | operation and request token `c8dbcd6d986b44d9920a1e0f34cfdbcf`; exact replay returned the same operation |
| Active inventory | 212 entities, 388 components, 29 containments, 357 relationships |
| Source/evidence | source `F661131DE62CB9CF0A4AB247C60D55130A8B1706FF531DB2150A426EC83D29AC`; evidence `B583A1C147739D323565A2BCE6CC137710AFDBA500C97E3A23301F03EE9BB125` |
| Binding | `dnd2024-main`, revision 1, fingerprint `D2C9C4B7ECBB4094B84CBCF2A3B8586C8C773745A589E0A3B458477E157E7C9D` |
| Tombstones | all four confirmed corrupt fixture IDs absent from the adopted state space |
| Legacy preservation | total legacy rows remain 233 entities, 412 components, 30 containments, and 357 relationships |

## Trail Survival evidence

| Evidence | Result |
| --- | --- |
| Application | revision 1; fingerprint `899C9DA173F5752AB0E091B356DF25197A35F61020D6544B523BA51589E40535` |
| Source | `trail-survival-core`; fingerprint `5909759F99F51933464186183E8A949AFFA5E348735C7CB5B2E870673FE302F2` |
| Accepted preview | two identical valid confined scans; 1 source, 41 winners, no shadows/problems, not truncated |
| Preview fingerprints | preview `507EA2FDC4330FC212C8B847B30D7AE5D03B614D17252604BBEF9E66A9C8280E`; scanned documents `299155D39BF2FEDD99F84C8A3F989075A3E12C586D503F49330E737BC6FF63D8` |
| Component registry | 12 current qualified types |
| TG3 run revision | version 2, hash `1FEDC22171E37941FD5850A5DB0BAE29B8D024BEE673396DBBC02BCEBE08DD13`, operation `6af6a18aa66a42b9aa5fa0ae4572275c` |
| TG3 scenario type | version 1/v2 profile, hash `CD36C66DC1E81C65B1F4C5B717FAB6DEA41A08B622DCAA3E36EFD659B6A08F43`, operation `e25498cd74184d65b69add058a727086` |
| Activation | operation `895e76ea6f424e7080bcfb73bcc5d19c`; fingerprint `37BFEBF980BB4DE84B322D19A3779F56E56007A3742E4F908AF44B040779DFFD`; exact replay succeeded |
| Binding | `trail-survival-onboarding`, revision 1, fingerprint `284D8799D081EEC8D51A6C928998D512A230A62D45F70A23B9B7FA0CAC9C50B2` |
| State-space operation | preview `d9fc4799d5a94e22854f530ebcd8af85`; commit/replay `b1d580cc71664131b9f8788d04f1a3ea` |
| Isolation | zero entities, components, containments, and relationships |

## Verification

- Focused `LegacyStateAdoptionTests`: **5 passed, 0 failed**. Coverage includes complete adoption,
  exact replay, dry-run staleness, active invalid-value rejection, audit rollback, and exclusion of
  a deleted entity carrying invalid retained component/edge rows.
- Live D&D dry-run/commit/replay and MCP application readback passed with exact inventory.
- Live Trail component dry-runs, two stable source previews, activation replay, state-space
  create/replay, and empty-state readback passed.
- `/api/control/structure/applications` returned exactly `dnd2024` and `trail-survival`.
- Scoped `git diff --check` passed.

## Deliberate exclusions and next gate

Slice A does not add shared navigation, page/application association, system chat, application chat,
system capability adapters, buttons/forms, UI changes, startup registration, Trail scenario state,
or action execution. Those remain in Slices B–H.

The user accepted this completed installation by instructing implementation to continue. Slice B
may proceed under its own bounded implementation document.
