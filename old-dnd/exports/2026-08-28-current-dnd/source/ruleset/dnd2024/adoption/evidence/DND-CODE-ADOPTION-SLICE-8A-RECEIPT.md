# D&D code-adoption Slice 8A receipt — creature Speed profile

Date: 2026-08-25  
Status: **accepted**  
Boundary: Parent 8 / 8A standalone base-Speed family

## Delivered

- Recovered the archived `dnd2024.speed` component as closed canonical base state with walk,
  burrow, climb, fly, and swim values plus fixed SRD provenance.
- Recovered and adapted `mechanic.dnd2024.speed.write` for explicit record/correct transitions and
  `mechanic.dnd2024.speed.read` for effect-free absent, malformed, invalid, and valid diagnostics.
- Recovered `procedure.mechanic.dnd2024.speed` as the application-owned contract and kept the
  five-foot/1,000-foot limits explicit as repository canonicalization bounds rather than SRD claims.
- Extended the disposable activated-application harness through the real projection, JavaScript,
  typed-effect, SQLite revision, transaction, operation-replay, and diagnostic paths.
- Preserved the generic-kernel boundary: no production C#, migration, public operation, source
  overlay, live state, or donor runtime dependency changed.

## Verification

- `node --check` over all D&D application mechanics — passed, 19/19.
- Speed-focused `Dnd2024AbilityCheckTests` — passed, 11/11 cases.
- Full `Dnd2024AbilityCheckTests` — passed, 24/24.
- Combined D&D, application-execution, ECS-effect, and Trail Survival regression filter — passed,
  54/54.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed, 0 warnings and 0 errors.
- `roleplay validate catalog` — passed, 144 core-catalog records valid with 21 existing advisory
  near-duplicate warnings; no live data was touched. The D&D application source itself was also
  previewed and activated from a fresh disposable database in every focused harness case.
- Full repository suite — passed, 1,025/1,025.
- `git diff --check` — passed; Git emitted only line-ending notices for pre-existing dirty-worktree
  files.

## Acceptance evidence

The tests prove exact canonical bytes and fixed provenance, record-versus-correct preconditions,
one revision per successful transition, deterministic effect-free reads, idempotent operation
replay, closed input, lower/upper/numeric boundary rejection, missing/malformed/invalid diagnostics,
and unchanged bytes/revision after duplicate or invalid writes. The catalog schema rejected two
unsupported descriptive keywords during the first run; they were removed without changing state
meaning, and all acceptance commands then passed.

## Deliberate exclusions

This leaf adds no remaining-movement budget, turn refresh/spending, Conditions or temporary Speed
changes, position, grid, terrain, pathfinding, travel pace, jumping, encounter integration, fixtures,
archive deletion, or Foundry code. Each later archived mechanic family remains a separately planned
Slice 8 parent with its own source, dependency, semantic, and acceptance gate.
