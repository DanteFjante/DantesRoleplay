using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization;

/// <summary>Owns one immediate transaction for issuer-authorized immutable grant revisions and their audit receipt.</summary>
public sealed class SqliteStandingGrantAdministration(
    DantesRoleplayDbContext db, IStandingGrantIssuerPolicy issuers, IOperationLog operations) : IStandingGrantAdministration
{
    private const string Tool = "standing-grant-admin";
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<InteractionInvocationResult> MutateAsync(TrustedPrincipalContext issuer,
        StandingGrantMutationRequest request, string commandId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        if (!issuer.Verified) return Failed("STANDING_GRANT_ISSUER_DENIED");
        if (db.Database.CurrentTransaction is not null) return Failed("STANDING_GRANT_OUTER_TRANSACTION");
        if (db.ChangeTracker.HasChanges()) return Failed("STANDING_GRANT_CONTEXT_HAS_PENDING_WRITES");
        var originalEntries = db.ChangeTracker.Entries().Select(value => value.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        var opened = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(request, commandId);
            request = Normalize(request);
            var operationId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                "dantes-roleplay/standing-grant-admin/v1\n" + issuer.PrincipalId + "\n" + commandId)))[..32].ToLowerInvariant();
            var commandJson = Canonical(new { issuer = issuer.PrincipalId, commandId, request });
            var commandFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/standing-grant-command/v1", commandJson);
            var next = Build(request, operationId);
            var transition = new StandingGrantIssuerRequirement(request.Mutation, request.ExpectedCurrentRevision, next);
            StandingGrantContractRules.ValidateIssuerTransition(transition);

            await db.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken);

            // Membership is always current, including retries of already committed commands.
            var authority = await issuers.EvaluateAsync(issuer, transition, commandId, cancellationToken);
            if (!authority.Allowed)
                return authority.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal)
                    ? InteractionInvocationResult.Unavailable(authority.Code, "Installation operator verification is unavailable.")
                    : Failed(authority.Code);
            if (!authority.Evidence.Allowed || authority.Evidence.PrincipalReference != issuer.PrincipalId
                || authority.Evidence.AuthenticationMethod != issuer.AuthenticationMethod
                || authority.Evidence.CorrelationId != commandId)
                return Failed("STANDING_GRANT_ISSUER_EVIDENCE_INVALID");

            var replay = await db.Operations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken);
            if (replay is not null)
            {
                if (!replay.Success || replay.Tool != Tool || replay.Subject != request.GrantId || replay.ProjectionJson != commandJson)
                    return Failed("STANDING_GRANT_COMMAND_CONFLICT");
                var prior = await db.Set<StandingGrantRevisionRecord>().AsNoTracking()
                    .Where(value => value.IssuedByOperationId == operationId).Take(2).ToArrayAsync(cancellationToken);
                if (prior.Length != 1 || prior[0].GrantId != next.GrantId || prior[0].Revision != next.Revision
                    || prior[0].GrantReference != next.GrantReference || prior[0].ContentFingerprint != next.ContentFingerprint
                    || StandingGrantRevisionCanonicalization.RevisionJson(SqliteStandingGrantPolicy.Parse(prior[0])) != StandingGrantRevisionCanonicalization.RevisionJson(next))
                    return InteractionInvocationResult.Unavailable("STANDING_GRANT_RECEIPT_INCONSISTENT", "The stored grant receipt cannot be reconciled.");
                return Receipt(operationId, commandFingerprint);
            }

            if (request.Mutation != StandingGrantIssuerMutation.Revoke && request.ExpiresAtUtc <= DateTime.UtcNow)
                return Failed("STANDING_GRANT_EXPIRY_INVALID");
            if (!await db.Set<ApplicationRegistryRecord>().AsNoTracking().AnyAsync(
                    value => value.Id == request.ApplicationId.Value, cancellationToken))
                return Failed("STANDING_GRANT_APPLICATION_UNKNOWN");
            var current = await db.Set<StandingGrantCurrentRecord>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.GrantId == request.GrantId, cancellationToken);
            if ((current?.Revision ?? 0) != request.ExpectedCurrentRevision)
                return Failed("STANDING_GRANT_CAS_MISMATCH");
            if (current is not null)
            {
                var priorRow = await db.Set<StandingGrantRevisionRecord>().AsNoTracking().SingleOrDefaultAsync(
                    value => value.GrantId == current.GrantId && value.Revision == current.Revision, cancellationToken);
                if (priorRow is null) return Failed("STANDING_GRANT_CURRENT_INCONSISTENT");
                var prior = SqliteStandingGrantPolicy.Parse(priorRow);
                StandingGrantContractRules.ValidateConfiguration(prior);
                if (prior.Revoked) return Failed("STANDING_GRANT_TERMINAL");
                if (request.Mutation == StandingGrantIssuerMutation.Revoke && StandingGrantRevisionCanonicalization.BindingJson(prior) != StandingGrantRevisionCanonicalization.BindingJson(next))
                    return Failed("STANDING_GRANT_REVOKE_BINDINGS_CHANGED");
            }

            db.Add(new StandingGrantRevisionRecord
            {
                GrantId = next.GrantId, Revision = next.Revision, GrantReference = next.GrantReference,
                PrincipalReference = next.PrincipalReference, ApplicationId = next.ApplicationId.Value,
                Scope = next.Scope == StandingGrantScope.Application ? "application" : "stateSpace",
                StateSpaceId = next.StateSpaceId, PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(next), ContentFingerprint = next.ContentFingerprint,
                MaximumOperations = next.MaximumOperations, ExpiresAtUtc = next.ExpiresAtUtc, Revoked = next.Revoked,
                IssuedByOperationId = operationId
            });
            // Existing operation owner saves both records inside this enlisted transaction.
            await operations.RecordAsync(Tool, "Recorded standing-grant revision.", true, subject: request.GrantId,
                projectionJson: commandJson, guardEvidenceJson: Canonical(authority.Evidence), id: operationId,
                cancellationToken: cancellationToken);
            // The legacy audit owner can salvage its audit after a failed companion save.
            // A successful audit alone therefore cannot prove this immutable revision was stored.
            var saved = await db.Set<StandingGrantRevisionRecord>().AsNoTracking().SingleOrDefaultAsync(
                value => value.GrantId == next.GrantId && value.Revision == next.Revision, cancellationToken);
            if (saved is null || StandingGrantRevisionCanonicalization.RevisionJson(SqliteStandingGrantPolicy.Parse(saved))
                    != StandingGrantRevisionCanonicalization.RevisionJson(next))
                return InteractionInvocationResult.Unavailable("STANDING_GRANT_WRITE_INCONSISTENT",
                    "The grant revision could not be retained with its audit receipt.");
            if (current is null)
            {
                db.Add(new StandingGrantCurrentRecord { GrantId = next.GrantId, Revision = next.Revision });
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (await db.Set<StandingGrantCurrentRecord>()
                .Where(value => value.GrantId == current.GrantId && value.Revision == request.ExpectedCurrentRevision)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Revision, next.Revision), cancellationToken) != 1)
                return Failed("STANDING_GRANT_CAS_MISMATCH");
            await transaction.CommitAsync(cancellationToken);
            return Receipt(operationId, commandFingerprint);
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled("STANDING_GRANT_CANCELLED", "Grant administration was cancelled; reconcile the same command before retrying.");
        }
        catch (InteractionContractException exception) { return Failed(exception.Code); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or OverflowException)
        { return Failed("INVALID_STANDING_GRANT_REQUEST"); }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException)
        {
            return InteractionInvocationResult.Unavailable("STANDING_GRANT_STORAGE_UNAVAILABLE",
                "The command could not be reconciled. Retry this same command identity.");
        }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray())
                if (!originalEntries.Contains(entry.Entity)) entry.State = EntityState.Detached;
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    private static void Validate(StandingGrantMutationRequest request, string commandId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (commandId is not { Length: > 0 and <= InteractionContractLimits.IdempotencyKey }
            || commandId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-'))
            || request.GrantId is not { Length: > 0 and <= 160 } || request.GrantId.Any(char.IsWhiteSpace)
            || request.ExpectedCurrentRevision is < 0 or int.MaxValue
            || !TrustedPrincipalContext.IsValidPrincipalId(request.PrincipalReference)
            || request.ApplicationId is null || request.ExpiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The grant request is invalid.");
        // Validate all collections before normalizing them, including duplicate rejection.
        StandingGrantContractRules.ValidateIssuerTransition(new(request.Mutation, request.ExpectedCurrentRevision,
            Build(request, new string('0', 32))));
    }

    private static StandingGrantMutationRequest Normalize(StandingGrantMutationRequest request) => request with
    {
        Capabilities = request.Capabilities.Order().ToArray(), EffectKinds = request.EffectKinds.Order(StringComparer.Ordinal).ToArray(),
        Definitions = request.Definitions with
        {
            ExactIds = request.Definitions.ExactIds.Order(StringComparer.Ordinal).ToArray(),
            ApplicationOwnedNamespaces = request.Definitions.ApplicationOwnedNamespaces.OrderBy(value => value.NamespaceId, StringComparer.Ordinal)
                .Select(value => value with { DefinitionKinds = value.DefinitionKinds.Order(StringComparer.Ordinal).ToArray() }).ToArray()
        }
    };

    private static StandingGrantRevision Build(StandingGrantMutationRequest request, string operationId)
    {
        var revision = checked(request.ExpectedCurrentRevision + 1);
        var grant = new StandingGrantRevision($"grant:{request.GrantId}:{revision}", request.GrantId, revision, new string('0', 64),
            request.PrincipalReference, request.ApplicationId, request.Scope, request.StateSpaceId,
            request.Capabilities, request.Definitions, request.EffectKinds, request.MaximumOperations, request.ExpiresAtUtc,
            request.Mutation == StandingGrantIssuerMutation.Revoke, operationId);
        return grant with { ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant) };
    }
    private static string Canonical<T>(T value) => InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(value, Wire));
    private static InteractionInvocationResult Failed(string code) => InteractionInvocationResult.Failed(code, "The standing-grant mutation was not authorized or its precondition was not met.");
    private static InteractionInvocationResult Receipt(string operationId, string fingerprint) =>
        InteractionInvocationResult.Committed(new(operationId, fingerprint, [], EffectDetailsAvailable: false));
}
