using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqliteObservationIngestionService(
    DantesRoleplayDbContext db,
    IBoundedJsonSchemaValidator schemas,
    ITriggerObservationRateLimiter rateLimiter,
    ITriggerSchedulingStore store,
    IEnumerable<IObservationIngestionPolicy>? policies = null) : IObservationIngestionService
{
    public async Task<TriggerSchedulingWriteResult<StoredObservation>> SubmitAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        ObservationSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(submission);
        if (!principal.Verified)
            throw Failure("OBSERVATION_PRINCIPAL_REQUIRED", "A verified private-host principal is required.");
        if (!await db.Set<ApplicationRegistryRecord>().AsNoTracking()
                .AnyAsync(value => value.Id == applicationId.Value, cancellationToken))
            throw Failure("TRIGGER_SCHEDULING_APPLICATION_NOT_FOUND", "The application is not registered.");

        var currentSource = await db.TriggerObservationSourceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == submission.Source.Id, cancellationToken);
        if (currentSource is null)
            throw Failure("TRIGGER_SCHEDULING_SOURCE_NOT_FOUND", "The observation source is not registered.");
        var source = await db.TriggerObservationSources.AsNoTracking()
            .Include(value => value.AllowedPrincipals)
            .SingleAsync(value => value.ApplicationId == applicationId.Value &&
                value.Id == submission.Source.Id && value.Version == currentSource.CurrentVersion, cancellationToken);
        if (source.Status != "enabled")
            throw Failure("OBSERVATION_SOURCE_DISABLED", "The observation source is disabled.");
        if (!source.AllowedPrincipals.Any(value => value.PrincipalId == principal.PrincipalId))
            throw Failure("OBSERVATION_PRINCIPAL_FORBIDDEN", "The observation source does not permit this principal.");
        foreach (var policy in policies ?? [])
            await policy.ValidateAsync(principal, applicationId, submission, cancellationToken);

        var currentStructure = await db.TriggerObservationStructureCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == submission.Structure.Id, cancellationToken);
        if (currentStructure is null)
            throw Failure("TRIGGER_SCHEDULING_STRUCTURE_NOT_FOUND", "The observation structure is not registered.");
        if (currentStructure.CurrentVersion != submission.Structure.Version)
            throw Failure("TRIGGER_SCHEDULING_OBSERVATION_STALE", "The requested observation structure is no longer current.");
        var structure = await db.TriggerObservationStructures.AsNoTracking().SingleAsync(value =>
            value.ApplicationId == applicationId.Value && value.Id == submission.Structure.Id &&
            value.Version == submission.Structure.Version, cancellationToken);

        await using var lease = await rateLimiter.TryAcquireAsync(
            principal.PrincipalId, applicationId, source.Id, source.RequestsPerMinute, cancellationToken);
        if (lease is null)
            throw Failure("OBSERVATION_RATE_LIMITED", "The observation request limit was reached. Try again shortly.");

        var validation = schemas.Validate(structure.SchemaProfileId, structure.NormalizedSchema, submission.Data.Json);
        if (validation.Status == SchemaValueStatus.Invalid)
            throw Failure("OBSERVATION_SCHEMA_INVALID", "Observation data does not satisfy the registered structure.");
        if (validation.Status != SchemaValueStatus.Valid)
            throw Failure("OBSERVATION_SCHEMA_UNAVAILABLE", "The registered observation structure could not be evaluated.");

        return await store.AppendObservationAsync(principal, applicationId, submission, cancellationToken);
    }

    private static ObservationIngestionException Failure(string code, string message) => new(code, message);
}
