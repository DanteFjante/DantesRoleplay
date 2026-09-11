using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Read_candidate_reader_returns_only_current_read_application_grants_in_ranked_order()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await AddGrantAsync(db, "app-owned", "app-owned@1", "principal." + new string('a', 64),
            [StandingGrantCapability.Read], StandingGrantDefinitionMode.ApplicationOwned);
        await AddGrantAsync(db, "z-exact", "z-exact@1", "principal." + new string('a', 64),
            [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds);
        await AddGrantAsync(db, "a-exact", "a-exact@1", "principal." + new string('a', 64),
            [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds);
        await AddGrantAsync(db, "revoked", "revoked@1", "principal." + new string('a', 64),
            [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds, revoked: true);
        await AddGrantAsync(db, "state", "state@1", "principal." + new string('a', 64),
            [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds, scope: StandingGrantScope.StateSpace);
        await AddGrantAsync(db, "other", "other@1", "principal." + new string('b', 64),
            [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds);

        var result = await new SqliteStandingGrantReadCandidateReader(db).ReadAsync(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"), Application);

        Assert.Equal(StandingGrantReadCandidateStatus.Available, result.Status);
        Assert.Equal("STANDING_GRANT_READ_CANDIDATES_AVAILABLE", result.Code);
        Assert.Equal(["a-exact", "z-exact", "app-owned"], result.Candidates.Select(value => value.GrantId).ToArray());
    }

    [Fact]
    public async Task Read_candidate_reader_fails_closed_for_overflow_bad_canonical_data_and_utf8_byte_overflow()
    {
        await using var db = fixture.CreateContext();
        for (var index = 0; index < 33; index++)
            await AddGrantAsync(db, $"overflow-{index:D2}", $"overflow-{index:D2}@1", "principal." + new string('a', 64),
                [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds);
        var reader = new SqliteStandingGrantReadCandidateReader(db);
        var principal = TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test");

        var overflow = await reader.ReadAsync(principal, Application);
        Assert.Equal(StandingGrantReadCandidateStatus.Unavailable, overflow.Status);
        Assert.Empty(overflow.Candidates);

        await db.Set<StandingGrantCurrentRecord>().Where(value => value.GrantId.StartsWith("overflow-")).ExecuteDeleteAsync();
        await AddGrantAsync(db, "bad", "bad@1", principal.PrincipalId, [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds);
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints = ON; UPDATE system_standing_grant_revision SET \"ContentFingerprint\" = $fingerprint WHERE \"GrantId\" = 'bad';";
            var fingerprint = command.CreateParameter();
            fingerprint.ParameterName = "$fingerprint";
            fingerprint.Value = new string('a', 64);
            command.Parameters.Add(fingerprint);
            await command.ExecuteNonQueryAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
        var malformed = await reader.ReadAsync(principal, Application);
        Assert.Equal(StandingGrantReadCandidateStatus.Unavailable, malformed.Status);
        Assert.Empty(malformed.Candidates);

        await db.Set<StandingGrantCurrentRecord>().Where(value => value.GrantId == "bad").ExecuteDeleteAsync();
        await AddGrantAsync(db, "bytes", "bytes@1", principal.PrincipalId, [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds);
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints = ON; UPDATE system_standing_grant_revision SET \"PermissionsJson\" = $permissions WHERE \"GrantId\" = 'bytes';";
            var permissions = command.CreateParameter();
            permissions.ParameterName = "$permissions";
            permissions.Value = "{\"x\":\"" + new string('é', 8_001) + "\"}";
            command.Parameters.Add(permissions);
            await command.ExecuteNonQueryAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
        var oversized = await reader.ReadAsync(principal, Application);
        Assert.Equal(StandingGrantReadCandidateStatus.Unavailable, oversized.Status);
        Assert.Empty(oversized.Candidates);
    }

    [Fact]
    public async Task Read_candidate_reader_rejects_unverified_principals_without_returning_candidates()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await AddGrantAsync(db, "read", "read@1", "principal." + new string('a', 64),
            [StandingGrantCapability.Read], StandingGrantDefinitionMode.ExactIds);

        var result = await new SqliteStandingGrantReadCandidateReader(db).ReadAsync(
            TrustedPrincipalContext.Unauthenticated("MISSING_IDENTITY"), Application);

        Assert.Equal(StandingGrantReadCandidateStatus.Unavailable, result.Status);
        Assert.Empty(result.Candidates);
    }

    private static async Task AddGrantAsync(DantesRoleplayDbContext db, string grantId, string reference, string principal,
        IReadOnlyList<StandingGrantCapability> capabilities, StandingGrantDefinitionMode mode, bool revoked = false,
        StandingGrantScope scope = StandingGrantScope.Application)
    {
        var definitions = mode == StandingGrantDefinitionMode.ExactIds
            ? new StandingGrantDefinitionAllowance(mode, ["demo.runtime.inspect"], [])
            : new StandingGrantDefinitionAllowance(mode, [], [new("demo.runtime", false, ["procedure"])]);
        var operationId = "reader-" + grantId;
        var grant = new StandingGrantRevision(reference, grantId, 1, new string('0', 64), principal, Application, scope,
            scope == StandingGrantScope.StateSpace ? "state" : null, capabilities, definitions, [], 1,
            DateTime.UtcNow.AddMinutes(10), revoked, operationId);
        grant = grant with { ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant) };
        db.Add(new Operation { Id = operationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord { GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
            Scope = scope == StandingGrantScope.Application ? "application" : "stateSpace", StateSpaceId = grant.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant), ContentFingerprint = grant.ContentFingerprint,
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc, Revoked = grant.Revoked,
            IssuedByOperationId = operationId });
        db.Add(new StandingGrantCurrentRecord { GrantId = grantId, Revision = 1 });
        await db.SaveChangesAsync();
    }
}
