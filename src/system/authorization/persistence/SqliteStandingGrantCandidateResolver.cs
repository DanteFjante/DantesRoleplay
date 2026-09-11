using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization;

public sealed partial class SqliteStandingGrantTargetResolver
{
    public async Task<StandingGrantTargetResolution> ResolveCandidateReferenceAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selection);
        try
        {
            var app = host.ApplicationRevision.ApplicationId;
            if (app.IsSystem || candidate.ApplicationId != app || candidate.Revision < 1
                || candidate.CandidateId is not { Length: 32 }
                || candidate.CandidateId.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
                return Denied("STANDING_GRANT_CANDIDATE_SCOPE");
            CatalogNamespaceIdentity.ValidateRecordId(selection.DefinitionId);
            if (!selection.DefinitionId.StartsWith(app.Value + ".", StringComparison.Ordinal))
                return Denied("STANDING_GRANT_DEFINITION_SCOPE");
            if (selection.Kind is not (CatalogNamespaceKinds.Procedure or CatalogNamespaceKinds.Mechanic or CatalogNamespaceKinds.Query))
                return Unavailable("STANDING_GRANT_DEFINITION_KIND_UNAVAILABLE");
            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var reader = new ApplicationCandidateRetainedReader(db, applications);
            var retained = await reader.ReadMetadataAsync(app, candidate.CandidateId, candidate.Revision, cancellationToken);
            if (retained is null) return Unavailable("STANDING_GRANT_CANDIDATE_UNAVAILABLE");
            if (retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint)
                return Denied("STANDING_GRANT_CANDIDATE_STALE");
            var registered = applications.Get(app);
            if (registered is null || registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || registered.Revision != retained.ApplicationRevision.Revision
                || registered.Fingerprint != retained.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Denied("STANDING_GRANT_APPLICATION_STALE");
            var chain = CatalogNamespaceIdentity.NamespaceChain(selection.DefinitionId).Select(namespaceId => namespaces.Get(namespaceId)).ToArray();
            if (chain.Length == 0 || chain.Any(value => value is null || !value.IsEnabled
                    || value.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed))
                return Denied("STANDING_GRANT_NAMESPACE_UNREVIEWED");
            var leaf = chain[^1]!;
            if (leaf.Id == CatalogNamespaceIdentity.RootNamespaceId || !leaf.AllowedKinds.Contains(selection.Kind, StringComparer.Ordinal))
                return Denied("STANDING_GRANT_NAMESPACE_KIND_DENIED");
            ActiveApplicationManifest? basis = null;
            if (retained.RevisionRow.ExpectedActiveFingerprint is { } baseFingerprint)
            {
                var revision = await db.Set<ApplicationActivationRevisionRecord>().AsNoTracking()
                    .Where(value => value.ApplicationId == app.Value && value.ActivationFingerprint == baseFingerprint)
                    .Select(value => (int?)value.ActivationRevision).SingleOrDefaultAsync(cancellationToken);
                if (revision is null || (basis = activations.ReadRevision(app, revision.Value)) is null)
                    return Unavailable("STANDING_GRANT_CANDIDATE_BASE_UNAVAILABLE");
            }
            var changed = ApplicationCandidateDocumentSelection.ChangedPaths(retained.Documents, basis);
            var changedDocuments = await reader.ReadSelectedAsync(retained,
                ApplicationCandidateDocumentSelection.WithKnownSidecars(retained.Documents, changed), cancellationToken);
            var changedWinners = changedDocuments.ToDictionary(value => value.Document.RelativePath, value => value.Document, StringComparer.Ordinal);
            var changedBytes = changedDocuments.ToDictionary(value => value.Document.RelativePath, value => value.RetainedBytes, StringComparer.Ordinal);
            var changedRecords = changedDocuments.Select(value => ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(
                app, value.Document, changedWinners, changedBytes)).Where(value => value is not null).ToArray();
            var replacedPaths = changedRecords.Select(value => value!.SourceLogicalPath).ToHashSet(StringComparer.Ordinal);
            var baseRecords = basis is null ? Array.Empty<CatalogRecordDefinition>() : catalog.BuildPermissionSnapshot(app, basis).Manifest.Records;
            var matches = changedRecords.Concat(baseRecords.Where(value => !changed.Contains(value.SourceLogicalPath)
                    && !replacedPaths.Contains(value.SourceLogicalPath)))
                .Where(value => value?.QualifiedId == selection.DefinitionId).ToArray();
            if (matches.Length == 0) return Unavailable("STANDING_GRANT_DEFINITION_UNAVAILABLE");
            if (matches.Length != 1 || matches[0]!.Kind != selection.Kind)
                return Denied("STANDING_GRANT_DEFINITION_AMBIGUOUS");
            var record = matches[0]!;
            if (record.Version != selection.Revision || record.ContentFingerprint != selection.ContentFingerprint)
                return Denied("STANDING_GRANT_DEFINITION_STALE");
            var selectedPaths = ApplicationCandidateDocumentSelection.WithKnownSidecars(retained.Documents, [record.SourceLogicalPath]);
            var text = await reader.ReadSelectedAsync(retained, selectedPaths, cancellationToken);
            var winners = text.ToDictionary(value => value.Document.RelativePath, value => value.Document, StringComparer.Ordinal);
            var documents = text.ToDictionary(value => value.Document.RelativePath, value => value.RetainedBytes, StringComparer.Ordinal);
            var verified = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(app, winners[record.SourceLogicalPath], winners, documents);
            if (verified is null || verified.QualifiedId != record.QualifiedId || verified.Kind != record.Kind
                || verified.Version != record.Version || verified.ContentFingerprint != record.ContentFingerprint
                || verified.SourceId != record.SourceId || verified.SourceLogicalPath != record.SourceLogicalPath)
                return Denied("STANDING_GRANT_CANDIDATE_LOCATOR_STALE");
            using var content = JsonDocument.Parse(verified.ContentJson);
            if (!content.RootElement.TryGetProperty("id", out var id) || id.GetString() != selection.DefinitionId)
                return Denied("STANDING_GRANT_ALIAS_UNSUPPORTED");
            var selected = winners[record.SourceLogicalPath];
            var source = sources.Get(app, selected.SourceId);
            // Source specifications are immutable in the existing registry. Candidate links retain
            // their exact source identity, trust and precedence; retirement never creates a fallback.
            if (source is null || source.ApplicationId != app || source.Trust != selected.Trust || source.Precedence != selected.Precedence)
                return Denied("STANDING_GRANT_SOURCE_DRIFT");
            if (extensions.For(app).Any(value => value.SourceIds.Contains(selected.SourceId, StringComparer.Ordinal)
                    || value.NamespaceIds.Any(ns => leaf.Id == ns || leaf.Id.StartsWith(ns + ".", StringComparison.Ordinal))))
                return Unavailable("STANDING_GRANT_BORROWED_DEFINITION_UNAVAILABLE");
            var used = new List<ActivatedApplicationDocument> { selected };
            if (selection.Kind == CatalogNamespaceKinds.Mechanic)
            {
                var sidecar = winners[Path.ChangeExtension(selected.RelativePath, ".js").Replace('\\', '/')];
                if (sidecar.SourceId != selected.SourceId || sidecar.Trust != source.Trust || sidecar.Precedence != source.Precedence)
                    return Denied("STANDING_GRANT_SOURCE_SPLIT");
                used.Add(sidecar);
            }
            var proof = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                candidate, registered.Revision, registered.Fingerprint,
                sourceFingerprint = SourceRegistrationFingerprint.Compute(source), namespaceChain = chain,
                definitionId = record.QualifiedId, record.Kind, definitionRevision = record.Version,
                definitionFingerprint = record.ContentFingerprint, documents = used
            }));
            return new(StandingGrantTargetResolutionStatus.Available, "STANDING_GRANT_TARGET_AVAILABLE",
                new(selection.DefinitionId, selection.Kind, app, leaf.Id,
                    "candidate-owner:" + InteractionCanonicalJson.Fingerprint("dantes-roleplay/standing-grant-candidate-owner/v1", proof),
                    record.Version, record.ContentFingerprint, candidate));
        }
        catch (OperationCanceledException) { throw; }
        catch (ApplicationActivationException exception) when (exception.Code.Contains("CORRUPT", StringComparison.Ordinal))
        { return Denied("STANDING_GRANT_CANDIDATE_CORRUPT"); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or ApplicationActivationException
            or ApplicationCatalogMaterializationException or CatalogNamespaceException or InvalidOperationException or KeyNotFoundException)
        { return Unavailable("STANDING_GRANT_CANDIDATE_OWNER_UNAVAILABLE"); }
    }
}
