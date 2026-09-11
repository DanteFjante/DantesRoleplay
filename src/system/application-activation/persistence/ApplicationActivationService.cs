using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

public sealed class ApplicationActivationService : IApplicationActivationService,
    IActivatedApplicationEvidenceReader, IApplicationDefinitionChangeReader
{
    private readonly DantesRoleplayDbContext db;
    private readonly IApplicationPreviewService previews;
    private readonly IApplicationExtensionRegistry extensions;
    private readonly ISourceRegistry? sources;
    private readonly IAllowedSourceRootResolver? allowedRoots;
    private readonly IProjectionImpactService impacts;
    private readonly IOperationLog operations;
    private readonly ApplicationRetainedDocumentStore retainedDocuments;

    public ApplicationActivationService(
        DantesRoleplayDbContext db,
        IApplicationPreviewService previews,
        IApplicationExtensionRegistry extensions,
        ISourceRegistry sources,
        IAllowedSourceRootResolver allowedRoots,
        IProjectionImpactService impacts,
        IOperationLog operations)
    {
        this.db = db;
        this.previews = previews;
        this.extensions = extensions;
        this.sources = sources;
        this.allowedRoots = allowedRoots;
        this.impacts = impacts;
        this.operations = operations;
        retainedDocuments = new(db);
    }

    internal ApplicationActivationService(
        DantesRoleplayDbContext db,
        IApplicationPreviewService previews,
        ISourceRegistry sources,
        IAllowedSourceRootResolver allowedRoots,
        IProjectionImpactService impacts,
        IOperationLog operations)
        : this(db, previews, new EmptyApplicationExtensionRegistry(), sources, allowedRoots, impacts, operations)
    {
    }

    /// <summary>Legacy metadata-only seam for existing test fixtures; production DI cannot select it.</summary>
    internal ApplicationActivationService(
        DantesRoleplayDbContext db,
        IApplicationPreviewService previews,
        IProjectionImpactService impacts,
        IOperationLog operations)
        : this(db, previews, new EmptyApplicationExtensionRegistry(), impacts, operations)
    {
    }

    /// <summary>Legacy metadata-only seam for existing extension test fixtures; production DI cannot select it.</summary>
    internal ApplicationActivationService(
        DantesRoleplayDbContext db,
        IApplicationPreviewService previews,
        IApplicationExtensionRegistry extensions,
        IProjectionImpactService impacts,
        IOperationLog operations)
    {
        this.db = db;
        this.previews = previews;
        this.extensions = extensions;
        this.sources = null;
        this.allowedRoots = null;
        this.impacts = impacts;
        this.operations = operations;
        retainedDocuments = new(db);
    }

    private const string Kind = "system.application.activate";
    private const string CoverageVersion = "declared-component-field-projection-v1";
    private const string CurrentPreparationVersion = "retained-mechanic-body-v2";
    private const long MaximumDocumentBytes = 10L * 1024 * 1024;
    private const long MaximumActivationBytes = 256L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var current = db.Set<ApplicationActivationCurrentRecord>().AsNoTracking()
            .SingleOrDefault(row => row.ApplicationId == applicationId.Value);
        return current is null ? null : Read(applicationId.Value, current.ActivationRevision);
    }

    public ActivatedApplicationDocumentEvidence? ReadDocumentEvidence(
        ApplicationIdentifier applicationId,
        int activationRevision,
        string logicalIdentity)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (activationRevision < 1 || string.IsNullOrWhiteSpace(logicalIdentity))
            throw new ArgumentException("An activation revision and logical document identity are required.");
        var result = (from revision in db.Set<ApplicationActivationRevisionRecord>().AsNoTracking()
                join link in db.Set<ApplicationActivationDocumentRecord>().AsNoTracking()
                    on new { revision.ApplicationId, revision.ActivationRevision }
                    equals new { link.ApplicationId, link.ActivationRevision }
                join identity in db.Set<ApplicationActivationDocumentIdentityRecord>().AsNoTracking()
                    on new { link.ApplicationId, link.IdentityId }
                    equals new { identity.ApplicationId, IdentityId = identity.Id }
                join evidence in db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking()
                    on new { link.IdentityId, link.EvidenceVersion }
                    equals new { evidence.IdentityId, evidence.EvidenceVersion }
                where revision.ApplicationId == applicationId.Value
                      && revision.ActivationRevision == activationRevision
                      && identity.LogicalIdentity == logicalIdentity
                select new { revision.PreparationVersion, identity.LogicalIdentity, Evidence = evidence })
            .SingleOrDefault();
        if (result is null) return null;
        if (result.Evidence.RetainedBytes is null && result.PreparationVersion is not null)
            throw Invalid("ACTIVATION_EVIDENCE_MISSING",
                "A prepared activation revision is missing its retained document bytes.");
        if (result.Evidence.RetainedBytes is { } bytes &&
            (bytes.LongLength != result.Evidence.Length || HashBytes(bytes) != result.Evidence.ContentFingerprint))
            throw Invalid("ACTIVATION_EVIDENCE_CORRUPT",
                "Retained activation document bytes do not match their immutable evidence.");
        return new(applicationId, activationRevision, result.LogicalIdentity,
            result.Evidence.ContentFingerprint, result.Evidence.Length,
            result.Evidence.RetainedBytes?.ToArray(), result.PreparationVersion is null);
    }

    public ApplicationDefinitionChange? CurrentChange(ApplicationIdentifier applicationId)
    {
        var current = Current(applicationId);
        return current is null ? null : Change(current);
    }

    public ApplicationDefinitionChange? RevisionChange(
        ApplicationIdentifier applicationId,
        int activationRevision)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (activationRevision < 1) throw new ArgumentOutOfRangeException(nameof(activationRevision));
        return db.Set<ApplicationActivationRevisionRecord>().AsNoTracking().Any(value =>
            value.ApplicationId == applicationId.Value && value.ActivationRevision == activationRevision)
            ? Change(Read(applicationId.Value, activationRevision))
            : null;
    }

    public IReadOnlyList<ApplicationDefinitionChange> ChangesAfter(
        ApplicationIdentifier applicationId,
        int afterActivationRevision,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (afterActivationRevision < 0) throw new ArgumentOutOfRangeException(nameof(afterActivationRevision));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var revisions = db.Set<ApplicationActivationRevisionRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId.Value
                            && value.ActivationRevision > afterActivationRevision)
            .OrderBy(value => value.ActivationRevision)
            .Select(value => value.ActivationRevision).Take(limit).ToArray();
        return Array.AsReadOnly(revisions.Select(value => Change(Read(applicationId.Value, value))).ToArray());
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
        var outcome = current?.ActivationFingerprint == candidate.Manifest.ActivationFingerprint
            ? "unchanged" : "would-activate";
        var revision = outcome == "unchanged" ? current!.ActivationRevision : NextRevision(request.ApplicationId);
        var preview = candidate.Manifest with { ActivationRevision = revision };
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
                context.RequestToken, requestFingerprint, candidate.Manifest.ActivationFingerprint);
            var current = Current(request.ApplicationId);
            RequireExpectation(request.ExpectedActiveFingerprint, current?.ActivationFingerprint);
            var unchanged = current?.ActivationFingerprint == candidate.Manifest.ActivationFingerprint;
            var revision = unchanged ? current!.ActivationRevision : NextRevision(request.ApplicationId);
            var activation = unchanged ? current! : candidate.Manifest with { ActivationRevision = revision };
            var outcome = unchanged ? "unchanged" : "activated";

            // A new confirmation must not endorse a generation whose retained content can no
            // longer be read. Replays still return the historical receipt before reaching here.
            if (unchanged)
                foreach (var document in activation.Winners)
                    _ = ReadDocumentEvidence(request.ApplicationId, revision, document.LogicalIdentity)
                        ?? throw Invalid("ACTIVATION_EVIDENCE_MISSING",
                            "The active revision is missing its retained document evidence.");

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

            if (!unchanged) await PersistAsync(activation, candidate.RetainedBytes, cancellationToken);
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

    private async Task<ActivationCandidate> BuildAsync(
        ApplicationActivationRequest request,
        string operationId,
        CancellationToken cancellationToken)
    {
        ApplicationPreviewResult preview;
        try
        {
            preview = request.ExtensionIds is not null && request.SourceIds is not null
                ? await previews.PreviewAsync(request.ApplicationId,
                    CanonicalSourceIds(request.SourceIds), CanonicalExtensionIds(request.ExtensionIds), cancellationToken)
                : request.ExtensionIds is not null
                    ? await previews.PreviewExtensionsAsync(request.ApplicationId,
                        CanonicalExtensionIds(request.ExtensionIds), cancellationToken)
                : request.SourceIds is null
                    ? await previews.PreviewAsync(request.ApplicationId, cancellationToken)
                    : await previews.PreviewAsync(request.ApplicationId,
                        CanonicalSourceIds(request.SourceIds), cancellationToken);
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
        var activatedExtensions = preview.ExtensionIds.Select(extensionId =>
        {
            var registration = extensions.Get(request.ApplicationId, extensionId)
                ?? throw Invalid("EXTENSION_REGISTRATION_DRIFT",
                    "A previewed extension registration is no longer available.");
            return new ActivatedApplicationExtension(extensionId,
                ApplicationExtensionRegistrationFingerprint.Compute(registration),
                registration.SourceIds, registration.NamespaceIds,
                registration.HigherPriorityThan, registration.OverridesBase);
        }).ToArray();
        var retainedBytes = this.sources is null || allowedRoots is null
            ? null
            : await ReadCandidateBytesAsync(request.ApplicationId, preview.Sources, winners, cancellationToken);
        if (retainedBytes is not null) PrepareMechanics(winners, retainedBytes);
        var preparationVersion = retainedBytes is null ? null : CurrentPreparationVersion;
        var activationFingerprint = Fingerprint(preview, impact.GraphFingerprint, sources, winners,
            activatedExtensions, preparationVersion);
        var manifest = new ActiveApplicationManifest(request.ApplicationId, 0, preview.ApplicationRevision, preview.ApplicationFingerprint,
            preview.PreviewFingerprint, preview.ScannedDocumentsFingerprint,
            preview.CandidateManifestFingerprint, impact.GraphFingerprint, activationFingerprint,
            CoverageVersion, false, Array.AsReadOnly(sources), Array.AsReadOnly(winners), operationId,
            DateTime.UtcNow)
        {
            ResolutionFingerprint = preview.ResolutionFingerprint,
            Extensions = Array.AsReadOnly(activatedExtensions),
            PreparationVersion = preparationVersion
        };
        return new(manifest, retainedBytes);
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> ReadCandidateBytesAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<ApplicationPreviewSource> previewSources,
        IReadOnlyList<ActivatedApplicationDocument> winners,
        CancellationToken cancellationToken)
    {
        var previewById = previewSources.ToDictionary(value => value.SourceId, StringComparer.Ordinal);
        var registrations = this.sources!.For(applicationId)
            .ToDictionary(value => value.SourceId, StringComparer.Ordinal);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var winner in winners.OrderBy(value => value.LogicalIdentity, StringComparer.Ordinal))
        {
            if (!previewById.TryGetValue(winner.SourceId, out var previewSource)
                || !registrations.TryGetValue(winner.SourceId, out var registration)
                || SourceRegistrationFingerprint.Compute(registration) != previewSource.RegistrationFingerprint)
                throw Invalid("SOURCE_REGISTRATION_DRIFT",
                    "A candidate source registration changed after its preview was built.");
            if (!allowedRoots!.TryResolve(registration.AllowedRootId, out var configuredRoot)
                || string.IsNullOrWhiteSpace(configuredRoot))
                throw Invalid("SOURCE_ROOT_UNAVAILABLE", "A candidate source root is unavailable.");
            if (winner.Length < 0 || winner.Length > MaximumDocumentBytes
                || winner.Length > MaximumActivationBytes - totalBytes)
                throw Invalid("ACTIVATION_EVIDENCE_LIMIT",
                    "Candidate document evidence exceeds the bounded activation retention limit.");
            byte[] bytes;
            try
            {
                var root = Path.GetFullPath(configuredRoot);
                var path = Path.GetFullPath(Path.Combine(root,
                    winner.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!Inside(root, path) || HasReparsePoint(root, path))
                    throw Invalid("SOURCE_PATH_OUTSIDE_ROOT",
                        "A candidate document does not resolve to a regular path inside its allowed root.");
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length != winner.Length)
                    throw Invalid("SOURCE_DOCUMENT_DRIFT",
                        "A candidate document length changed after its exact preview.");
                bytes = new byte[checked((int)winner.Length)];
                await stream.ReadExactlyAsync(bytes, cancellationToken);
                if (await stream.ReadAsync(new byte[1], cancellationToken) != 0)
                    throw Invalid("SOURCE_DOCUMENT_DRIFT",
                        "A candidate document grew while its evidence was retained.");
            }
            catch (ApplicationActivationException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ApplicationActivationException("SOURCE_DOCUMENT_UNAVAILABLE",
                    "A candidate document could not be retained safely.", exception);
            }
            if (bytes.LongLength != winner.Length || HashBytes(bytes) != winner.ContentFingerprint)
                throw Invalid("SOURCE_DOCUMENT_DRIFT",
                    "A candidate document no longer matches the length and fingerprint in its exact preview.");
            totalBytes += bytes.LongLength;
            result.Add(winner.LogicalIdentity, bytes);
        }
        return result;
    }

    private static void PrepareMechanics(
        IReadOnlyList<ActivatedApplicationDocument> winners,
        IReadOnlyDictionary<string, byte[]> retainedBytes)
    {
        var byPath = winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        foreach (var markdownWinner in winners.Where(IsMechanicMarkdown)
                     .OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            var sourcePath = Path.ChangeExtension(markdownWinner.RelativePath, ".js").Replace('\\', '/');
            if (!byPath.TryGetValue(sourcePath, out var sourceWinner)
                || sourceWinner.SourceId != markdownWinner.SourceId)
                throw Invalid("MECHANIC_SOURCE_MISSING",
                    "A candidate mechanic has no same-source JavaScript sidecar.");
            try
            {
                var markdown = DecodeText(markdownWinner, retainedBytes[markdownWinner.LogicalIdentity]);
                var source = DecodeText(sourceWinner, retainedBytes[sourceWinner.LogicalIdentity]);
                var mechanic = MechanicFile.Parse(markdown, markdownWinner.RelativePath, source);
                _ = JintMechanicEngine.PrepareMechanicProgram(mechanic.Source);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ApplicationActivationException("EXECUTABLE_PREPARATION_FAILED",
                    $"Candidate mechanic '{markdownWinner.RelativePath}' could not be prepared.", exception);
            }
        }
    }

    private static bool IsMechanicMarkdown(ActivatedApplicationDocument value) =>
        value.IsText && value.RelativePath.EndsWith(".md", StringComparison.Ordinal)
        && value.RelativePath.Split('/').Contains("mechanics", StringComparer.Ordinal);

    private static string DecodeText(ActivatedApplicationDocument document, byte[] bytes)
    {
        if (!document.IsText) throw new InvalidOperationException("A mechanic document must be text.");
        var text = StrictUtf8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static bool Inside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !Path.IsPathRooted(relative);
    }

    private static bool HasReparsePoint(string root, string path)
    {
        var current = new DirectoryInfo(root);
        if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cursor = root;
        foreach (var segment in segments)
        {
            cursor = Path.Combine(cursor, segment);
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private static ApplicationDefinitionChange Change(ActiveApplicationManifest activation) => new(
        activation.ApplicationId,
        activation.ActivationRevision,
        activation.ActivationFingerprint,
        activation.ActivatedByOperationId,
        activation.ActivatedAtUtc,
        new(
            Array.AsReadOnly(activation.Sources.Select(value => value.SourceId)
                .Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(activation.Winners.Select(value => value.RelativePath)
                .Order(StringComparer.Ordinal).ToArray())),
        new(activation.DependencyGraphFingerprint, activation.DependencyCoverageVersion,
            activation.DependencyCoverageComplete),
        new("rebuildable", false, false));

    private async Task PersistAsync(
        ActiveApplicationManifest activation,
        IReadOnlyDictionary<string, byte[]>? retainedBytes,
        CancellationToken cancellationToken)
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
            ResolutionFingerprint = activation.ResolutionFingerprint,
            ActivationFingerprint = activation.ActivationFingerprint,
            DependencyCoverageVersion = activation.DependencyCoverageVersion,
            DependencyCoverageComplete = activation.DependencyCoverageComplete,
            PreparationVersion = activation.PreparationVersion,
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
        foreach (var (extension, ordinal) in activation.Extensions.Select((value, index) => (value, index)))
            db.Add(new ApplicationActivationExtensionRecord
            {
                ApplicationId = activation.ApplicationId.Value,
                ActivationRevision = activation.ActivationRevision,
                Ordinal = ordinal,
                ExtensionId = extension.ExtensionId,
                RegistrationFingerprint = extension.RegistrationFingerprint,
                SourceIdsJson = JsonSerializer.Serialize(extension.SourceIds),
                NamespaceIdsJson = JsonSerializer.Serialize(extension.NamespaceIds),
                HigherPriorityThanJson = JsonSerializer.Serialize(extension.HigherPriorityThan),
                OverridesBase = extension.OverridesBase
            });
        var retainedLinks = await retainedDocuments.RetainAsync(
            activation.ApplicationId, activation.Winners, retainedBytes, cancellationToken);
        foreach (var link in retainedLinks)
        {
            db.Add(new ApplicationActivationDocumentRecord
            {
                ApplicationId = activation.ApplicationId.Value,
                ActivationRevision = activation.ActivationRevision,
                Ordinal = link.Ordinal,
                IdentityId = link.IdentityId,
                EvidenceVersion = link.EvidenceVersion
            });
        }
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
        var winners = (from link in db.Set<ApplicationActivationDocumentRecord>().AsNoTracking()
                join identity in db.Set<ApplicationActivationDocumentIdentityRecord>().AsNoTracking()
                    on new { link.ApplicationId, link.IdentityId }
                    equals new { identity.ApplicationId, IdentityId = identity.Id }
                join evidence in db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking()
                    on new { link.IdentityId, link.EvidenceVersion }
                    equals new { evidence.IdentityId, evidence.EvidenceVersion }
                where link.ApplicationId == applicationId && link.ActivationRevision == activationRevision
                orderby link.Ordinal
                select new { identity.LogicalIdentity, Evidence = evidence })
            .AsEnumerable()
            .Select(value => new ActivatedApplicationDocument(value.LogicalIdentity, value.Evidence.SourceId,
                (SourceTrust)value.Evidence.Trust, value.Evidence.Precedence, value.Evidence.RelativePath,
                value.Evidence.MediaType, value.Evidence.ContentFingerprint, value.Evidence.Length,
                value.Evidence.IsText)).ToArray();
        var activatedExtensions = db.Set<ApplicationActivationExtensionRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId && value.ActivationRevision == activationRevision)
            .OrderBy(value => value.Ordinal).AsEnumerable()
            .Select(value => new ActivatedApplicationExtension(value.ExtensionId,
                value.RegistrationFingerprint, Strings(value.SourceIdsJson), Strings(value.NamespaceIdsJson),
                Strings(value.HigherPriorityThanJson), value.OverridesBase)).ToArray();
        return new ActiveApplicationManifest(ApplicationIdentifier.Parse(row.ApplicationId), row.ActivationRevision,
            row.ApplicationRevision, row.ApplicationFingerprint, row.PreviewFingerprint,
            row.ScannedDocumentsFingerprint, row.CandidateManifestFingerprint,
            row.DependencyGraphFingerprint, row.ActivationFingerprint, row.DependencyCoverageVersion,
            row.DependencyCoverageComplete, Array.AsReadOnly(sources), Array.AsReadOnly(winners),
            row.ActivatedByOperationId, DateTime.SpecifyKind(row.ActivatedAtUtc, DateTimeKind.Utc))
        {
            ResolutionFingerprint = row.ResolutionFingerprint,
            Extensions = Array.AsReadOnly(activatedExtensions),
            PreparationVersion = row.PreparationVersion
        };
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
        if (request.ExtensionIds is not null) _ = CanonicalExtensionIds(request.ExtensionIds);
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
        sourceIds = request.SourceIds is null ? null : CanonicalSourceIds(request.SourceIds),
        extensionIds = request.ExtensionIds is null ? null : CanonicalExtensionIds(request.ExtensionIds)
    });

    private static IReadOnlyList<string> CanonicalSourceIds(IReadOnlyList<string> values)
    {
        if (values.Count is < 1 or > 100
            || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw Invalid("INVALID_PAYLOAD", "sourceIds must contain 1 through 100 unique source IDs.");
        return Array.AsReadOnly(values.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<string> CanonicalExtensionIds(IReadOnlyList<string> values)
    {
        if (values.Count > 100 || values.Any(value => !ApplicationExtensionIdentity.IsValid(value))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw Invalid("INVALID_PAYLOAD", "extensionIds must contain at most 100 unique registered extension IDs.");
        return Array.AsReadOnly(values.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static string Fingerprint(
        ApplicationPreviewResult preview,
        string dependencyGraphFingerprint,
        IReadOnlyList<ActivatedApplicationSource> sources,
        IReadOnlyList<ActivatedApplicationDocument> winners,
        IReadOnlyList<ActivatedApplicationExtension> activatedExtensions,
        string? preparationVersion) => Hash(new
    {
        applicationId = preview.ApplicationId.Value,
        preview.ApplicationRevision,
        preview.ApplicationFingerprint,
        preview.PreviewFingerprint,
        preview.ScannedDocumentsFingerprint,
        preview.CandidateManifestFingerprint,
        dependencyGraphFingerprint,
        preview.ResolutionFingerprint,
        dependencyCoverageVersion = CoverageVersion,
        dependencyCoverageComplete = false,
        preparationVersion,
        sources,
        winners,
        extensions = activatedExtensions
    });

    private static IReadOnlyList<string> Strings(string json) =>
        Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(json)
            ?? throw Invalid("ACTIVATION_INCONSISTENT", "Stored activation extension metadata is invalid."));

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
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

    private sealed record ActivationCandidate(
        ActiveApplicationManifest Manifest,
        IReadOnlyDictionary<string, byte[]>? RetainedBytes);
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
    public required string ResolutionFingerprint { get; set; }
    public required string ActivationFingerprint { get; set; }
    public required string DependencyCoverageVersion { get; set; }
    public bool DependencyCoverageComplete { get; set; }
    public string? PreparationVersion { get; set; }
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
    public long IdentityId { get; set; }
    public int EvidenceVersion { get; set; }
}

internal sealed class ApplicationActivationDocumentIdentityRecord
{
    public long Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string LogicalIdentity { get; set; }
}

internal sealed class ApplicationActivationDocumentEvidenceRecord
{
    public long IdentityId { get; set; }
    public int EvidenceVersion { get; set; }
    public required string SourceId { get; set; }
    public int Trust { get; set; }
    public int Precedence { get; set; }
    public required string RelativePath { get; set; }
    public required string MediaType { get; set; }
    public required string ContentFingerprint { get; set; }
    public long Length { get; set; }
    public bool IsText { get; set; }
    public byte[]? RetainedBytes { get; set; }
}

internal sealed class ApplicationActivationExtensionRecord
{
    public required string ApplicationId { get; set; }
    public int ActivationRevision { get; set; }
    public int Ordinal { get; set; }
    public required string ExtensionId { get; set; }
    public required string RegistrationFingerprint { get; set; }
    public required string SourceIdsJson { get; set; }
    public required string NamespaceIdsJson { get; set; }
    public required string HigherPriorityThanJson { get; set; }
    public bool OverridesBase { get; set; }
}

internal sealed class ApplicationActivationReceiptRecord
{
    public required string OperationId { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string ApplicationId { get; set; }
    public int ActivationRevision { get; set; }
    public required string Outcome { get; set; }
}
