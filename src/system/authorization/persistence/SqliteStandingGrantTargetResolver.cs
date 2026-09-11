using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization;

/// <summary>Rehydrates grant ownership from retained source provenance without executing or registering content.</summary>
public sealed partial class SqliteStandingGrantTargetResolver(
    DantesRoleplayDbContext db, IApplicationRegistry applications,
    IApplicationActivationReader activations, IActivatedApplicationEvidenceReader evidence,
    ISourceRegistry sources, IApplicationExtensionRegistry extensions,
    ICatalogNamespaceRegistry namespaces, ActivatedApplicationCatalogMaterializer catalog) : IStandingGrantTargetResolver
{
    // A lookup is bounded independently of the larger activation retention allowance.
    internal const int MaximumLookupDocuments = 128;
    internal const long MaximumLookupBytes = 16L * 1024 * 1024;

    public async Task<StandingGrantTargetResolution> RevalidateAsync(InteractionInvocationHost host,
        StandingGrantDefinitionTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var selection = new StandingGrantDefinitionReference(target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint);
        var result = target.Candidate is { } candidate
            ? await ResolveCandidateReferenceAsync(host, candidate, selection, cancellationToken)
            : target.RetainedActivation is { } origin
            ? await ResolveRetainedAsync(host, origin, selection, cancellationToken)
            : await ResolveAsync(host, selection, cancellationToken);
        return result.Status == StandingGrantTargetResolutionStatus.Available && result.Target != target
            ? Denied("STANDING_GRANT_OWNER_EVIDENCE_STALE") : result;
    }

    public async Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
        StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var result = await ResolveCurrentAsync(host, selection.DefinitionId, selection.Kind, cancellationToken);
        return result.Target is { } target && (target.Revision != selection.Revision
                || target.ContentFingerprint != selection.ContentFingerprint)
            ? Denied("STANDING_GRANT_DEFINITION_STALE") : result;
    }

    public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
        string exactDefinitionId, string kind, CancellationToken cancellationToken = default) =>
        ResolveGenerationAsync(host, exactDefinitionId, kind, null, cancellationToken);

    public async Task<StandingGrantTargetResolution> ResolveRetainedAsync(InteractionInvocationHost host,
        StandingGrantActivationOrigin origin, StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Kind != CatalogNamespaceKinds.Procedure)
            return Denied("STANDING_GRANT_RETAINED_KIND_DENIED");
        var result = await ResolveGenerationAsync(host, selection.DefinitionId, selection.Kind, origin, cancellationToken);
        return result.Target is { } target && (target.Revision != selection.Revision || target.ContentFingerprint != selection.ContentFingerprint)
            ? Denied("STANDING_GRANT_DEFINITION_STALE") : result;
    }

    private async Task<StandingGrantTargetResolution> ResolveGenerationAsync(InteractionInvocationHost host,
        string exactDefinitionId, string kind, StandingGrantActivationOrigin? origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();
        var app = host.ApplicationRevision.ApplicationId;
        try
        {
            CatalogNamespaceIdentity.ValidateRecordId(exactDefinitionId);
            if (app.IsSystem || !exactDefinitionId.StartsWith(app.Value + ".", StringComparison.Ordinal)
                || exactDefinitionId.Contains('*') || exactDefinitionId.Contains('?'))
                return Denied("STANDING_GRANT_DEFINITION_SCOPE");
            if (kind is not (CatalogNamespaceKinds.Mechanic or CatalogNamespaceKinds.Procedure or CatalogNamespaceKinds.Query))
                return Unavailable("STANDING_GRANT_DEFINITION_KIND_UNAVAILABLE");

            // Reuse the caller's transaction. Standalone reads get one consistent owner view.
            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var registered = applications.Get(app);
            var activation = origin is null ? activations.Current(app) : activations.ReadRevision(app, origin.ActivationRevision);
            if (registered is null || activation is null || activation.PreparationVersion is null)
                return Unavailable("STANDING_GRANT_RETAINED_GENERATION_UNAVAILABLE");
            var actualOrigin = new StandingGrantActivationOrigin(activation.ActivationRevision,
                activation.ActivationFingerprint, activation.ApplicationRevision, activation.ApplicationFingerprint);
            if (origin is not null && origin != actualOrigin)
                return Denied("STANDING_GRANT_RETAINED_ORIGIN_STALE");
            if (registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Denied("STANDING_GRANT_APPLICATION_STALE");
            var selectedApplication = origin is null ? registered : applications.Get(app, activation.ApplicationRevision);
            if (selectedApplication is null || activation.ApplicationRevision != selectedApplication.Revision
                || activation.ApplicationFingerprint != selectedApplication.Fingerprint)
                return Denied("STANDING_GRANT_APPLICATION_STALE");

            var chain = CatalogNamespaceIdentity.NamespaceChain(exactDefinitionId)
                .Select(id => namespaces.Get(id)).ToArray();
            if (chain.Length == 0 || chain.Any(value => value is null || !value.IsEnabled
                    || value.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed))
                return Denied("STANDING_GRANT_NAMESPACE_UNREVIEWED");
            var leaf = chain[^1]!;
            if (leaf.Id == CatalogNamespaceIdentity.RootNamespaceId || !leaf.AllowedKinds.Contains(kind, StringComparer.Ordinal))
                return Denied("STANDING_GRANT_NAMESPACE_KIND_DENIED");

            var prepared = catalog.BuildPermissionSnapshot(app, activation);
            if (prepared.EffectiveSetFingerprint != activation.ActivationFingerprint)
                return Denied("STANDING_GRANT_LOCATOR_STALE");
            var located = prepared.Manifest.Records.Where(value => value.QualifiedId == exactDefinitionId).ToArray();
            if (located.Length == 0) return Unavailable("STANDING_GRANT_DEFINITION_UNAVAILABLE");
            if (located.Length != 1 || located[0].Kind != kind)
                return Denied("STANDING_GRANT_DEFINITION_AMBIGUOUS");
            var locator = located[0];
            var paths = kind == CatalogNamespaceKinds.Mechanic
                ? new[] { locator.SourceLogicalPath, Path.ChangeExtension(locator.SourceLogicalPath, ".js").Replace('\\', '/') }
                : new[] { locator.SourceLogicalPath };
            var selectedWinners = activation.Winners.Where(value => paths.Contains(value.RelativePath, StringComparer.Ordinal)).ToArray();
            if (selectedWinners.Length != paths.Length || selectedWinners.Select(value => value.RelativePath).Distinct(StringComparer.Ordinal).Count() != paths.Length)
                return Denied("STANDING_GRANT_DEFINITION_AMBIGUOUS");
            if (selectedWinners.Any(value => !value.IsText || value.Length < 0 || value.Length > MaximumLookupBytes)
                || selectedWinners.Sum(value => value.Length) > MaximumLookupBytes)
                return Unavailable("STANDING_GRANT_LOOKUP_LIMIT");
            var winners = selectedWinners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
            var documents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var winner in selectedWinners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var retained = evidence.ReadDocumentEvidence(app, activation.ActivationRevision, winner.LogicalIdentity);
                if (retained is null || retained.IsLegacyMetadataOnly || retained.RetainedBytes is not { } bytes)
                    return Unavailable("STANDING_GRANT_RETAINED_BYTES_UNAVAILABLE");
                if (retained.ApplicationId != app || retained.ActivationRevision != activation.ActivationRevision
                    || retained.LogicalIdentity != winner.LogicalIdentity || retained.ContentFingerprint != winner.ContentFingerprint
                    || retained.Length != winner.Length || bytes.LongLength != winner.Length
                    || Convert.ToHexString(SHA256.HashData(bytes)) != winner.ContentFingerprint)
                    return Denied("STANDING_GRANT_RETAINED_BYTES_CORRUPT");
                documents.Add(winner.RelativePath, bytes);
            }
            var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(app,
                winners[locator.SourceLogicalPath], winners, documents);
            if (record is null || record.QualifiedId != exactDefinitionId || record.Kind != kind
                || record.Version != locator.Version || record.ContentFingerprint != locator.ContentFingerprint
                || record.SourceId != locator.SourceId || record.SourceLogicalPath != locator.SourceLogicalPath)
                return Denied("STANDING_GRANT_LOCATOR_STALE");
            using var content = JsonDocument.Parse(record.ContentJson);
            if (!content.RootElement.TryGetProperty("id", out var id) || id.GetString() != exactDefinitionId)
                return Denied("STANDING_GRANT_ALIAS_UNSUPPORTED");
            var selected = winners[record.SourceLogicalPath];
            var source = sources.Get(app, selected.SourceId);
            var retainedSources = activation.Sources.Where(value => value.SourceId == selected.SourceId).ToArray();
            if (source is null || source.ApplicationId != app || retainedSources.Length != 1
                || SourceRegistrationFingerprint.Compute(source) != retainedSources[0].RegistrationFingerprint)
                return Denied("STANDING_GRANT_SOURCE_DRIFT");
            var declaredExtensions = extensions.For(app);
            if (declaredExtensions.Any(value => value.SourceIds.Contains(selected.SourceId, StringComparer.Ordinal)
                    || value.NamespaceIds.Any(ns => leaf.Id == ns || leaf.Id.StartsWith(ns + ".", StringComparison.Ordinal)))
                || activation.Extensions.Any(value => value.SourceIds.Contains(selected.SourceId, StringComparer.Ordinal)))
                return Unavailable("STANDING_GRANT_BORROWED_DEFINITION_UNAVAILABLE");

            var used = new List<ActivatedApplicationDocument> { selected };
            if (kind == CatalogNamespaceKinds.Mechanic)
            {
                var sidecar = winners[Path.ChangeExtension(selected.RelativePath, ".js").Replace('\\', '/')];
                if (sidecar.SourceId != selected.SourceId) return Denied("STANDING_GRANT_SOURCE_SPLIT");
                used.Add(sidecar);
            }
            var proof = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                applicationId = app.Value, registered.Revision, registered.Fingerprint,
                activation.ActivationRevision, activation.ActivationFingerprint,
                sourceFingerprint = retainedSources[0].RegistrationFingerprint,
                namespaceChain = chain, definitionId = record.QualifiedId, record.Kind,
                definitionRevision = record.Version, definitionFingerprint = record.ContentFingerprint,
                documents = used.Select(value => new { value.LogicalIdentity, value.SourceId, value.RelativePath, value.ContentFingerprint, value.Length })
            }));
            var target = new StandingGrantDefinitionTarget(exactDefinitionId, kind, app, leaf.Id,
                "active-owner:" + InteractionCanonicalJson.Fingerprint("dantes-roleplay/standing-grant-owner/v1", proof),
                record.Version, record.ContentFingerprint, RetainedActivation: origin);
            return new(StandingGrantTargetResolutionStatus.Available, "STANDING_GRANT_TARGET_AVAILABLE", target,
                CurrentActivation: origin is null ? actualOrigin : null);
        }
        catch (OperationCanceledException) { throw; }
        catch (ApplicationActivationException exception) when (exception.Code == "ACTIVATION_EVIDENCE_CORRUPT")
        {
            return Denied("STANDING_GRANT_RETAINED_BYTES_CORRUPT");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException
            or ApplicationActivationException or ApplicationCatalogMaterializationException
            or CatalogNamespaceException or InvalidOperationException or KeyNotFoundException)
        {
            return Unavailable("STANDING_GRANT_OWNER_EVIDENCE_UNAVAILABLE");
        }
    }

    public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        // Only the typed lookup survives this boundary; all content is freshly read from SQLite.
        return ResolveCandidateReferenceAsync(host, candidate.Candidate, selection, cancellationToken);
    }

    private static StandingGrantTargetResolution Denied(string code) => new(StandingGrantTargetResolutionStatus.Denied, code, null);
    private static StandingGrantTargetResolution Unavailable(string code) => new(StandingGrantTargetResolutionStatus.Unavailable, code, null);
}
