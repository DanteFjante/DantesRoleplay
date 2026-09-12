using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Explicitly adopts or moves an existing source using current installation-operator membership.</summary>
public sealed class SqliteInformationSourceOwnershipAdministration(DantesRoleplayDbContext db,
    IInstallationOperatorMembershipPolicy membership, IApplicationRegistry applications,
    ICatalogNamespaceRegistry namespaces, IApplicationActivationReader activations,
    ActivatedApplicationCatalogMaterializer catalog, IOperationLog operations) : IInformationSourceOwnershipAdministration
{
    public async Task<InteractionInvocationResult> BindAsync(InteractionInvocationHost host,
        InformationSourceOwnershipWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (!host.Principal.Verified) return Failed("INFORMATION_OWNER_PRINCIPAL_UNVERIFIED");
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Failed("INFORMATION_CONTEXT_NOT_CLEAN");
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        if (request is null || string.IsNullOrWhiteSpace(request.SourceId) || request.SourceId.Length > 200
            || request.NewApplicationId is null || request.QualifiedTargetId is null
            || request.ExpectedOwnershipRevision is < 0 or int.MaxValue
            || (request.ExpectedOwnershipRevision == 0 ? request.ExpectedOwnershipFingerprint is not null
                : request.ExpectedOwnershipFingerprint is not { Length: 64 } hash || !hash.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F')))
            return Failed("INVALID_PAYLOAD");
        var opened = false;
        try
        {
            var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                principal = host.Principal.PrincipalId, applicationId = host.ApplicationRevision.ApplicationId.Value,
                host.CommandId, request
            }));
            if (Encoding.UTF8.GetByteCount(canonical) > 65536) return Failed("INVALID_PAYLOAD");
            var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/information-ownership-command/v1", canonical);
            var operationId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                "dantes-roleplay/information-ownership-command/v1\n" + host.Principal.PrincipalId + "\n"
                + host.ApplicationRevision.ApplicationId.Value + "\n" + host.CommandId)))[..32].ToLowerInvariant();
            await db.Database.OpenConnectionAsync(cancellationToken); opened = true;
            await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var member = await membership.EvaluateAsync(host.Principal, cancellationToken);
            if (member.Status != InstallationOperatorMembershipStatus.Member)
                return member.Status == InstallationOperatorMembershipStatus.Unavailable ? Unavailable(member.Code) : Failed(member.Code);
            var currentApplication = applications.Get(host.ApplicationRevision.ApplicationId);
            if (currentApplication is null || currentApplication.Revision != host.ApplicationRevision.Revision
                || currentApplication.Fingerprint != host.ApplicationRevision.Fingerprint
                || !currentApplication.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Failed("INFORMATION_APPLICATION_STALE");
            if (!await db.Set<InformationSource>().AnyAsync(value => value.Id == request.SourceId, cancellationToken))
                return Failed("INFORMATION_SOURCE_NOT_FOUND");
            var error = await InformationOwnershipTargetRules.ValidateAsync(db, applications, namespaces, activations,
                catalog, request.NewApplicationId, request.SourceId, request.QualifiedTargetId, cancellationToken);
            if (error is not null) return Failed(error);
            var priorOperation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken);
            if (priorOperation is not null)
            {
                if (priorOperation.Tool != "information-source-owner" || !priorOperation.Success
                    || priorOperation.Subject != request.SourceId || priorOperation.ProjectionJson != canonical)
                    return Failed("INFORMATION_COMMAND_CONFLICT");
                var rows = await db.Set<InformationSourceOwnerRevisionRecord>().AsNoTracking()
                    .Where(value => value.BoundByOperationId == operationId).Take(2).ToArrayAsync(cancellationToken);
                if (rows.Length != 1 || rows[0].SourceId != request.SourceId
                    || rows[0].ApplicationId != request.NewApplicationId.Value || rows[0].QualifiedTargetId != request.QualifiedTargetId
                    || rows[0].Revision != request.ExpectedOwnershipRevision + 1
                    || rows[0].PreviousFingerprint != request.ExpectedOwnershipFingerprint
                    || rows[0].ContentFingerprint != InformationSourceOwnership.Fingerprint(rows[0]))
                    return Unavailable("INFORMATION_OWNER_RECEIPT_INCONSISTENT");
            }
            else
            {
                var current = await InformationSourceOwnership.ReadAsync(db, request.SourceId, cancellationToken);
                var hasCurrent = await db.Set<InformationSourceOwnerCurrentRecord>().AnyAsync(value => value.SourceId == request.SourceId, cancellationToken);
                if (hasCurrent && current is null) return Unavailable("INFORMATION_SOURCE_OWNER_UNAVAILABLE");
                if ((current?.Owner.Revision ?? 0) != request.ExpectedOwnershipRevision
                    || current?.Owner.ContentFingerprint != request.ExpectedOwnershipFingerprint)
                    return Failed("INFORMATION_OWNER_REVISION_CONFLICT");
                await operations.RecordAsync("information-source-owner", "Explicit information source ownership transition.", true,
                    subject: request.SourceId, projectionJson: canonical,
                    guardEvidenceJson: InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                    {
                        principal = host.Principal.PrincipalId, host.Principal.AuthenticationMethod, membership = member.Code
                    })), id: operationId, cancellationToken: cancellationToken);
                var retained = await InformationSourceOwnership.AppendAsync(db, request.SourceId, request.NewApplicationId.Value,
                    request.QualifiedTargetId, operationId, current, cancellationToken);
                var readback = await InformationSourceOwnership.ReadAsync(db, request.SourceId, cancellationToken);
                if (readback?.Owner.ContentFingerprint != retained.ContentFingerprint)
                    return Unavailable("INFORMATION_OWNER_WRITE_INCONSISTENT");
            }
            await transaction.CommitAsync(cancellationToken);
            return InteractionInvocationResult.Committed(new(operationId, fingerprint, []));
        }
        catch (OperationCanceledException) { return InteractionInvocationResult.Cancelled("INFORMATION_OWNER_CANCELLED", "Reconcile the command receipt before retrying."); }
        catch (Exception) { return Unavailable("INFORMATION_OWNER_UNAVAILABLE"); }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray()) entry.State = EntityState.Detached;
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    private static InteractionInvocationResult Failed(string code) => InteractionInvocationResult.Failed(code, "The ownership transition was rejected.");
    private static InteractionInvocationResult Unavailable(string code) => InteractionInvocationResult.Unavailable(code, "The ownership transition is unavailable.");
}
