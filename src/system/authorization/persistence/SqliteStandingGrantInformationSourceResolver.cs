using System.Text.Json;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization;

public sealed partial class SqliteStandingGrantTargetResolver
{
    private async Task<StandingGrantTargetResolution> ResolveInformationSourceAsync(InteractionInvocationHost host,
        string exactDefinitionId, CancellationToken cancellationToken)
    {
        try
        {
            CatalogNamespaceIdentity.ValidateRecordId(exactDefinitionId);
            var app = host.ApplicationRevision.ApplicationId;
            if (app.IsSystem || !exactDefinitionId.StartsWith(app.Value + ".", StringComparison.Ordinal))
                return Denied("STANDING_GRANT_DEFINITION_SCOPE");
            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var registered = applications.Get(app);
            if (registered is null || registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Denied("STANDING_GRANT_APPLICATION_STALE");
            var current = await db.Set<InformationSourceOwnerCurrentRecord>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.QualifiedTargetId == exactDefinitionId, cancellationToken);
            if (current is null) return Unavailable("STANDING_GRANT_INFORMATION_OWNER_UNAVAILABLE");
            var value = await InformationSourceOwnership.ReadAsync(db, current.SourceId, cancellationToken);
            if (value is null || value.Owner.ApplicationId != app.Value)
                return Denied("STANDING_GRANT_INFORMATION_OWNER_STALE");
            var chain = CatalogNamespaceIdentity.NamespaceChain(exactDefinitionId).Select(id => namespaces.Get(id)).ToArray();
            if (chain.Length == 0 || chain.Any(item => item is null || !item.IsEnabled
                    || item.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed))
                return Denied("STANDING_GRANT_NAMESPACE_UNREVIEWED");
            var leaf = chain[^1]!;
            if (leaf.Id == CatalogNamespaceIdentity.RootNamespaceId
                || !leaf.AllowedKinds.Contains(CatalogNamespaceKinds.InformationSource, StringComparer.Ordinal))
                return Denied("STANDING_GRANT_NAMESPACE_KIND_DENIED");
            var activation = activations.Current(app);
            if (activation is not null && catalog.BuildPermissionSnapshot(app, activation).Manifest.Records
                    .Any(record => record.QualifiedId == exactDefinitionId))
                return Denied("STANDING_GRANT_DEFINITION_AMBIGUOUS");
            var proof = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                applicationId = app.Value, registered.Revision, registered.Fingerprint, namespaceChain = chain,
                ownerRevision = value.Owner.Revision, ownerFingerprint = value.Owner.ContentFingerprint,
                value.Source.Id, sourceRevision = value.Source.Revision, value.Source.ContentHash
            }));
            return new(StandingGrantTargetResolutionStatus.Available, "STANDING_GRANT_TARGET_AVAILABLE",
                new(exactDefinitionId, CatalogNamespaceKinds.InformationSource, app, leaf.Id,
                    "information-owner:" + InteractionCanonicalJson.Fingerprint("dantes-roleplay/standing-grant-owner/v1", proof),
                    value.Source.Revision, value.Source.ContentHash));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or CatalogNamespaceException or JsonException or ApplicationActivation.ApplicationActivationException
            or CatalogNavigation.ApplicationCatalogMaterializationException)
        {
            return Unavailable("STANDING_GRANT_INFORMATION_OWNER_UNAVAILABLE");
        }
    }
}
