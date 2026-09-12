using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.Authorization;

/// <summary>Lists a bounded, current application read-grant candidate set without selecting authority.</summary>
public sealed class SqliteStandingGrantReadCandidateReader(DantesRoleplayDbContext db) : IStandingGrantReadCandidateReader
{
    public async Task<StandingGrantReadCandidateResult> ReadAsync(TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId, CancellationToken cancellationToken = default) =>
        await ReadAsync(principal, applicationId, StandingGrantScope.Application, null,
            new HashSet<StandingGrantCapability> { StandingGrantCapability.Read }, cancellationToken);

    public async Task<StandingGrantReadCandidateResult> ReadAsync(TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId, StandingGrantScope scope, string? stateSpaceId,
        IReadOnlySet<StandingGrantCapability> requiredCapabilities,
        CancellationToken cancellationToken = default)
    {
        if (principal is null || !principal.Verified || applicationId is null || applicationId.IsSystem)
            return Unavailable("STANDING_GRANT_READ_CANDIDATES_UNAVAILABLE");
        if (!Enum.IsDefined(scope) || requiredCapabilities is null || requiredCapabilities.Count is < 1 or > 2
            || requiredCapabilities.Any(value => !Enum.IsDefined(value))
            || (scope == StandingGrantScope.Application) != (stateSpaceId is null))
            return Unavailable("STANDING_GRANT_READ_CANDIDATES_UNAVAILABLE");
        try
        {
            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var now = DateTime.UtcNow;
            var metadata = await (from record in db.Set<StandingGrantRevisionRecord>().AsNoTracking()
                                  join current in db.Set<StandingGrantCurrentRecord>().AsNoTracking()
                                      on new { record.GrantId, record.Revision } equals new { current.GrantId, current.Revision }
                                  where record.PrincipalReference == principal.PrincipalId && record.ApplicationId == applicationId.Value
                                      && record.Scope == (scope == StandingGrantScope.Application ? "application" : "stateSpace")
                                      && record.StateSpaceId == stateSpaceId && !record.Revoked
                                      && record.ExpiresAtUtc > now
                                  orderby record.GrantId, record.Revision
                                  select new GrantKey(record.GrantId, record.Revision)).Take(33).ToArrayAsync(cancellationToken);
            if (metadata.Length > 32) return Unavailable("STANDING_GRANT_READ_CANDIDATES_LIMIT");
            var grants = new List<StandingGrantRevision>(metadata.Length);
            foreach (var key in metadata)
            {
                var bytes = await PermissionsByteLengthAsync(key, cancellationToken);
                if (bytes is < 0 or > 16000)
                    return Unavailable("STANDING_GRANT_READ_CANDIDATES_INVALID");
                var row = await db.Set<StandingGrantRevisionRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
                    value.GrantId == key.GrantId && value.Revision == key.Revision, cancellationToken);
                if (row is null) return Unavailable("STANDING_GRANT_READ_CANDIDATES_UNAVAILABLE");
                try { grants.Add(SqliteStandingGrantPolicy.Parse(row)); }
                catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException or InteractionContractException)
                { return Unavailable("STANDING_GRANT_READ_CANDIDATES_INVALID"); }
            }
            var candidates = grants.Where(value => requiredCapabilities.All(value.Capabilities.Contains))
                .OrderBy(value => value.Definitions.Mode == StandingGrantDefinitionMode.ExactIds ? 0 : 1)
                .ThenBy(value => value.GrantId, StringComparer.Ordinal).ThenBy(value => value.Revision)
                .ToArray();
            return new(StandingGrantReadCandidateStatus.Available, "STANDING_GRANT_READ_CANDIDATES_AVAILABLE", Array.AsReadOnly(candidates));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable("STANDING_GRANT_READ_CANDIDATES_UNAVAILABLE"); }
    }

    private async Task<long> PermissionsByteLengthAsync(GrantKey key, CancellationToken cancellationToken)
    {
        var command = ((SqliteConnection)db.Database.GetDbConnection()).CreateCommand();
        await using (command)
        {
            command.Transaction = (SqliteTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SELECT length(CAST(\"PermissionsJson\" AS BLOB)) FROM system_standing_grant_revision WHERE \"GrantId\" = $grantId AND \"Revision\" = $revision";
            command.Parameters.AddWithValue("$grantId", key.GrantId);
            command.Parameters.AddWithValue("$revision", key.Revision);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? -1 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static StandingGrantReadCandidateResult Unavailable(string code) =>
        new(StandingGrantReadCandidateStatus.Unavailable, code, Array.Empty<StandingGrantRevision>());

    private sealed record GrantKey(string GrantId, int Revision);
}
