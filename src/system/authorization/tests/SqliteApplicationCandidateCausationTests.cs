using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Candidate_causation_proves_only_the_exact_retained_write_command_and_receipt()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        var write = await service.WriteCandidateAsync(ApplicationHost(setup, "causal-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var validation = await service.ValidateAsync(ApplicationHost(setup, "later-validation", InteractionExecutionProfile.Atomic), candidate);
        Assert.Equal(InteractionInvocationResultTag.Committed, validation.Tag);
        var reader = new ApplicationCandidateCausationReader(db, setup.Applications, setup.Activation);

        var evidence = await reader.ReadAsync(candidate, "causal-write", write.Receipt!.OperationId);
        Assert.NotNull(evidence);
        Assert.Equal(candidate, evidence.CandidateRef);
        Assert.Equal("causal-write", evidence.CausalCommandId);
        Assert.Equal(write.Receipt.OperationId, evidence.OperationId);
        Assert.Equal(row.CanonicalCommandFingerprint, evidence.CanonicalCommandFingerprint);
        Assert.Equal(write.Receipt.RequestFingerprint, evidence.ReceiptFingerprint);
        Assert.Null(await reader.ReadAsync(candidate, "later-validation", write.Receipt.OperationId));
        Assert.Null(await reader.ReadAsync(candidate, "later-validation", validation.Receipt!.OperationId));
        Assert.Null(await reader.ReadAsync(candidate, "causal-write", new string('a', 32)));
        Assert.Null(await reader.ReadAsync(candidate with { ContentFingerprint = new string('A', 64) }, "causal-write", write.Receipt.OperationId));
        // An old receipt is immutable causation, never permission to perform a new operation.
        await RevokeGrantAsync(db);
        Assert.Equal(evidence, await reader.ReadAsync(candidate, "causal-write", write.Receipt.OperationId));
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("authorization")]
    public async Task Candidate_causation_rejects_a_corrupted_source_operation(string mutation)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "corrupt-causal-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var operation = await db.Operations.SingleAsync(value => value.Id == write.Receipt!.OperationId);
        MutateCandidateOperation(operation, mutation);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);

        Assert.Null(await new ApplicationCandidateCausationReader(db, setup.Applications, setup.Activation)
            .ReadAsync(candidate, "corrupt-causal-write", write.Receipt!.OperationId));
    }
}
