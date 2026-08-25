using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.RegistryAdministration;

/// <summary>
/// SQLite transaction owner for administrative registration and its replay receipt. The existing
/// operation primary key is reused as the idempotency-key constraint, so no parallel ledger or
/// migration can drift away from the public audit.
/// </summary>
public sealed class RegistryAdministrationService(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    ISourceRegistry sources,
    IOperationLog operations) : IRegistryAdministrationService
{
    private const string ApplicationKind = "system.application.register";
    private const string SourceKind = "system.source.register";

    public async Task<RegistryRegistrationPreview<ApplicationRevision>> PreviewApplicationAsync(
        ApplicationRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var requestFingerprint = ApplicationRequestFingerprint(registration, context.ExpectedFingerprint);
        var replay = ReplayApplication(registration, context, requestFingerprint);
        if (replay is not null)
            return await RecordPreviewAsync(ApplicationKind, context, requestFingerprint,
                replay.Registration, replay.Fingerprint, replay.Outcome, cancellationToken);

        var (revision, outcome) = ValidateApplication(registration, context.ExpectedFingerprint);
        return await RecordPreviewAsync(ApplicationKind, context, requestFingerprint,
            revision, revision.Fingerprint, PreviewOutcome(outcome), cancellationToken);
    }

    public async Task<RegistryRegistrationReceipt<ApplicationRevision>> RegisterApplicationAsync(
        ApplicationRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var requestFingerprint = ApplicationRequestFingerprint(registration, context.ExpectedFingerprint);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = ReplayApplication(registration, context, requestFingerprint);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            RequirePreview(ApplicationKind, context.RequestToken, requestFingerprint);

            var (_, outcome) = ValidateApplication(registration, context.ExpectedFingerprint);
            var revision = applications.Register(registration);
            await RecordSuccessAsync(
                ApplicationKind,
                context,
                requestFingerprint,
                $"Registered application '{registration.Id.Value}' at immutable revision {revision.Revision}.",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(revision, revision.Fingerprint, outcome, context.RequestToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<RegistryRegistrationPreview<SourceRegistration>> PreviewSourceAsync(
        SourceRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var requestFingerprint = SourceRequestFingerprint(registration, context.ExpectedFingerprint);
        var replay = ReplaySource(registration, context, requestFingerprint);
        if (replay is not null)
            return await RecordPreviewAsync(SourceKind, context, requestFingerprint,
                replay.Registration, replay.Fingerprint, replay.Outcome, cancellationToken);

        var (validated, fingerprint, outcome) = ValidateSource(registration, context.ExpectedFingerprint);
        return await RecordPreviewAsync(SourceKind, context, requestFingerprint,
            validated, fingerprint, PreviewOutcome(outcome), cancellationToken);
    }

    public async Task<RegistryRegistrationReceipt<SourceRegistration>> RegisterSourceAsync(
        SourceRegistration registration,
        RegistryAdministrationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var requestFingerprint = SourceRequestFingerprint(registration, context.ExpectedFingerprint);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = ReplaySource(registration, context, requestFingerprint);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            RequirePreview(SourceKind, context.RequestToken, requestFingerprint);

            var (_, fingerprint, outcome) = ValidateSource(registration, context.ExpectedFingerprint);
            var persisted = sources.Register(registration);
            await RecordSuccessAsync(
                SourceKind,
                context,
                requestFingerprint,
                $"Registered source '{registration.SourceId}' for application '{registration.ApplicationId.Value}'.",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(persisted, fingerprint, outcome, context.RequestToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private RegistryRegistrationReceipt<ApplicationRevision>? ReplayApplication(
        ApplicationRegistration registration,
        RegistryAdministrationContext context,
        string requestFingerprint)
    {
        var operation = ExistingOperation(context.RequestToken);
        if (operation is null) return null;
        RequireMatchingReplay(operation, ApplicationKind, requestFingerprint);
        var revision = applications.Get(registration.Id)
            ?? throw new RegistryAdministrationException("REGISTRY_INCONSISTENT", "The prior application receipt has no matching immutable registration.");
        if (revision.Fingerprint != ApplicationRegistrationFingerprint.Compute(registration))
            throw new RegistryAdministrationException("REGISTRY_INCONSISTENT", "The prior application receipt no longer matches registry state.");
        return new(revision, revision.Fingerprint, Outcome(registration.Id, context.ExpectedFingerprint), operation.Id);
    }

    private RegistryRegistrationReceipt<SourceRegistration>? ReplaySource(
        SourceRegistration registration,
        RegistryAdministrationContext context,
        string requestFingerprint)
    {
        var operation = ExistingOperation(context.RequestToken);
        if (operation is null) return null;
        RequireMatchingReplay(operation, SourceKind, requestFingerprint);
        var persisted = sources.Get(registration.ApplicationId, registration.SourceId)
            ?? throw new RegistryAdministrationException("REGISTRY_INCONSISTENT", "The prior source receipt has no matching immutable registration.");
        var fingerprint = SourceRegistrationFingerprint.Compute(persisted);
        if (fingerprint != SourceRegistrationFingerprint.Compute(registration))
            throw new RegistryAdministrationException("REGISTRY_INCONSISTENT", "The prior source receipt no longer matches registry state.");
        return new(persisted, fingerprint, Outcome(registration.ApplicationId, registration.SourceId, context.ExpectedFingerprint), operation.Id);
    }

    private (ApplicationRevision Revision, string Outcome) ValidateApplication(
        ApplicationRegistration registration,
        string? expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.DisplayName))
            throw Invalid("INVALID_APPLICATION", "An application display name is required.");
        if (registration.BaseApplications.Distinct().Count() != registration.BaseApplications.Count)
            throw Invalid("INVALID_APPLICATION", "An application may list each base only once.");
        if (registration.BaseApplications.Contains(registration.Id))
            throw Invalid("INVALID_APPLICATION", "An application cannot be its own base.");
        if (registration.BaseApplications.Any(baseId => applications.Get(baseId) is null))
            throw Invalid("APPLICATION_UNKNOWN", "Every base application must already be registered.");

        var existing = applications.Get(registration.Id);
        if (existing is null)
        {
            RequireExpectation(expectedFingerprint, null);
            var fingerprint = ApplicationRegistrationFingerprint.Compute(registration);
            return (new(registration.Id, 1, fingerprint, Array.AsReadOnly(registration.BaseApplications.ToArray())), "registered");
        }

        RequireExpectation(expectedFingerprint, existing.Fingerprint);
        if (existing.Fingerprint != ApplicationRegistrationFingerprint.Compute(registration))
            throw Invalid("REGISTRATION_CONFLICT", "The application ID already has different immutable metadata.");
        return (existing, "unchanged");
    }

    private (SourceRegistration Registration, string Fingerprint, string Outcome) ValidateSource(
        SourceRegistration registration,
        string? expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (applications.Get(registration.ApplicationId) is null)
            throw Invalid("APPLICATION_UNKNOWN", "A source can only be registered for an existing application.");

        var current = sources.For(registration.ApplicationId);
        var validator = new InMemorySourceRegistry();
        try
        {
            foreach (var source in current) validator.Register(source);
            validator.Register(registration);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("INVALID_SOURCE", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid("REGISTRATION_CONFLICT", exception.Message);
        }

        var fingerprint = SourceRegistrationFingerprint.Compute(registration);
        var existing = sources.Get(registration.ApplicationId, registration.SourceId);
        if (existing is null)
        {
            RequireExpectation(expectedFingerprint, null);
            return (registration, fingerprint, "registered");
        }

        var currentFingerprint = SourceRegistrationFingerprint.Compute(existing);
        RequireExpectation(expectedFingerprint, currentFingerprint);
        if (currentFingerprint != fingerprint)
            throw Invalid("REGISTRATION_CONFLICT", "The source ID already has different immutable metadata.");
        return (existing, currentFingerprint, "unchanged");
    }

    private async Task RecordSuccessAsync(
        string kind,
        RegistryAdministrationContext context,
        string requestFingerprint,
        string summary,
        CancellationToken cancellationToken)
    {
        await operations.RecordAsync(
            "commit",
            summary,
            success: true,
            context.Intent,
            Subject(kind, requestFingerprint),
            context.ProceduresUsed,
            consumesReadEvidence: true,
            cancellationToken: cancellationToken,
            guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence),
            id: context.RequestToken);
    }

    private async Task<RegistryRegistrationPreview<T>> RecordPreviewAsync<T>(
        string kind,
        RegistryAdministrationContext context,
        string requestFingerprint,
        T registration,
        string fingerprint,
        string outcome,
        CancellationToken cancellationToken)
    {
        var operation = await operations.RecordAsync(
            "commit",
            $"Validated {kind} without changing registry state.",
            success: true,
            context.Intent,
            PreviewSubject(kind, context.RequestToken, requestFingerprint),
            context.ProceduresUsed,
            consumesReadEvidence: false,
            cancellationToken: cancellationToken,
            guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence));
        return new(registration, fingerprint, outcome, operation.Id);
    }

    private Operation? ExistingOperation(string requestToken) => db.Operations
        .AsNoTracking().SingleOrDefault(operation => operation.Id == requestToken);

    private static void RequireMatchingReplay(Operation operation, string kind, string requestFingerprint)
    {
        if (!operation.Success || operation.Tool != "commit" || operation.Subject != Subject(kind, requestFingerprint))
            throw Invalid("REQUEST_TOKEN_CONFLICT", "That requestToken was already used by a different operation or canonical request.");
    }

    private void RequirePreview(string kind, string requestToken, string requestFingerprint)
    {
        var subject = PreviewSubject(kind, requestToken, requestFingerprint);
        if (!db.Operations.AsNoTracking().Any(operation =>
                operation.Tool == "commit" && operation.Subject == subject && operation.Success))
            throw Invalid("DRY_RUN_REQUIRED", "Commit the exact payload with dryRun: true before applying it.");
    }

    private static void RequireExpectation(string? supplied, string? current)
    {
        if (!string.Equals(supplied, current, StringComparison.Ordinal))
            throw Invalid("REGISTRY_STALE", current is null
                ? "The target is absent but expectedFingerprint did not expect absence."
                : "expectedFingerprint does not match the current immutable registration.");
    }

    private static void ValidateContext(RegistryAdministrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.RequestToken.Length != 32
            || context.RequestToken.Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw Invalid("INVALID_PAYLOAD", "requestToken must contain exactly 32 lowercase hexadecimal characters.");
        if (context.ExpectedFingerprint is not null && !UpperSha256(context.ExpectedFingerprint))
            throw Invalid("INVALID_PAYLOAD", "expectedFingerprint must be null or an uppercase SHA-256 fingerprint.");
        if (!context.AuthorizationEvidence.Allowed)
            throw Invalid("PRIVATE_OPERATOR_DENIED", "A successful authorization decision is required.");
    }

    private static string ApplicationRequestFingerprint(ApplicationRegistration registration, string? expected) => Hash(new
    {
        kind = ApplicationKind,
        application = new
        {
            id = registration.Id.Value,
            registration.DisplayName,
            registration.Description,
            baseApplications = registration.BaseApplications.Select(value => value.Value).ToArray()
        },
        expectedFingerprint = expected
    });

    private static string SourceRequestFingerprint(SourceRegistration registration, string? expected) => Hash(new
    {
        kind = SourceKind,
        source = new
        {
            applicationId = registration.ApplicationId.Value,
            registration.SourceId,
            registration.AllowedRootId,
            registration.RelativePathOrGlob,
            trust = registration.Trust.ToString().ToLowerInvariant(),
            registration.Precedence,
            registration.LogicalIdentity
        },
        expectedFingerprint = expected
    });

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static string Subject(string kind, string fingerprint) => $"{kind}|{fingerprint}";
    private static string PreviewSubject(string kind, string token, string fingerprint) =>
        $"preview|{kind}|{token}|{fingerprint}";
    private static string Outcome(ApplicationIdentifier _, string? expected) => expected is null ? "registered" : "unchanged";
    private static string Outcome(ApplicationIdentifier _, string __, string? expected) => expected is null ? "registered" : "unchanged";
    private static string PreviewOutcome(string outcome) => outcome == "registered" ? "would-register" : outcome;
    private static bool UpperSha256(string value) => value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static RegistryAdministrationException Invalid(string code, string message) => new(code, message);
}
