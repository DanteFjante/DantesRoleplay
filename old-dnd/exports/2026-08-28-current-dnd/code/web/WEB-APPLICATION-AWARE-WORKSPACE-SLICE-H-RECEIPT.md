# Application-aware workspace Slice H receipt — combined acceptance and live activation

Status: **accepted by the user's 2026-08-26 instruction to complete Slice H**  
Completed: **2026-08-26**  
Implementation: [Slice H](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-IMPLEMENTATION.md)  
Parent: [Application-aware workspace dependency plan](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral verification, deployment, and accepted-boundary corrections**

## Delivered boundary

- Activated the reviewed `home` and `control-center` pages through the existing versioned private
  page boundary. Their live content is byte-for-byte identical to the authored sources.
- Proved shared Home, Control center, D&D 2024, and Trail Survival navigation and exact
  application/state-space chat binding in a real browser.
- Corrected application outer-chat grounding: every outer turn now receives the exact application
  revision/fingerprint, selected state space, and a bounded untrusted transcript. Application facts
  must be delegated to current contract discovery instead of being guessed from model knowledge.
- Kept single user actions and questions as one exact task/batch. Explicit numbered multi-task
  requests may still use the bounded agenda model. This prevents a request such as “Orban attacks
  the caravan driver” from being split into unrelated invented preparation steps.
- Made the Ollama-facing agenda schema structural-only because Ollama rejected the earlier bounded
  grammar. The authoritative parser still enforces all task, batch, dependency, text, JSON, and
  fingerprint bounds after generation.
- Corrected the private launcher so Production startup maps the reviewed repository source root,
  publishes the two installed application catalogs, enables the local inner/outer providers, and
  restores every prior process environment value when it exits.
- Removed duplicate safe non-resolution messages while preserving the inner receipt and failure
  code as the task's authority.

## Backup and live revisions

- Pre-activation archive:
  `web/exports/application-aware-slice-h-before-20260826T100705Z.zip`.
- Archive SHA-256:
  `FDFB98762F04608CD20928EA6DF92AD1D72E9C79CD519EA65DE54F65F07C6525`.
- `home`: active revision **5**, 18,396 bytes, SHA-256
  `086004EEA36FE4DCA3F653896A54BEFB43DBD5A4B0936CA7C3E364B5E52BEC85`.
- `control-center`: active revision **10**, 98,412 bytes, SHA-256
  `C79E4A737424921B12CF5DCEEF1B9B1E194773D6055BF4B4092FDBAB155DDBB6`.

## Live model and no-change evidence

- The Trail chat accurately reported its exact binding as application `trail-survival`, state space
  `trail-survival-onboarding`.
- A D&D stored-world/location question was delegated without guessing. Because the active D&D
  catalog currently contains no model-visible world/location query contract, it returned exactly
  one `TRUSTED_FEATURE_NOT_FOUND` receipt, one task, and one batch. No action was proposed or run.
- The D&D public application catalog materialized with 34 records after the host source-root mapping
  was present. Missing query/action contracts remain explicit application-owned work; this slice did
  not invent game contracts or permanent application IDs.
- Application registry SHA-256 remained
  `44827F68EA89160A31FA0645C558FEA661D1BD286706CE7AA209DFA7F598AF02`.
- D&D state-space discovery SHA-256 remained
  `443CC06FE66C24FBC81C2D9A0B07D528E96A60E79A59085D340418871103A12D`.
- Trail state-space discovery SHA-256 remained
  `15C7D7C4B83D41C99E658C65CBC437EF753B617BCA864BE7576F2EBE1221F878`.
- Live smoke created only bounded conversation/receipt evidence. It never confirmed a proposal and
  did not change application ECS state.

## Verification

- Application conversation, outer-provider, agenda, and private-launcher focus: **44 passed**.
- Full shared suite: **1,106 passed, 0 failed, 0 skipped**.
- Standalone local-AI suite: **21 passed, 0 failed, 0 skipped**.
- Solution build: **0 warnings, 0 errors**.
- Disposable catalog validation: **144 valid records**, with 21 existing non-blocking
  near-duplicate warnings; no live data touched.
- Private launcher PowerShell parsing: valid.
- Scoped `git diff --check`: no whitespace errors; only repository line-ending notices.
- Normal host was stopped cleanly after final readback.

## Operational note and deliberate exclusions

An early diagnostic startup pointed at the separate repository-root `data/dantesroleplay.db` and
applied its pending accepted migrations before the registered-application database was identified.
No application was registered in that separate database and no interaction was performed there.
All live acceptance and hashes above use `DantesRoleplay.MCPServer/data/dantesroleplay.db`.

The slice does not add application-specific query or action contracts. Consequently the local chat
can now identify its bound application, traverse every published trusted contract, plan supported
requests, and return an exact receipt when unsupported, but it cannot read arbitrary ECS values.
Stored-data questions require reviewed application-owned projection/query contracts in a later
slice; this is the intentional authority boundary rather than model unavailability.
