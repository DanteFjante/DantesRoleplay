using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Candidate_validation_retains_unavailable_pipeline_diagnostics_and_inspection_exposes_them()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "validation-write", InteractionExecutionProfile.Atomic), Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);

        var validation = await service.ValidateAsync(Host(setup, "validation", InteractionExecutionProfile.Atomic), candidate);
        var inspected = await service.InspectAsync(Host(setup, "validation-inspect"), new(row.CandidateId, row.Revision));
        var persisted = Assert.Single(await db.Set<ApplicationCandidateValidationRecord>().ToArrayAsync());

        Assert.Equal(InteractionInvocationResultTag.Committed, validation.Tag);
        Assert.Equal("unavailable", persisted.Outcome);
        Assert.False(persisted.DependenciesComplete);
        Assert.Equal("[]", persisted.DependenciesJson);
        Assert.Null(persisted.PreparationVersion);
        Assert.Equal(InteractionInvocationResultTag.Completed, inspected.Tag);
        using var json = JsonDocument.Parse(inspected.DataJson!);
        Assert.Equal("unavailable", json.RootElement.GetProperty("validation").GetProperty("Outcome").GetString());
    }

    [Fact]
    public async Task Candidate_validation_replay_reuses_its_receipt_without_another_validation_ledger()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "validation-replay-write", InteractionExecutionProfile.Atomic), Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);

        var first = await service.ValidateAsync(Host(setup, "validation-replay", InteractionExecutionProfile.Atomic), candidate);
        WriteProcedure("Later active generation.");
        await ActivateAsync(setup, setup.Activation.Current(Application)!.ActivationFingerprint);
        var replay = await service.ValidateAsync(Host(setup, "validation-replay", InteractionExecutionProfile.Atomic), candidate);

        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Single(await db.Set<ApplicationCandidateValidationRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Candidate_validation_requires_current_validate_authority_and_exact_candidate_pin()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "validation-denied-write", InteractionExecutionProfile.Atomic), Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);

        var denied = await service.ValidateAsync(Host(setup, "validation-denied", InteractionExecutionProfile.Atomic), candidate);
        var tampered = await service.ValidateAsync(Host(setup, "validation-tampered", InteractionExecutionProfile.Atomic), candidate with { ContentFingerprint = new string('A', 64) });

        Assert.Equal(InteractionInvocationResultTag.Failed, denied.Tag);
        Assert.Equal("STANDING_GRANT_DENIED", denied.Code);
        Assert.Equal(InteractionInvocationResultTag.Failed, tampered.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_NOT_FOUND", tampered.Code);
        Assert.Empty(await db.Set<ApplicationCandidateValidationRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Revoked_validate_grant_denies_validation_replay_without_a_second_ledger()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "validation-revoked-write", InteractionExecutionProfile.Atomic), Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        _ = await service.ValidateAsync(Host(setup, "validation-revoked", InteractionExecutionProfile.Atomic), candidate);
        await RevokeGrantAsync(db);

        var replay = await service.ValidateAsync(Host(setup, "validation-revoked", InteractionExecutionProfile.Atomic), candidate);

        Assert.Equal(InteractionInvocationResultTag.Failed, replay.Tag);
        Assert.Equal("STANDING_GRANT_NOT_CURRENT", replay.Code);
        Assert.Single(await db.Set<ApplicationCandidateValidationRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Tampered_validation_diagnostics_cannot_replay_a_prior_receipt()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "validation-tamper-write", InteractionExecutionProfile.Atomic), Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var candidateRow = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, candidateRow.CandidateId, candidateRow.Revision, candidateRow.ContentFingerprint);
        _ = await service.ValidateAsync(Host(setup, "validation-tamper", InteractionExecutionProfile.Atomic), candidate);
        var validation = await db.Set<ApplicationCandidateValidationRecord>().SingleAsync();
        validation.DiagnosticsJson = "[]";
        await db.SaveChangesAsync();

        var replay = await service.ValidateAsync(Host(setup, "validation-tamper", InteractionExecutionProfile.Atomic), candidate);

        Assert.Equal(InteractionInvocationResultTag.Unavailable, replay.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", replay.Code);
    }
}
