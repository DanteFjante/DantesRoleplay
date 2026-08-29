using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

public sealed class ApplicationActivationService(
    DantesRoleplayDbContext db,
    IApplicationPreviewService previews,
    IProjectionImpactService impacts,
    IOperationLog operations) : IApplicationActivationService
{
    private const string Kind = "system.application.activate";
    private const string CoverageVersion = "declared-component-field-projection-v1";

    public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var current = db.Set<ApplicationActivationCurrentRecord>().AsNoTracking()
            .SingleOrDefault(row => row.ApplicationId == applicationId.Value);
        return current is null ? null : Read(applicationId.Value, current.ActivationRevision);
    }

    public async Task<ApplicationActivationPreview> PreviewAsync(
        ApplicationActivationRequest request,
        ApplicationActivationContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var requestFingerprint = RequestFingerprint(request);
        var replay = Replay(request, context, requestFingerprint);
        if (replay is not null)
        {
            var operation = await RecordPreviewAsync(
                context, requestFingerprint, replay.Activation.ActivationFingerprint, cancellationToken);
            return new(replay.Activation, replay.Outcome, operation.Id);
        }

        var candidate = await BuildAsync(request, context.RequestToken, cancellationToken);
        var current = Current(request.ApplicationId);
        RequireExpectation(request.ExpectedActiveFingerprint, current?.ActivationFingerprint);
        var outcome = current?.ActivationFingerprint == candidate.ActivationFingerprint
            ? "unchanged" : "would-activate";
        var revision = outcome == "unchanged" ? current!.ActivationRevision : NextRevision(request.ApplicationId);
        var preview = candidate with { ActivationRevision = revision };
        var audit = await RecordPreviewAsync(
            context, requestFingerprint, preview.ActivationFingerprint, cancellationToken);
        return new(preview, outcome, audit.Id);
    }

    public async Task<ApplicationActivationReceipt> ActivateAsync(
        ApplicationActivationRequest request,
        ApplicationActivationContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var requestFingerprint = RequestFingerprint(request);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = Replay(request, context, requestFingerprint);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            RequireAnyPreview(context.RequestToken, requestFingerprint);
            var candidate = await BuildAsync(request, context.RequestToken, cancellationToken);
            RequireExactPreview(
                context.RequestToken, requestFingerprint, candidate.ActivationFingerprint);
            var current = Current(request.ApplicationId);
            RequireExpectation(request.ExpectedActiveFingerprint, current?.ActivationFingerprint);
            var unchanged = current?.ActivationFingerprint == candidate.ActivationFingerprint;
            var revision = unchanged ? current!.ActivationRevision : NextRevision(request.ApplicationId);
            var activation = unchanged ? current! : candidate with { ActivationRevision = revision };
            var outcome = unchanged ? "unchanged" : "activated";

            await operations.RecordAsync(
                "commit",
                unchanged
                    ? $"Confirmed unchanged active application overlay '{request.ApplicationId.Value}'."
                    : $"Activated application overlay '{request.ApplicationId.Value}' revision {revision}.",
                success: true,
                context.Intent,
                Subject(requestFingerprint),
                context.ProceduresUsed,
                consumesReadEvidence: true,
                cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence),
                id: context.RequestToken);

            if (!unchanged) Persist(activation);
            db.Add(new ApplicationActivationReceiptRecord
            {
                OperationId = context.RequestToken,
                RequestFingerprint = requestFingerprint,
                ApplicationId = request.ApplicationId.Value,
                ActivationRevision = revision,
                Outcome = outcome
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(activation, outcome, context.RequestToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ActiveApplicationManifest> BuildAsync(
        ApplicationActivationRequest request,
        string operationId,
        CancellationToken cancellationToken)
    {
        ApplicationPreviewResult preview;
        try
        {
            preview = request.SourceIds is null
                ? await previews.PreviewAsync(request.ApplicationId, cancellationToken)
                : await previews.PreviewAsync(request.ApplicationId, CanonicalSourceIds(request.SourceIds), cancellationToken);
        }
        catch (ApplicationPreviewException exception)
        {
            throw Invalid(exception.Code, exception.Message);
        }
        if (!preview.IsValid)
            throw Invalid("PREVIEW_INVALID", "Only a valid application preview can be activated.");
        if (!string.Equals(preview.PreviewFingerprint, request.PreviewFingerprint, StringComparison.Ordinal))
            throw Invalid("PREVIEW_STALE", "previewFingerprint does not match the current registered sources and files.");

        var impact = impacts.Analyze(request.ApplicationId);
        var sources = preview.Sources.Select(source => new ActivatedApplicationSource(
            source.SourceId, source.RegistrationFingerprint, source.DocumentCount, source.ProblemCount)).ToArray();
        var winners = preview.Winners.Select(document => new ActivatedApplicationDocument(
            document.LogicalIdentity, document.SourceId, document.Trust, document.Precedence,
            document.RelativePath, document.MediaType, document.ContentFingerprint, document.Length,
            document.IsText)).ToArray();
        var activationFingerprint = Fingerprint(preview, impact.GraphFingerprint, sources, winners);
        return new(request.ApplicationId, 0, preview.ApplicationRevision, preview.ApplicationFingerprint,
            preview.PreviewFingerprint, preview.ScannedDocumentsFingerprint,
            preview.CandidateManifestFingerprint, impact.GraphFingerprint, activationFingerprint,
            CoverageVersion, false, Array.AsReadOnly(sources), Array.AsReadOnly(winners), operationId,
            DateTime.UtcNow);
    }

    private void Persist(ActiveApplicationManifest activation)
    {
        db.Add(new ApplicationActivationRevisionRecord
        {
            ApplicationId = activation.ApplicationId.Value,
            ActivationRevision = activation.ActivationRevision,
            ApplicationRevision = activation.ApplicationRevision,
            ApplicationFingerprint = activation.ApplicationFingerprint,
            PreviewFingerprint = activation.PreviewFingerprint,
            ScannedDocumentsFingerprint = activation.ScannedDocumentsFingerprint,
            CandidateManifestFingerprint = activation.CandidateManifestFingerprint,
            DependencyGraphFingerprint = activation.DependencyGraphFingerprint,
            ActivationFingerprint = activation.ActivationFingerprint,
            DependencyCoverageVersion = activation.DependencyCoverageVersion,
            DependencyCoverageComplete = activation.DependencyCoverageComplete,
            ActivatedByOperationId = activation.ActivatedByOperationId,
            ActivatedAtUtc = activation.ActivatedAtUtc
        });
        foreach (var (source, ordinal) in activation.Sources.Select((value, index) => (value, index)))
            db.Add(new ApplicationActivationSourceRecord
            {
                ApplicationId = activation.ApplicationId.Value,
                ActivationRevision = activation.ActivationRevision,
                Ordinal = ordinal,
                SourceId = source.SourceId,
                RegistrationFingerprint = source.RegistrationFingerprint,
                DocumentCount = source.DocumentCount,
                ProblemCount = source.ProblemCount
            });
        foreach (var (document, ordinal) in activation.Winners.Select((value, index) => (value, index)))
            db.Add(new ApplicationActivationDocumentRecord
            {
                ApplicationId = activation.ApplicationId.Value,
                ActivationRevision = activation.ActivationRevision,
                Ordinal = ordinal,
                LogicalIdentity = document.LogicalIdentity,
                SourceId = document.SourceId,
                Trust = (int)document.Trust,
                Precedence = document.Precedence,
                RelativePath = document.RelativePath,
                MediaType = document.MediaType,
                ContentFingerprint = document.ContentFingerprint,
                Length = document.Length,
                IsText = document.IsText
            });
        var current = db.Set<ApplicationActivationCurrentRecord>()
            .SingleOrDefault(row => row.ApplicationId == activation.ApplicationId.Value);
        if (current is null)
            db.Add(new ApplicationActivationCurrentRecord
            {
                ApplicationId = activation.ApplicationId.Value,
                ActivationRevision = activation.ActivationRevision
            });
        else current.ActivationRevision = activation.ActivationRevision;
    }

    private ApplicationActivationReceipt? Replay(
        ApplicationActivationRequest request,
        ApplicationActivationContext context,
        string requestFingerprint)
    {
        var operation = db.Operations.AsNoTracking().SingleOrDefault(row => row.Id == context.RequestToken);
        if (operation is null) return null;
        if (!operation.Success || operation.Tool != "commit" || operation.Subject != Subject(requestFingerprint))
            throw Invalid("REQUEST_TOKEN_CONFLICT", "That requestToken was already used by a different operation or canonical request.");
        var receipt = db.Set<ApplicationActivationReceiptRecord>().AsNoTracking()
            .SingleOrDefault(row => row.OperationId == context.RequestToken)
            ?? throw Invalid("ACTIVATION_INCONSISTENT", "The prior activation operation has no immutable receipt.");
        if (receipt.RequestFingerprint != requestFingerprint || receipt.ApplicationId != request.ApplicationId.Value)
            throw Invalid("ACTIVATION_INCONSISTENT", "The prior activation receipt does not match its request.");
        return new(Read(receipt.ApplicationId, receipt.ActivationRevision), receipt.Outcome, receipt.OperationId);
    }

    private ActiveApplicationManifest Read(string applicationId, int activationRevision)
    {
        var row = db.Set<ApplicationActivationRevisionRecord>().AsNoTracking().Single(value =>
            value.ApplicationId == applicationId && value.ActivationRevision == activationRevision);
        var sources = db.Set<ApplicationActivationSourceRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId && value.ActivationRevision == activationRevision)
            .OrderBy(value => value.Ordinal)
            .Select(value => new ActivatedApplicationSource(value.SourceId, value.RegistrationFingerprint,
                value.DocumentCount, value.ProblemCount)).ToArray();
        var winners = db.Set<ApplicationActivationDocumentRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId && value.ActivationRevision == activationRevision)
            .OrderBy(value => value.Ordinal).AsEnumerable()
            .Select(value => new ActivatedApplicationDocument(value.LogicalIdentity, value.SourceId,
                (SourceTrust)value.Trust, value.Precedence, value.RelativePath, value.MediaType,
                value.ContentFingerprint, value.Length, value.IsText)).ToArray();
        return new(ApplicationIdentifier.Parse(row.ApplicationId), row.ActivationRevision,
            row.ApplicationRevision, row.ApplicationFingerprint, row.PreviewFingerprint,
            row.ScannedDocumentsFingerprint, row.CandidateManifestFingerprint,
            row.DependencyGraphFingerprint, row.ActivationFingerprint, row.DependencyCoverageVersion,
            row.DependencyCoverageComplete, Array.AsReadOnly(sources), Array.AsReadOnly(winners),
            row.ActivatedByOperationId, DateTime.SpecifyKind(row.ActivatedAtUtc, DateTimeKind.Utc));
    }

    private int NextRevision(ApplicationIdentifier applicationId) =>
        db.Set<ApplicationActivationRevisionRecord>().AsNoTracking()
            .Where(row => row.ApplicationId == applicationId.Value)
            .Max(row => (int?)row.ActivationRevision).GetValueOrDefault() + 1;

    private async Task<Operation> RecordPreviewAsync(
        ApplicationActivationContext context,
        string requestFingerprint,
        string activationFingerprint,
        CancellationToken cancellationToken) => await operations.RecordAsync(
            "commit", "Validated application activation without changing active state.", success: true,
            context.Intent, PreviewSubject(
                context.RequestToken, requestFingerprint, activationFingerprint),
            context.ProceduresUsed, consumesReadEvidence: false, cancellationToken: cancellationToken,
            guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence));

    private void RequireAnyPreview(string requestToken, string requestFingerprint)
    {
        var subjectPrefix = PreviewSubjectPrefix(requestToken, requestFingerprint);
        if (!db.Operations.AsNoTracking().Any(operation =>
                operation.Tool == "commit" && operation.Subject.StartsWith(subjectPrefix)
                && operation.Success))
            throw Invalid("DRY_RUN_REQUIRED", "Commit the exact payload with dryRun: true before applying it.");
    }

    private void RequireExactPreview(
        string requestToken,
        string requestFingerprint,
        string activationFingerprint)
    {
        var subject = PreviewSubject(requestToken, requestFingerprint, activationFingerprint);
        if (!db.Operations.AsNoTracking().Any(operation =>
                operation.Tool == "commit" && operation.Subject == subject && operation.Success))
            throw Invalid("DRY_RUN_STALE", "The derived activation evidence changed after dry run; dry-run the exact payload again.");
    }

    private static void RequireExpectation(string? expected, string? current)
    {
        if (!string.Equals(expected, current, StringComparison.Ordinal))
            throw Invalid("ACTIVATION_STALE", current is null
                ? "No application overlay is active, but expectedActiveFingerprint did not expect absence."
                : "expectedActiveFingerprint does not match the current active application overlay.");
    }

    private static void Validate(ApplicationActivationRequest request, ApplicationActivationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!UpperSha256(request.PreviewFingerprint)
            || request.ExpectedActiveFingerprint is not null && !UpperSha256(request.ExpectedActiveFingerprint))
            throw Invalid("INVALID_PAYLOAD", "Activation fingerprints must be uppercase SHA-256 values or null where allowed.");
        if (request.SourceIds is not null) _ = CanonicalSourceIds(request.SourceIds);
        if (context.RequestToken.Length != 32
            || context.RequestToken.Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw Invalid("INVALID_PAYLOAD", "requestToken must contain exactly 32 lowercase hexadecimal characters.");
        if (!context.AuthorizationEvidence.Allowed)
            throw Invalid("PRIVATE_OPERATOR_DENIED", "A successful authorization decision is required.");
    }

    private static string RequestFingerprint(ApplicationActivationRequest request) => Hash(new
    {
        kind = Kind,
        applicationId = request.ApplicationId.Value,
        request.PreviewFingerprint,
        request.ExpectedActiveFingerprint,
        sourceIds = request.SourceIds is null ? null : CanonicalSourceIds(request.SourceIds)
    });

    private static IReadOnlyList<string> CanonicalSourceIds(IReadOnlyList<string> values)
    {
        if (values.Count is < 1 or > 100
            || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw Invalid("INVALID_PAYLOAD", "sourceIds must contain 1 through 100 unique source IDs.");
        return Array.AsReadOnly(values.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static string Fingerprint(
        ApplicationPreviewResult preview,
        string dependencyGraphFingerprint,
        IReadOnlyList<ActivatedApplicationSource> sources,
        IReadOnlyList<ActivatedApplicationDocument> winners) => Hash(new
    {
        applicationId = preview.ApplicationId.Value,
        preview.ApplicationRevision,
        preview.ApplicationFingerprint,
        preview.PreviewFingerprint,
        preview.ScannedDocumentsFingerprint,
        preview.CandidateManifestFingerprint,
        dependencyGraphFingerprint,
        dependencyCoverageVersion = CoverageVersion,
        dependencyCoverageComplete = false,
        sources,
        winners
    });

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static bool UpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static string Subject(string requestFingerprint) => $"{Kind}|{requestFingerprint}";
    private static string PreviewSubjectPrefix(string requestToken, string requestFingerprint) =>
        $"preview|{Kind}|{requestToken}|{requestFingerprint}|";
    private static string PreviewSubject(
        string requestToken,
        string requestFingerprint,
        string activationFingerprint) =>
        PreviewSubjectPrefix(requestToken, requestFingerprint) + activationFingerprint;
    private static ApplicationActivationException Invalid(string code, string message) => new(code, message);
}

internal sealed class ApplicationActivationRevisionRecord
{
    public required string ApplicationId { get; set; }
    public int ActivationRevision { get; set; }
    public int ApplicationRevision { get; set; }
    public required string ApplicationFingerprint { get; set; }
    public required string PreviewFingerprint { get; set; }
    public required string ScannedDocumentsFingerprint { get; set; }
    public required string CandidateManifestFingerprint { get; set; }
    public required string DependencyGraphFingerprint { get; set; }
    public required string ActivationFingerprint { get; set; }
    public required string DependencyCoverageVersion { get; set; }
    public bool DependencyCoverageComplete { get; set; }
    public required string ActivatedByOperationId { get; set; }
    public DateTime ActivatedAtUtc { get; set; }
}

internal sealed class ApplicationActivationCurrentRecord
{
    public required string ApplicationId { get; set; }
    public int ActivationRevision { get; set; }
}

internal sealed class ApplicationActivationSourceRecord
{
    public required string ApplicationId { get; set; }
    public int ActivationRevision { get; set; }
    public int Ordinal { get; set; }
    public required string SourceId { get; set; }
    public required string RegistrationFingerprint { get; set; }
    public int DocumentCount { get; set; }
    public int ProblemCount { get; set; }
}

internal sealed class ApplicationActivationDocumentRecord
{
    public required string ApplicationId { get; set; }
    public int ActivationRevision { get; set; }
    public int Ordinal { get; set; }
    public required string LogicalIdentity { get; set; }
    public required string SourceId { get; set; }
    public int Trust { get; set; }
    public int Precedence { get; set; }
    public required string RelativePath { get; set; }
    public required string MediaType { get; set; }
    public required string ContentFingerprint { get; set; }
    public long Length { get; set; }
    public bool IsText { get; set; }
}

internal sealed class ApplicationActivationReceiptRecord
{
    public required string OperationId { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string ApplicationId { get; set; }
    public int ActivationRevision { get; set; }
    public required string Outcome { get; set; }
}
