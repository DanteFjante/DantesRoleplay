using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.TriggerScheduling;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>
/// Rehydrates an exact retained catalog predicate, captures its declared state projection in the
/// source transaction, and later runs only that frozen projection through the bounded JS engine.
/// </summary>
public sealed class CatalogJavaScriptObserverPredicateAdapter(
    DantesRoleplayDbContext db,
    IApplicationActivationReader activations,
    ActivatedApplicationCatalogMaterializer catalogs,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants,
    IApplicationMechanicProjectionMappingResolver mappings,
    IApplicationMechanicProjectionResolver projections,
    ApplicationMechanicEvaluator evaluator,
    IBoundedJsonSchemaValidator schemas)
    : IApplicationObserverPredicateInputCapture, IApplicationObserverPredicateEvaluator
{
    internal const int MaximumInputBytes = 256 * 1024;
    internal const int MaximumProjectionBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly byte[] CursorKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("dantes-roleplay/observer-predicate/retained-catalog-cursor/v1"));

    public async Task<ApplicationObserverPredicateCapturedInput> CaptureAsync(
        InteractionInvocationHost host, ApplicationObserverPredicateSelection selection,
        string admittedInputJson, string? admittedEventJson, long seed,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw Failure("OBSERVER_PREDICATE_CAPTURE_TRANSACTION_REQUIRED",
                "Predicate input must be captured in the admitted source transaction.");
        var resolved = await ResolveAsync(host, selection, cancellationToken);
        if (resolved.Failure is not null) throw resolved.Failure;
        var input = CanonicalObject(admittedInputJson, "OBSERVER_PREDICATE_INPUT_INVALID");
        var eventJson = CanonicalObject(string.IsNullOrWhiteSpace(admittedEventJson) ? "{}" : admittedEventJson,
            "OBSERVER_PREDICATE_EVENT_INVALID");
        if (Encoding.UTF8.GetByteCount(input) + Encoding.UTF8.GetByteCount(eventJson) > MaximumInputBytes)
            throw Failure("OBSERVER_PREDICATE_INPUT_LIMIT", "Predicate input exceeds its byte limit.");
        if (resolved.Requirements!.InputSchema is { } inputSchema)
        {
            var compiled = schemas.Compile(inputSchema.GetRawText());
            if (!compiled.IsAccepted || schemas.Validate(compiled.ProfileId,
                    compiled.NormalizedSchema, input).Status != SchemaValueStatus.Valid)
                throw Failure("OBSERVER_PREDICATE_INPUT_SCHEMA", "Predicate input does not match its declared schema.");
        }
        var mapping = await mappings.ResolveAsync(host.StateSpaceId!, selection.ApplicationId,
            selection.Mechanic.DefinitionId, resolved.Requirements, cancellationToken);
        if (!mapping.Resolved)
            throw Failure(mapping.Problems.FirstOrDefault()?.Code ?? "OBSERVER_PREDICATE_MAPPING_FAILED",
                "The predicate projection mapping is unavailable.");
        var projected = await projections.ResolveAsync(host.StateSpaceId!, selection.ApplicationId,
            resolved.Requirements, mapping.Mapping!, selection.RoleEntityIds, input, seed, cancellationToken);
        if (!projected.Ok || projected.Projection is null)
            throw Failure(Code(projected.Problems.FirstOrDefault(), "OBSERVER_PREDICATE_PROJECTION_FAILED"),
                "The predicate input projection could not be captured.");
        var frozen = Freeze(projected.Projection with { Event = eventJson });
        var projectionJson = CanonicalProjection(frozen);
        if (Encoding.UTF8.GetByteCount(projectionJson) > MaximumProjectionBytes)
            throw Failure("OBSERVER_PREDICATE_PROJECTION_LIMIT", "The predicate projection exceeds its byte limit.");
        var mappingFingerprint = PredicateProof.Mapping(mapping.Mapping!);
        var inputFingerprint = PredicateProof.Input(input, eventJson);
        var projectionFingerprint = PredicateProof.Projection(projectionJson);
        var captureFingerprint = PredicateProof.Capture(selection, mappingFingerprint,
            inputFingerprint, projectionFingerprint);
        return new(Freeze(selection), frozen, mappingFingerprint, inputFingerprint,
            projectionFingerprint, captureFingerprint);
    }

    public async Task<ApplicationObserverPredicateResult> EvaluateAsync(
        InteractionInvocationHost freshHost, ApplicationObserverPredicateCapturedInput captured,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
            return Rejected("OBSERVER_PREDICATE_EVALUATION_TRANSACTION");
        if (!TryVerifyCaptured(freshHost, captured, out var projectionJson, out var problem))
            return Rejected(problem);
        ResolvedSelection resolved;
        await using (var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken))
        {
            resolved = await ResolveAsync(freshHost, captured.Selection, cancellationToken);
            if (resolved.Failure is not null) return Rejected(resolved.Failure.Code);
        }
        if (db.Database.CurrentTransaction is not null)
            return Rejected("OBSERVER_PREDICATE_EVALUATION_TRANSACTION");
        var navigator = new InMemoryCatalogNavigator(resolved.Catalog!.Manifest,
            new CatalogCursorCodec(CursorKey), resolved.Catalog.Resolution);
        var evaluation = await evaluator.EvaluateCapturedAsync(new(
            captured.Projection.StateSpaceId, captured.Selection.ApplicationId,
            captured.Selection.Mechanic.DefinitionId, captured.Selection.Mechanic.ContentFingerprint,
            new ApplicationMechanicProjectionMapping(
                new Dictionary<string, DantesRoleplay.Ecs.EcsComponentReference>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal)), captured.Selection.RoleEntityIds,
            captured.Projection.Input, captured.Projection.Seed, Event: captured.Projection.Event),
            navigator, captured.Projection, cancellationToken);
        if (!evaluation.Evaluated || evaluation.Run is null || !evaluation.Run.Ok)
            return Rejected(string.IsNullOrWhiteSpace(evaluation.Run?.LimitHit)
                ? "OBSERVER_PREDICATE_EVALUATION_FAILED" : "OBSERVER_PREDICATE_LIMIT");
        if (!ClosedOutput(evaluation, out var matches))
            return Rejected("OBSERVER_PREDICATE_OUTPUT_INVALID");
        var evidence = new ApplicationObserverPredicateEvidence(Freeze(captured.Selection),
            captured.MappingFingerprint, captured.InputFingerprint, captured.ProjectionFingerprint,
            captured.CaptureFingerprint);
        return new(true, matches, "OBSERVER_PREDICATE_EVALUATED", evidence);
    }

    private async Task<ResolvedSelection> ResolveAsync(InteractionInvocationHost host,
        ApplicationObserverPredicateSelection selection, CancellationToken cancellationToken)
    {
        try
        {
            if (!ValidSelection(host, selection))
                return ResolvedSelection.Rejected(Failure("OBSERVER_PREDICATE_SELECTION_INVALID",
                    "The predicate selection is invalid."));
            var target = await targets.ResolveRetainedAsync(host, selection.CatalogOrigin,
                selection.Mechanic, cancellationToken);
            if (target.Status != StandingGrantTargetResolutionStatus.Available || target.Target is null)
                return ResolvedSelection.Rejected(Failure(target.Code,
                    "The exact retained predicate is unavailable."));
            var authority = await grants.EvaluateAsync(host, new(StandingGrantCapability.Read,
                StandingGrantScope.StateSpace, [target.Target], []), cancellationToken);
            if (!authority.Allowed)
                return ResolvedSelection.Rejected(Failure(authority.Code,
                    "Current Read authority does not permit the retained predicate."));
            var activation = activations.ReadRevision(selection.ApplicationId,
                selection.CatalogOrigin.ActivationRevision);
            if (activation is null || activation.ActivationFingerprint != selection.CatalogOrigin.ActivationFingerprint
                || activation.ApplicationRevision != selection.CatalogOrigin.ApplicationRevision
                || activation.ApplicationFingerprint != selection.CatalogOrigin.ApplicationFingerprint)
                return ResolvedSelection.Rejected(Failure("OBSERVER_PREDICATE_CATALOG_STALE",
                    "The retained predicate catalog origin is unavailable."));
            var snapshot = catalogs.BuildPermissionSnapshot(selection.ApplicationId, activation);
            if (snapshot.EffectiveSetFingerprint != selection.CatalogOrigin.ActivationFingerprint)
                return ResolvedSelection.Rejected(Failure("OBSERVER_PREDICATE_CATALOG_STALE",
                    "The retained predicate catalog fingerprint changed."));
            var records = snapshot.Manifest.Records.Where(value =>
                value.QualifiedId == selection.Mechanic.DefinitionId && value.Kind == "mechanic").ToArray();
            if (records.Length != 1 || records[0].Version != selection.Mechanic.Revision
                || records[0].ContentFingerprint != selection.Mechanic.ContentFingerprint)
                return ResolvedSelection.Rejected(Failure("OBSERVER_PREDICATE_SELECTION_STALE",
                    "The retained predicate selection does not match its catalog record."));
            var retainedSource = activation.Sources.Where(value => value.SourceId == records[0].SourceId).ToArray();
            if (retainedSource.Length != 1
                || retainedSource[0].RegistrationFingerprint != selection.SourceRegistrationFingerprint)
                return ResolvedSelection.Rejected(Failure("OBSERVER_PREDICATE_SOURCE_STALE",
                    "The retained predicate source registration does not match."));
            var requirementsJson = RequirementsJson(records[0]);
            var canonical = CanonicalObject(requirementsJson, "OBSERVER_PREDICATE_REQUIREMENTS_INVALID");
            var fingerprint = PredicateProof.Requirements(canonical);
            if (canonical != selection.CanonicalRequirementsJson
                || fingerprint != selection.RequirementsFingerprint)
                return ResolvedSelection.Rejected(Failure("OBSERVER_PREDICATE_REQUIREMENTS_STALE",
                    "The retained predicate requirements do not match."));
            var requirements = MechanicRequirements.Parse(canonical);
            var requirementsProblem = PredicateRequirementsProblem(requirementsJson, requirements);
            if (requirementsProblem is not null)
                return ResolvedSelection.Rejected(Failure(requirementsProblem,
                    "The mechanic is outside the closed observer predicate subset."));
            return new(snapshot, requirements, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (TriggerSchedulingContractException exception) { return ResolvedSelection.Rejected(exception); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or JsonException or ApplicationActivationException or ApplicationCatalogMaterializationException)
        {
            return ResolvedSelection.Rejected(Failure("OBSERVER_PREDICATE_OWNER_UNAVAILABLE",
                "The retained predicate owner evidence is unavailable."));
        }
    }

    private static bool TryVerifyCaptured(InteractionInvocationHost host,
        ApplicationObserverPredicateCapturedInput captured, out string projectionJson, out string problem)
    {
        projectionJson = "";
        problem = "OBSERVER_PREDICATE_CAPTURE_INVALID";
        if (host is null || captured?.Selection is null || captured.Projection is null
            || host.StateSpaceId != captured.Projection.StateSpaceId
            || !UpperHash(captured.MappingFingerprint) || !UpperHash(captured.InputFingerprint)
            || !UpperHash(captured.ProjectionFingerprint) || !UpperHash(captured.CaptureFingerprint)) return false;
        try
        {
            if (!ValidSelection(host, captured.Selection)) return false;
            var input = CanonicalObject(captured.Projection.Input, problem);
            var eventJson = CanonicalObject(captured.Projection.Event, problem);
            projectionJson = CanonicalProjection(captured.Projection);
            if (Encoding.UTF8.GetByteCount(projectionJson) > MaximumProjectionBytes
                || PredicateProof.Input(input, eventJson) != captured.InputFingerprint
                || PredicateProof.Projection(projectionJson) != captured.ProjectionFingerprint
                || PredicateProof.Capture(captured.Selection, captured.MappingFingerprint,
                    captured.InputFingerprint, captured.ProjectionFingerprint) != captured.CaptureFingerprint)
                return false;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException) { return false; }
    }

    private static bool ClosedOutput(ApplicationMechanicEvaluationResult evaluation, out bool matches)
    {
        matches = false;
        var output = evaluation.Run!.Output;
        if (evaluation.Proposal.Effects.Count != 0 || evaluation.Proposal.Events.Count != 0
            || evaluation.Proposal.Notifications.Count != 0 || output.Effects.Count != 0
            || output.Events.Count != 0 || output.Notifications.Count != 0 || !output.HasData
            || output.Narration.Length != 0 || output.Decision.Length != 0
            || output.Code.Length != 0 || output.Reason.Length != 0) return false;
        try
        {
            using var document = JsonDocument.Parse(output.Data);
            var properties = document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().ToArray() : [];
            if (properties.Length != 1 || properties[0].Name != "matches"
                || properties[0].Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            matches = properties[0].Value.GetBoolean();
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string? PredicateRequirementsProblem(string source, MechanicRequirements requirements)
    {
        using var document = JsonDocument.Parse(source);
        var allowed = new HashSet<string>(["roles", "graphSnapshots", "inputSchema"], StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.EnumerateObject().Any(value => !allowed.Contains(value.Name)))
            return "OBSERVER_PREDICATE_REQUIREMENTS_UNSUPPORTED";
        if (requirements.ObjectRoles.Count != 0 || requirements.SnapshotObjects.Count != 0
            || requirements.AuthorizedContext is not null || requirements.Event is not null
            || requirements.Children.Count != 0 || requirements.EffectComponentIds.Count != 0
            || requirements.ElapsedTime is not null || requirements.ProjectionProblems().Count != 0
            || requirements.CompositionProblems().Count != 0)
            return "OBSERVER_PREDICATE_REQUIREMENTS_UNSUPPORTED";
        return null;
    }

    private static bool ValidSelection(InteractionInvocationHost host,
        ApplicationObserverPredicateSelection selection)
    {
        if (host is null || selection?.ApplicationId is null || selection.Mechanic is null
            || selection.CatalogOrigin is null || selection.RoleEntityIds is not { Count: <= 32 }) return false;
        try
        {
            return host.ApplicationRevision.ApplicationId == selection.ApplicationId && host.StateSpaceId is not null
                && host.StateRevision is not null && selection.Mechanic.Kind == "mechanic"
                && selection.Mechanic.Revision > 0 && UpperHash(selection.Mechanic.ContentFingerprint)
                && selection.CatalogOrigin.ActivationRevision > 0 && selection.CatalogOrigin.ApplicationRevision > 0
                && UpperHash(selection.CatalogOrigin.ActivationFingerprint)
                && UpperHash(selection.CatalogOrigin.ApplicationFingerprint)
                && UpperHash(selection.SourceRegistrationFingerprint)
                && UpperHash(selection.RequirementsFingerprint)
                && selection.RoleEntityIds.All(value => !string.IsNullOrWhiteSpace(value.Key)
                    && value.Key.Length <= 100 && !string.IsNullOrWhiteSpace(value.Value) && value.Value.Length <= 200)
                && selection.CanonicalRequirementsJson == CanonicalObject(selection.CanonicalRequirementsJson,
                    "OBSERVER_PREDICATE_REQUIREMENTS_INVALID")
                && PredicateProof.Requirements(selection.CanonicalRequirementsJson) == selection.RequirementsFingerprint;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException) { return false; }
    }

    private static string RequirementsJson(CatalogRecordDefinition record)
    {
        using var document = JsonDocument.Parse(record.ContentJson);
        return document.RootElement.TryGetProperty("requirements", out var requirements)
               && requirements.ValueKind == JsonValueKind.String
            ? requirements.GetString()! : throw new JsonException();
    }

    private static ApplicationObserverPredicateSelection Freeze(ApplicationObserverPredicateSelection value) =>
        value with { RoleEntityIds = new Dictionary<string, string>(value.RoleEntityIds, StringComparer.Ordinal) };

    private static MechanicProjection Freeze(MechanicProjection value) =>
        JsonSerializer.Deserialize<MechanicProjection>(CanonicalProjection(value), Wire)
        ?? throw new JsonException();

    private static string CanonicalProjection(MechanicProjection value) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(value, Wire));

    private static string CanonicalObject(string value, string code)
    {
        try { return InteractionCanonicalJson.CanonicalizeObject(value); }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        { throw Failure(code, "A bounded canonical JSON object is required."); }
    }

    private static string Code(string? problem, string fallback)
    {
        if (string.IsNullOrWhiteSpace(problem)) return fallback;
        var separator = problem.IndexOf(':');
        var value = separator < 0 ? problem : problem[..separator];
        return value.Length is > 0 and <= 100 ? value : fallback;
    }

    private static bool UpperHash(string? value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private static ApplicationObserverPredicateResult Rejected(string code) => new(false, false, code);
    private static TriggerSchedulingContractException Failure(string code, string message) => new(code, message);
    private sealed record ResolvedSelection(ActiveCatalogFeatureSnapshot? Catalog,
        MechanicRequirements? Requirements, TriggerSchedulingContractException? Failure)
    {
        internal static ResolvedSelection Rejected(TriggerSchedulingContractException failure) =>
            new(null, null, failure);
    }
}

internal static class PredicateProof
{
    internal static string Requirements(string canonicalJson) => Fingerprint(
        "dantes-roleplay/observer-predicate/requirements/v1", canonicalJson);

    internal static string Input(string input, string eventJson) => Fingerprint(
        "dantes-roleplay/observer-predicate/input/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { input, eventJson })));

    internal static string Mapping(ApplicationMechanicProjectionMapping mapping) => Fingerprint(
        "dantes-roleplay/observer-predicate/mapping/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            components = mapping.Components.OrderBy(value => value.Key, StringComparer.Ordinal),
            relationships = mapping.Relationships.OrderBy(value => value.Key, StringComparer.Ordinal)
        })));

    internal static string Projection(string canonicalJson) => Fingerprint(
        "dantes-roleplay/observer-predicate/projection/v1", canonicalJson);

    internal static string Capture(ApplicationObserverPredicateSelection selection,
        string mappingFingerprint, string inputFingerprint, string projectionFingerprint) => Fingerprint(
        "dantes-roleplay/observer-predicate/capture/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            applicationId = selection.ApplicationId.Value,
            mechanic = selection.Mechanic,
            catalogOrigin = selection.CatalogOrigin,
            selection.SourceRegistrationFingerprint,
            selection.CanonicalRequirementsJson,
            selection.RequirementsFingerprint,
            roles = selection.RoleEntityIds.OrderBy(value => value.Key, StringComparer.Ordinal),
            mappingFingerprint,
            inputFingerprint,
            projectionFingerprint
        })));

    private static string Fingerprint(string domain, string canonicalJson) =>
        InteractionCanonicalJson.Fingerprint(domain, canonicalJson);
}
