# Story plan orchestration — Slice 0 confirmation

Status: **Approved 2026-08-21 by the request to implement the accepted plan**

This records the semantic boundary before code, database, catalog, or MCP changes begin.

## Approved permanent names

- Public query and commit kind: `story-plan`.
- Orchestration contract: `procedure.play.story-plan`.
- Story-plan statuses: `pending`, `running`, `completed`, `blocked`, `failed`, `cancelled`.
- Step statuses: `pending`, `running`, `completed`, `blocked`, `failed`, `skipped`.
- First-version step kinds: `campaign-context`, `knowledge`, `action`.

## Approved operating boundary

- A remote story model submits a linear 1–6 step semantic plan and later polls its handoff. It may
  contain at most four `action` steps; `campaign-context` is optional, unique, and first when
  present. No branches, loops, conditions, retries, bindings, parallelism, or dynamic steps exist.
- The backend owns retrieval, procedure versions, route selection, validation, execution, persistence,
  and stop behavior. The remote model cannot name a mechanic, procedure, effect, command, query,
  or database operation.
- Context reads the fixed campaign resume, knowledge uses the authorized answer owner, and action
  uses the existing route proposal plus action runner.
- Completed actions are played history. A later blocked, failed, or cancelled step never rolls them
  back; no compensation, resume, branch, loop, binding, parallelism, or caller-defined workflow is
  introduced.
- Version 1 is development-GM-only. It deliberately defers production authentication and actor
  execution.

## Approved result and failure boundary

- A plan exposes only its bounded status, completed step summaries, missing information, mechanic
  narration, affected entity IDs, and a terminal handoff. It never exposes procedure bodies,
  candidate lists, local-model prompts or replies, raw effects, projections, policy/lease data,
  hidden knowledge identifiers, or stack traces.
- A terminal handoff names only `procedure.play.storytelling` for the remote narrator's next read.
- The stable outcomes are invalid request, audience denial, token conflict, missing plan, missing
  or oversized context, unavailable knowledge, route missing/needs-input, missing/stale/oversized/
  rejected procedures, unavailable local model, action failure, cancellation, timeout, and generic
  internal failure. An authorized unknown knowledge answer completes as unknown rather than failing.
- There is no resume operation. A blocked or failed plan remains historical; the remote model must
  submit a new plan after resolving missing information.

## Fixed acceptance fixture

Use the existing sealed-observatory fixture initialized by `CampaignFeature3Tests`:

- campaign: `campaign.test.sealed-observatory`;
- world: `world.feature-01.fixture`;
- action mechanic: `mechanic.game.core.world.rumour.confirm`;
- action procedure: `procedure.game.core.world.knowledge`;
- action roles: `rumour.feature-04.observatory-signal` as `rumour`, and
  `world.feature-01.fixture` as `world`.

The fixed request uses request token `story-plan.acceptance-01` and objective
“Investigate the observatory signal and act on what is known.” Its ordered steps are:

1. `campaign-context` — “Recall the current sealed-observatory campaign context.”
2. `knowledge` — “What is known about the observatory signal?”
3. `action` — “confirm the observatory signal”, with the `rumour` and `world` roles above and
   input `{}`.

The expected action narration is `The Observatory Signal is confirmed.`
