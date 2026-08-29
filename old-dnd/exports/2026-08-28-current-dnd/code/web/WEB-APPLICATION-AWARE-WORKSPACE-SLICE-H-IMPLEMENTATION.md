# Application-aware workspace Slice H implementation — combined acceptance and live activation

Status: **accepted — see [receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-RECEIPT.md)**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md), Slice H  
Ruleset alignment: **ruleset-neutral verification and deployment**  
Source ID and locator: **not applicable**  
Outcome: prove the complete A–G workspace boundary across catalog, migrations, protocol,
authorization/privacy, compatibility, model adapters, browser behavior, and the full suite; then
activate the exact reviewed home and control-center pages in the normal private host and close the
feature with recoverable evidence.  
Exclusions: new product behavior, routes, IDs, schemas, migrations, application/page associations,
game rules, ECS mutations, system/application action confirmation during smoke, public hosting, and
unrelated cleanup.  
Allowed files/areas: focused corrections strictly inside the already accepted A–G web boundary if
acceptance exposes a defect; test/verification artifacts; one pre-activation database backup;
existing private page-upload records for `home` and `control-center`; Feature 4 status and receipt.  
Stop point: all required checks and live readback pass, exact active revisions are recorded, the
normal host is stopped cleanly, the parent plan/roadmap are closed, and the final receipt is written.

## Confirmed decisions

The user's 2026-08-26 instruction, “Confirmed. Complete slice H and finish it all,” accepts Slice G
and explicitly confirms this final backup, live activation, and acceptance boundary. It does not
authorize unrelated game-state changes or new semantics.

Only the reviewed authored `home.html` and `control-center/index.html` are activated. The authored
application-page file remains a fixture because the accepted parent deliberately has no
application-to-page association contract. Registered applications remain reachable through the
existing control-center deep links.

Before starting the normal host, create a timestamped recoverable database archive and SHA-256.
Live smoke may create bounded assistant/application conversation evidence, but it must not confirm
or execute a system/application proposal and must not mutate application ECS state.

## Prerequisite evidence and owners

- Slice A owns reviewed live application/state-space onboarding.
- Slices B and G own shared navigation and page composition.
- Slices C and F own common system contracts and reusable action/form request surfaces.
- Slices D and E own system-scoped chat and confirmed system task orchestration.
- Existing application conversation and interaction orchestration own application/state execution.
- Authorization and the web remote boundary own principal, control, observation, and MCP isolation.
- SQLite remains live state authority; the authored pages become live only through the existing
  versioned private page-upload boundary.

## Acceptance behavior and no-change contract

1. Catalog validation uses a disposable database and accepts the authored catalog without touching
   live state.
2. Migration checks prove the current model is complete and upgrades remain consistent.
3. Protocol checks prove the accepted three-verb surface and remote `/mcp` exclusion remain exact.
4. Privacy/security tests prove system scope cannot see application ECS/files/secrets and
   application chat cannot acquire system control authority.
5. Full shared and local-AI suites must pass, except a genuinely unrelated already-recorded issue
   may be disclosed only if independently reproducible and outside this confirmed boundary.
6. The normal database backup is completed and hashed before host startup or page activation.
7. Upload exact authored home/control HTML, read back the active revisions, and compare active
   content hashes with the reviewed files.
8. Real-browser smoke proves shared navigation, Home/Control links, D&D/Trail deep links, distinct
   system/application chat bindings, notes/clock preservation, and accessible failure/status UI.
9. A bounded live system question may verify context-backed knowledge. A bounded application
   question may verify exact binding. Neither smoke path confirms or executes an action.
10. Stop the host and verify no unexpected live application/ECS or public-surface artifact changed.

## Failure and recovery

- Any backup failure stops before host startup.
- Any catalog/migration/protocol/security/full-suite failure is investigated within the existing
  boundary; do not activate pages while a relevant blocker remains.
- If upload/readback/hash verification fails, stop the host and retain the backup plus the prior
  active revision. Do not claim activation.
- If browser smoke finds a page-only defect, correct the authored page, rerun focused acceptance,
  and append a new versioned live revision. Never edit live rows directly.
- Local-model unavailability may be reported accurately, but scope/layout/navigation and no-change
  checks must still pass. Do not substitute a remote model silently.
- Do not confirm any prepared proposal during live smoke. A visible proposal is inert evidence.

## Acceptance matrix

| Layer | Required evidence |
| --- | --- |
| Backup | Timestamped archive exists, has a recorded SHA-256, and predates activation. |
| Catalog | Fresh disposable catalog validation succeeds. |
| Migration | Drift/current-model checks succeed; normal startup applies no unexpected migration. |
| Protocol | Public protocol/registration walk and remote MCP boundary tests succeed. |
| Authorization/privacy | Control, observation, system/application scope, secret/path, and no-change guards pass. |
| Components | All accepted elements register once; syntax and compatibility tests pass. |
| Full suites | Shared solution tests and local-AI tests complete with recorded totals. |
| Live revisions | Home/control active content exactly matches reviewed files and revisions are recorded. |
| Navigation | Home, Control center, D&D, and Trail Survival links are visible and usable. |
| System chat | No app/state/provider binding; bounded context question returns accurate result or explicit unavailability. |
| Application chat | Exact application/state binding is visible; switching applications creates a fresh session. |
| Actions | No live smoke action is automatically confirmed or executed. |
| Accessibility/theme | Green/vine home, notes, clock, labels, live statuses, and keyboard navigation remain present. |
| Closure | Parent plan and roadmap point to one final receipt; deliberate exclusions remain explicit. |

## Verification sequence

- create and hash the normal-database backup;
- focused A–G web/system/app/authorization tests;
- migration drift and catalog coverage tests;
- public protocol and remote-boundary tests;
- local-AI test project;
- `roleplay validate catalog` against its disposable validation host;
- full solution test suite and clean solution build;
- extracted component JavaScript syntax checks and scoped diff check;
- exact live upload/readback/hash verification;
- real-browser home/control/application/system-chat smoke;
- final live state/revision readback and host shutdown.

## Completion receipt and exit gate

Write `WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-RECEIPT.md` with backup/hash, commands and counts,
live revisions/content hashes, browser evidence, any bounded model evidence, normal-state changes,
and deliberate exclusions. Mark H and the parent feature complete only when every relevant gate
passes and no required work remains.
