using System.Text.Json;
using System.Text.Json.Nodes;

namespace DantesRoleplay.Events;

/// <summary>
/// Builds what a mechanic sees as <c>ctx.event</c>.
///
/// One definition, because a guard and a reaction must see the same SHAPE and differ only in the
/// fields that genuinely differ. The contract is explicit that a script branches on its declared
/// <c>mode</c> rather than null-testing its way into supporting both — which only works if `mode`
/// is always present and the rest of the envelope is predictable.
///
/// A guard's envelope has no <c>id</c> and no <c>sequence</c>: it is being asked about something
/// that does not exist yet and may never. It has <c>proposalOrdinal</c> instead, which is where the
/// event would land if allowed. Omitting them rather than sending nulls is the point — a guard that
/// reads <c>ctx.event.id</c> should fail loudly while it is being written, not silently compare
/// null to something later.
///
/// The payload is embedded as JSON, never as a string containing JSON.
/// </summary>
public static class EventEnvelope
{
    /// <summary>What a guard sees while deciding whether a proposal may become an event.</summary>
    public static string ForGuard(ProposedEvent proposal, int typeVersion)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var envelope = Common("guard", proposal.Type, typeVersion, proposal.Scope, proposal.PayloadJson,
            proposal.EntityIds, proposal.CorrelationId, proposal.CausationId, proposal.Depth);

        envelope["proposalOrdinal"] = proposal.Ordinal;

        return envelope.ToJsonString();
    }

    /// <summary>What a reaction sees while handling an event that has been accepted.</summary>
    public static string ForReaction(EventDetail accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);

        var envelope = Common("reaction", accepted.TypeId, accepted.TypeVersion, accepted.Scope,
            accepted.PayloadJson, accepted.EntityIds, accepted.CorrelationId, accepted.CausationId,
            accepted.Depth);

        envelope["id"] = accepted.Id;
        envelope["sequence"] = accepted.Sequence;

        return envelope.ToJsonString();
    }

    private static JsonObject Common(
        string mode,
        string typeId,
        int typeVersion,
        string scope,
        string payloadJson,
        IReadOnlyList<string> entityIds,
        string correlationId,
        string causationId,
        int depth)
    {
        var entities = new JsonArray();

        foreach (var id in entityIds)
        {
            entities.Add(id);
        }

        return new JsonObject
        {
            ["mode"] = mode,
            ["type"] = typeId,
            ["typeVersion"] = typeVersion,
            ["scope"] = scope,
            ["payload"] = Payload(payloadJson),
            ["entityIds"] = entities,
            ["correlationId"] = correlationId,

            // Empty rather than absent: "nothing caused this" is a fact worth stating, and a script
            // testing `ctx.event.causationId === ''` reads better than one testing for a key.
            ["causationId"] = causationId,
            ["depth"] = depth
        };
    }

    private static JsonNode Payload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(payloadJson) ?? new JsonObject();
        }
        catch (JsonException)
        {
            // The payload was built by the host against a registered schema, so this cannot happen
            // from a valid path. An empty object keeps a guard evaluable rather than failing the
            // whole world change over presentation.
            return new JsonObject();
        }
    }
}
