# D&D 2024 G6 authoritative clock bridge implementation

Status: accepted

## Boundary

This slice closes complete-campaign dependency G6 by making an application-installed base clock
executable from the derived application without creating a second clock owner.

Included:

- generic application/base component resolution in `ApplicationActionRunner`;
- one D&D application clock-advance mechanic and its procedure;
- focused application-execution and D&D catalog contract tests;
- complete-campaign graph and completion evidence after acceptance.

Excluded:

- wall-clock time, scheduling, calendar formatting, automatic time passage, or event reactions;
- a campaign-owned duplicate of world time;
- live activation, database writes, prototype migration, or UI controls;
- rest completion or other elapsed-time consumer behavior beyond proving the shared coordinate.

## Ownership and mapping

The generic `game` base application remains the component owner. Inside the D&D application, the
installed identity `dnd2024.game.core.world.clock` resolves to the exact current base component
`game.core.world.clock`; the same rule applies generically to an application-installed identity of
the form `<application>.<base-application>.<local-id>`. Resolution is allowed only when the base
application is declared on the exact state-space application revision and the target component's
registered owner matches that base application.

The application-local key remains the projection and mechanic key. The resolved exact component
type/version/schema hash remains the persistence authority. No fallback searches an unrelated
application and no duplicate D&D clock component is registered.

## Clock action

`dnd2024.mechanic.world.clock.advance` projects one active world with
`dnd2024.game.core.world.root` and `dnd2024.game.core.world.clock`. It accepts exactly positive
integer `minutes` from 1 through 1,440. It:

- validates the closed root and clock state;
- preserves `calendarId`;
- adds the requested minutes without exceeding 1,000,000,000;
- increments the embedded clock revision exactly once without exceeding 2,147,483,647;
- proposes one exact component replacement and no caller-derived state;
- returns the before/after coordinate as data.

The existing application execution projection records the persisted component revision. The typed
effect transaction rejects a stale component snapshot, and operation identity makes a successful
advance replay-safe. Invalid, zero, negative, overflow, corrupt, stale, or non-closed requests make
no clock change.

## Acceptance

- application-installed base component resolution succeeds only for a declared base owner;
- a D&D clock action advances the base-owned coordinate and embedded revision exactly once;
- replay does not advance it again;
- closed input prevents a caller from supplying the target minute, revision, or calendar;
- corrupt, overflow, and stale state fail without partial mutation;
- existing D&D rest projection resolves the same installed clock key;
- focused execution tests and catalog validation pass.
