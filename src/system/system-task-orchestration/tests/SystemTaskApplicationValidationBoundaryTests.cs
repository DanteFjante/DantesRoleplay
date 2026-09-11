using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

// Uses the actual SQLite application, authoring, candidate-selection and standing-grant owners.
// Internal DTO construction below supplies no authority and cannot bypass incomplete coverage.
public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Theory]
    [InlineData(true, true, "INNER_VALIDATION_DEPENDENCIES_UNAVAILABLE")]
    [InlineData(true, false, "INNER_VALIDATION_NOT_AUTHORIZED")]
    [InlineData(false, true, "INNER_VALIDATION_NOT_AUTHORIZED")]
    public async Task Durable_validation_requires_actual_read_validate_and_complete_owner_coverage(
        bool read, bool validate, string expected)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        var capabilities = new List<StandingGrantCapability> { StandingGrantCapability.Author };
        if (read) capabilities.Add(StandingGrantCapability.Read);
        if (validate) capabilities.Add(StandingGrantCapability.Validate);
        await SeedGrantAsync(db, capabilities);
        var written = await Service(db, setup).WriteCandidateAsync(
            ApplicationHost(setup, "durable-validation-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleAsync();
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var host = ApplicationHost(setup, "durable-validation-submit");
        var operations = await db.Operations.CountAsync();
        var service = new SystemTaskApplicationValidationService(db, ValidationGate(db, setup), TimeProvider.System);

        var result = await service.SubmitAsync(ValidationProfile(host, candidate),
            written.Receipt!.OperationId, "durable-validation-write");

        Assert.Equal(expected, result.Code);
        Assert.NotEqual(InteractionInvocationResultTag.Pending, result.Tag);
        Assert.Equal(1, host.Budget.RemainingOperations);
        Assert.Equal(operations, await db.Operations.CountAsync());
        await AssertNoValidationStagingAsync(db);
    }

    [Fact]
    public async Task Durable_validation_verifies_exact_causation_and_current_grants_before_admission()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read, StandingGrantCapability.Validate]);
        var written = await Service(db, setup).WriteCandidateAsync(
            ApplicationHost(setup, "validation-causal-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleAsync();
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var host = ApplicationHost(setup, "validation-causal-submit");
        var profile = ValidationProfile(host, candidate);
        var service = new SystemTaskApplicationValidationService(db, ValidationGate(db, setup), TimeProvider.System);

        var wrongCommand = await service.SubmitAsync(profile, written.Receipt!.OperationId, "unrelated-later-command");
        Assert.Equal("INNER_VALIDATION_CAUSATION_UNAVAILABLE", wrongCommand.Code);
        var correct = await service.SubmitAsync(profile, written.Receipt.OperationId, "validation-causal-write");
        Assert.Equal("INNER_VALIDATION_DEPENDENCIES_UNAVAILABLE", correct.Code);
        await RevokeGrantAsync(db);
        var revoked = await service.SubmitAsync(profile, written.Receipt.OperationId, "validation-causal-write");
        Assert.Equal("INNER_VALIDATION_NOT_AUTHORIZED", revoked.Code);
        Assert.Equal(1, host.Budget.RemainingOperations);
        await AssertNoValidationStagingAsync(db);
    }

    [Fact]
    public async Task Validation_read_authority_does_not_imply_validation_control_authority()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var written = await Service(db, setup).WriteCandidateAsync(
            ApplicationHost(setup, "validation-read-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleAsync();
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var host = ApplicationHost(setup, "validation-read");
        await using var boundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default);
        var gate = ValidationGate(db, setup);

        var read = await gate.CheckAsync(host, candidate, false);
        var control = await gate.CheckAsync(host, candidate, true);

        Assert.Null(read.Failure);
        Assert.NotNull(read.ReadGrant);
        Assert.Null(read.ValidateGrant);
        Assert.False(read.Selection!.DependenciesComplete);
        Assert.Equal("INNER_VALIDATION_NOT_AUTHORIZED", control.Failure!.Code);
        Assert.Equal(1, host.Budget.RemainingOperations);
    }

    private static SystemTaskApplicationValidationGate ValidationGate(DantesRoleplayDbContext db, SetupState setup) =>
        new(db, setup.Applications, setup.Activation, setup.Resolver,
            new SqliteStandingGrantPolicy(db, setup.Resolver), TimeProvider.System);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Internally_staged_validation_readback_and_cancel_recheck_actual_current_authority(bool readOnly)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read, StandingGrantCapability.Validate]);
        var written = await Service(db, setup).WriteCandidateAsync(
            ApplicationHost(setup, "validation-control-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleAsync();
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        SystemTaskDurableHandle handle;
        // Deliberately stage an inert fixture directly. Production admission is covered above
        // and remains unavailable; this isolates actual authorization of existing task controls.
        await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, true, default))
        {
            var staged = await boundary.Store.StageEnqueueValidationAsync(
                ValidationProfile(ApplicationHost(setup, "validation-control-staged"), candidate), false,
                null, null, boundary.Connection, boundary.Transaction);
            Assert.Equal(SystemTaskEnqueueDisposition.Created, staged.Disposition);
            handle = staged.Handle!;
            await boundary.CommitAsync();
        }
        if (readOnly) await ReplaceGrantCapabilitiesAsync(db, [StandingGrantCapability.Read]);
        var host = ApplicationHost(setup, "validation-control", grantReference: readOnly ? "grant@2" : "grant@1");
        var service = new SystemTaskApplicationValidationService(db, ValidationGate(db, setup), TimeProvider.System);

        var read = await service.ReadAsync(host, handle);
        var cancelled = await service.CancelAsync(host, handle);

        Assert.Equal(InteractionInvocationResultTag.Completed, read.Tag);
        Assert.DoesNotContain("fixture.validate", read.DataJson ?? "");
        Assert.DoesNotContain("fixture.manual", read.DataJson ?? "");
        Assert.Equal(readOnly ? InteractionInvocationResultTag.Failed : InteractionInvocationResultTag.Cancelled, cancelled.Tag);
        if (readOnly) Assert.Equal("INNER_VALIDATION_NOT_AUTHORIZED", cancelled.Code);
        await using var inspect = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default);
        var retained = await inspect.Store.ReadInTransactionAsync(handle, inspect.Connection, inspect.Transaction);
        Assert.Equal(readOnly ? SystemTaskLifecycleState.Queued : SystemTaskLifecycleState.Cancelled, retained!.State);
        Assert.Equal(!readOnly, retained.CancellationAcknowledged);
    }

    private static SystemInnerWorkerResolvedProfile ValidationProfile(InteractionInvocationHost host,
        ApplicationCandidateReference candidate)
    {
        const string schema = "{\"type\":\"object\"}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema)));
        return new(new(new SystemInnerWorkerSubject.ApplicationCandidateValidation(candidate), host, "{}", schema),
            SystemInnerWorkerCandidateReviewer.ProfileVersion, SystemInnerWorkerCandidateReviewer.Profile, hash,
            [], [], new("fixture.manual", hash), new("fixture.validate", "1", hash), new(toolCalls: 0),
            new("fixture.read", "1", hash));
    }

    private static async Task AssertNoValidationStagingAsync(DantesRoleplayDbContext db)
    {
        foreach (var table in new[] { "system_task_lifecycle", "system_task_root_budget", "system_task_ai_ceiling", "system_task_ai_reservation" })
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM " + table;
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }
}
