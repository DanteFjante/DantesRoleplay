using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Content;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Owns conditional information commands, current source authority, retained history and atomic receipts.</summary>
public sealed class SqliteConditionalInformationStore(DantesRoleplayDbContext db, InformationStore store,
    IApplicationRegistry applications, ICatalogNamespaceRegistry namespaces, IApplicationActivationReader activations,
    ActivatedApplicationCatalogMaterializer catalog, IStandingGrantPolicy grants, IStandingGrantTargetResolver targets,
    IOperationLog operations) : IConditionalInformationStore
{
    public Task<InteractionInvocationResult> WriteSourceAsync(InteractionInvocationHost host,
        InformationSourceConditionalWriteRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(host, "source", request, async (operation, replay) =>
        {
            if (request?.Value is null || request.ExpectedRevision is < 0 or int.MaxValue)
                return Failed("INVALID_PAYLOAD");
            var value = request.Value;
            var existing = await db.Set<InformationSource>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == value.Id, cancellationToken);
            var owner = await InformationSourceOwnership.ReadAsync(db, value.Id, cancellationToken);
            if (replay || existing is not null)
            {
                if (owner is null) return Unavailable("INFORMATION_SOURCE_OWNER_UNAVAILABLE");
                if (request.QualifiedTargetId is not null && request.QualifiedTargetId != owner.Owner.QualifiedTargetId)
                    return Failed("INFORMATION_SOURCE_TARGET_MISMATCH");
                var authority = await AuthorAsync(host, [owner.Owner.QualifiedTargetId], request.ExpectedRevision == 0, cancellationToken);
                if (!authority.Allowed) return Reject(authority);
                if (replay)
                {
                    var retained = await HasOutcomeAsync(operation, "source", value.Id, request.ExpectedRevision,
                        ContentHash.Of(value.Id, value.ScopeId, value.Name, value.Description, value.MetadataSchemaJson), cancellationToken);
                    return retained ? null : Unavailable("INFORMATION_RECEIPT_INCONSISTENT");
                }
                operation.GuardEvidenceJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(authority.Evidence));
            }
            else if (request.ExpectedRevision != 0 || request.QualifiedTargetId is null)
                return Failed("INFORMATION_SOURCE_TARGET_REQUIRED");

            var result = await store.WriteSourceConditionallyAsync(value, request.ExpectedRevision, operation.Id, cancellationToken);
            if (result.Status == "rejected") return Failed(result.ErrorCode, result.ErrorMessage);
            if (existing is null)
            {
                var error = await InformationOwnershipTargetRules.ValidateAsync(db, applications, namespaces, activations,
                    catalog, host.ApplicationRevision.ApplicationId, value.Id, request.QualifiedTargetId!, cancellationToken);
                if (error is not null) return Failed(error);
                await InformationSourceOwnership.AppendAsync(db, value.Id, host.ApplicationRevision.ApplicationId.Value,
                    request.QualifiedTargetId!, operation.Id, null, cancellationToken);
                var authority = await AuthorAsync(host, [request.QualifiedTargetId!], requireApplicationOwned: true, cancellationToken);
                if (!authority.Allowed) return Reject(authority);
                operation.GuardEvidenceJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(authority.Evidence));
            }
            await BindOutcomeAsync(operation, "source", value.Id, result.Source!.Revision, cancellationToken);
            return null;
        }, cancellationToken);

    public Task<InteractionInvocationResult> WriteRecordAsync(InteractionInvocationHost host,
        InformationRecordConditionalWriteRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(host, "record", request, async (operation, replay) =>
        {
            if (request?.Value is null || request.ExpectedRevision is < 0 or int.MaxValue)
                return Failed("INVALID_PAYLOAD");
            var value = request.Value;
            var existing = await db.Set<InformationRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == value.Id, cancellationToken);
            var sourceIds = new[] { existing?.SourceId, value.SourceId }.Where(x => x is not null).Distinct(StringComparer.Ordinal);
            var targetIds = new List<string>();
            foreach (var sourceId in sourceIds)
            {
                var owner = await InformationSourceOwnership.ReadAsync(db, sourceId!, cancellationToken);
                if (owner is null) return Unavailable("INFORMATION_SOURCE_OWNER_UNAVAILABLE");
                targetIds.Add(owner.Owner.QualifiedTargetId);
            }
            var authority = await AuthorAsync(host, targetIds, false, cancellationToken);
            if (!authority.Allowed) return Reject(authority);
            if (replay)
            {
                var retained = await HasOutcomeAsync(operation, "record", value.Id, request.ExpectedRevision,
                    ContentHash.Of(value.Id, value.SourceId, value.Title, value.Content, value.MetadataJson), cancellationToken);
                return retained ? null : Unavailable("INFORMATION_RECEIPT_INCONSISTENT");
            }
            var result = await store.WriteRecordConditionallyAsync(value, request.ExpectedRevision, operation.Id, cancellationToken);
            if (result.Status == "rejected") return Failed(result.ErrorCode, result.ErrorMessage);
            operation.GuardEvidenceJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(authority.Evidence));
            await BindOutcomeAsync(operation, "record", value.Id, result.Record!.Revision, cancellationToken);
            return null;
        }, cancellationToken);

    private async Task<StandingGrantDecision> AuthorAsync(InteractionInvocationHost host, IReadOnlyList<string> ids,
        bool requireApplicationOwned, CancellationToken cancellationToken)
    {
        var resolved = new List<StandingGrantDefinitionTarget>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var target = await targets.ResolveCurrentAsync(host, id, CatalogNamespaceKinds.InformationSource, cancellationToken);
            if (target.Status != StandingGrantTargetResolutionStatus.Available || target.Target is null)
                return AuthorDenied(host, target.Code);
            resolved.Add(target.Target);
        }
        var decision = await grants.EvaluateAsync(host,
            new(StandingGrantCapability.Author, StandingGrantScope.Application, resolved, []), cancellationToken);
        if (decision.Allowed && requireApplicationOwned && decision.Grant?.Definitions.Mode != StandingGrantDefinitionMode.ApplicationOwned)
            return AuthorDenied(host, "INFORMATION_SOURCE_CREATE_REQUIRES_APPLICATION_OWNED_GRANT");
        return decision;
    }

    private async Task<InteractionInvocationResult> ExecuteAsync(InteractionInvocationHost host, string kind, object? request,
        Func<Operation, bool, Task<InteractionInvocationResult?>> action, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Failed("INFORMATION_CONTEXT_NOT_CLEAN");
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        var opened = false;
        try
        {
            var app = host.ApplicationRevision.ApplicationId;
            var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                principal = host.Principal.PrincipalId, applicationId = app.Value, host.CommandId, kind, request
            }));
            if (Encoding.UTF8.GetByteCount(canonical) > 65536) return Failed("INVALID_PAYLOAD");
            var operationId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                "dantes-roleplay/information-command/v1\n" + host.Principal.PrincipalId + "\n" + app.Value + "\n" + host.CommandId)))[..32].ToLowerInvariant();
            var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/information-command/v1", canonical);
            await db.Database.OpenConnectionAsync(cancellationToken); opened = true;
            await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var registered = applications.Get(app);
            if (app.IsSystem || registered is null || registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Failed("INFORMATION_APPLICATION_STALE");
            var prior = await db.Operations.SingleOrDefaultAsync(x => x.Id == operationId, cancellationToken);
            if (prior is not null && (prior.Tool != "information-conditional" || !prior.Success
                    || prior.Subject != app.Value || prior.ProjectionJson != canonical)) return Failed("INFORMATION_COMMAND_CONFLICT");
            var operation = prior ?? await operations.RecordAsync("information-conditional", "Conditional information write.", true,
                subject: app.Value, projectionJson: canonical, guardEvidenceJson: "{}", id: operationId, cancellationToken: cancellationToken);
            var error = await action(operation, prior is not null);
            if (error is not null) return error;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return InteractionInvocationResult.Committed(new(operationId, fingerprint, []));
        }
        catch (OperationCanceledException) { return InteractionInvocationResult.Cancelled("INFORMATION_WRITE_CANCELLED", "Reconcile the command receipt before retrying."); }
        catch (Exception) { return Unavailable("INFORMATION_WRITE_UNAVAILABLE"); }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray()) entry.State = EntityState.Detached;
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    private async Task BindOutcomeAsync(Operation operation, string kind, string id, int revision, CancellationToken ct)
    {
        var retained = await db.Set<InformationContentRevisionRecord>().AsNoTracking()
            .SingleAsync(value => value.Kind == kind && value.Id == id && value.Revision == revision, ct);
        using var authorization = JsonDocument.Parse(operation.GuardEvidenceJson!);
        operation.GuardEvidenceJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            authorization = authorization.RootElement,
            outcome = new { kind, id, revision, retained.ContentFingerprint }
        }));
    }

    private async Task<bool> HasOutcomeAsync(Operation operation, string kind, string id, int expected, string desiredHash, CancellationToken ct)
    {
        using var guard = JsonDocument.Parse(operation.GuardEvidenceJson ?? "{}");
        if (!guard.RootElement.TryGetProperty("outcome", out var outcome)
            || outcome.GetProperty("kind").GetString() != kind || outcome.GetProperty("id").GetString() != id
            || !outcome.GetProperty("revision").TryGetInt32(out var revision)
            || revision != expected && revision != expected + 1) return false;
        var retained = await db.Set<InformationContentRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Kind == kind && value.Id == id && value.Revision == revision, ct);
        if (retained is null || retained.ContentFingerprint != outcome.GetProperty("ContentFingerprint").GetString()
            || retained.ContentFingerprint != InteractionCanonicalJson.Fingerprint(
                "dantes-roleplay/information-content-revision/v1", retained.ContentJson)) return false;
        using var content = JsonDocument.Parse(retained.ContentJson);
        return content.RootElement.GetProperty("ContentHash").GetString() == desiredHash;
    }

    private static InteractionInvocationResult Reject(StandingGrantDecision decision) => decision.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal)
        ? Unavailable(decision.Code) : Failed(decision.Code);
    private static InteractionInvocationResult Failed(string code, string message = "The information command was rejected.") =>
        InteractionInvocationResult.Failed(code, message.Length <= 500 ? message : message[..500]);
    private static InteractionInvocationResult Unavailable(string code) => InteractionInvocationResult.Unavailable(code, "The information command is unavailable.");
    private static StandingGrantDecision AuthorDenied(InteractionInvocationHost host, string code) =>
        new(false, code, null, new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod,
            "information-author", host.ApplicationRevision.ApplicationId.Value, host.CommandId, false, code));
}

internal static class InformationOwnershipTargetRules
{
    internal static async Task<string?> ValidateAsync(DantesRoleplayDbContext db, IApplicationRegistry applications,
        ICatalogNamespaceRegistry namespaces, IApplicationActivationReader activations, ActivatedApplicationCatalogMaterializer catalog,
        ApplicationIdentifier app, string sourceId, string targetId, CancellationToken cancellationToken)
    {
        CatalogNamespaceIdentity.ValidateRecordId(targetId);
        if (app.IsSystem || applications.Get(app) is null || !targetId.StartsWith(app.Value + ".", StringComparison.Ordinal))
            return "INFORMATION_OWNER_APPLICATION_MISMATCH";
        var chain = CatalogNamespaceIdentity.NamespaceChain(targetId).Select(id => namespaces.Get(id)).ToArray();
        if (chain.Length == 0 || chain.Any(value => value is null || !value.IsEnabled
                || value.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed)
            || !chain[^1]!.AllowedKinds.Contains(CatalogNamespaceKinds.InformationSource, StringComparer.Ordinal))
            return "INFORMATION_OWNER_NAMESPACE_DENIED";
        var identity = await db.Set<InformationSourceTargetIdentityRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.QualifiedTargetId == targetId, cancellationToken);
        if (identity is not null && identity.SourceId != sourceId) return "INFORMATION_TARGET_ALREADY_ASSIGNED";
        var activation = activations.Current(app);
        if (activation is not null && catalog.BuildPermissionSnapshot(app, activation).Manifest.Records.Any(value => value.QualifiedId == targetId))
            return "INFORMATION_TARGET_CATALOG_COLLISION";
        return null;
    }
}
