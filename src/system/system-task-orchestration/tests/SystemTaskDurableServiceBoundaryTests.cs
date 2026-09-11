using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskDurableServiceBoundaryTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ManifestHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DefinitionId = "fixture-app.jobs.procedure-one";
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("fixture-app");

    [Fact]
    public async Task Submit_owns_authorization_transaction_and_returns_pending_only_after_commit()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();

        var result = await fixture.Service.SubmitAsync(Request(fixture, Host(fixture, "command.submit")));

        Assert.Equal(InteractionInvocationResultTag.Pending, result.Tag);
        Assert.True(fixture.Resolver.SawActiveTransaction);
        Assert.True(fixture.Policy.SawActiveTransaction);
        Assert.NotNull(result.TaskHandle);
        await using var second = await fixture.OpenSecondConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(second,
            "SELECT COUNT(*) FROM system_task_lifecycle WHERE task_id = $task", ("$task", result.TaskHandle!.TaskId)));
        Assert.NotNull(await fixture.Store.ReadAsync(result.TaskHandle));
    }

    [Theory]
    [InlineData(StandingGrantTargetResolutionStatus.Denied, "SYSTEM_TASK_NOT_AUTHORIZED")]
    [InlineData(StandingGrantTargetResolutionStatus.Unavailable, "SYSTEM_TASK_TARGET_UNAVAILABLE")]
    public async Task Resolver_refusal_has_no_private_fallback_and_writes_no_job(
        StandingGrantTargetResolutionStatus status, string expectedCode)
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        fixture.Resolver.Status = status;

        var result = await fixture.Service.SubmitAsync(Request(fixture, Host(fixture, "command.resolver")));

        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, fixture.Policy.CallCount);
        Assert.Equal(0L, await fixture.CountTasksAsync());
    }

    [Theory]
    [InlineData(false, "STANDING_GRANT_DENIED", "SYSTEM_TASK_NOT_AUTHORIZED")]
    [InlineData(false, "STANDING_GRANT_UNAVAILABLE", "SYSTEM_TASK_AUTHORIZATION_UNAVAILABLE")]
    [InlineData(true, "STANDING_GRANT_ALLOWED_WITHOUT_EVIDENCE", "SYSTEM_TASK_NOT_AUTHORIZED")]
    public async Task Policy_denial_unavailability_or_missing_verified_grant_writes_no_job(
        bool allowed, string decisionCode, string expectedCode)
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        fixture.Policy.Decide = (host, _, _) => Decision(host, allowed, decisionCode,
            allowed ? null : Grant(host));

        var result = await fixture.Service.SubmitAsync(Request(fixture, Host(fixture, "command.policy")));

        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0L, await fixture.CountTasksAsync());
    }

    [Fact]
    public async Task Stale_state_binding_rolls_back_admission()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var state = fixture.StateSpaces.Get("state.1")!;
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "boundary-test"),
            new ApplicationRevision(App, 2, Hash, []), state.StateSpaceId, "grant.current",
            "command.stale", InteractionStateRevision.From(state), InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, fixture.TimeProvider.GetUtcNow().AddHours(1).UtcDateTime));

        var result = await fixture.Service.SubmitAsync(Request(fixture, host));

        Assert.Equal("INVOCATION_SCOPE_STALE", result.Code);
        Assert.Equal(0, fixture.Resolver.CallCount);
        Assert.Equal(0L, await fixture.CountTasksAsync());
    }

    [Fact]
    public async Task Current_grant_can_differ_from_stored_grant_for_read_and_cancel()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var stored = (await fixture.Store.EnqueueAsync(Request(fixture,
            Host(fixture, "command.grant", grant: "grant.stored")))).Handle!;
        var current = Host(fixture, "command.control", grant: "grant.current");

        var read = await fixture.Service.GetAsync(current, stored);
        var cancel = await fixture.Service.CancelAsync(Host(fixture, "command.cancel", grant: "grant.current"), stored);

        Assert.Equal(InteractionInvocationResultTag.Pending, read.Tag);
        Assert.Equal(InteractionInvocationResultTag.Cancelled, cancel.Tag);
        Assert.All(fixture.Policy.HostGrantReferences, value => Assert.Equal("grant.current", value));
        Assert.True((await fixture.Store.ReadAsync(stored))!.CancellationRequested);
    }

    [Theory]
    [InlineData("get", "missing")]
    [InlineData("get", "principal")]
    [InlineData("get", "state")]
    [InlineData("read", "missing")]
    [InlineData("read", "principal")]
    [InlineData("read", "state")]
    [InlineData("children", "missing")]
    [InlineData("children", "principal")]
    [InlineData("children", "state")]
    [InlineData("cancel", "missing")]
    [InlineData("cancel", "principal")]
    [InlineData("cancel", "state")]
    public async Task Missing_or_wrong_scope_handle_uses_one_nondisclosing_result(
        string operation, string mismatch)
    {
        await using var fixture = await BoundaryFixture.CreateAsync(includeSecondState: true);
        var stored = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.hidden")))).Handle!;
        var handle = mismatch == "missing" ? new SystemTaskDurableHandle("task.missing", "command.missing") : stored;
        var host = mismatch switch
        {
            "principal" => Host(fixture, "command.read", principal: OtherPrincipal),
            "state" => Host(fixture, "command.read", stateSpace: "state.2"),
            _ => Host(fixture, "command.read")
        };

        var result = operation switch
        {
            "get" => await fixture.Service.GetAsync(host, handle),
            "read" => await fixture.Service.ReadAsync(host, handle),
            "children" => await fixture.Service.ListChildrenAsync(host, handle),
            "cancel" => await fixture.Service.CancelAsync(host, handle),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("SYSTEM_TASK_NOT_AUTHORIZED", result.Code);
        Assert.Null(result.DataJson);
        Assert.Null(result.TaskHandle);
        Assert.Equal(0, fixture.Policy.CallCount);
    }

    [Fact]
    public async Task Denied_descendant_cancellation_leaves_the_entire_graph_unchanged()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var root = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.root")))).Handle!;
        var child = (await fixture.Store.EnqueueAsync(Request(fixture,
            Host(fixture, "command.child", parentCommand: root.CommandId)), propagateCancellation: true)).Handle!;
        fixture.Policy.Decide = (host, requirement, call) => call == 2
            ? Decision(host, false, "STANDING_GRANT_DENIED", Grant(host))
            : Decision(host, true, "STANDING_GRANT_ALLOWED", Grant(host));

        var result = await fixture.Service.CancelAsync(Host(fixture, "command.cancel"), root);

        Assert.Equal("SYSTEM_TASK_NOT_AUTHORIZED", result.Code);
        Assert.False((await fixture.Store.ReadAsync(root))!.CancellationRequested);
        Assert.False((await fixture.Store.ReadAsync(child))!.CancellationRequested);
    }

    [Fact]
    public async Task Existing_caller_transaction_is_rejected_without_committing_it()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        await fixture.Db.Database.OpenConnectionAsync();
        await using var callerTransaction = await fixture.Db.Database.BeginTransactionAsync();

        var result = await fixture.Service.SubmitAsync(Request(fixture, Host(fixture, "command.outer")));

        Assert.Equal("SYSTEM_TASK_OUTER_TRANSACTION_ACTIVE", result.Code);
        Assert.Same(callerTransaction, fixture.Db.Database.CurrentTransaction);
        Assert.Equal(0L, await fixture.CountTasksAsync());
        await callerTransaction.RollbackAsync();
    }

    [Fact]
    public async Task Completed_task_without_host_journal_returns_computation_evidence()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var handle = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.complete")))).Handle!;
        var lease = (await fixture.Store.ClaimNextAsync("worker.boundary", TimeSpan.FromMinutes(1)))!;
        Assert.True(await fixture.Store.CompleteAsync(lease, new("{\"ok\":true}", "evidence.complete")));

        var result = await fixture.Service.GetAsync(Host(fixture, "command.read"), handle);

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("{\"ok\":true}", result.DataJson);
        Assert.Equal("evidence.complete", result.CompletionEvidenceReference);
        Assert.Empty(result.PreviousCommits);
    }

    [Fact]
    public async Task Any_host_journal_makes_readback_unavailable_without_forged_receipts()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var handle = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.journal")))).Handle!;
        var lease = (await fixture.Store.ClaimNextAsync("worker.boundary", TimeSpan.FromMinutes(1)))!;
        var call = await fixture.Store.BeginHostCallAsync(lease, "operation.1", "{}");
        Assert.True(await fixture.Store.CompleteHostCallAsync(lease, call.OperationId,
            call.RequestFingerprint, "{\"committed\":true}"));
        Assert.True(await fixture.Store.CompleteAsync(lease, new("{\"ok\":true}", "evidence.complete")));

        var result = await fixture.Service.GetAsync(Host(fixture, "command.read"), handle);

        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Equal("SYSTEM_TASK_COMMIT_EVIDENCE_UNAVAILABLE", result.Code);
        Assert.Null(result.Receipt);
        Assert.Empty(result.PreviousCommits);
    }

    [Fact]
    public async Task Deadline_elapsing_during_policy_rolls_back_admission()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var host = Host(fixture, "command.deadline", deadline: fixture.TimeProvider.GetUtcNow().AddSeconds(1));
        fixture.Policy.Decide = (current, _, _) =>
        {
            fixture.TimeProvider.Advance(TimeSpan.FromSeconds(2));
            return Decision(current, true, "STANDING_GRANT_ALLOWED", Grant(current));
        };

        var result = await fixture.Service.SubmitAsync(Request(fixture, host));

        Assert.Equal(InteractionInvocationResultTag.Cancelled, result.Tag);
        Assert.Equal(0L, await fixture.CountTasksAsync());
    }

    [Fact]
    public async Task Historical_selection_unavailable_does_not_substitute_current_target_or_cancel()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var old = new SystemTaskSelectedDefinition(DefinitionId, 1, Hash);
        var handle = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.old"), old))).Handle!;
        fixture.Resolver.Status = StandingGrantTargetResolutionStatus.Unavailable;

        var read = await fixture.Service.GetAsync(Host(fixture, "command.read"), handle);
        var cancel = await fixture.Service.CancelAsync(Host(fixture, "command.cancel"), handle);

        Assert.Equal("SYSTEM_TASK_TARGET_UNAVAILABLE", read.Code);
        Assert.Equal("SYSTEM_TASK_TARGET_UNAVAILABLE", cancel.Code);
        Assert.All(fixture.Resolver.Selections, value =>
        {
            Assert.Equal(old.ExactDefinitionId, value.DefinitionId);
            Assert.Equal(old.Version, value.Revision);
            Assert.Equal(old.Fingerprint, value.ContentFingerprint);
        });
        Assert.Equal(0, fixture.Resolver.ResolveCurrentCallCount);
        Assert.False((await fixture.Store.ReadAsync(handle))!.CancellationRequested);
    }

    [Fact]
    public async Task Readback_exposes_bounded_status_without_authority_payload_or_runtime_state()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var request = new SystemTaskDurableSubmissionRequest(
            Host(fixture, "command.status", grant: "grant.stored"), new(DefinitionId, 1, Hash),
            "{\"secretInput\":true}", new("checkpoint.one", "handler.one", "correlation.one",
                "{\"secretCheckpoint\":true}"));
        var handle = (await fixture.Store.EnqueueAsync(request)).Handle!;
        Assert.NotNull(await fixture.Store.ClaimNextAsync("worker.secret", TimeSpan.FromMinutes(1)));

        var result = await fixture.Service.ReadAsync(Host(fixture, "command.read"), handle);
        var status = JsonSerializer.Deserialize<SystemTaskDurableReadback>(result.DataJson!, WebJson)!;

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.StartsWith("task-readback.", result.CompletionEvidenceReference, StringComparison.Ordinal);
        Assert.Equal("running", status.Phase);
        Assert.Equal(1, status.AttemptCount);
        Assert.False(status.CompletionAvailable);
        Assert.Equal("checkpoint.one", status.CheckpointName);
        Assert.DoesNotContain("secretInput", result.DataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secretCheckpoint", result.DataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Principal, result.DataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("grant.stored", result.DataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("lease", result.DataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stateRevision", result.DataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readback_completion_is_available_only_without_a_host_call_journal()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var clean = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.clean")))).Handle!;
        var cleanLease = (await fixture.Store.ClaimNextAsync("worker.clean", TimeSpan.FromMinutes(1)))!;
        Assert.True(await fixture.Store.CompleteAsync(cleanLease, new("{\"value\":1}", "evidence.clean")));
        var journaled = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.journaled")))).Handle!;
        var journaledLease = (await fixture.Store.ClaimNextAsync("worker.journaled", TimeSpan.FromMinutes(1)))!;
        var call = await fixture.Store.BeginHostCallAsync(journaledLease, "operation.readback", "{}");
        Assert.True(await fixture.Store.CompleteHostCallAsync(journaledLease, call.OperationId,
            call.RequestFingerprint, "{\"done\":true}"));
        Assert.True(await fixture.Store.CompleteAsync(journaledLease, new("{\"value\":2}", "evidence.journaled")));

        var cleanResult = await fixture.Service.ReadAsync(Host(fixture, "command.read-clean"), clean);
        var journaledResult = await fixture.Service.ReadAsync(Host(fixture, "command.read-journal"), journaled);
        var cleanStatus = JsonSerializer.Deserialize<SystemTaskDurableReadback>(cleanResult.DataJson!, WebJson)!;
        var journaledStatus = JsonSerializer.Deserialize<SystemTaskDurableReadback>(journaledResult.DataJson!, WebJson)!;

        Assert.True(cleanStatus.CompletionAvailable);
        Assert.Equal(["evidence.clean"], cleanStatus.EvidenceReferences);
        Assert.Equal("completed", journaledStatus.Phase);
        Assert.False(journaledStatus.CompletionAvailable);
        Assert.Equal("SYSTEM_TASK_COMMIT_EVIDENCE_UNAVAILABLE", journaledStatus.DiagnosticCode);
        Assert.True(journaledStatus.RecoveryRequired);
        Assert.Empty(journaledStatus.EvidenceReferences);
        Assert.Empty(journaledResult.PreviousCommits);
    }

    [Fact]
    public async Task Child_readback_includes_only_authorized_direct_children_and_never_dependencies()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var dependency = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.dependency")))).Handle!;
        var parent = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.parent")))).Handle!;
        var visible = (await fixture.Store.EnqueueAsync(Request(fixture,
            Host(fixture, "command.visible", parentCommand: parent.CommandId)))).Handle!;
        var denied = (await fixture.Store.EnqueueAsync(Request(fixture,
            Host(fixture, "command.denied", parentCommand: parent.CommandId)))).Handle!;
        var childRequest = Request(fixture, Host(fixture, "command.grandchild", parentCommand: visible.CommandId));
        _ = await fixture.Store.EnqueueAsync(new(childRequest.InvocationHost, childRequest.SelectedDefinition,
            childRequest.InputJson, dependencyHandles: [dependency]));
        fixture.Policy.Decide = (host, requirement, _) => requirement.Task?.Handle == denied
            ? Decision(host, false, "STANDING_GRANT_DENIED", Grant(host))
            : Decision(host, true, "STANDING_GRANT_ALLOWED", Grant(host));

        var result = await fixture.Service.ListChildrenAsync(Host(fixture, "command.list"), parent);
        var readback = JsonSerializer.Deserialize<SystemTaskDurableChildrenReadback>(result.DataJson!, WebJson)!;

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal(parent, readback.Parent);
        Assert.Equal([visible], readback.Children.Select(value => value.Handle));
        Assert.DoesNotContain(readback.Children, value => value.Handle == dependency);
    }

    [Fact]
    public async Task Unavailable_child_ownership_makes_the_whole_list_unavailable_without_data()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var parent = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.parent")))).Handle!;
        var historical = new SystemTaskSelectedDefinition("fixture-app.jobs.historical", 1, Hash);
        _ = await fixture.Store.EnqueueAsync(Request(fixture,
            Host(fixture, "command.historical", parentCommand: parent.CommandId), historical));
        fixture.Resolver.StatusFor = selection => selection.DefinitionId == historical.ExactDefinitionId
            ? StandingGrantTargetResolutionStatus.Unavailable
            : StandingGrantTargetResolutionStatus.Available;

        var result = await fixture.Service.ListChildrenAsync(Host(fixture, "command.list"), parent);

        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Equal("SYSTEM_TASK_TARGET_UNAVAILABLE", result.Code);
        Assert.Null(result.DataJson);
    }

    [Fact]
    public async Task Child_readback_is_bounded_to_sixteen_and_rejects_oversize_retained_graph()
    {
        await using var fixture = await BoundaryFixture.CreateAsync();
        var parent = (await fixture.Store.EnqueueAsync(Request(fixture, Host(fixture, "command.parent")))).Handle!;
        var children = new List<SystemTaskDurableHandle>();
        for (var index = 0; index < 16; index++)
            children.Add((await fixture.Store.EnqueueAsync(Request(fixture,
                Host(fixture, $"command.child.{index:00}", parentCommand: parent.CommandId)))).Handle!);

        var bounded = await fixture.Service.ListChildrenAsync(Host(fixture, "command.list.16"), parent);
        var readback = JsonSerializer.Deserialize<SystemTaskDurableChildrenReadback>(bounded.DataJson!, WebJson)!;
        Assert.Equal(16, readback.Children.Count);

        await using (var connection = await fixture.OpenSecondConnectionAsync())
            await CloneLifecycleRowAsync(connection, children[0], "task.overflow", "command.overflow");
        var overflow = await fixture.Service.ListChildrenAsync(Host(fixture, "command.list.17"), parent);

        Assert.Equal(InteractionInvocationResultTag.Unavailable, overflow.Tag);
        Assert.Equal("SYSTEM_TASK_GRAPH_UNAVAILABLE", overflow.Code);
        Assert.Null(overflow.DataJson);
    }

    private const string OtherPrincipal =
        "principal.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static SystemTaskDurableSubmissionRequest Request(BoundaryFixture fixture,
        InteractionInvocationHost host, SystemTaskSelectedDefinition? selection = null) =>
        new(host, selection ?? new(DefinitionId, 1, Hash), "{}");

    private static InteractionInvocationHost Host(BoundaryFixture fixture, string command,
        string grant = "grant.current", string principal = Principal, string stateSpace = "state.1",
        string? parentCommand = null, DateTimeOffset? deadline = null)
    {
        var state = fixture.StateSpaces.Get(stateSpace)!;
        return new(TrustedPrincipalContext.VerifiedPrincipal(principal, "boundary-test"),
            state.ApplicationRevision, stateSpace, grant, command, InteractionStateRevision.From(state),
            InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16,
                (deadline ?? fixture.TimeProvider.GetUtcNow().AddHours(1)).UtcDateTime), parentCommand);
    }

    private static StandingGrantRevision Grant(InteractionInvocationHost host) => new(
        host.GrantReference, "grant.family", 1, Hash, host.Principal.PrincipalId,
        host.ApplicationRevision.ApplicationId, StandingGrantScope.StateSpace, host.StateSpaceId,
        [StandingGrantCapability.Execute, StandingGrantCapability.ReadTask, StandingGrantCapability.CancelTask],
        new(StandingGrantDefinitionMode.ExactIds, [DefinitionId], []), [], 16,
        host.Budget.DeadlineUtc, false, "operation.issuer");

    private static StandingGrantDecision Decision(InteractionInvocationHost host, bool allowed,
        string code, StandingGrantRevision? grant) => new(allowed, code, grant,
        new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, "boundary-test",
            host.StateSpaceId, host.CommandId, allowed, code));

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task CloneLifecycleRowAsync(SqliteConnection connection,
        SystemTaskDurableHandle source, string taskId, string commandId)
    {
        var columns = new List<string>();
        await using (var info = connection.CreateCommand())
        {
            info.CommandText = "SELECT name FROM pragma_table_info('system_task_lifecycle') ORDER BY cid";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        }
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var selections = columns.Select(column => column switch
        {
            "task_id" => "$task",
            "command_id" => "$command",
            _ => Quote(column)
        });
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO system_task_lifecycle ({string.Join(',', columns.Select(Quote))}) "
            + $"SELECT {string.Join(',', selections)} FROM system_task_lifecycle WHERE task_id = $source";
        command.Parameters.AddWithValue("$task", taskId);
        command.Parameters.AddWithValue("$command", commandId);
        command.Parameters.AddWithValue("$source", source.TaskId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class BoundaryFixture : IAsyncDisposable
    {
        private readonly SystemTaskLifecycleSchemaFixture schema;

        private BoundaryFixture(SystemTaskLifecycleSchemaFixture schema, DantesRoleplayDbContext db,
            BoundaryOnlyStateSpaceRegistry stateSpaces, BoundaryOnlyStandingGrantPolicy policy,
            BoundaryOnlyStandingGrantTargetResolver resolver)
        {
            this.schema = schema;
            Db = db;
            StateSpaces = stateSpaces;
            Policy = policy;
            Resolver = resolver;
            Store = schema.CreateStore();
            Service = new(db, policy, resolver, stateSpaces, schema.TimeProvider);
        }

        internal DantesRoleplayDbContext Db { get; }
        internal MutableTimeProvider TimeProvider => schema.TimeProvider;
        internal SqliteSystemTaskLifecycleStore Store { get; }
        internal SqliteSystemTaskDurableService Service { get; }
        internal BoundaryOnlyStateSpaceRegistry StateSpaces { get; }
        internal BoundaryOnlyStandingGrantPolicy Policy { get; }
        internal BoundaryOnlyStandingGrantTargetResolver Resolver { get; }

        internal static async Task<BoundaryFixture> CreateAsync(bool includeSecondState = false)
        {
            var schema = await SystemTaskLifecycleSchemaFixture.CreateAsync();
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite(schema.ConnectionString).Options;
            var db = new DantesRoleplayDbContext(options);
            var states = new BoundaryOnlyStateSpaceRegistry(includeSecondState);
            var policy = new BoundaryOnlyStandingGrantPolicy(db);
            var resolver = new BoundaryOnlyStandingGrantTargetResolver(db);
            return new(schema, db, states, policy, resolver);
        }

        internal async Task<long> CountTasksAsync()
        {
            await using var connection = await OpenSecondConnectionAsync();
            return await ScalarAsync(connection, "SELECT COUNT(*) FROM system_task_lifecycle");
        }

        internal async Task<SqliteConnection> OpenSecondConnectionAsync()
        {
            var connection = new SqliteConnection(schema.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await schema.DisposeAsync();
        }
    }

    private sealed class BoundaryOnlyStateSpaceRegistry : IStateSpaceRegistry
    {
        private readonly IReadOnlyDictionary<string, StateSpaceView> values;

        internal BoundaryOnlyStateSpaceRegistry(bool includeSecondState)
        {
            var first = State("state.1", 1);
            var states = new List<StateSpaceView> { first };
            if (includeSecondState) states.Add(State("state.2", 1));
            values = states.ToDictionary(value => value.StateSpaceId, StringComparer.Ordinal);
        }

        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => values.GetValueOrDefault(stateSpaceId);
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId,
            string? afterStateSpaceId, int limit) => new(values.Values.ToArray(), null);

        private static StateSpaceView State(string id, int bindingRevision) => new(id,
            new ApplicationRevision(App, 1, Hash, []), ManifestHash, bindingRevision,
            DateTime.UnixEpoch, DateTime.UnixEpoch);
    }

    private sealed class BoundaryOnlyStandingGrantTargetResolver(DantesRoleplayDbContext db)
        : IStandingGrantTargetResolver
    {
        internal StandingGrantTargetResolutionStatus Status { get; set; } = StandingGrantTargetResolutionStatus.Available;
        internal Func<StandingGrantDefinitionReference, StandingGrantTargetResolutionStatus>? StatusFor { get; set; }
        internal bool SawActiveTransaction { get; private set; }
        internal int CallCount { get; private set; }
        internal int ResolveCurrentCallCount { get; private set; }
        internal List<StandingGrantDefinitionReference> Selections { get; } = [];

        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
        {
            CallCount++;
            SawActiveTransaction |= db.Database.CurrentTransaction is not null;
            Selections.Add(selection);
            var status = StatusFor?.Invoke(selection) ?? Status;
            var target = status == StandingGrantTargetResolutionStatus.Available
                ? new StandingGrantDefinitionTarget(selection.DefinitionId, selection.Kind, App,
                    CatalogNamespaceIdentity.NamespaceOf(selection.DefinitionId), "ownership.fixture",
                    selection.Revision, selection.ContentFingerprint)
                : null;
            return Task.FromResult(new StandingGrantTargetResolution(status, "BOUNDARY_TARGET_" + status, target));
        }

        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
            string exactDefinitionId, string kind, CancellationToken cancellationToken = default)
        {
            ResolveCurrentCallCount++;
            return Task.FromResult(new StandingGrantTargetResolution(
                StandingGrantTargetResolutionStatus.Unavailable, "BOUNDARY_CURRENT_TARGET_UNAVAILABLE", null));
        }

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BoundaryOnlyStandingGrantPolicy(DantesRoleplayDbContext db) : IStandingGrantPolicy
    {
        internal bool SawActiveTransaction { get; private set; }
        internal int CallCount { get; private set; }
        internal List<string> HostGrantReferences { get; } = [];
        internal Func<InteractionInvocationHost, StandingGrantRequirement, int, StandingGrantDecision> Decide { get; set; }
            = (host, _, _) => Decision(host, true, "STANDING_GRANT_ALLOWED", Grant(host));

        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
            StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
        {
            CallCount++;
            SawActiveTransaction |= db.Database.CurrentTransaction is not null;
            HostGrantReferences.Add(host.GrantReference);
            return Task.FromResult(Decide(host, requirement, CallCount));
        }
    }
}
