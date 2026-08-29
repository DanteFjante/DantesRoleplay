namespace DantesRoleplay.Events;

/// <summary>
/// An event a mechanic asks for by name, rather than one the kernel derived from a world change.
///
/// The two are genuinely different claims. A <c>world.*</c> event is a RECORD: something changed,
/// and the ledger says what. A declared event is an ASSERTION: a rule says something happened that
/// no structural change describes — an alarm was raised, a bargain was struck, a spell ended. The
/// world may look identical before and after, and the fact is still worth recording, because the
/// next rule in the chain has no other way to hear about it.
///
/// This is why a mechanic cannot declare a <c>world.*</c> type. Those are the kernel's own record
/// of what it did, and a rule able to forge one could claim a component was replaced that never
/// was — with the ledger, whose entire job is to be believable, saying so.
///
/// The payload is a JSON string here for the same reason everything crossing the sandbox boundary
/// is: only strings cross. It is validated against the event type's registered schema at the
/// moment it is emitted, against the exact version then active, so a type revised later cannot
/// retroactively make a recorded event non-conforming.
/// </summary>
public sealed record DeclaredEvent
{
    /// <summary>The registered event type's id. Must exist, be active, and not be a <c>world.*</c> type.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Payload as JSON text, validated against the type's schema at emission.</summary>
    public string Payload { get; init; } = "{}";

    /// <summary>
    /// Which entities the event is about. Every id must name a live entity: an event indexed
    /// against something that does not exist is unfindable by the one filter people actually use.
    /// </summary>
    public IReadOnlyList<string> EntityIds { get; init; } = [];

    /// <summary>Scope, matching the subscription scope rules. Empty means everywhere.</summary>
    public string Scope { get; init; } = string.Empty;
}
