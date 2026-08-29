# D&D 2024 web UI Slice 6A receipt — healing and Temporary HP controls

Status: **accepted 2026-08-27**
Completed: **2026-08-27**
Implementation: [Slice 6A](DND2024-WEB-UI-SLICE-6A-IMPLEMENTATION.md)
Parent: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 6 / E2
Ruleset alignment: **dnd2024-owned**
Model assignment: `gpt-5.6-sol`, high reasoning

## Delivered boundary

- Extended the existing Vitals panel with a game-styled healing card and separate Temporary HP
  card. Both use bounded 1–999 minus/plus steppers instead of generic number forms.
- Healing binds only `mechanic.dnd2024.healing.apply` with the selected actor as `subject` and
  exactly `{amount}`. The browser neither clamps healing nor calculates applied/excess values.
- Temporary HP binds only `mechanic.dnd2024.temporary-hit-points.write`. An absent buffer prepares
  `{mode:'grant',amount}`; a present buffer requires an explicit **Keep current** or **Replace with
  incoming** choice and adds only `onExisting:'keep|replace'`; expiry is a separate reviewed action.
- Each action mounts the accepted `<application-action-button>`, preserving descriptor lookup,
  prepare, server proposal, separate confirmation, execution, replay, transaction, and receipt
  ownership. The browser contains no direct POST and constructs no effect.
- A completed execution receipt causes an exact selected-entity reread. No optimistic HP or
  Temporary HP value is committed locally, and the reread remains authoritative even for a returned
  unsuccessful result.
- Controls appear only for a current playable binding with a selected actor and valid stored HP /
  optional Temporary HP shape. The registered legacy Brackenford campaign remains readable with
  mutation controls locked until an explicit migration.

## Rules and reference alignment

The UI sends only the closed source inputs accepted by the current local healing and Temporary HP
mechanics under `source.dnd2024.srd-5.2.1`, printed pages 17–18. The inspected `hit-points.write`
owner explicitly records/corrects complete HP pairs and is neither damage nor healing, so it is not
exposed as a player shortcut. Damage and mitigation remain with their later composition owner.

Foundry dnd5e `module/documents/actor/actor.mjs`, branch `6.0.x`, commit
`a7aa584f7afb1a2e714391b94209eb72e04f1941`, retrieved SHA-256
`834bb4b1dde60c8770f567f5748522c45b7d23a2fc4e668d6c50b36f2773952c`, was reviewed only for
the engineering separation between sheet widgets and authoritative actor mutation. No Foundry
code, rules, automatic buffer choice, damage logic, assets, or dependency was copied.

## Verification

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
  — passed.
- Focused `WebInterfaceTests` — **89 passed, 0 failed**. The asset contract proves both exact
  mechanic IDs, all closed input variants, selected-subject binding, explicit Keep/Replace/Expire,
  receipt-triggered refresh, and absence of raw HP correction, browser effects, direct writes,
  browser RNG/storage, control routes, MCP routes, or HTML injection.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with **0 warnings, 0 errors**.
- Local-AI regression suite — **21 passed, 0 failed**.
- The restarted local host returned the current D&D workspace asset with healing and Temporary HP
  controls present, raw HP correction absent, and no direct POST. The live page still showed the
  expected stale-binding lock for Brackenford rather than exposing unsupported actions.
- `git diff --check` found no whitespace error; it reported only checkout line-ending notices.

Two directly relevant catalog-owner tests were also invoked. Both failed during harness setup,
before either owner executed, on the concurrently edited character-creation schema diagnostic
`SCHEMA_PATTERN` at `#/properties/templateKey/pattern` ("syntax outside the bounded non-branching
grammar"). Slice 6A changes no catalog or character-creation file. This is an external verification
blocker, not a healing/Temporary HP assertion failure; the existing owner tests themselves still
document first grant, Keep/Replace, expiry, replay, healing clamp, and no-write behavior.

## Deliberate exclusions and acceptance gate

No raw HP record/correction, damage, mitigation, attack, zero-HP/death behavior, inventory or
equipment mutation, mechanic/procedure/schema, server route, new custom-element ID, migration,
database synchronization, or live activation was added. Concurrent character-creation changes and
their current schema failure were preserved untouched.

The user's 2026-08-27 instruction to continue accepts this completed feature boundary. The next
bounded Order 6 slice owns direct-item equip/unequip controls; damage remains with the later
encounter/combat composition slice.
