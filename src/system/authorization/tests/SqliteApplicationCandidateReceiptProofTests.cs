using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Theory]
    [InlineData("subject")]
    [InlineData("authorization")]
    [InlineData("selectedDefinitions")]
    [InlineData("commandFingerprint")]
    [InlineData("extra")]
    [InlineData("missing")]
    public async Task Candidate_write_receipt_rejects_mutated_operation_proof_on_replay_and_inspect(string mutation)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        var request = Request(setup.Activation.Current(Application)!.ActivationFingerprint);
        var first = await service.WriteCandidateAsync(ApplicationHost(setup, "proof-write", InteractionExecutionProfile.Atomic), request);
        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var operation = await db.Operations.SingleAsync(value => value.Id == first.Receipt!.OperationId);
        MutateCandidateOperation(operation, mutation);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var replay = await service.WriteCandidateAsync(ApplicationHost(setup, "proof-write", InteractionExecutionProfile.Atomic), request);
        var byCandidate = await service.InspectAsync(ApplicationHost(setup, "proof-inspect"), new(row.CandidateId, row.Revision));
        var byReceipt = await service.InspectAsync(ApplicationHost(setup, "proof-inspect-receipt"), new(null, 0, first.Receipt!.OperationId));

        Assert.NotEqual(InteractionInvocationResultTag.Committed, replay.Tag);
        Assert.Null(replay.Receipt);
        Assert.NotEqual(InteractionInvocationResultTag.Completed, byCandidate.Tag);
        Assert.NotEqual(InteractionInvocationResultTag.Completed, byReceipt.Tag);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("authorization")]
    [InlineData("selectedDefinitions")]
    [InlineData("commandFingerprint")]
    [InlineData("extra")]
    [InlineData("missing")]
    public async Task Candidate_validation_receipt_rejects_mutated_operation_proof(string mutation)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(ApplicationHost(setup, "proof-validation-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var first = await service.ValidateAsync(ApplicationHost(setup, "proof-validation", InteractionExecutionProfile.Atomic), candidate);
        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        var operation = await db.Operations.SingleAsync(value => value.Id == first.Receipt!.OperationId);
        MutateCandidateOperation(operation, mutation);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var replay = await service.ValidateAsync(ApplicationHost(setup, "proof-validation", InteractionExecutionProfile.Atomic), candidate);
        var inspected = await service.InspectAsync(ApplicationHost(setup, "proof-validation-inspect"), new(row.CandidateId, row.Revision));

        Assert.NotEqual(InteractionInvocationResultTag.Committed, replay.Tag);
        Assert.Null(replay.Receipt);
        Assert.Equal(InteractionInvocationResultTag.Completed, inspected.Tag);
        using var json = JsonDocument.Parse(inspected.DataJson!);
        var validation = json.RootElement.GetProperty("validation");
        // A corrupted operation may be omitted or surfaced as inconsistent; it cannot retain its
        // original actionable diagnostics merely because the candidate itself remains readable.
        if (validation.ValueKind != JsonValueKind.Null)
        {
            Assert.Equal("unavailable", validation.GetProperty("Outcome").GetString());
            Assert.Contains("EVIDENCE_INCONSISTENT", validation.GetProperty("DiagnosticsJson").GetString());
        }
        Assert.Single(await db.Set<ApplicationCandidateValidationRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Candidate_receipt_keeps_original_author_proof_when_a_different_authorized_principal_inspects()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        var request = Request(setup.Activation.Current(Application)!.ActivationFingerprint);
        var write = await service.WriteCandidateAsync(ApplicationHost(setup, "original-author", InteractionExecutionProfile.Atomic), request);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        _ = await service.ValidateAsync(ApplicationHost(setup, "original-validator", InteractionExecutionProfile.Atomic), candidate);
        var original = SqliteStandingGrantPolicy.Parse(await db.Set<StandingGrantRevisionRecord>().AsNoTracking().SingleAsync());
        var reader = original with { GrantId = "reader", GrantReference = "reader@1", PrincipalReference = "principal." + new string('b', 64),
            Capabilities = [StandingGrantCapability.Read], IssuedByOperationId = "reader-operation" };
        db.Add(new Operation { Id = reader.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord { GrantId = reader.GrantId, Revision = reader.Revision, GrantReference = reader.GrantReference,
            PrincipalReference = reader.PrincipalReference, ApplicationId = Application.Value, Scope = "application", StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(reader), ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(reader),
            MaximumOperations = reader.MaximumOperations, ExpiresAtUtc = reader.ExpiresAtUtc, Revoked = false, IssuedByOperationId = reader.IssuedByOperationId });
        db.Add(new StandingGrantCurrentRecord { GrantId = reader.GrantId, Revision = reader.Revision });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var readerHost = InteractionInvocationHost.ForApplication(TrustedPrincipalContext.VerifiedPrincipal(reader.PrincipalReference, "different-reader-method"),
            ApplicationHost(setup).ApplicationRevision, reader.GrantReference, "other-reader", InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

        var inspected = await service.InspectAsync(readerHost, new(null, 0, write.Receipt!.OperationId));
        Assert.Equal(InteractionInvocationResultTag.Completed, inspected.Tag);
        using var json = JsonDocument.Parse(inspected.DataJson!);
        Assert.Equal(write.Receipt.OperationId, json.RootElement.GetProperty("sourceOperationId").GetString());
        Assert.DoesNotContain("EVIDENCE_INCONSISTENT", inspected.DataJson);

        var changedMethod = InteractionInvocationHost.ForApplication(TrustedPrincipalContext.VerifiedPrincipal(original.PrincipalReference, "changed-method"),
            ApplicationHost(setup).ApplicationRevision, original.GrantReference, "original-author", InteractionExecutionProfile.Atomic,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));
        var replay = await service.WriteCandidateAsync(changedMethod, request);
        Assert.Equal(InteractionInvocationResultTag.Failed, replay.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_COMMAND_CONFLICT", replay.Code);
    }

    private static void MutateCandidateOperation(Operation operation, string mutation)
    {
        if (mutation == "subject") { operation.Subject = "another-application"; return; }
        var guard = JsonNode.Parse(operation.GuardEvidenceJson!)!.AsObject();
        switch (mutation)
        {
            case "authorization": guard["authorization"]!["AuthenticationMethod"] = "forged-method"; break;
            case "selectedDefinitions": guard["selectedDefinitions"] = new JsonArray(); break;
            case "commandFingerprint": guard["commandFingerprint"] = new string('A', 64); break;
            case "extra": guard["unbound"] = true; break;
            case "missing": guard.Remove("authorization"); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        operation.GuardEvidenceJson = InteractionCanonicalJson.CanonicalizeObject(guard.ToJsonString());
    }
}
