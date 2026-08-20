# Adaptive semantic retrieval for procedure contracts

Status: **Draft — design plan only; implementation waits for the retrieval trigger and approval**  
Last updated: 2026-08-20

## Goal

Let an AI find the right procedure contract with fewer searches as the procedure library grows. Search
uses the contract's canonical text plus learned, auditable intent aliases: alternate natural-language
requests that have been confirmed as useful routes to the same procedure.

One procedure can therefore gain many safe entry points:

    Canonical procedure: procedure.subscription.create
    Canonical description: Register a guard or reaction subscription.

    Confirmed aliases:
    - "make a rule happen whenever something changes"
    - "add a listener after an entity moves"
    - "trigger a curse when damage is applied"

A later AI asking any of those things receives the same underlying contract. The alias improves
retrieval; it does not change the procedure's instructions, governance, status, or authority.

## Recommended approach

Use a hybrid, local retrieval system rather than a standalone external vector database:

1. Filter by contract status, scope, and optional category before ranking.
2. Use SQLite full-text search for exact names, IDs, command kinds, and rare terms.
3. Use vector similarity over canonical procedure search documents and confirmed aliases for
   paraphrases.
4. Aggregate all matching entry points by procedure id, then return a small ranked candidate list
   with matching evidence.
5. The AI reads the actual procedure and still decides what applies. Retrieval never executes a
   write or directly authorises another procedure.

Keep the database portable as one campaign file. The vector implementation must be behind a small
provider interface so a local SQLite-compatible extension can be used when ready, without exposing
database-specific vector syntax to the rest of the engine.

## Why aliases need a feedback model

A model finding a procedure is not proof that it was the correct one. Automatically treating every
search result or cited contract as a permanent shortcut would gradually train the database on
guesses, and a popular but wrong alias would become a self-reinforcing routing error.

Use three evidence levels:

| Level | Meaning | Rank treatment |
| --- | --- | --- |
| Canonical | Text authored with the procedure version | Always searchable |
| Candidate alias | An AI proposes a paraphrase after retrieving and reading a procedure | Low weight; inspectable but cannot dominate canonical results |
| Confirmed alias | A later successful governed operation used the procedure and explicitly confirmed the route | Eligible for normal semantic ranking |
| Rejected alias | The route proved misleading or the procedure became incompatible | Excluded from search but retained as audit evidence |

The first release may skip candidate aliases and permit only confirmed ones. That is the safer
default if the confirmation flow does not create too much MCP overhead.

## Retrieval memory model

### Canonical searchable document

Generate one search document for every active procedure version. It includes only stable,
retrieval-relevant fields:

- id, name, category, description, governs, and constraints;
- a concise extracted task summary where the format makes that safe;
- explicitly authored examples or common intents, if the contract later gains such a field.

It excludes raw instructions from embeddings unless evaluation proves they improve retrieval; long
operational text can add noise and accidental semantic matches. The complete procedure is fetched
only after a candidate is selected.

Each document records procedure id/version, source-field fingerprint, scope/category, embedding
model id, model revision, creation time, and index status. A procedure revision invalidates only
the canonical document derived from the changed fields.

### Learned intent alias

An alias is a separate, immutable retrieval-evidence record:

- alias id and normalized query text;
- target procedure id, with the procedure version seen when it was learned;
- source search operation id and, for confirmation, the successful governed operation id;
- scope/category context and optional non-sensitive tags;
- evidence level, status, author/source, timestamps, confirmation count, rejection count, and
  last-confirmed time;
- embedding model/fingerprint/index state.

The target is the stable procedure id, rather than a frozen version, so a useful intent remains a
route to the latest active procedure. Search always resolves and displays the current eligible
version. When a revision materially changes purpose or governance, the procedure author may retire
or re-review its aliases.

Do not store full private conversation text by default. The AI supplies a concise normalized intent
phrase intended for reuse; reject or redact obvious secrets and give aliases a retention/expiry
policy if deployments later become multi-user.

## Confirmation flow

The intended low-cost learning loop is:

    1. AI searches procedures with a natural-language need.
    2. Search returns ranked candidates and an opaque search-operation id.
    3. AI reads a candidate procedure.
    4. AI performs the governed operation successfully and cites that procedure.
    5. AI records one concise intent alias linked to search operation, procedure, and outcome.
    6. Future searches can retrieve the contract through the canonical text or that alias.

The record command validates that:

- the target procedure was returned by the referenced search or otherwise explicitly read;
- the target was active and eligible in the original scope;
- the cited successful operation occurred in the same bounded evidence window;
- the phrase is within length/format limits, has no prohibited sensitive material, and is not
  identical to a prior alias;
- a procedure cannot confirm its own alias through a failed, dry-run, unrelated, or unaudited
  operation.

A later explicit rejection marks an alias ineligible and records why. Repeated confirmations
increase its confidence slowly; one success must not allow a vague alias to overwhelm exact
matches.

The exact feedback command name is a design choice. Keep it within the existing three verbs,
for example query(kind: "procedures") to search and commit(kind: "procedure-retrieval") with an
operation of record, confirm, or reject. Do not add a fourth MCP tool.

## Ranking and result contract

For each search, rank at the procedure level, not at the individual-alias level:

    eligible scope/status filter
      -> exact id/name and FTS candidates
      -> vector candidates for canonical documents and aliases
      -> combine and de-duplicate by procedure id
      -> confidence/quality adjustments
      -> return top candidates plus evidence

The response should identify why a candidate was returned without exposing vector internals as an
authority claim:

- canonical field match;
- exact or full-text terms;
- matching confirmed alias text, redacted when necessary;
- applicable scope/category and procedure version;
- a coarse confidence tier and deterministic fallback notice.

Hard rules:

- An exact ID or active category/governs match outranks a vague semantic alias.
- Inactive, archived, wrong-scope, or rejected-alias targets never appear.
- Scores only rank candidates. They never select a procedure automatically, create a contract, or
  execute a write.
- Return a small bounded list. If confidence is weak, say that no confident route was found rather
  than inventing a match.
- FTS remains a tested fallback if embeddings, index, or local model are unavailable.
- Ranking uses recency and confirmation evidence as bounded tie-breakers, not an opaque popularity
  contest.

## Embedding and index design

Create narrow interfaces in the core for embedding and candidate search, with the provider
implementation in DataAccess or a dedicated infrastructure project:

    IProcedureEmbeddingProvider
      - model identity and readiness
      - batch embed normalized search text

    IProcedureSemanticIndex
      - upsert canonical document or alias
      - mark/delete stale entries
      - return bounded nearest candidates subject to metadata filters

The initial host configuration is explicit and local: provider endpoint/model, timeout, batch size,
maximum input length, and disabled-by-default status. It must never automatically download a model,
call a hosted embedding API, or block normal procedure search if unavailable.

Persist vectors, metadata, and indexes in or alongside the portable SQLite database according to
the selected SQLite-compatible provider. Include index/model state in catalog verification and
rebuild facilities. A model change or embedding-format change marks affected entries stale and
rebuilds them deterministically; it never mixes incomparable vectors in one ranking.

## Delivery slices

### Slice 0 — evidence gate and evaluation corpus

Do not build vector infrastructure first. Instrument present procedure search and collect an
anonymised local evaluation corpus: query text, result ids, which contracts were read, which were
ultimately cited successfully, zero-result cases, and correction/retry patterns. Define retrieval
quality measures before selecting a provider.

The agreed implementation trigger is evidence of material burden: about 150 procedure contracts,
logged known-procedure retrieval misses, or repeated costly search chains. The current architecture
also names this threshold.

**Acceptance:** a curated test corpus has at least exact, paraphrase, ambiguous, no-match,
wrong-scope, and deprecated-procedure examples, with a written baseline from existing FTS search.

### Slice 1 — strengthen deterministic search first

Verify tokenisation, stop-word behaviour, exact-id/name matching, category/governs filters,
pagination, and FTS ranking. Add the procedure-search operation id and structured matching evidence
to read-only search results. Do not add embeddings yet.

**Acceptance:** known lexical searches return the right active procedure with explainable evidence;
the test suite catches intent collisions and no-result regressions.

### Slice 2 — retrieval-memory data and feedback without vectors

Add alias/evidence persistence, migrations, store interfaces, catalog/export rules, expiry/review
policy, MCP contracts, validation, and query inspection. Index aliases with FTS first to prove the
learning loop and audit model before semantic infrastructure is introduced.

**Acceptance:** a confirmed alias improves a paraphrase lookup; a candidate or rejected alias
cannot outrank a canonical exact match; all aliases trace to search and successful-operation
evidence.

### Slice 3 — provider abstraction and canonical embeddings

Add the disabled-by-default local embedding provider and semantic-index interfaces. Generate,
fingerprint, backfill, invalidate, and rebuild canonical procedure documents. Retain deterministic
FTS fallback and record provider/model details in retrieval evidence.

**Acceptance:** an unavailable provider leaves normal search fully functional; changing a procedure
revision or model makes precisely the affected vector documents stale and rebuildable.

### Slice 4 — alias embeddings and hybrid ranking

Embed eligible confirmed aliases, implement metadata-filtered vector retrieval, aggregate candidates
per procedure, and combine canonical, FTS, and alias evidence with documented bounded weights.
Build an offline evaluator that compares hybrid ranking to the Slice 0 corpus before enabling it
for ordinary searches.

**Acceptance:** hybrid search meets an agreed quality improvement over the FTS baseline without
regressing exact IDs, scope isolation, status filtering, or known no-match cases.

### Slice 5 — controlled learning and quality controls

Expose record, confirm, reject, list, and retire alias paths through the existing MCP verbs.
Require confirmation evidence by default, add duplicate/near-duplicate alias warnings, cap aliases
per procedure, rate-limit writes, and add optional human review for promotion. Provide a clear
fallback/recovery response when feedback is rejected.

**Acceptance:** a wrong alias can be rejected and immediately disappears from ranking; one noisy
AI cannot flood a procedure with high-ranking shortcuts; every promoted alias remains auditable.

### Slice 6 — operations and website visibility

Add read-only inspection to the later maintainer surface: search diagnostics, stale index state,
top aliases, promotion/rejection evidence, and rebuild health. The human-facing campaign website
does not expose internal procedure retrieval records.

**Acceptance:** a maintainer can answer why a procedure was returned, which alias helped, whether
the vector index is current, and how to correct a bad route.

## Required tests

- deterministic baseline search, exact IDs, categories, scopes, statuses, and FTS fallback;
- canonical document extraction and revision/model invalidation;
- candidate, confirmed, rejected, duplicate, expired, and retired aliases;
- evidence-link validation, including attempts to confirm a route without a matching successful
  governed operation;
- privacy/length/redaction rules and stored-text retention;
- aggregation of many aliases to one procedure without duplicate results;
- score/ranking regression suite, known retrieval misses, ambiguity, and deliberate no-match cases;
- provider timeout, malformed embedding, unavailable index, partial backfill, and model upgrade;
- catalog portability, backup/restore, migration, and rebuild from canonical records;
- no change of world state from any search or feedback read path;
- MCP protocol walk proving search, read, successful use, confirmation, later retrieval, and
  rejection of the alias.

## Non-goals

This plan does not use vectors to select or run mechanics, write procedures automatically, treat
raw chat as permanent training data, call a cloud embedding service by default, introduce a
separate hosted vector database, or replace full-text search. Semantic retrieval is an advisory
layer over the existing governed procedure system.

## Relationship to existing plans

This is the detailed implementation of dynamic procedure retrieval in ARCHITECTURE.md P13 and the
vector-retrieval stage of LOCAL_INTENT_ROUTING_PLAN.md. It adds adaptive, confirmed intent aliases
as the proposed learning mechanism. Update those higher-level documents only after this design is
ratified, so one committed decision remains the source of truth.

