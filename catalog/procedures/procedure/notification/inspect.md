---
id: procedure.notification.inspect
category: notification
name: Read and clear notifications
governs: query(kind: "notifications"), notification state, what a rule may tell a person
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Notifications are what rules want a person told. Reading them is a query; clearing them is a
separate commit. Nothing delivers them — they wait until somebody asks.

## Instructions
1. Start with `query(kind: "notifications", state: "unread")`. That is the question a reader
   actually has, and everything else is a narrowing of it.
2. Filter by `topic` for one kind of notice, by `entityId` for everything anyone was told about one
   creature, and by `correlationId` for everything one committed change produced.
3. Bound a search with `from` and `to` as ISO-8601 UTC instants. `from` is inclusive and `to` is
   exclusive, so adjacent windows neither overlap nor skip.
4. A notice carries exactly one of the states `unread`, `read`, or `archived`. There is no
   `notification` commit kind: moving a notice between states has no protocol verb today.
5. Raise one from a rule by returning `notifications: [{ topic, subject, body, entityIds }]` from a
   reaction — see `procedure.event.react`. Give it a topic somebody would filter by and a subject
   readable in a list without opening it.
6. Read `correlationId` back through `query(kind: "events")` when you want to know why you were
   told something. The chain is still there.

## Constraints
- Content and links are written once, by a reaction that committed with its whole chain, and are
  never editable afterwards. A notice is evidence that a rule at a version decided this was worth
  saying; text that could be revised later would look like evidence and not be.
- The only thing a commit can change is the delivery state. There is no create, no edit, no delete.
- Archiving is one-way in this release. "I have dealt with this" must not be something a later
  mistake quietly undoes. An archived notice is still readable with `state: "archived"`.
- Marking read records when it was FIRST read. Marking unread clears that; archiving keeps it.
- A state change is idempotent for the state a notice already holds, so a client retrying a call it
  is unsure about does not have to find out which way the first attempt went.
- Neither the query nor the commit emits an event. Telling somebody something is not a change to
  the world, and reading a notice is not a change to anything.
- Nothing here is delivery. There is no push, no mail, no webhook, no polling and no scheduler, and
  none is planned in this release. A notification is a row that waits.
- A notice with no topic, no subject, or naming an entity that does not exist fails the whole root
  change. A notice nobody can find by the thing it concerns is a notice nobody will find.
