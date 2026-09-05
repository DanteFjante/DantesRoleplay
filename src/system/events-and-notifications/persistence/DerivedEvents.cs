using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Ecs;
using DantesRoleplay.Events;
using Json.Schema;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>What validating one reaction's declared events produced.</summary>
/// <param name="Code">Empty when every event was accepted; a stable failure code otherwise.</param>
internal sealed record DerivedEventResult(
    IReadOnlyList<ProposedEvent> Proposals,
    string Code = "",
    string Reason = "")
{
    public bool Ok => Code.Length == 0;

    public static DerivedEventResult Accepted(IReadOnlyList<ProposedEvent> proposals) => new(proposals);

    public static DerivedEventResult Rejected(string code, string reason) => new([], code, reason);
}

/// <summary>
/// Turns the events a reaction declared into proposals the chain can guard and record.
///
/// Everything here is a refusal that fails the whole root change, and that is not severity for its
/// own sake. A declared event is an assertion the rest of the chain will act on; one that names an
/// unregistered type, carries a payload its schema rejects, or points at an entity that does not
/// exist is a rule that is wrong about the world. Recording it and continuing would put a false
/// statement in the one place the system asks people to believe.
///
/// Validation happens at EMISSION, against the version of the type active right now, inside the
/// transaction that will either commit both the event and its cause or neither. A type revised
/// tomorrow therefore cannot make today's recorded event retroactively non-conforming — which is
/// the difference between an audit trail and a snapshot of current opinion.
/// </summary>
internal static class DerivedEvents
{
    /// <summary>
    /// Structural types are the kernel's own record of what it did. A rule able to declare one
    /// could claim a component was replaced that never was, in the one place whose entire value is
    /// that it can be believed.
    /// </summary>
    private const string ReservedPrefix = "world.";

    public static async Task<DerivedEventResult> ProposeAsync(
        DantesRoleplayDbContext db,
        IReadOnlyList<DeclaredEvent> declared,
        string producer,
        string executionId,
        string correlationId,
        string causationEventId,
        int depth,
        CancellationToken cancellationToken,
        string applicationStateSpaceId = "")
    {
        if (declared.Count == 0)
        {
            return DerivedEventResult.Accepted([]);
        }

        var proposals = new List<ProposedEvent>(declared.Count);

        for (var ordinal = 0; ordinal < declared.Count; ordinal++)
        {
            var candidate = declared[ordinal];
            var type = (candidate.Type ?? string.Empty).Trim();
            var where = $"{producer} declared event {ordinal}";

            if (type.Length == 0)
            {
                return Reject($"{where} with no type. An event without a type is an assertion nobody can read.");
            }

            if (type.StartsWith(ReservedPrefix, StringComparison.Ordinal))
            {
                return Reject(
                    $"{where} of type '{type}'. Types beginning '{ReservedPrefix}' are the kernel's "
                    + "record of structural change and cannot be declared by a rule — propose the "
                    + "effect instead, and the event follows from it.");
            }

            var registered = await db.EventTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == type, cancellationToken);

            if (registered is null)
            {
                return Reject($"{where} of type '{type}', which is not registered. Define it before emitting it.");
            }

            if (registered.Status != EventTypeStatus.Active)
            {
                return Reject(
                    $"{where} of type '{type}', which is {registered.Status.ToString().ToLowerInvariant()} "
                    + "rather than active.");
            }

            var version = await db.EventTypeVersions.AsNoTracking()
                .FirstOrDefaultAsync(
                    v => v.EventTypeId == type && v.Version == registered.CurrentVersion,
                    cancellationToken);

            if (version is null)
            {
                return Reject(
                    $"{where} of type '{type}', whose current version {registered.CurrentVersion} has no "
                    + "stored schema. The type is registered but unusable.");
            }

            JsonNode? payload;

            try
            {
                payload = JsonNode.Parse(string.IsNullOrWhiteSpace(candidate.Payload) ? "{}" : candidate.Payload);
            }
            catch (JsonException ex)
            {
                return Reject($"{where} with a payload that is not JSON: {ex.Message}");
            }

            if (payload is not JsonObject)
            {
                return Reject($"{where} with a payload that is not a JSON object. Every event payload has an object root.");
            }

            // Canonical text, then an element: the evaluator reads JsonElement, and the same
            // canonical form is what gets recorded, so what was validated is exactly what is stored.
            var canonical = payload.ToJsonString();
            var problem = SchemaProblem(version.PayloadSchema, canonical, $"{where} of type '{type}'");

            if (problem is not null)
            {
                return Reject(problem);
            }

            var ids = new List<string>();

            foreach (var raw in candidate.EntityIds ?? [])
            {
                var id = (raw ?? string.Empty).Trim();

                if (id.Length == 0)
                {
                    continue;
                }

                // Live only. The entity index is the filter people actually search by, and an
                // event pointing at nothing is a row that can never be found the way it will be
                // looked for.
                var exists = applicationStateSpaceId.Length == 0
                    ? await db.Entities.AsNoTracking()
                        .AnyAsync(e => e.Id == id && e.DeletedAt == null, cancellationToken)
                    : await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
                        .AnyAsync(e => e.StateSpaceId == applicationStateSpaceId
                            && e.Id == id && e.DeletedAtUtc == null, cancellationToken);

                if (!exists)
                {
                    return Reject($"{where} naming entity '{id}', which does not exist or has been deleted.");
                }

                if (!ids.Contains(id, StringComparer.Ordinal))
                {
                    ids.Add(id);
                }
            }

            proposals.Add(new ProposedEvent(
                type,
                canonical,
                ids,
                (candidate.Scope ?? string.Empty).Trim(),
                ordinal,
                Depth: depth,
                CorrelationId: correlationId,

                // A declared event is caused by the event the rule was handling, exactly as its
                // effects are. Nothing about being declared rather than derived changes its place
                // in the chain.
                CausationId: causationEventId,

                // Who asserted it. Causation alone cannot say: two subscriptions answering the
                // same event would both name it, and a reader could not tell which made the claim.
                ProducerExecutionId: executionId));
        }

        return DerivedEventResult.Accepted(proposals);

        static DerivedEventResult Reject(string reason) =>
            DerivedEventResult.Rejected("SUBSCRIBER_INVALID_EVENT", reason);
    }

    /// <summary>
    /// The payload against the schema, or null when it conforms.
    ///
    /// A schema that will not compile is reported as the event's problem rather than thrown: the
    /// type was registered with a syntax check, so this can only happen if the stored text was
    /// damaged, and taking the root change down with an explanation beats an unhandled exception.
    /// </summary>
    private static string? SchemaProblem(string schemaJson, string payloadJson, string where)
    {
        JsonSchema schema;

        try
        {
            schema = JsonSchema.FromText(EventPayloadRoleMetadata.WithoutExtension(string.IsNullOrWhiteSpace(schemaJson) ? "{}" : schemaJson),
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        }
        catch (Exception ex) when (ex is JsonException or JsonSchemaException)
        {
            return $"{where}, whose registered schema cannot be read: {ex.Message}";
        }

        using var instance = JsonDocument.Parse(payloadJson);

        var evaluation = schema.Evaluate(
            instance.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (evaluation.IsValid)
        {
            return null;
        }

        // The verdict is what matters and it is already decided; the detail is a courtesy. Reading
        // it walks a nested result tree whose shape belongs to the schema library, so a change there
        // must not be able to turn a clean rejection into an unhandled exception — which is exactly
        // what it did the first time, by way of a null Details on a leaf.
        List<string> complaints;

        try
        {
            complaints = Complaints(evaluation).Take(3).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            complaints = [];
        }

        return complaints.Count == 0
            ? $"{where}: the payload does not match its registered schema."
            : $"{where}: the payload does not match its registered schema — {string.Join("; ", complaints)}.";
    }

    /// <summary>
    /// The failing assertions, flattened, so the message names what is actually wrong rather than
    /// saying "invalid". A rule author reading this has no debugger and one round trip.
    /// </summary>
    private static IEnumerable<string> Complaints(EvaluationResults results)
    {
        if (results is null || results.IsValid)
        {
            yield break;
        }

        if (results.Errors is not null)
        {
            foreach (var error in results.Errors)
            {
                var at = results.InstanceLocation.ToString();
                yield return string.IsNullOrEmpty(at) ? error.Value : $"{at}: {error.Value}";
            }
        }

        // Null on a leaf. Not a defensive habit — the absence of this check is what made a rejected
        // payload throw instead of being reported as rejected.
        if (results.Details is null)
        {
            yield break;
        }

        foreach (var nested in results.Details)
        {
            foreach (var complaint in Complaints(nested))
            {
                yield return complaint;
            }
        }
    }
}
