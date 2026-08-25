using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution;

public sealed class ApplicationMechanicEvaluator(
    IPublicApplicationCatalogProvider catalogs,
    IApplicationMechanicProjectionResolver projections,
    IMechanicEngine engine) : IApplicationMechanicEvaluator
{
    public async Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
        ApplicationMechanicEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!catalogs.TryGet(request.ApplicationId, out var catalog))
            return Failed(request, "APPLICATION_CATALOG_UNAVAILABLE: The exact active application catalog is unavailable.");
        CatalogRecordView record;
        try { record = catalog.Inspect(new(request.ApplicationId, request.ApplicationId.Value, request.QualifiedMechanicId)); }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        { return Failed(request, "MECHANIC_UNKNOWN: The requested application mechanic is unavailable."); }
        if (record.Summary.Kind != "mechanic"
            || record.Summary.Status != "active"
            || record.Summary.ContentFingerprint != request.ContentFingerprint)
            return Failed(request, "MECHANIC_STALE: The mechanic does not match the requested exact fingerprint.");
        MechanicDocument document;
        try
        {
            document = JsonSerializer.Deserialize<MechanicDocument>(record.ContentJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new JsonException();
        }
        catch (JsonException) { return Failed(request, "MECHANIC_INVALID: The active mechanic contract is malformed."); }
        MechanicRequirements requirements;
        try { requirements = MechanicRequirements.Parse(document.Requirements ?? "{}"); }
        catch (JsonException) { return Failed(request, "MECHANIC_INVALID: The active mechanic requirements are malformed."); }
        var projection = await projections.ResolveAsync(request.StateSpaceId, request.ApplicationId,
            requirements, request.Mapping, request.RoleEntityIds, request.InputJson, request.Seed, cancellationToken);
        if (!projection.Ok)
            return new(request.QualifiedMechanicId, request.ContentFingerprint, null, null, projection.Problems);
        var run = await engine.RunAsync(document.Source ?? "", projection.Projection!, ExecutionLimits.Default, cancellationToken);
        return new(request.QualifiedMechanicId, request.ContentFingerprint, projection.Projection, run, []);
    }

    private static ApplicationMechanicEvaluationResult Failed(ApplicationMechanicEvaluationRequest request, string problem) =>
        new(request.QualifiedMechanicId, request.ContentFingerprint, null, null, [problem]);
    private sealed record MechanicDocument(string? Requirements, string? Source);
}
