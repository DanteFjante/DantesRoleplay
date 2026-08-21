# Knowledge and facts — Slice 3 time, contradiction, and supersession confirmation

Status: **Approved by the user and implemented as Slice 3 on 2026-08-21**  
Date: 2026-08-21

## Existing authoritative owner

`game.core.world.clock` on an active world root is the authoritative in-world coordinate. Its
`currentMinute` is monotonic during ordinary play and shares the root's `calendarId`. Knowledge
records must not duplicate the clock, calendar, operation ID, or wall-clock timestamp.

The structural event ledger remains audit evidence only. It does not supply a stable queryable
world-time projection and must not be reinterpreted as validity or contradiction provenance.

## Proposed permanent vocabulary

| ID | Shape | Closed purpose |
| --- | --- | --- |
| `game.core.world.knowledge.validity` | companion component on a fact, rumour, secret, or clue | `{validFromMinute, validUntilMinute?}`. Minutes use the record's scoped world clock; start is inclusive and optional end is exclusive. |
| `game.core.world.knowledge.contradicts` | canonical-order relationship between two knowledge records | Exact `{}`. The lower stable ID is `from`; both endpoints have the same world and `knowledge.about` target. It records intentional conflict but does not select a truth. |
| `game.core.world.knowledge.supersedes` | directed newer-knowledge → prior-knowledge relationship | Exact `{}`. Both endpoints have the same world and `knowledge.about` target. It replaces one time-bounded record with another. |

Existing Feature 4 records remain valid **atemporal** records until a reviewed administrative
initialization gives them a validity component. They are not silently assigned minute zero. An
atemporal record cannot participate in `supersedes`; this forces authored history to be explicit.

## Validity semantics

1. `validFromMinute` is an integer from 0 through 1,000,000,000. `validUntilMinute`, when
   present, is an integer strictly greater than `validFromMinute` and within the same bound.
2. A time-bounded record is effective at minute `m` exactly when
   `validFromMinute <= m < validUntilMinute`, or when it has no end and `validFromMinute <= m`.
3. A trusted history read takes an explicit `asOfMinute` or, when omitted, reads the active world
   root clock once at the start of the operation. It returns atemporal records at every minute,
   marks a timed-out record historical, and never rewrites a record's summary/provenance/kind.
4. This slice does not add scheduled future truth, world-clock correction automation, an authored
   real-world timestamp, or a new time field to acquisition records. Future intentions and
   predictions remain a later modality extension.

## Supersession semantics

1. Recording `newer supersedes prior` is an atomic trusted-host operation. Both knowledge records
   are already valid and time-bounded in the same world and about the same subject.
2. The operation requires `prior.validUntilMinute == newer.validFromMinute`; it therefore creates
   no gap or overlap. It also rejects self-links, duplicate links, any second successor for the
   prior record, and cycles.
3. A successor may itself later be superseded, making a linear history for one subject. The old
   entities remain queryable at their earlier minutes; only their effective-current projection
   changes.
4. A supersession changes no actor's epistemic state. What an actor knows is Slice 1/2 state;
   canonical applicability at a time is a separate answer.

## Contradiction semantics

1. `contradicts` is stored once in lexical stable-ID order and has no direction of truth. It is
   allowed only between records that share a world and an `about` target.
2. Contradictory records may overlap in validity. An as-of read returns each applicable record and
   a deterministic `contested` indication; it does not collapse them to one answer.
3. A contradiction link never automatically changes rumour confirmation, fact status, secret
   sensitivity, baseline dissemination, individual belief, or authorization.

## Initial coordinator and test boundary

Add a trusted-host `IKnowledgeTimelineCoordinator`, not a public MCP tool. It owns validity
initialization/correction, contradiction linking, supersession, and bounded trusted history reads.
It validates world scope, clock bounds, endpoint kinds, relationship order, same-subject rules,
interval adjacency, and cycles before writing.

The fixture will add two explicitly timed knowledge records about the same subject: one ends at
minute 120 and is superseded by the other beginning at minute 120. A separate pair remains
simultaneously effective and linked as contradictory. Tests prove minute 119/120 behavior,
history persistence, cycle rejection, canonical contradiction order, and that no actor knowledge
state changes.

## Confirmation requested

The user approved this vocabulary. Slice 3 now implements the validity component, timeline
coordinator, timed/superseded and contested fixtures, focused tests, catalog validation, and the
receipt. It still adds no player queries, vector retrieval, public MCP methods, or generic temporal
engine.
