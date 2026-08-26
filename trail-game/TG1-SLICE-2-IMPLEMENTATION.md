# Trail Game TG1 Slice 2 implementation — operator onboarding through existing protocol

Status: **accepted 2026-08-25**; [receipt](TG1-SLICE-2-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG1 application package / TG1.2](TG1-APPLICATION-PACKAGE-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; application administration only**
Outcome: Give a private operator one exact documented and tested sequence that registers,
previews, activates, and creates an empty `trail-survival` state space using only the existing
three-verb system protocol.
Exclusions: New public kinds/routes, startup automation, live installation, component schemas,
mechanics, scenarios, UI, catalog publication, migrations, and `dnd2024` changes.
Allowed files/areas: one operator-onboarding document under `trail-game/`; the existing
`SystemCatalogMcpWalkTests` for one real-host Trail Survival protocol proof; TG1 plan/receipt/status
documents.
Stop point: Stop after the documented protocol sequence and fresh-host test pass; do not publish the
catalog, begin TG1.3, or begin TG2.

## Confirmed decisions

- Reuse only existing public kinds:
  `system.application.register`, `system.source.register`, `system.application-preview`,
  `system.application.activate`, `system.state-space.create`, `system.applications`, and
  `system.sources`.
- Mutating commits retain the existing required dry-run then exact commit behavior.
- The application source resolves through configured allowed-root ID `repository`; protocol input
  never carries an absolute host path.
- Production startup remains application-neutral and does not auto-register Trail Survival.
- Catalog publication is excluded. The operator can inspect registration/activation/state-space
  evidence through authenticated system queries; player-facing publication is later work.

## External implementation reference

No external implementation applies. This slice exercises existing generic protocol contracts.

## Prerequisite evidence

- [TG0 confirmation](TG0-PRODUCT-CONTRACT-CONFIRMATION.md) owns all application/source identities.
- [TG1 Slice 1 receipt](TG1-SLICE-1-RECEIPT.md) proves the authored source and internal generic seams.
- Existing `SystemCatalogMcpWalkTests` prove the real host, private-operator authorization, exact
  dry-run/commit/replay behavior, and state-space creation protocol.

## Runtime artifacts

Add no runtime/catalog artifact. Add one operator document that records exact payload shapes,
ordering, derived fingerprint handoffs, no-live default, and recovery behavior. Add one disposable
real-host test using the repository source and a temporary SQLite database.

## Authoritative state and closed input

The existing system capability catalog owns serialized protocol shapes. TG0 owns the exact opaque
application/source identities. The authored Trail Survival procedure remains source authority.
The host configuration resolves `repository` to an absolute path outside protocol input.

Preview derives the preview fingerprint; activation consumes it exactly and derives the active
fingerprint; state-space creation consumes that exact active fingerprint. Operators never supply
winner hashes, an activation result, an absolute path, or state effects.

## Behavior

The real-host proof must:

1. confirm the public tool set remains exactly `orient`, `query`, and `commit`;
2. dry-run and commit the immutable application registration;
3. dry-run and commit the immutable trusted source registration;
4. query a valid one-winner preview for the authored procedure;
5. dry-run, commit, and replay exact activation;
6. materialize the one-record active catalog through the internal generic owner;
7. dry-run, commit, and replay empty state-space creation;
8. query back the application/source/active/state-space evidence; and
9. prove no application ECS entity/component row or normal database is changed.

## Failure, replay, and rollback contract

- Direct application/source/activation/state-space commits without required dry-run retain their
  existing typed failure and no-change behavior.
- Exact replay returns the original operation/result and does not duplicate registration,
  activation, or state-space history.
- A stale derived fingerprint requires a fresh preview/dry-run.
- Unknown allowed root or source scan failure cannot activate.
- The temporary host/database is disposed and deleted after the test.

## Implementation sequence

1. Author the operator onboarding sequence from existing protocol contracts.
2. Add one focused real-host Trail Survival walk to the existing MCP protocol test owner.
3. Run that focused walk, Slice 1 tests, catalog validation, full shared/local-AI suites, build,
   link checks, and TG1 diff checks.
4. Record the Slice 2 receipt and stop before TG1.3.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Surface | Tool list remains exactly three verbs; no kind/route added. |
| Positive | Fresh host completes register/source/preview/activate/state-space sequence. |
| Derived authority | Preview and active fingerprints flow from prior server results. |
| Dry run | Every mutation uses exact dry-run then commit. |
| Replay | Activation and state-space duplicate requests return original operations/results. |
| Catalog | Internal active materializer finds exactly the descriptive procedure. |
| State | Empty binding exists; ECS entity/component tables remain empty. |
| Isolation | No startup registration, normal database, `dnd2024` state, or public catalog policy changes. |

## Verification commands

- Focused real-host Trail Survival onboarding test.
- Focused `TrailSurvivalApplicationSeamTests`.
- `roleplay validate catalog` using the disposable validator.
- Full shared and standalone local-AI suites.
- Warning-free solution build.
- Markdown-link and `git diff --check` checks for TG1-owned changes.

## Completion receipt and exit gate

Record `TG1-SLICE-2-RECEIPT.md`, update TG1 status once, and stop before publication/coexistence
acceptance, simulation state, mechanics, content, UI, migration, or live installation.
