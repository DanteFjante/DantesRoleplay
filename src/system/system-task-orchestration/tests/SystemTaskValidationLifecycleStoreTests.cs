using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskValidationLifecycleStoreTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Schema = "{\"type\":\"object\"}";

    [Fact]
    public async Task Validation_staging_persists_closed_shape_and_claim_rehydrates_it()
    {
        await using var fixture = await ValidationFixture.CreateAsync();
        var profile = fixture.Profile("validation.shape");

        var result = await fixture.StageAsync(profile);
        var snapshot = await fixture.Store.ReadAsync(result.Handle!);
        var lease = await fixture.Store.ClaimNextAsync("validation.worker", TimeSpan.FromMinutes(5));

        Assert.Equal(SystemTaskEnqueueDisposition.Created, result.Disposition);
        Assert.Equal(0, profile.Worker.InvocationHost.Budget.RemainingOperations);
        Assert.NotNull(snapshot);
        Assert.Equal(SystemTaskPurpose.ApplicationValidation, snapshot!.Request.Purpose);
        Assert.Null(snapshot.Request.SelectedDefinition);
        Assert.Throws<InvalidDataException>(() => snapshot.Request.WorkflowDefinition);
        Assert.Null(snapshot.Request.Invocation.StateSpaceId);
        Assert.Null(snapshot.Request.Invocation.StateRevision);
        Assert.Equal(fixture.Candidate, snapshot.Request.Candidate);
        Assert.Null(snapshot.Request.Causation);
        Assert.Equal(InteractionExecutionProfile.ReadOnly, snapshot.Request.Invocation.Profile);
        Assert.NotNull(lease);
        Assert.Equal(snapshot.Request.Handle, lease!.Request.Handle);
        Assert.Equal(fixture.Candidate, lease.Request.Candidate);
    }

    [Fact]
    public async Task Replay_and_conflict_are_resolved_before_a_second_budget_transfer()
    {
        await using var fixture = await ValidationFixture.CreateAsync();
        var profile = fixture.Profile("validation.replay");
        var created = await fixture.StageAsync(profile);
        Assert.Equal(0, profile.Worker.InvocationHost.Budget.RemainingOperations);

        var replay = await fixture.StageAsync(profile);
        var changed = fixture.Profile("validation.replay", input: "{\"changed\":true}");
        var conflict = await fixture.StageAsync(changed);

        Assert.Equal(SystemTaskEnqueueDisposition.Existing, replay.Disposition);
        Assert.Equal(created.Handle, replay.Handle);
        Assert.Equal(SystemTaskEnqueueDisposition.Conflict, conflict.Disposition);
        Assert.Equal(4, changed.Worker.InvocationHost.Budget.RemainingOperations);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM system_task_lifecycle"));
    }

    [Fact]
    public async Task Durable_validation_child_uses_persisted_ledger_and_requires_the_same_candidate_graph()
    {
        await using var fixture = await ValidationFixture.CreateAsync();
        var root = await fixture.StageAsync(fixture.Profile("validation.root"));
        var childProfile = fixture.Profile("validation.child", parentCommand: root.Handle!.CommandId);

        var child = await fixture.StageAsync(childProfile);
        var otherCandidate = fixture.Candidate with { CandidateId = new string('b', 32) };
        var rejectedProfile = fixture.Profile("validation.wrong-candidate", parentCommand: root.Handle.CommandId,
            candidate: otherCandidate);
        var rejected = await fixture.StageAsync(rejectedProfile);

        Assert.Equal(SystemTaskEnqueueDisposition.Created, child.Disposition);
        Assert.Equal(4, childProfile.Worker.InvocationHost.Budget.RemainingOperations);
        Assert.Equal(SystemTaskEnqueueDisposition.Rejected, rejected.Disposition);
        Assert.Equal("SYSTEM_TASK_PARENT_SCOPE_MISMATCH", rejected.Code);
        Assert.Equal(4, rejectedProfile.Worker.InvocationHost.Budget.RemainingOperations);
        Assert.Equal(root.Handle.TaskId, await fixture.StringAsync(
            "SELECT parent_task_id FROM system_task_lifecycle WHERE task_id=$task", ("$task", child.Handle!.TaskId)));
        Assert.Equal(root.Handle.TaskId, await fixture.StringAsync(
            "SELECT root_task_id FROM system_task_lifecycle WHERE task_id=$task", ("$task", child.Handle.TaskId)));
    }

    [Fact]
    public async Task Validation_admission_proof_and_causation_corruption_fail_closed()
    {
        await using var fixture = await ValidationFixture.CreateAsync();
        var firstOperation = new string('1', 32);
        var secondOperation = new string('2', 32);
        await fixture.ExecuteAsync("INSERT INTO operation(Id) VALUES($first),($second)",
            ("$first", firstOperation), ("$second", secondOperation));
        var staged = await fixture.StageAsync(fixture.Profile("validation.cause"), firstOperation, "candidate.write");
        var handle = staged.Handle!;
        var read = await fixture.Store.ReadAsync(handle);
        Assert.Equal(new SystemTaskValidationCausation(firstOperation, "candidate.write"), read!.Request.Causation);

        await fixture.ExecuteAsync("UPDATE system_task_lifecycle SET causation_operation_id=$cause WHERE task_id=$task",
            ("$cause", secondOperation), ("$task", handle.TaskId));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ReadAsync(handle));

        var inputStaged = await fixture.StageAsync(fixture.Profile("validation.input-proof"));
        await fixture.ExecuteAsync("UPDATE system_task_lifecycle SET input_json='{}' WHERE task_id=$task",
            ("$task", inputStaged.Handle!.TaskId));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ReadAsync(inputStaged.Handle));
    }

    [Fact]
    public async Task Validation_ai_enrollment_reservation_dispatch_and_accounting_use_the_closed_subject()
    {
        await using var fixture = await ValidationFixture.CreateAsync();
        var profile = fixture.Profile("validation.ai");
        var staged = await fixture.InTransactionAsync(async (connection, transaction) =>
        {
            var enqueue = await fixture.Store.StageEnqueueValidationAsync(profile, false, null, null,
                connection, transaction);
            var enrollment = await fixture.Store.StageEnrollAiBudgetAsync(enqueue.Handle!, profile, connection, transaction);
            Assert.True(enrollment.Accepted, enrollment.Code);
            return enqueue;
        });
        Assert.Equal("application-validation|<null>", await fixture.StringAsync("""
            SELECT task_purpose || '|' || COALESCE(definition_id,'<null>')
            FROM system_task_ai_ceiling WHERE task_id=$task
            """, ("$task", staged.Handle!.TaskId)));
        var lease = (await fixture.Store.ClaimNextAsync("validation.ai.worker", TimeSpan.FromMinutes(5)))!;

        var check = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.CheckAiInvocationAsync(lease, profile, connection, transaction));
        var reservation = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.StageReserveAiBudgetAsync(new(profile.Worker.InvocationHost, staged.Handle, lease.Attempt,
                "provider.validation", profile.AiBudget, 100, 0), connection, transaction));
        var call = new AiProviderCallDescriptor("provider.1", 0,
            new("model.1", [new(AiMessageRole.User, "review")], AiRequestKind.Task,
                AiReasoningEffort.None, Schema, [], null, 128));
        var dispatch = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.StageRecordAiProviderDispatchAsync(reservation.Reservation!.RecordReference,
                lease.Attempt, profile, call, connection, transaction));
        var outcome = new AiProviderCallObservation(AiDispatchCompletionKind.Returned,
            new(true, null, "ok", "{}", [], Usage: new(20, 10, 30, true)));
        var accounted = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.StageRecordAiProviderOutcomeAsync(reservation.Reservation!.RecordReference,
                lease.Attempt, outcome, connection, transaction));
        var wrong = fixture.Profile("validation.ai", candidate: fixture.Candidate with { CandidateId = new string('b', 32) });
        var wrongCheck = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.CheckAiInvocationAsync(lease, wrong, connection, transaction));

        Assert.True(check.Accepted, check.Code);
        Assert.True(reservation.Accepted, reservation.Code);
        Assert.True(dispatch.Accepted, dispatch.Code);
        Assert.True(accounted.Accepted, accounted.Code);
        Assert.Equal(30, accounted.Usage!.ChargedProviderTokens);
        Assert.False(wrongCheck.Accepted);
        Assert.Equal("INNER_AI_INVOCATION_SCOPE_MISMATCH", wrongCheck.Code);
    }

    [Fact]
    public async Task Validation_ai_enrollment_must_match_the_original_admission_profile_proof()
    {
        await using var fixture = await ValidationFixture.CreateAsync();
        var admitted = fixture.Profile("validation.enrollment-proof");
        var staged = await fixture.StageAsync(admitted);
        var changedSchema = fixture.Profile("validation.enrollment-proof",
            schema: "{\"type\":\"object\",\"required\":[]}");
        var changedManual = fixture.Profile("validation.enrollment-proof", manualFingerprint: new string('B', 64));
        var changedContext = fixture.Profile("validation.enrollment-proof", context: ["context.changed"]);
        var changedBudget = fixture.Profile("validation.enrollment-proof",
            aiBudget: new SystemInnerWorkerAiBudget(100, 0));

        foreach (var changed in new[] { changedSchema, changedManual, changedContext, changedBudget })
        {
            var rejected = await fixture.InTransactionAsync((connection, transaction) =>
                fixture.Store.StageEnrollAiBudgetAsync(staged.Handle!, changed, connection, transaction));
            Assert.False(rejected.Accepted);
            Assert.Equal("INNER_AI_ENROLLMENT_ADMISSION_MISMATCH", rejected.Code);
        }
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM system_task_ai_ceiling"));
        var accepted = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.StageEnrollAiBudgetAsync(staged.Handle!, admitted, connection, transaction));
        Assert.True(accepted.Accepted, accepted.Code);
    }

    [Fact]
    public async Task Validation_ai_invocation_check_requires_the_current_fence_and_observes_cancellation_without_debiting()
    {
        await using var fixture = await ValidationFixture.CreateAsync();
        var profile = fixture.Profile("validation.ai-check");
        var staged = await fixture.InTransactionAsync(async (connection, transaction) =>
        {
            var enqueue = await fixture.Store.StageEnqueueValidationAsync(profile, false, null, null,
                connection, transaction);
            Assert.True((await fixture.Store.StageEnrollAiBudgetAsync(enqueue.Handle!, profile,
                connection, transaction)).Accepted);
            return enqueue;
        });
        var lease = (await fixture.Store.ClaimNextAsync("validation.check.worker", TimeSpan.FromMinutes(5)))!;
        var consumed = await fixture.ScalarAsync(
            "SELECT consumed_operations FROM system_task_root_budget WHERE root_task_id=$task",
            ("$task", staged.Handle!.TaskId));
        var forgedAttempt = new SystemTaskAttemptIdentity(lease.Attempt.StableCommandId, lease.Attempt.AttemptId,
            lease.Attempt.LeaseToken, lease.Attempt.FencingCounter + 1, lease.Attempt.LeaseExpiresAtUtc);
        var forged = lease with { Attempt = forgedAttempt };

        var stale = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.CheckAiInvocationAsync(forged, profile, connection, transaction));
        Assert.False(stale.Accepted);
        Assert.Equal("INNER_AI_LEASE_STALE", stale.Code);
        Assert.Equal(consumed, await fixture.ScalarAsync(
            "SELECT consumed_operations FROM system_task_root_budget WHERE root_task_id=$task",
            ("$task", staged.Handle.TaskId)));

        await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.StageCancellationAsync(staged.Handle, false, connection, transaction));
        var cancelled = await fixture.InTransactionAsync((connection, transaction) =>
            fixture.Store.CheckAiInvocationAsync(lease, profile, connection, transaction));
        Assert.False(cancelled.Accepted);
        Assert.Equal("INNER_AI_LEASE_STALE", cancelled.Code);
        Assert.Equal(consumed, await fixture.ScalarAsync(
            "SELECT consumed_operations FROM system_task_root_budget WHERE root_task_id=$task",
            ("$task", staged.Handle.TaskId)));
    }

    private sealed class ValidationFixture : IAsyncDisposable
    {
        private readonly SystemTaskAiAccountingSchemaFixture schema;
        private readonly MutableTimeProvider time;

        private ValidationFixture(SystemTaskAiAccountingSchemaFixture schema)
        {
            this.schema = schema;
            time = new(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
            Store = new(schema.ConnectionString, time);
            Candidate = new(ApplicationIdentifier.Parse("fixture-app"), new string('a', 32), 1, Hash);
        }

        internal SqliteSystemTaskLifecycleStore Store { get; }
        internal ApplicationCandidateReference Candidate { get; }
        internal static async Task<ValidationFixture> CreateAsync() => new(await SystemTaskAiAccountingSchemaFixture.CreateAsync());

        internal SystemInnerWorkerResolvedProfile Profile(string command, string? input = null,
            string? parentCommand = null, ApplicationCandidateReference? candidate = null,
            string? schema = null, string? manualFingerprint = null, IReadOnlyList<string>? context = null,
            SystemInnerWorkerAiBudget? aiBudget = null)
        {
            var resultSchema = schema ?? Schema;
            var host = InteractionInvocationHost.ForApplication(
                TrustedPrincipalContext.VerifiedPrincipal(
                    "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "fixture"),
                new(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []), "grant.1", command,
                InteractionExecutionProfile.ReadOnly, new(4, time.GetUtcNow().AddHours(1).UtcDateTime), parentCommand);
            var worker = new SystemInnerWorkerRequest(new SystemInnerWorkerSubject.ApplicationCandidateValidation(candidate ?? Candidate),
                host, input ?? "{\"work\":true}", resultSchema);
            return new(worker, SystemInnerWorkerCandidateReviewer.ProfileVersion,
                SystemInnerWorkerCandidateReviewer.Profile, HashOf(worker.ResultSchemaJson), [], context ?? [],
                new("manual.packet.1", manualFingerprint ?? Hash),
                new("authority.validate", "validate-revision.1", Hash), aiBudget ?? new(toolCalls: 0),
                new("authority.read", "read-revision.1", Hash));
        }

        internal Task<SystemTaskEnqueueResult> StageAsync(SystemInnerWorkerResolvedProfile profile,
            string? operationId = null, string? causalCommandId = null) => InTransactionAsync((connection, transaction) =>
            Store.StageEnqueueValidationAsync(profile, false, operationId, causalCommandId, connection, transaction));

        internal async Task<T> InTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action)
        {
            await using var connection = await schema.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await action(connection, transaction);
            await transaction.CommitAsync();
            return result;
        }

        internal async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = await schema.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        internal async Task<string?> StringAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = await schema.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            return Convert.ToString(await command.ExecuteScalarAsync());
        }

        internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = await schema.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync() => schema.DisposeAsync();
        private static string HashOf(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
