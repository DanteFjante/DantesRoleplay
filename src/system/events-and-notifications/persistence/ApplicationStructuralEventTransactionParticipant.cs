using System.Text.Json.Nodes;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Derives the generic structural component events from receipts captured by the authoritative
/// ECS write transaction. Catalog rules cannot forge these records: the payload and snapshot both
/// come from the actual before/after values the store applied.
/// </summary>
public sealed class ApplicationStructuralEventTransactionParticipant(
    IStateSpaceRegistry stateSpaces,
    IEventLedger events,
    IEventTypeStore eventTypes,
    IBoundedJsonSchemaValidator schemas) : IApplicationEcsEventSourceParticipant
{
    public async Task<IReadOnlyList<EventDetail>> StageEventsAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        ApplicationEcsEventEmissionContext emission,
        CancellationToken cancellationToken = default)
    {
        var proposals = new List<ProposedEvent>();
        foreach (var receipt in receipts.OrderBy(value => value.Index))
        {
            if (receipt.BatchEffectIndex < 0 || receipt.BatchEffectIndex >= batch.Effects.Count)
                throw new ApplicationEcsTransactionParticipantException(
                    "An application component event receipt is outside its effect batch.");
            var effect = batch.Effects[receipt.BatchEffectIndex];
            var type = EventType(receipt.Type);
            if (type is null) continue;
            if (receipt.QualifiedTypeId.Length == 0 || receipt.ComponentTypeVersion is not > 0)
                throw new ApplicationEcsTransactionParticipantException(
                    "A changed component has no exact registered type receipt.");

            var payload = new JsonObject
            {
                ["effectIndex"] = receipt.Index,
                ["entityId"] = receipt.EntityId,
                ["definitionId"] = receipt.QualifiedTypeId,
                ["before"] = Snapshot(receipt.BeforeJson),
                ["after"] = Snapshot(receipt.AfterJson)
            };
            if (receipt.Type == ApplicationEcsEffectType.ComponentMerge)
                payload["patch"] = Snapshot(effect.DataJson);

            var payloadJson = payload.ToJsonString();
            var registered = await eventTypes.GetAsync(type, cancellationToken: cancellationToken);
            if (registered is null || registered.Status != EventTypeStatus.Active
                || schemas.Validate(EventPayloadRoleMetadata.WithoutExtension(registered.PayloadSchema), payloadJson).Status
                    != SchemaValueStatus.Valid)
                throw new ApplicationEcsTransactionParticipantException(
                    "The derived application component event does not satisfy an active registered contract.");

            proposals.Add(new ProposedEvent(type, payloadJson, [receipt.EntityId], "",
                receipt.Index, emission.Depth, emission.RootOperationId, emission.CausationEventId,
                ComponentSnapshot: new EventComponentSnapshotDetail(
                    receipt.EntityId, receipt.QualifiedTypeId, receipt.ComponentTypeVersion.Value,
                    receipt.BeforeJson, receipt.BeforeRevision, receipt.AfterJson, receipt.AfterRevision)));
        }

        if (proposals.Count == 0) return [];
        var source = ApplicationEventSourceContext.Require(stateSpaces, batch);
        return await events.WriteAcceptedAsync(proposals, emission.RootOperationId, cancellationToken, source);
    }

    private static string? EventType(string effectType) => effectType switch
    {
        ApplicationEcsEffectType.ComponentAdd => "world.component.added",
        ApplicationEcsEffectType.ComponentSet or ApplicationEcsEffectType.ClockAdvance => "world.component.replaced",
        ApplicationEcsEffectType.ComponentMerge => "world.component.merged",
        ApplicationEcsEffectType.ComponentRemove => "world.component.removed",
        _ => null
    };

    private static JsonNode? Snapshot(string? json)
    {
        if (json is null) return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            throw new ApplicationEcsTransactionParticipantException(
                "An authoritative component receipt did not contain JSON.");
        }
    }
}
