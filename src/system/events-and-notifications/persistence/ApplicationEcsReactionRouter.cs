using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Routes explicitly application-scoped subscriptions into the application evaluator. It only
/// prepares typed batches; the application ECS applier owns verification, effects, event staging,
/// cascade bounds and the transaction. Legacy source-null reactions remain on <see cref="EventRouter"/>.
/// </summary>
public sealed class ApplicationEcsReactionRouter(
    DantesRoleplayDbContext db,
    IPublicApplicationCatalogProvider catalogs,
    IStateSpaceRegistry stateSpaces,
    IApplicationMechanicProjectionMappingResolver mappings,
    IApplicationMechanicEvaluator evaluator,
    IApplicationEcsEffectBatchBuilder batchBuilder) : IApplicationEcsReactionRouter
{
    public async IAsyncEnumerable<ApplicationEcsReactionResult> RouteAsync(
        ApplicationEcsReactionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Source.IsValid)
        {
            yield return ApplicationEcsReactionResult.Reject("APPLICATION_EVENT_SOURCE_INVALID", "The application event source is invalid.");
            yield break;
        }

        ApplicationIdentifier? application = null;
        try { application = ApplicationIdentifier.Parse(context.Source.ApplicationId); }
        catch (ArgumentException) { }
        if (application is null)
        {
            yield return ApplicationEcsReactionResult.Reject("APPLICATION_UNKNOWN", "The application event application is invalid.");
            yield break;
        }

        var stateSpace = stateSpaces.Get(context.Source.StateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != application)
        {
            yield return ApplicationEcsReactionResult.Reject("STATE_SPACE_APPLICATION_MISMATCH", "The event source does not name the exact application state space.");
            yield break;
        }

        var ordinal = 0;
        foreach (var @event in context.AcceptedEvents.OrderBy(value => value.Sequence))
        {
            if (@event.Source is null || @event.Source != context.Source)
            {
                yield return ApplicationEcsReactionResult.Reject("APPLICATION_EVENT_SOURCE_MISMATCH", "Every routed event must carry the exact trusted application source.");
                yield break;
            }

            var rows = await MatchingAsync(@event, context.Source, cancellationToken);
            foreach (var registration in rows)
            {
                if (!EventRouterMatches(registration.Version, @event)) continue;
                var overspent = context.Budget.CountExecution(registration.Id, registration.Version.MaxExecutionsPerChain);
                if (overspent is not null)
                {
                    yield return ApplicationEcsReactionResult.Reject(overspent,
                        $"{ChainBudget.Explain(overspent)} Reached by application subscription '{registration.Id}'.");
                    yield break;
                }

                var result = await PrepareAsync(application, stateSpace, @event, registration,
                    context.RootOperationId, context.RootSeed, ordinal, cancellationToken);
                if (!result.Ok)
                {
                    yield return ApplicationEcsReactionResult.Reject(result.Code, result.Reason);
                    yield break;
                }
                yield return ApplicationEcsReactionResult.Allow([result.Batch!]);
                ordinal++;
            }
        }
    }

    private async Task<IReadOnlyList<Registration>> MatchingAsync(
        EventDetail @event,
        EventSourceContext source,
        CancellationToken cancellationToken) =>
        (await db.Subscriptions.AsNoTracking()
            .Where(value => value.Status == SubscriptionStatus.Active
                && (value.Scope == @event.Scope || value.Scope == ""))
            .Join(db.SubscriptionVersions.AsNoTracking(),
                value => new { SubscriptionId = value.Id, Version = value.CurrentVersion },
                version => new { SubscriptionId = version.SubscriptionId, Version = version.Version },
                (value, version) => new { value, version })
            .Where(value => value.version.Mode == SubscriptionMode.Reaction
                && value.version.EventTypeId == @event.TypeId
                && value.version.ApplicationId == source.ApplicationId
                && value.version.StateSpaceId == source.StateSpaceId)
            .OrderBy(value => value.version.Order).ThenBy(value => value.value.Id)
            .ToListAsync(cancellationToken))
        .Select(value => new Registration(value.value.Id, value.value.Scope, value.version))
        .ToArray();

    private async Task<Preparation> PrepareAsync(
        ApplicationIdentifier application,
        StateSpaceView stateSpace,
        EventDetail @event,
        Registration registration,
        string rootOperationId,
        long rootSeed,
        int ordinal,
        CancellationToken cancellationToken)
    {
        if (!catalogs.TryGet(application, out var catalog))
            return Preparation.Failed("APPLICATION_CATALOG_UNAVAILABLE", "The exact active application catalog is unavailable.");

        CatalogRecordView record;
        try { record = catalog.Inspect(new(application, application.Value, registration.Version.EventMechanicId)); }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        { return Preparation.Failed("SUBSCRIBER_UNAVAILABLE", "The application reaction mechanic is unavailable."); }
        if (record.Summary.Kind != "mechanic" || record.Summary.Status != "active")
            return Preparation.Failed("SUBSCRIBER_UNAVAILABLE", "The application reaction mechanic is not active.");

        MechanicRequirements requirements;
        try
        {
            using var content = JsonDocument.Parse(record.ContentJson);
            if (!content.RootElement.TryGetProperty("requirements", out var raw)
                || raw.ValueKind != JsonValueKind.String)
                return Preparation.Failed("SUBSCRIBER_INVALID", "The application reaction mechanic has no valid requirements.");
            requirements = MechanicRequirements.Parse(raw.GetString()!);
        }
        catch (JsonException)
        { return Preparation.Failed("SUBSCRIBER_INVALID", "The application reaction mechanic requirements are malformed."); }

        if (requirements.Event is null || requirements.Event.Mode != EventMechanicMode.Reaction
            || !requirements.Event.Types.Contains(@event.TypeId, StringComparer.Ordinal)
            || requirements.Children.Count > 0)
            return Preparation.Failed("SUBSCRIBER_UNAVAILABLE", "The application mechanic no longer declares this exact reaction event.");

        if (!SubscriptionFanoutSelectorMetadata.TryRead(registration.Version.FanoutSelectorJson,
                out var fanout, out var fanoutProblem))
            return Preparation.Failed("SUBSCRIBER_INVALID_FANOUT_SELECTOR", fanoutProblem);
        if (fanout is not null)
            return Preparation.Failed("APPLICATION_FANOUT_UNSUPPORTED", "Application reactions require explicit role bindings; fan-out is not enabled on this generic path.");

        var bindings = ParseBindings(registration.Version.FixedRoleEntityIdsJson);
        if (bindings is null)
            return Preparation.Failed("SUBSCRIBER_INVALID_BINDINGS", "Application reaction fixed role bindings are corrupt.");
        if (!SubscriptionRoleFromEventPayload.TryRead(registration.Version.RoleFromEventPayloadJson,
                out var payloadRole, out var payloadProblem))
            return Preparation.Failed("SUBSCRIBER_INVALID_ROLE_BINDING", payloadProblem);

        if (payloadRole is { } payloadBinding)
        {
            if (requirements.Children.Count > 0 || !requirements.Roles.ContainsKey(payloadBinding.Key)
                || bindings.ContainsKey(payloadBinding.Key))
                return Preparation.Failed("SUBSCRIBER_INVALID_ROLE_BINDING", "The event payload role no longer matches the exact mechanic roles.");

            var typeVersion = await db.EventTypeVersions.AsNoTracking().FirstOrDefaultAsync(value =>
                value.EventTypeId == @event.TypeId && value.Version == @event.TypeVersion, cancellationToken);
            if (typeVersion is null || !EventPayloadRoleMetadata.TryRead(typeVersion.PayloadSchema, out var fields, out _)
                || !fields.Contains(payloadBinding.Value, StringComparer.Ordinal))
                return Preparation.Failed("SUBSCRIBER_INVALID_ROLE_BINDING", "The payload role field is not declared by the exact event schema.");
            string? entityId = null;
            try
            {
                using var payload = JsonDocument.Parse(@event.PayloadJson);
                if (payload.RootElement.ValueKind == JsonValueKind.Object
                    && payload.RootElement.TryGetProperty(payloadBinding.Value, out var value)
                    && value.ValueKind == JsonValueKind.String)
                    entityId = value.GetString();
            }
            catch (JsonException) { }
            if (string.IsNullOrWhiteSpace(entityId) || entityId != entityId.Trim()
                || @event.EntityIds.Count(value => value == entityId) != 1)
                return Preparation.Failed("SUBSCRIBER_INVALID_EVENT_PAYLOAD_ROLE", "The payload role must name exactly one accepted event entity.");
            bindings[payloadBinding.Key] = entityId;
        }

        var mapping = await mappings.ResolveAsync(stateSpace.StateSpaceId, application,
            record.Summary.QualifiedId, requirements, cancellationToken);
        if (!mapping.Resolved)
            return Preparation.Failed("SUBSCRIBER_PROJECTION_MAPPING_FAILED", mapping.Problems[0].SafeMessage);

        var seed = EventRouter.DeriveSeed(rootSeed, @event.Sequence, registration.Id, "reaction", ordinal);
        var operationId = Hash(rootOperationId + "\u001f" + @event.Id + "\u001f" + registration.Id + "\u001f" + ordinal)[..32];
        var evaluation = await evaluator.EvaluateAsync(new(
            stateSpace.StateSpaceId, application, record.Summary.QualifiedId,
            record.Summary.ContentFingerprint, mapping.Mapping!, bindings, "{}", seed,
            new MechanicExecutionContext(rootOperationId, operationId, @event.Id, ordinal),
            Event: EventEnvelope.ForReaction(@event)), cancellationToken);
        if (!evaluation.Evaluated || evaluation.Run is null)
            return Preparation.Failed("SUBSCRIBER_PROJECTION_FAILED",
                evaluation.Problems.FirstOrDefault() ?? "The application reaction projection could not be evaluated.");
        if (!evaluation.Run.Ok)
            return Preparation.Failed(evaluation.Run.LimitHit.Length > 0 ? "SUBSCRIBER_LIMIT" : "SUBSCRIBER_FAILED",
                "The application reaction mechanic failed.");
        if (!string.IsNullOrWhiteSpace(evaluation.Run.Output.Decision)
            || evaluation.Run.Output.Notifications.Count > 0)
            return Preparation.Failed("SUBSCRIBER_FORBIDDEN_OUTPUT", "Application reactions may only produce typed effects and declared events.");

        var proposal = evaluation.Proposal.Append(evaluation.Run.Output);
        var built = await batchBuilder.BuildAsync(stateSpace, mapping.Mapping!, evaluation.Projection!,
            requirements, proposal, record.Summary.QualifiedId, record.Summary.Version, seed, cancellationToken);
        if (!built.Ok)
            return Preparation.Failed(built.Problems[0].Code, built.Problems[0].SafeMessage);

        var producerExecutionId = operationId;
        return Preparation.Success(new ApplicationEcsReactionBatch(built.Batch!, @event.Id,
            @event.Depth + 1, producerExecutionId));
    }

    private static bool EventRouterMatches(SubscriptionVersion subscription, EventDetail @event)
    {
        try
        {
            using var payload = JsonDocument.Parse(@event.PayloadJson);
            using var filter = JsonDocument.Parse(subscription.PayloadEqualsJson);
            if (filter.RootElement.EnumerateObject().Any(property =>
                    !payload.RootElement.TryGetProperty(property.Name, out var value)
                    || value.GetRawText() != property.Value.GetRawText())) return false;
            using var tracked = JsonDocument.Parse(subscription.TrackedEntityIdsJson);
            var ids = tracked.RootElement.EnumerateArray().Select(value => value.GetString())
                .Where(value => value is not null).Cast<string>().ToArray();
            return ids.Length == 0 || ids.Intersect(@event.EntityIds, StringComparer.Ordinal).Any();
        }
        catch (JsonException) { return false; }
    }

    private static Dictionary<string, string>? ParseBindings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(value)
                    || value != value.Trim() || !result.TryAdd(property.Name, value)) return null;
            }
            return result;
        }
        catch (JsonException) { return null; }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Registration(string Id, string Scope, SubscriptionVersion Version);
    private sealed record Preparation(bool Ok, ApplicationEcsReactionBatch? Batch, string Code, string Reason)
    {
        public static Preparation Success(ApplicationEcsReactionBatch batch) => new(true, batch, "", "");
        public static Preparation Failed(string code, string reason) => new(false, null, code, reason);
    }
}
