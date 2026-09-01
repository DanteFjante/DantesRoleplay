using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.StateSpaceAdministration;

public sealed class StateSpaceAdministrationService(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IBoundedJsonSchemaValidator schemas,
    IOperationLog operations) : IStateSpaceAdministrationService
{
    private const string CreateKind = "system.state-space.create";
    private const string UpgradeKind = "system.state-space.upgrade";

    public StateSpaceBindingSummary? Get(string stateSpaceId)
    {
        ValidateStateSpaceId(stateSpaceId);
        var value = stateSpaces.Get(stateSpaceId);
        return value is null ? null : Summary(value);
    }

    public IReadOnlyList<StateSpaceBindingSummary> List(ApplicationIdentifier applicationId, int limit)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (limit is < 1 or > 100)
            throw Invalid("INVALID_PAYLOAD", "limit must be from 1 through 100.");
        return Array.AsReadOnly(stateSpaces.ListPage(applicationId, null, limit).StateSpaces
            .Select(Summary).ToArray());
    }

    public async Task<StateSpaceCreationPreview> PreviewCreateAsync(
        StateSpaceCreationRequest request,
        StateSpaceCreationContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var requestFingerprint = CreateRequestFingerprint(request);
        var binding = BuildCreate(request);
        var audit = await RecordPreviewAsync(CreateKind, context.RequestToken, requestFingerprint,
            binding.BindingFingerprint, context.Intent, context.ProceduresUsed,
            context.AuthorizationEvidence, "Validated state-space creation without changing runtime state.",
            cancellationToken);
        return new(binding, "would-create", audit.Id);
    }

    public async Task<StateSpaceCreationReceipt> CreateAsync(
        StateSpaceCreationRequest request,
        StateSpaceCreationContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var requestFingerprint = CreateRequestFingerprint(request);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await ReplayCreateAsync(request, context.RequestToken, requestFingerprint, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            RequireAnyPreview(CreateKind, context.RequestToken, requestFingerprint);
            var candidate = BuildCreate(request);
            RequireExactPreview(CreateKind, context.RequestToken, requestFingerprint,
                candidate.BindingFingerprint);
            var application = applications.Get(request.ApplicationId)
                ?? throw Invalid("APPLICATION_UNKNOWN", "The requested application is not registered.");
            var active = RequireActive(request.ApplicationId, request.ActiveFingerprint, application);
            var created = stateSpaces.Create(new(request.StateSpaceId, application,
                request.ActiveFingerprint, active.ResolutionFingerprint, request.Scope));
            await operations.RecordAsync(
                "commit", $"Created empty state space '{request.StateSpaceId}' for application '{request.ApplicationId.Value}'.",
                success: true, context.Intent, Subject(CreateKind, requestFingerprint), context.ProceduresUsed,
                consumesReadEvidence: true, cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence), id: context.RequestToken);

            var summary = Summary(created);
            PersistHistory(summary, null, "created-empty", 0, 0, active.DependencyCoverageVersion,
                active.DependencyCoverageComplete, context.RequestToken, summary.CreatedAtUtc!.Value);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(summary, "created", context.RequestToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<StateSpaceUpgradePreview> PreviewUpgradeAsync(
        StateSpaceUpgradeRequest request,
        StateSpaceUpgradeContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var requestFingerprint = UpgradeRequestFingerprint(request);
        var candidate = BuildUpgrade(request);
        var evidenceFingerprint = UpgradeEvidenceFingerprint(candidate.Target, candidate.Compatibility);
        var audit = await RecordPreviewAsync(UpgradeKind, context.RequestToken, requestFingerprint,
            evidenceFingerprint, context.Intent, context.ProceduresUsed, context.AuthorizationEvidence,
            "Validated state-space binding compatibility without changing runtime state.", cancellationToken);
        return new(candidate.Previous, candidate.Target, candidate.Compatibility, "would-upgrade", audit.Id);
    }

    public async Task<StateSpaceUpgradeReceipt> UpgradeAsync(
        StateSpaceUpgradeRequest request,
        StateSpaceUpgradeContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var requestFingerprint = UpgradeRequestFingerprint(request);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await ReplayUpgradeAsync(request, context.RequestToken, requestFingerprint, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            RequireAnyPreview(UpgradeKind, context.RequestToken, requestFingerprint);
            var candidate = BuildUpgrade(request);
            var evidenceFingerprint = UpgradeEvidenceFingerprint(candidate.Target, candidate.Compatibility);
            RequireExactPreview(UpgradeKind, context.RequestToken, requestFingerprint, evidenceFingerprint);

            RetainBaselineIfMissing(candidate.Previous);
            var row = db.Set<ApplicationStateSpaceRecord>().Single(value => value.Id == request.StateSpaceId);
            if (row.BindingRevision != candidate.Previous.BindingRevision
                || row.ManifestFingerprint != candidate.Previous.ActiveFingerprint)
                throw Invalid("BINDING_STALE", "The state-space binding changed after validation.");
            var now = DateTime.UtcNow;
            row.ApplicationRevision = candidate.Target.ApplicationRevision;
            row.ManifestFingerprint = candidate.Target.ActiveFingerprint;
            row.ResolutionFingerprint = candidate.Target.ResolutionFingerprint;
            row.BindingRevision = candidate.Target.BindingRevision;
            row.UpdatedAtUtc = now;
            await operations.RecordAsync(
                "commit", $"Upgraded state space '{request.StateSpaceId}' to binding revision {row.BindingRevision}.",
                success: true, context.Intent, Subject(UpgradeKind, requestFingerprint), context.ProceduresUsed,
                consumesReadEvidence: true, cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence), id: context.RequestToken);

            var binding = candidate.Target with { UpdatedAtUtc = now };
            PersistHistory(binding, candidate.Previous.BindingFingerprint, candidate.Compatibility.Code,
                candidate.Compatibility.EntityCount, candidate.Compatibility.ComponentCount,
                candidate.Compatibility.DependencyCoverageVersion,
                candidate.Compatibility.DependencyCoverageComplete, context.RequestToken, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(candidate.Previous, binding, candidate.Compatibility, "upgraded", context.RequestToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private StateSpaceBindingSummary BuildCreate(StateSpaceCreationRequest request)
    {
        if (stateSpaces.Get(request.StateSpaceId) is not null)
            throw Invalid("STATE_SPACE_EXISTS", "stateSpaceId already belongs to an immutable state space.");
        var application = applications.Get(request.ApplicationId)
            ?? throw Invalid("APPLICATION_UNKNOWN", "The requested application is not registered.");
        var active = RequireActive(request.ApplicationId, request.ActiveFingerprint, application);
        return Summary(request.StateSpaceId, application, active.ActivationFingerprint,
            active.ResolutionFingerprint, request.Scope, 1, null, null);
    }

    private UpgradeCandidate BuildUpgrade(StateSpaceUpgradeRequest request)
    {
        var current = stateSpaces.Get(request.StateSpaceId)
            ?? throw Invalid("STATE_SPACE_UNKNOWN", "The requested state space does not exist.");
        var previous = Summary(current);
        if (previous.ApplicationId != request.ApplicationId)
            throw Invalid("STATE_SPACE_APPLICATION_MISMATCH", "The state space belongs to another application.");
        if (!string.Equals(previous.BindingFingerprint, request.ExpectedBindingFingerprint, StringComparison.Ordinal))
            throw Invalid("BINDING_STALE", "expectedBindingFingerprint does not match the current state-space binding.");
        var application = applications.Get(request.ApplicationId)
            ?? throw Invalid("APPLICATION_UNKNOWN", "The requested application is not registered.");
        var active = RequireActive(request.ApplicationId, request.ActiveFingerprint, application);
        if (previous.ActiveFingerprint == active.ActivationFingerprint)
            throw Invalid("STATE_SPACE_ALREADY_CURRENT", "The state space already uses the requested active application binding.");

        var entityCount = db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Count(value => value.StateSpaceId == request.StateSpaceId);
        var componentCount = db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Count(value => value.StateSpaceId == request.StateSpaceId);
        if (componentCount != 0)
            RequireCompatibleComponents(request.StateSpaceId, application);

        var target = Summary(request.StateSpaceId, application, active.ActivationFingerprint,
            active.ResolutionFingerprint,
            previous.Scope, previous.BindingRevision + 1, previous.CreatedAtUtc, null);
        var compatibility = new StateSpaceCompatibilityEvidence(
            entityCount == 0 && componentCount == 0
                ? "empty-state-compatible"
                : "populated-state-compatible-rebind", entityCount,
            componentCount, active.DependencyCoverageVersion, active.DependencyCoverageComplete);
        return new(previous, target, compatibility);
    }

    private void RequireCompatibleComponents(string stateSpaceId, ApplicationRevision application)
    {
        var baseApplications = db.Set<ApplicationRevisionBaseRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == application.ApplicationId.Value
                && value.Revision == application.Revision)
            .Select(value => value.BaseApplicationId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var component in db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
                     .Where(value => value.StateSpaceId == stateSpaceId)
                     .OrderBy(value => value.EntityId).ThenBy(value => value.QualifiedTypeId))
        {
            var registered = componentTypes.Get(component.QualifiedTypeId, component.TypeVersion);
            if (registered is null
                || (!registered.Owner.IsSystem
                    && registered.Owner != application.ApplicationId
                    && !baseApplications.Contains(registered.Owner.Value))
                || registered.SchemaHash != component.SchemaHash)
                throw Invalid("MIGRATION_REQUIRED",
                    "The state space contains a component whose exact registered contract is unavailable.");
            var validation = schemas.Validate(registered.ProfileId, registered.SchemaJson, component.Data);
            if (validation.Status != SchemaValueStatus.Valid)
                throw Invalid("MIGRATION_REQUIRED",
                    "The state space contains a component that no longer satisfies its exact registered contract.");
        }
    }

    private ActiveApplicationManifest RequireActive(
        ApplicationIdentifier applicationId,
        string activeFingerprint,
        ApplicationRevision application)
    {
        var active = activations.Current(applicationId)
            ?? throw Invalid("ACTIVATION_REQUIRED", "The application must have an active overlay.");
        if (!string.Equals(activeFingerprint, active.ActivationFingerprint, StringComparison.Ordinal))
            throw Invalid("ACTIVATION_STALE", "activeFingerprint does not match the current active application overlay.");
        if (application.Revision != active.ApplicationRevision
            || application.Fingerprint != active.ApplicationFingerprint)
            throw Invalid("APPLICATION_STALE", "The active overlay no longer matches the registered application revision.");
        return active;
    }

    private async Task<StateSpaceCreationReceipt?> ReplayCreateAsync(
        StateSpaceCreationRequest request, string token, string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var operation = await operations.GetAsync(token, cancellationToken);
        if (operation is null) return null;
        if (!operation.Success || operation.Tool != "commit"
            || operation.Subject != Subject(CreateKind, requestFingerprint))
            throw Invalid("REQUEST_TOKEN_CONFLICT", "requestToken already identifies another operation.");
        var retained = db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
            .SingleOrDefault(value => value.OperationId == token);
        var summary = retained is null
            ? stateSpaces.Get(request.StateSpaceId) is { } current ? Summary(current) : null
            : Summary(retained);
        if (summary is null || summary.ApplicationId != request.ApplicationId
            || summary.ActiveFingerprint != request.ActiveFingerprint || summary.Scope != request.Scope)
            throw Invalid("REPLAY_EVIDENCE_MISSING", "The immutable state-space replay evidence is unavailable.");
        return new(summary, "created", token);
    }

    private async Task<StateSpaceUpgradeReceipt?> ReplayUpgradeAsync(
        StateSpaceUpgradeRequest request, string token, string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var operation = await operations.GetAsync(token, cancellationToken);
        if (operation is null) return null;
        if (!operation.Success || operation.Tool != "commit"
            || operation.Subject != Subject(UpgradeKind, requestFingerprint))
            throw Invalid("REQUEST_TOKEN_CONFLICT", "requestToken already identifies another operation.");
        var retained = db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
            .SingleOrDefault(value => value.OperationId == token)
            ?? throw Invalid("REPLAY_EVIDENCE_MISSING", "The immutable state-space upgrade evidence is unavailable.");
        var previous = db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
            .SingleOrDefault(value => value.StateSpaceId == retained.StateSpaceId
                && value.BindingRevision == retained.BindingRevision - 1)
            ?? throw Invalid("REPLAY_EVIDENCE_MISSING", "The prior immutable binding evidence is unavailable.");
        return new(Summary(previous), Summary(retained), Compatibility(retained), "upgraded", token);
    }

    private async Task<Operation> RecordPreviewAsync(
        string kind, string token, string requestFingerprint, string evidenceFingerprint,
        string intent, IReadOnlyList<string> procedures, AuthorizationAuditEvidence authorization,
        string summary, CancellationToken cancellationToken) => await operations.RecordAsync(
            "commit", summary, success: true, intent,
            PreviewSubject(kind, token, requestFingerprint, evidenceFingerprint), procedures,
            consumesReadEvidence: false, cancellationToken: cancellationToken,
            guardEvidenceJson: JsonSerializer.Serialize(authorization));

    private void RequireAnyPreview(string kind, string token, string requestFingerprint)
    {
        var prefix = PreviewSubjectPrefix(kind, token, requestFingerprint);
        if (!db.Operations.AsNoTracking().Any(operation => operation.Tool == "commit"
                && operation.Success && operation.Subject.StartsWith(prefix)))
            throw Invalid("DRY_RUN_REQUIRED", "Commit the exact payload with dryRun: true before applying it.");
    }

    private void RequireExactPreview(string kind, string token, string requestFingerprint, string evidenceFingerprint)
    {
        var subject = PreviewSubject(kind, token, requestFingerprint, evidenceFingerprint);
        if (!db.Operations.AsNoTracking().Any(operation => operation.Tool == "commit"
                && operation.Success && operation.Subject == subject))
            throw Invalid("DRY_RUN_STALE", "Derived state-space evidence changed after dry run; dry-run the exact payload again.");
    }

    private void RetainBaselineIfMissing(StateSpaceBindingSummary binding)
    {
        if (db.Set<StateSpaceBindingRevisionRecord>().Any(value =>
                value.StateSpaceId == binding.StateSpaceId && value.BindingRevision == binding.BindingRevision))
            return;
        var summary = $"Created empty state space '{binding.StateSpaceId}' for application '{binding.ApplicationId.Value}'.";
        var matchingOperations = db.Operations.AsNoTracking().Where(operation =>
                operation.Tool == "commit" && operation.Success && operation.Summary == summary
                && operation.Subject.StartsWith(CreateKind + "|"))
            .Select(operation => operation.Id).Take(2).ToArray();
        var creationOperationId = matchingOperations.Length == 1 ? matchingOperations[0] : null;
        PersistHistory(binding, null, "retained-baseline", 0, 0,
            "unknown-prior-coverage", false, creationOperationId,
            binding.UpdatedAtUtc ?? binding.CreatedAtUtc ?? DateTime.UtcNow);
    }

    private void PersistHistory(
        StateSpaceBindingSummary binding, string? previousBindingFingerprint, string compatibilityCode,
        int entityCount, int componentCount, string coverageVersion, bool coverageComplete,
        string? operationId, DateTime recordedAtUtc) => db.Add(new StateSpaceBindingRevisionRecord
    {
        StateSpaceId = binding.StateSpaceId,
        BindingRevision = binding.BindingRevision,
        ApplicationId = binding.ApplicationId.Value,
        ApplicationRevision = binding.ApplicationRevision,
        ApplicationFingerprint = binding.ApplicationFingerprint,
        ActiveFingerprint = binding.ActiveFingerprint,
        ResolutionFingerprint = binding.ResolutionFingerprint,
        Scope = EcsComponentRolePolicyParser.ScopeName(binding.Scope),
        BindingFingerprint = binding.BindingFingerprint,
        PreviousBindingFingerprint = previousBindingFingerprint,
        CompatibilityCode = compatibilityCode,
        EntityCount = entityCount,
        ComponentCount = componentCount,
        DependencyCoverageVersion = coverageVersion,
        DependencyCoverageComplete = coverageComplete,
        OperationId = operationId,
        CreatedAtUtc = binding.CreatedAtUtc ?? recordedAtUtc,
        UpdatedAtUtc = binding.UpdatedAtUtc ?? recordedAtUtc,
        RecordedAtUtc = recordedAtUtc
    });

    private static StateSpaceBindingSummary Summary(StateSpaceView value) => Summary(
        value.StateSpaceId, value.ApplicationRevision, value.ManifestFingerprint,
        value.ResolutionFingerprint, value.Scope, value.BindingRevision,
        DateTime.SpecifyKind(value.CreatedAtUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(value.UpdatedAtUtc, DateTimeKind.Utc));

    private static StateSpaceBindingSummary Summary(StateSpaceBindingRevisionRecord value) => new(
        value.StateSpaceId, ApplicationIdentifier.Parse(value.ApplicationId), value.ApplicationRevision,
        value.ApplicationFingerprint, value.ActiveFingerprint, value.BindingRevision,
        value.BindingFingerprint, DateTime.SpecifyKind(value.CreatedAtUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(value.UpdatedAtUtc, DateTimeKind.Utc))
        {
            ResolutionFingerprint = value.ResolutionFingerprint,
            Scope = EcsComponentRolePolicyParser.ParseScope(value.Scope)
        };

    private static StateSpaceBindingSummary Summary(
        string stateSpaceId, ApplicationRevision application, string activeFingerprint,
        string resolutionFingerprint,
        EcsStateSpaceScope scope, int bindingRevision, DateTime? createdAtUtc, DateTime? updatedAtUtc)
    {
        var fingerprint = BindingFingerprint(stateSpaceId, application.ApplicationId.Value,
            application.Revision, application.Fingerprint, activeFingerprint,
            resolutionFingerprint, scope, bindingRevision);
        return new(stateSpaceId, application.ApplicationId, application.Revision,
            application.Fingerprint, activeFingerprint, bindingRevision, fingerprint,
            createdAtUtc, updatedAtUtc)
        {
            ResolutionFingerprint = resolutionFingerprint,
            Scope = scope
        };
    }

    private static StateSpaceCompatibilityEvidence Compatibility(StateSpaceBindingRevisionRecord value) => new(
        value.CompatibilityCode, value.EntityCount, value.ComponentCount,
        value.DependencyCoverageVersion, value.DependencyCoverageComplete);

    private static void Validate(StateSpaceCreationRequest request, StateSpaceCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ValidateStateSpaceId(request.StateSpaceId);
        ValidateActiveFingerprint(request.ActiveFingerprint);
        if (!Enum.IsDefined(request.Scope))
            throw Invalid("INVALID_PAYLOAD", "scope must be runtime-state-space or application-publication.");
        if (request.ExpectedFingerprint is not null)
            throw Invalid("STATE_SPACE_EXPECTED_ABSENT", "expectedFingerprint must be null when creating a state space.");
        ValidateContext(context.RequestToken, context.AuthorizationEvidence);
    }

    private static void Validate(StateSpaceUpgradeRequest request, StateSpaceUpgradeContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ValidateStateSpaceId(request.StateSpaceId);
        ValidateActiveFingerprint(request.ActiveFingerprint);
        if (!UpperSha256(request.ExpectedBindingFingerprint))
            throw Invalid("INVALID_PAYLOAD", "expectedBindingFingerprint must be an uppercase SHA-256 value.");
        ValidateContext(context.RequestToken, context.AuthorizationEvidence);
    }

    private static void ValidateActiveFingerprint(string value)
    {
        if (!UpperSha256(value))
            throw Invalid("INVALID_PAYLOAD", "activeFingerprint must be an uppercase SHA-256 value.");
    }

    private static void ValidateContext(string token, AuthorizationAuditEvidence evidence)
    {
        if (token.Length != 32
            || token.Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw Invalid("INVALID_PAYLOAD", "requestToken must contain exactly 32 lowercase hexadecimal characters.");
        if (!evidence.Allowed)
            throw Invalid("PRIVATE_OPERATOR_DENIED", "A successful authorization decision is required.");
    }

    private static void ValidateStateSpaceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl))
            throw Invalid("INVALID_PAYLOAD", "stateSpaceId must be a nonblank bounded identifier without control characters.");
    }

    private static string CreateRequestFingerprint(StateSpaceCreationRequest request) => Hash(new
    {
        kind = CreateKind, request.StateSpaceId, applicationId = request.ApplicationId.Value,
        request.ActiveFingerprint, request.ExpectedFingerprint,
        scope = EcsComponentRolePolicyParser.ScopeName(request.Scope)
    });

    private static string UpgradeRequestFingerprint(StateSpaceUpgradeRequest request) => Hash(new
    {
        kind = UpgradeKind, request.StateSpaceId, applicationId = request.ApplicationId.Value,
        request.ActiveFingerprint, request.ExpectedBindingFingerprint
    });

    private static string UpgradeEvidenceFingerprint(
        StateSpaceBindingSummary target, StateSpaceCompatibilityEvidence compatibility) => Hash(new
    {
        target.BindingFingerprint, compatibility.Code, compatibility.EntityCount,
        compatibility.ComponentCount, compatibility.DependencyCoverageVersion,
        compatibility.DependencyCoverageComplete
    });

    private static string BindingFingerprint(
        string stateSpaceId, string applicationId, int applicationRevision,
        string applicationFingerprint, string activeFingerprint,
        string resolutionFingerprint, EcsStateSpaceScope scope, int bindingRevision) => Hash(new
    {
        stateSpaceId, applicationId, applicationRevision, applicationFingerprint,
        activeFingerprint, resolutionFingerprint, scope = EcsComponentRolePolicyParser.ScopeName(scope), bindingRevision
    });

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static bool UpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static string Subject(string kind, string requestFingerprint) => $"{kind}|{requestFingerprint}";
    private static string PreviewSubjectPrefix(string kind, string token, string requestFingerprint) =>
        $"preview|{kind}|{token}|{requestFingerprint}|";
    private static string PreviewSubject(string kind, string token, string requestFingerprint, string evidenceFingerprint) =>
        PreviewSubjectPrefix(kind, token, requestFingerprint) + evidenceFingerprint;
    private static StateSpaceAdministrationException Invalid(string code, string message) => new(code, message);

    private sealed record UpgradeCandidate(
        StateSpaceBindingSummary Previous,
        StateSpaceBindingSummary Target,
        StateSpaceCompatibilityEvidence Compatibility);
}

internal sealed class StateSpaceBindingRevisionRecord
{
    public required string StateSpaceId { get; set; }
    public int BindingRevision { get; set; }
    public required string ApplicationId { get; set; }
    public int ApplicationRevision { get; set; }
    public required string ApplicationFingerprint { get; set; }
    public required string ActiveFingerprint { get; set; }
    public string ResolutionFingerprint { get; set; } = new('0', 64);
    public string Scope { get; set; } = "runtime-state-space";
    public required string BindingFingerprint { get; set; }
    public string? PreviousBindingFingerprint { get; set; }
    public required string CompatibilityCode { get; set; }
    public int EntityCount { get; set; }
    public int ComponentCount { get; set; }
    public required string DependencyCoverageVersion { get; set; }
    public bool DependencyCoverageComplete { get; set; }
    public string? OperationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
