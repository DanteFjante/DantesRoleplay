using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Projections;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.MCPServer;

public sealed record ApplicationReadinessRecovery(string Action, string Description);

public sealed record ApplicationReadinessEvidence(
    string? Revision = null,
    string? Fingerprint = null,
    string? Detail = null);

public sealed record ApplicationReadinessCheck(
    string Name,
    string Status,
    string Code,
    string Message,
    ApplicationReadinessRecovery? Recovery = null,
    ApplicationReadinessEvidence? Evidence = null);

public sealed record ApplicationReadinessReport(
    string Status,
    string ApplicationId,
    DateTime CheckedAtUtc,
    IReadOnlyList<ApplicationReadinessCheck> Checks);

public sealed class ApplicationReadinessException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Builds one transport-neutral readiness report for the private web endpoint, MCP, and direct AI.
/// Every check names its independently recoverable owner so a responsive listener cannot hide a
/// broken catalog, query, page release, or audience binding behind an empty application surface.
/// </summary>
public sealed class ApplicationReadinessService(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IPublicApplicationCatalogProvider catalogs,
    IWebPagePublicationDirectory publications,
    IWebPageStore pages,
    ILocalKnowledgeSeatProvider seats,
    IAuthorizedKnowledgeAudiencePolicy audiences,
    IKnowledgeApplicationBindingResolver bindings,
    IKnowledgeActorParticipationVerifier participation,
    IProjectionDefinitionRegistry? projections = null,
    IStateSpaceRegistry? stateSpaces = null,
    ILogger<ApplicationReadinessService>? log = null)
{
    public async Task<ApplicationReadinessReport> ReadAsync(
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        ApplicationIdentifier application;
        try { application = ApplicationIdentifier.Parse(applicationId); }
        catch (ArgumentException)
        {
            throw new ApplicationReadinessException(
                "APPLICATION_ID_INVALID", "The application ID is invalid.");
        }

        var checks = new List<ApplicationReadinessCheck>();
        await CheckDatabaseAsync(checks, cancellationToken);

        ApplicationRevision? registration = null;
        try { registration = applications.Get(application); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(Failed("application-registration", "APPLICATION_REGISTRY_UNAVAILABLE",
                "The application registry is unavailable.",
                "retry", "Restore the application registry, then request readiness again."));
        }
        if (!checks.Any(value => value.Name == "application-registration"))
            checks.Add(registration is null
                ? Failed("application-registration", "APPLICATION_NOT_REGISTERED",
                    "The application is not registered.", "register-application",
                    "Review and register the application through the authorized application capability.")
                : Ready("application-registration", "APPLICATION_REGISTERED",
                    $"Revision {registration.Revision}; fingerprint {registration.Fingerprint}.",
                    registration.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    registration.Fingerprint));

        ActiveApplicationManifest? activation = null;
        try { activation = activations.Current(application); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(Failed("active-catalog-snapshot", "APPLICATION_ACTIVATION_UNAVAILABLE",
                "Application activation is unavailable.",
                "retry", "Restore application activation storage, then request readiness again."));
        }
        if (!checks.Any(value => value.Name == "active-catalog-snapshot"))
            checks.Add(activation is null
                ? Failed("active-catalog-snapshot", "APPLICATION_NOT_ACTIVATED",
                    "The application has no active catalog snapshot.", "activate-application",
                    "Review an exact application preview and activate it through the authorized capability.")
                : Ready("active-catalog-snapshot", "APPLICATION_ACTIVATED",
                    $"Activation revision {activation.ActivationRevision}; fingerprint {activation.ActivationFingerprint}.",
                    activation.ActivationRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    activation.ActivationFingerprint));

        ICatalogNavigator? catalog = null;
        try
        {
            if (catalogs.TryGet(application, out var materialized))
            {
                catalog = materialized;
                checks.Add(Ready("catalog-materialization", "APPLICATION_CATALOG_AVAILABLE",
                    "The active catalog snapshot materialized and its retained files passed fingerprint checks.",
                    fingerprint: activation?.ActivationFingerprint));
            }
            else
            {
                var failure = (catalogs as IPublicApplicationCatalogDiagnostics)?.LastFailure(application);
                checks.Add(Failed("catalog-materialization",
                    failure?.Code ?? "APPLICATION_CATALOG_UNAVAILABLE",
                    failure?.Message ?? "The active catalog snapshot could not be materialized.",
                    "repair-catalog",
                    "Validate the catalog, restore the retained source at its exact fingerprint, and reactivate it."));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log?.LogError(exception,
                "Application catalog '{Application}' failed during readiness materialization.",
                application.Value);
            checks.Add(Failed("catalog-materialization", "APPLICATION_CATALOG_UNAVAILABLE",
                "The active catalog snapshot could not be materialized.",
                "repair-catalog", "Validate and restore the active catalog snapshot, then request readiness again."));
        }

        checks.Add(activation is null
            ? Failed("extension-resolution", "EXTENSION_RESOLUTION_UNAVAILABLE",
                "Extension resolution is unavailable without an active catalog snapshot.",
                "activate-application", "Activate a reviewed application snapshot before inspecting extension winners.")
            : Ready("extension-resolution", "EXTENSION_RESOLUTION_AVAILABLE",
                $"Fingerprint {activation.ResolutionFingerprint}; extensions " +
                (activation.Extensions.Count == 0
                    ? "none."
                    : string.Join(", ", activation.Extensions.Select(value => value.ExtensionId)) + "."),
                activation.ActivationRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                activation.ResolutionFingerprint,
                activation.Extensions.Count == 0
                    ? "No extensions are active."
                    : $"{activation.Extensions.Count} extension winner set(s) are active."));

        checks.Add(CheckQueries(application, catalog, projections));
        await CheckPageAsync(checks, application, cancellationToken);
        await CheckAudienceAsync(checks, application, activation, cancellationToken);

        return new(
            checks.All(value => value.Status == "ready") ? "ready" : "failed",
            application.Value,
            DateTime.UtcNow,
            checks.AsReadOnly());
    }

    private async Task CheckDatabaseAsync(
        ICollection<ApplicationReadinessCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            var available = await db.Database.CanConnectAsync(cancellationToken);
            checks.Add(available
                ? Ready("database", "DATABASE_AVAILABLE", "The authoritative database is available.",
                    fingerprint: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        db.Database.ProviderName + "\n" + db.Database.GetDbConnection().DataSource))),
                    detail: "Provider and data-source identity; not a mutable database-content hash.")
                : Failed("database", "DATABASE_UNAVAILABLE", "The authoritative database is unavailable.",
                    "restore-database", "Restore the configured database and request readiness again."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(Failed("database", "DATABASE_UNAVAILABLE",
                "The authoritative database is unavailable.",
                "restore-database", "Restore the configured database and request readiness again."));
        }
    }

    private static ApplicationReadinessCheck CheckQueries(
        ApplicationIdentifier application,
        ICatalogNavigator? catalog,
        IProjectionDefinitionRegistry? projections)
    {
        if (catalog is null)
            return Failed("query-callability", "APPLICATION_QUERY_CATALOG_UNAVAILABLE",
                "Query callability cannot be checked because the active catalog is unavailable.",
                "repair-catalog", "Repair the active catalog before retrying its query contracts.");

        try
        {
            var search = catalog.Search(new(
                application,
                application.Value,
                Kinds: [ApplicationQueryContract.CatalogKind],
                Statuses: ["active"],
                PageSize: CatalogNavigationLimits.MaximumPageSize));
            if (search.NextCursor is not null)
                return Failed("query-callability", "APPLICATION_QUERY_SET_TOO_LARGE",
                    "The active query set exceeds the bounded readiness inspection limit.",
                    "reduce-query-set", "Retire superseded query contracts and request readiness again.");
            if (search.Records.Count == 0)
                return Failed("query-callability", "APPLICATION_QUERY_UNAVAILABLE",
                    "The active catalog exposes no callable query contract.",
                    "repair-query", "Add or restore an active query backed by an exact active projection.");

            foreach (var hit in search.Records)
            {
                var queryRecord = catalog.Inspect(new(
                    application, hit.Record.Collection, hit.Record.QualifiedId));
                var query = ApplicationQueryContract.Parse(queryRecord.ContentJson, application);
                if (query.IsObjectProjection)
                {
                    var objectDefinition = projections?.Get(query.ProjectionQualifiedId, query.ProjectionVersion);
                    if (query.Status != "active" || objectDefinition is null || objectDefinition.Owner != application ||
                        objectDefinition.ContentHash != query.ProjectionContentHash || objectDefinition.ObjectContract is null
                        || !ValidObjectCollection(objectDefinition.ObjectContract, query.ObjectCollectionId)
                        || !ValidObjectRoles(objectDefinition.ObjectContract, query.ObjectCollectionId, query.Roles.Keys))
                        return Failed("query-callability", "APPLICATION_QUERY_PROJECTION_STALE",
                            $"Query '{query.Id}' does not match its exact registered object projection.",
                            "repair-query", "Restore the declared object projection version and fingerprint, then reactivate the catalog.");
                    continue;
                }
                var projection = catalog.Inspect(new(
                    application, queryRecord.Summary.Collection, query.ProjectionQualifiedId));
                if (query.Status != "active" || projection.Summary.Kind != "mechanic" ||
                    projection.Summary.Status != "active" ||
                    projection.Summary.Version != query.ProjectionVersion ||
                    projection.Summary.ContentFingerprint != query.ProjectionContentHash)
                    return Failed("query-callability", "APPLICATION_QUERY_PROJECTION_STALE",
                        $"Query '{query.Id}' does not match its exact active projection.",
                        "repair-query", "Restore the declared projection version and fingerprint, then reactivate the catalog.");
            }

            return Ready("query-callability", "APPLICATION_QUERIES_CALLABLE",
                $"{search.Records.Count} active query contract(s) resolve to exact active projections.",
                fingerprint: ActivationFingerprint(search.Records));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or JsonException)
        {
            return Failed("query-callability", "APPLICATION_QUERY_CONTRACT_INVALID",
                "An active query contract is invalid.",
                "repair-query", "Repair the active query contract and exact projection reference, then reactivate the catalog.");
        }

        static string ActivationFingerprint(IReadOnlyList<CatalogSearchHit> records)
        {
            var canonical = string.Join('\n', records.Select(value =>
                $"{value.Record.QualifiedId}:{value.Record.Version}:{value.Record.ContentFingerprint}"));
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonical)));
        }

        static bool ValidObjectCollection(RegisteredApplicationObjectContract contract, string? collectionId) =>
            collectionId is null ? contract.Collections.Count == 0
                : contract.Collections.Any(value => value.CollectionId == collectionId);

        static bool ValidObjectRoles(RegisteredApplicationObjectContract contract, string? collectionId,
            IEnumerable<string> queryRoles)
        {
            var declared = contract.Roles.Select(value => value.RoleId).ToHashSet(StringComparer.Ordinal);
            var bound = queryRoles.ToHashSet(StringComparer.Ordinal);
            if (!bound.IsSubsetOf(declared)) return false;
            var required = contract.Roles.Where(value => value.Required)
                .Select(value => value.RoleId).ToHashSet(StringComparer.Ordinal);
            if (collectionId is not null)
            {
                var collection = contract.Collections.Single(value => value.CollectionId == collectionId);
                var relationship = contract.Relationships.Single(value => value.RelationshipId == collection.SourceId);
                required.Remove(relationship.Direction == "incoming" ? relationship.FromRole : relationship.ToRole);
            }
            return required.IsSubsetOf(bound);
        }
    }

    private async Task CheckPageAsync(
        ICollection<ApplicationReadinessCheck> checks,
        ApplicationIdentifier application,
        CancellationToken cancellationToken)
    {
        try
        {
            var publication = await publications.FindIndexAsync(application, cancellationToken);
            var summary = publication is null
                ? null
                : await pages.GetSummaryAsync(publication.ContentPageId, cancellationToken);
            if (publication is null || summary is null || summary.ActiveRevision < 1)
            {
                checks.Add(Failed("web-page-release", "WEB_INDEX_PAGE_UNAVAILABLE",
                    "No active published index page revision is available for the application.",
                    "publish-page", "Stage, verify, and activate an application index-page bundle."));
                return;
            }

            var active = await pages.GetRevisionAsync(
                publication.ContentPageId, summary.ActiveRevision, cancellationToken);
            if (active is null)
            {
                checks.Add(Failed("web-page-release", "WEB_INDEX_PAGE_REVISION_MISSING",
                    "The selected active page revision cannot be read back.",
                    "restore-page", "Restore the active page revision or activate a verified rollback revision."));
                return;
            }
            if (summary.ActiveRevision != summary.LatestRevision)
            {
                checks.Add(Failed("web-page-release", "WEB_INDEX_PAGE_STALE",
                    $"Active revision {summary.ActiveRevision} trails latest revision {summary.LatestRevision}.",
                    "review-page-release",
                    "Compare the latest bundle and hashes, then activate the reviewed release revision.",
                    summary.ActiveRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    active.Summary.ContentHash,
                    $"Latest revision {summary.LatestRevision}."));
                return;
            }

            checks.Add(Ready("web-page-release", "WEB_INDEX_PAGE_CURRENT",
                $"Page {publication.ContentPageId} revision {summary.ActiveRevision} is active and current; hash {active.Summary.ContentHash}.",
                summary.ActiveRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                active.Summary.ContentHash,
                $"{active.Summary.AssetCount} asset(s), {active.Summary.AssetBytes} byte(s)."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(Failed("web-page-release", "WEB_INDEX_PAGE_UNAVAILABLE",
                "The published index page could not be inspected.",
                "retry-page-read", "Restore page storage and request readiness again."));
        }
    }

    private async Task CheckAudienceAsync(
        ICollection<ApplicationReadinessCheck> checks,
        ApplicationIdentifier application,
        ActiveApplicationManifest? activation,
        CancellationToken cancellationToken)
    {
        var outcome = await SystemAudienceContextHandler.ResolveAsync(
            seats, audiences, bindings, participation, cancellationToken);
        if (outcome.Error is not null)
        {
            checks.Add(Failed("audience-binding", outcome.Error.Code,
                outcome.Error.Why, "bind-audience", outcome.Error.Fix));
            return;
        }

        var value = JsonSerializer.SerializeToElement(outcome.Data);
        var status = value.TryGetProperty("status", out var statusValue)
            ? statusValue.GetString() : null;
        var boundApplication = value.TryGetProperty("applicationId", out var applicationValue)
            ? applicationValue.GetString() : null;
        if (!string.Equals(boundApplication, application.Value, StringComparison.Ordinal))
        {
            checks.Add(Failed("audience-binding", "AUDIENCE_APPLICATION_MISMATCH",
                "The current host-authorized audience is bound to another application.",
                "bind-audience", "Select a valid local seat for this application and request readiness again."));
            return;
        }
        if (status == "character-creation-required")
        {
            checks.Add(Failed("audience-binding", "AUDIENCE_CHARACTER_REQUIRED",
                "The audience is authorized, but the player seat has no active character participation.",
                "create-character", "Complete character creation and attach the character to this campaign."));
            return;
        }
        if (status != "bound")
        {
            checks.Add(Failed("audience-binding", "AUDIENCE_CONTEXT_UNAVAILABLE",
                "The current host-authorized audience is not bound.",
                "bind-audience", "Configure the local seat and active campaign participation, then retry."));
            return;
        }

        var stateSpaceId = value.TryGetProperty("stateSpaceId", out var stateSpace)
            ? stateSpace.GetString() : null;
        var role = value.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : null;
        StateSpaceView? boundStateSpace = null;
        try
        {
            if (stateSpaces is not null && !string.IsNullOrWhiteSpace(stateSpaceId))
                boundStateSpace = stateSpaces.Get(stateSpaceId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Report the same recoverable boundary as a missing or stale binding without
            // exposing persistence details through the public readiness surface.
        }
        if (activation is null || boundStateSpace is null
            || boundStateSpace.ApplicationRevision.ApplicationId != application
            || boundStateSpace.ApplicationRevision.Revision != activation.ApplicationRevision
            || boundStateSpace.ApplicationRevision.Fingerprint != activation.ApplicationFingerprint
            || boundStateSpace.ManifestFingerprint != activation.ActivationFingerprint
            || boundStateSpace.ResolutionFingerprint != activation.ResolutionFingerprint)
        {
            checks.Add(Failed("audience-binding", "AUDIENCE_STATE_SPACE_STALE",
                "The server-selected audience state space is not bound to the active application resolution.",
                "upgrade-state-space",
                "Preview and commit the exact compatible state-space upgrade, then request readiness again.",
                boundStateSpace?.BindingRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                boundStateSpace?.ManifestFingerprint));
            return;
        }
        checks.Add(Ready("audience-binding", "AUDIENCE_CONTEXT_BOUND",
            $"The server-selected {role ?? "audience"} context is bound to state space {stateSpaceId ?? "unknown"}.",
            detail: $"Application {application.Value}; role {role ?? "unknown"}."));
    }

    private static ApplicationReadinessCheck Ready(
        string name,
        string code,
        string message,
        string? revision = null,
        string? fingerprint = null,
        string? detail = null) => new(
            name, "ready", code, message, null,
            revision is null && fingerprint is null && detail is null
                ? null
                : new(revision, fingerprint, detail));

    private static ApplicationReadinessCheck Failed(
        string name,
        string code,
        string message,
        string action,
        string recovery,
        string? revision = null,
        string? fingerprint = null,
        string? detail = null) => new(
            name, "failed", code, message, new(action, recovery),
            revision is null && fingerprint is null && detail is null
                ? null
                : new(revision, fingerprint, detail));
}
