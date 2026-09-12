using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using DantesRoleplay.TriggerScheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public void Procedure_worker_host_policy_matches_system_capability_governance_identity()
    {
        var applications = new InMemoryApplicationRegistry();
        var application = applications.Register(new(Application, "Fixture", "Fixture application.", []));
        var principal = TrustedPrincipalContext.VerifiedPrincipal(
            "principal." + new string('a', 64), "test");
        var host = new InteractionInvocationHost(principal, application, "state", "grant@1",
            "worker-command", "state-revision", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));
        var request = new SystemInnerWorkerRequest(host,
            new("system.inspect", 1, new string('A', 64)),
            "{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"Inspect the registry.\"}",
            "{\"type\":\"object\"}");
        var catalog = new SystemCapabilityCatalog(
            [new ApplicationsSystemCapabilityHandler(applications)],
            new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy());
        var context = new SystemCapabilityInvocationContext(principal,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope, "inner-worker-policy");

        var selection = new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
            new("web.inner", "Inner AI", "Perform the bounded host-selected procedure.")
        ]), catalog).Resolve(request, context, DateTime.UtcNow);

        var binding = Assert.Single(selection.ToolBindings);
        Assert.Equal(SystemCapabilityIds.Applications, binding.CapabilityVersion.ExactDefinitionId);
        Assert.Equal(SystemCapabilityMode.Read, binding.Mode);
        Assert.Equal("system_applications", binding.Definition.Name);
    }

    [Fact]
    public async Task Procedure_worker_and_due_trigger_routes_use_the_registered_durable_runner()
    {
        await using var database = await InnerWorkerDatabase.CreateAsync();
        var db = database.Db;
        var setup = Setup(db);
        await ActivateAsync(setup);
        var activation = setup.Activation.Current(Application)!;
        var application = setup.Applications.Get(Application)!;
        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var state = stateSpaces.Create(new("state", application, activation.ActivationFingerprint,
            activation.ResolutionFingerprint));
        await SeedInnerWorkerGrantAsync(db);
        var procedureFile = ProcedureFile.Parse(File.ReadAllText(Path.Combine(root,
            RelativePath.Replace('/', Path.DirectorySeparatorChar))), RelativePath);
        var procedure = await new ProcedureStore(db).WriteAsync(new WriteProcedureRequest
        {
            Id = procedureFile.Id, Category = procedureFile.Category, Name = procedureFile.Name,
            Description = procedureFile.Description, Governs = procedureFile.Governs,
            Matches = procedureFile.Matches, Instructions = "obsolete manual instructions",
            Constraints = procedureFile.Constraints, Status = procedureFile.Status,
            CreatedBy = "fixture", ChangeNote = "Focused worker fixture."
        });
        var deadline = DateTime.UtcNow.AddMinutes(2);
        const string ephemeralParent = "workflow-service.command";
        var host = InnerWorkerHost(application, state, "inner-worker-command", deadline,
            ephemeralParent);
        StandingGrantTargetResolution target;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            target = await setup.Resolver.ResolveCurrentAsync(host, procedure.Procedure.Id, "procedure");
            await transaction.CommitAsync();
        }
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, target.Status);
        Assert.NotEqual(procedure.Procedure.SourceHash, target.Target!.ContentFingerprint);
        var selected = new SystemTaskSelectedDefinition(target.Target.DefinitionId,
            target.Target.Revision, target.Target.ContentFingerprint);

        var catalog = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
            setup.Sources, setup.Roots, setup.Extensions).UsePreparationCache(
                new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority());
        var snapshots = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), catalog,
            new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)), setup.Activation);
        var retrieval = new InteractionFeatureRetriever(snapshots, namespaces: setup.Namespaces,
            changes: setup.Activation);
        var interactionAuthority = new PrivateHostInteractionAuthorizationPolicy(stateSpaces);
        var contexts = new InteractionTaskContextMaterializer(interactionAuthority, retrieval,
            snapshots, new UnusedReadModels());
        var resolver = new SystemInnerWorkerProcedureResolver(
            new InteractionEnvelopeFactory(setup.Applications, setup.Activation, stateSpaces, interactionAuthority),
            contexts, snapshots, new SystemInnerWorkerPreparation(new ProcedureStore(db), contexts),
            new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
                new("web.inner", "Inner AI", "Perform the bounded host-selected procedure.")
            ])), TimeProvider.System);
        var standingPolicy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var durable = new SqliteSystemTaskDurableService(db, standingPolicy, setup.Resolver,
            stateSpaces, TimeProvider.System);
        var service = new SystemInnerWorkerService(resolver, durable);
        var selectedApplicationOwner = new SelectedApplicationInnerWorkerOwner(
            db, stateSpaces, () => service, TimeProvider.System);
        var selectedApplicationCatalog = new SystemCapabilityCatalog(
            [
                new SelectedApplicationInnerWorkerReadCapabilityHandler(selectedApplicationOwner),
                new SelectedApplicationInnerWorkerReadCapabilityHandler(
                    SystemCapabilityIds.InnerWorkerList, selectedApplicationOwner),
                new SelectedApplicationInnerWorkerReadCapabilityHandler(
                    SystemCapabilityIds.InnerWorkerWait, selectedApplicationOwner)
            ],
            new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy(),
            [
                new SelectedApplicationInnerWorkerWriteCapabilityHandler(
                    SystemCapabilityIds.InnerWorkerSubmit, selectedApplicationOwner),
                new SelectedApplicationInnerWorkerWriteCapabilityHandler(
                    SystemCapabilityIds.InnerWorkerCancel, selectedApplicationOwner)
            ]);
        var gateway = new ApplicationCandidateCapabilityGateway(selectedApplicationCatalog);
        var provider = new SuccessfulProcedureProvider(procedureFile.Instructions, procedureFile.Constraints);
        var systemAi = new SystemAiAgentService([], new AiService([provider]));
        var invoker = new SystemInnerWorkerProcedureInvoker(systemAi);
        var lifecycleServices = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(durable)
            .AddSingleton(resolver)
            .AddSingleton(invoker)
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .AddSingleton<SystemTaskAiInvocationLifecycleFactory>()
            .AddScoped<SystemInnerWorkerProcedureExecutor>()
            .AddSingleton<SystemTaskWorkflowBackgroundWorker>()
            .BuildServiceProvider();
        using (lifecycleServices)
        {
            const string schema = """
                {"additionalProperties":false,"properties":{"answer":{"type":"string"},"summary":{"type":"string"}},"required":["answer","summary"],"type":"object"}
                """;
            var assignment = JsonSerializer.Serialize(new
            {
                format = SystemInnerWorkerAssignmentV1.Format,
                instruction = "Inspect the active runtime definition and return a short answer."
            });
            var ordinary = await service.SubmitAsync(new(
                InnerWorkerHost(application, state, "inner-worker-ordinary", deadline,
                    ephemeralParent), selected, assignment, schema));
            Assert.Equal(InteractionInvocationResultTag.Failed, ordinary.Tag);
            Assert.Equal("SYSTEM_TASK_PARENT_UNKNOWN", ordinary.Code);

            var transportInput = JsonSerializer.Serialize(new
            {
                stateSpaceId = state.StateSpaceId,
                procedure = new
                {
                    definitionId = selected.ExactDefinitionId,
                    revision = selected.Version,
                    contentFingerprint = selected.Fingerprint
                },
                instruction = "Inspect the active runtime definition and return a short answer.",
                resultSchema = schema,
                dependencyHandles = Array.Empty<object>()
            });
            var submitted = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, transportInput, "inner-worker-gateway",
                "website");
            Assert.True(submitted.Ok, submitted.Error?.Code + ": " + submitted.Error?.Message);
            var pending = submitted.Data!.Value;
            Assert.Equal("pending", pending.GetProperty("tag").GetString());
            var pendingHandle = pending.GetProperty("pending");
            var handle = new SystemTaskDurableHandle(
                pendingHandle.GetProperty("taskId").GetString()!,
                pendingHandle.GetProperty("commandId").GetString()!);
            var admitted = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .SingleAsync(value => value.TaskId == handle.TaskId);
            Assert.Null(admitted.ParentTaskId);
            Assert.Equal(admitted.TaskId, admitted.RootTaskId);
            Assert.Equal(submitted.OperationId, admitted.ParentCommandId);
            Assert.Contains("\"originatingParentMode\":\"ephemeral-root\"",
                admitted.AdmissionPayloadJson, StringComparison.Ordinal);

            var cancelTarget = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, transportInput, "inner-worker-cancel-target",
                "website");
            Assert.True(cancelTarget.Ok, cancelTarget.Error?.Message);
            var cancelPending = cancelTarget.Data!.Value.GetProperty("pending");
            var cancelHandle = new SystemTaskDurableHandle(
                cancelPending.GetProperty("taskId").GetString()!,
                cancelPending.GetProperty("commandId").GetString()!);
            var cancelled = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerCancel, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    taskId = cancelHandle.TaskId,
                    commandId = cancelHandle.CommandId
                }), "inner-worker-cancel", "website");
            Assert.True(cancelled.Ok, cancelled.Error?.Message);
            Assert.Equal("cancelled", cancelled.Data!.Value.GetProperty("tag").GetString());

            var firstList = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerList, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    pageSize = 1,
                    cursor = (string?)null
                }), null, "website-list-first");
            Assert.True(firstList.Ok, firstList.Error?.Message);
            using var firstListData = JsonDocument.Parse(firstList.Data!.Value.GetProperty("dataJson").GetString()!);
            var firstListItem = Assert.Single(firstListData.RootElement.GetProperty("items").EnumerateArray());
            var firstListTask = firstListItem.GetProperty("handle").GetProperty("taskId").GetString();
            var cursor = firstListData.RootElement.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(cursor));
            var secondList = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerList, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    pageSize = 1,
                    cursor
                }), null, "website-list-second");
            Assert.True(secondList.Ok, secondList.Error?.Message);
            using var secondListData = JsonDocument.Parse(secondList.Data!.Value.GetProperty("dataJson").GetString()!);
            var secondListItem = Assert.Single(secondListData.RootElement.GetProperty("items").EnumerateArray());
            var secondListTask = secondListItem.GetProperty("handle").GetProperty("taskId").GetString();
            Assert.Equal(new[] { handle.TaskId, cancelHandle.TaskId }.Order(),
                new[] { firstListTask, secondListTask }.Order());
            Assert.Equal(JsonValueKind.Null, secondListData.RootElement.GetProperty("nextCursor").ValueKind);

            var wrongScopeList = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerList, JsonSerializer.Serialize(new
                {
                    stateSpaceId = "state.other",
                    pageSize = 16
                }), null, "website-list-wrong-state");
            Assert.True(wrongScopeList.Ok, wrongScopeList.Error?.Message);
            Assert.Equal("failed", wrongScopeList.Data!.Value.GetProperty("tag").GetString());

            var timedOutWait = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerWait, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    taskId = handle.TaskId,
                    commandId = handle.CommandId,
                    waitMilliseconds = 1
                }), null, "website-wait-timeout");
            Assert.True(timedOutWait.Ok, timedOutWait.Error?.Message);
            Assert.Equal("pending", timedOutWait.Data!.Value.GetProperty("tag").GetString());
            Assert.Equal(0, provider.Calls);
            Assert.Equal(2L, await db.Set<SystemTaskAiCeilingRecord>().LongCountAsync());

            var worker = lifecycleServices.GetRequiredService<SystemTaskWorkflowBackgroundWorker>();
            Assert.True(await worker.RunOnceAsync("inner-worker"));

            var replay = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, transportInput, "inner-worker-gateway",
                "website-replay");
            Assert.True(replay.Ok, replay.Error?.Code + ": " + replay.Error?.Message);
            Assert.Equal(handle.TaskId,
                replay.Data!.Value.GetProperty("pending").GetProperty("taskId").GetString());
            Assert.False(await worker.RunOnceAsync("inner-worker"));
            Assert.Equal(1, provider.Calls);

            var staleWaitHost = InnerWorkerHost(application, state, "stale-wait-host", deadline);
            var sharedWaitBudget = staleWaitHost.Budget;
            var stateRecord = await db.Set<ApplicationStateSpaceRecord>()
                .SingleAsync(value => value.Id == state.StateSpaceId);
            stateRecord.BindingRevision++;
            stateRecord.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            state = stateSpaces.Get(state.StateSpaceId)!;
            var refreshedWaitHost = Assert.Single(SelectedApplicationInnerWorkerOwner.RefreshStateHosts(
                stateSpaces, [staleWaitHost], state.StateSpaceId, Application));
            Assert.NotEqual(staleWaitHost.StateRevision, refreshedWaitHost.StateRevision);
            Assert.Same(sharedWaitBudget, refreshedWaitHost.Budget);

            var completedWait = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerWait, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    taskId = handle.TaskId,
                    commandId = handle.CommandId,
                    waitMilliseconds = 25_000
                }), null, "codex-wait-completed");
            Assert.True(completedWait.Ok, completedWait.Error?.Message);
            Assert.Equal("completed", completedWait.Data!.Value.GetProperty("tag").GetString());
            Assert.StartsWith("inner-result.",
                completedWait.Data.Value.GetProperty("completionEvidenceReference").GetString(), StringComparison.Ordinal);
            Assert.Equal(1, provider.Calls);
            Assert.Equal(2L, await db.Set<SystemTaskAiCeilingRecord>().LongCountAsync());

            var completedList = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerList, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    pageSize = 16
                }), null, "codex-list-completed");
            Assert.True(completedList.Ok, completedList.Error?.Message);
            using var completedListData = JsonDocument.Parse(
                completedList.Data!.Value.GetProperty("dataJson").GetString()!);
            var listedCompletion = completedListData.RootElement.GetProperty("items").EnumerateArray()
                .Single(value => value.GetProperty("handle").GetProperty("taskId").GetString() == handle.TaskId)
                .GetProperty("result");
            Assert.Equal("completed", listedCompletion.GetProperty("tag").GetString());
            Assert.Equal(completedWait.Data.Value.GetProperty("completionEvidenceReference").GetString(),
                listedCompletion.GetProperty("completionEvidenceReference").GetString());
            Assert.Equal(completedWait.Data.Value.GetProperty("previousCommits").GetRawText(),
                listedCompletion.GetProperty("previousCommits").GetRawText());

            var read = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerRead, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    taskId = handle.TaskId,
                    commandId = handle.CommandId
                }), null, "codex");
            Assert.True(read.Ok, read.Error?.Code + ": " + read.Error?.Message);
            var completed = read.Data!.Value;
            Assert.Equal("completed", completed.GetProperty("tag").GetString());
            Assert.Contains("\"answer\":\"42\"", completed.GetProperty("dataJson").GetString(),
                StringComparison.Ordinal);
            Assert.StartsWith("inner-result.",
                completed.GetProperty("completionEvidenceReference").GetString(), StringComparison.Ordinal);
            Assert.Equal(1, provider.Calls);
            Assert.Equal(1L, await db.Set<SystemTaskAiDispatchEvidenceRecord>().LongCountAsync(
                value => value.Kind == "dispatch" && value.DispatchKind == "provider"));
            Assert.Equal(1L, await db.Set<SystemTaskAiDispatchEvidenceRecord>().LongCountAsync(
                value => value.Kind == "usage" && value.IsComplete == 1));

            var dependentInput = JsonSerializer.Serialize(new
            {
                stateSpaceId = state.StateSpaceId,
                procedure = new
                {
                    definitionId = selected.ExactDefinitionId,
                    revision = selected.Version,
                    contentFingerprint = selected.Fingerprint
                },
                instruction = "Use the validated prerequisite answer.",
                resultSchema = schema,
                dependencyHandles = new[] { new { taskId = handle.TaskId, commandId = handle.CommandId } },
                dependencyInputs = new[]
                {
                    new
                    {
                        name = "priorAnswer",
                        handle = new { taskId = handle.TaskId, commandId = handle.CommandId },
                        jsonPointer = "/answer"
                    }
                }
            });
            var dependent = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, dependentInput, "inner-worker-dependent", "website-dependent");
            Assert.True(dependent.Ok, dependent.Error?.Message);
            var dependentPending = dependent.Data!.Value.GetProperty("pending");
            var dependentHandle = new SystemTaskDurableHandle(
                dependentPending.GetProperty("taskId").GetString()!,
                dependentPending.GetProperty("commandId").GetString()!);
            Assert.True(await worker.RunOnceAsync("inner-worker-dependent"));
            var dependentResult = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerRead, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    taskId = dependentHandle.TaskId,
                    commandId = dependentHandle.CommandId
                }), null, "dependent-result");
            Assert.True(dependentResult.Ok, dependentResult.Error?.Message);
            Assert.Equal("completed", dependentResult.Data!.Value.GetProperty("tag").GetString());
            Assert.Equal(1, provider.CallsWithPrerequisites);
            Assert.Equal(2, provider.Calls);
            var mappedDispatch = await db.Set<SystemTaskAiDispatchEvidenceRecord>().AsNoTracking()
                .SingleAsync(value => value.Kind == "dispatch" && value.DispatchKind == "provider"
                    && value.RequestJson != null && value.RequestJson.Contains("priorAnswer"));
            using var frozenDispatch = JsonDocument.Parse(mappedDispatch.RequestJson!);
            var frozenUserMessage = frozenDispatch.RootElement.GetProperty("messages").EnumerateArray()
                .Single(value => value.GetProperty("content").GetString()!.Contains("priorAnswer"));
            using var frozenPrompt = JsonDocument.Parse(frozenUserMessage.GetProperty("content").GetString()!);
            var frozenPrerequisite = Assert.Single(
                frozenPrompt.RootElement.GetProperty("prerequisites").EnumerateArray());
            Assert.Equal("42", frozenPrerequisite.GetProperty("value").GetString());
            var dependentReplay = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, dependentInput, "inner-worker-dependent", "website-dependent-replay");
            Assert.True(dependentReplay.Ok, dependentReplay.Error?.Message);
            Assert.Equal(dependentHandle.TaskId,
                dependentReplay.Data!.Value.GetProperty("pending").GetProperty("taskId").GetString());
            Assert.False(await worker.RunOnceAsync("inner-worker-dependent-replay"));
            Assert.Equal(2, provider.Calls);

            var failedDependencyInput = JsonSerializer.Serialize(new
            {
                stateSpaceId = state.StateSpaceId,
                procedure = new
                {
                    definitionId = selected.ExactDefinitionId,
                    revision = selected.Version,
                    contentFingerprint = selected.Fingerprint
                },
                instruction = "This must not run after a failed prerequisite.",
                resultSchema = schema,
                dependencyHandles = new[] { new { taskId = cancelHandle.TaskId, commandId = cancelHandle.CommandId } },
                dependencyInputs = new[]
                {
                    new
                    {
                        name = "cancelledAnswer",
                        handle = new { taskId = cancelHandle.TaskId, commandId = cancelHandle.CommandId },
                        jsonPointer = "/answer"
                    }
                }
            });
            var failedDependent = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, failedDependencyInput,
                "inner-worker-failed-dependent", "website-failed-dependent");
            Assert.True(failedDependent.Ok, failedDependent.Error?.Message);
            var failedPending = failedDependent.Data!.Value.GetProperty("pending");
            var failedHandle = new SystemTaskDurableHandle(
                failedPending.GetProperty("taskId").GetString()!,
                failedPending.GetProperty("commandId").GetString()!);
            Assert.False(await worker.RunOnceAsync("inner-worker-failed-dependent"));
            var failedResult = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerRead, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    taskId = failedHandle.TaskId,
                    commandId = failedHandle.CommandId
                }), null, "failed-dependent-result");
            Assert.True(failedResult.Ok, failedResult.Error?.Message);
            Assert.Equal("failed", failedResult.Data!.Value.GetProperty("tag").GetString());
            Assert.Equal("SYSTEM_TASK_DEPENDENCY_FAILED", failedResult.Data.Value.GetProperty("code").GetString());
            Assert.Equal(2, provider.Calls);

            var triggerNow = DateTimeOffset.UtcNow;
            var existingBeforeTrigger = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .Select(value => value.TaskId).ToArrayAsync();
            var triggerClock = new RuntimeTriggerClock(
                triggerNow.AddTicks(-(triggerNow.Ticks % TimeSpan.TicksPerSecond)));
            var triggerHost = InnerWorkerHost(application, state, "trigger-binding-command", deadline);
            var triggerTarget = TriggerProcedureWorkflowTarget.Create(triggerHost, selected,
                assignment, schema, TimeSpan.FromMinutes(2));
            var triggerStore = new SqliteTriggerSchedulingStore(db, triggerClock,
                durableTasks: durable);
            await triggerStore.AppendOneTimeTriggerAsync(OneTimeTriggerDefinition.Create(
                Application, "demo.runtime.inner-trigger", 1, triggerClock.UtcNow,
                TriggerMisfirePolicy.FireOnce, TriggerFireTarget.ProcedureWorkflow,
                procedureWorkflow: triggerTarget));
            var triggerWorker = new SqliteOneTimeTriggerWorker(db, triggerClock, triggerStore,
                new SystemTaskTriggerTransactionParticipant(db,
                    new TriggerNotificationTransactionParticipant(db, triggerClock), durable,
                    resolver));

            var fired = await triggerWorker.RunBatchAsync("trigger-inner-worker");

            Assert.Equal(1, fired.Completed);
            var triggerTask = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .SingleAsync(value => !existingBeforeTrigger.Contains(value.TaskId));
            Assert.NotNull(triggerTask.AdmissionPayloadJson);
            Assert.Contains("\"innerWorker\"", triggerTask.AdmissionPayloadJson,
                StringComparison.Ordinal);
            Assert.Equal(5L, await db.Set<SystemTaskAiCeilingRecord>().LongCountAsync());
            Assert.True(await worker.RunOnceAsync("inner-worker-trigger-trigger"));
            var triggerResult = await service.GetAsync(
                InnerWorkerHost(application, state, "trigger-result-read", deadline),
                new(triggerTask.TaskId, triggerTask.CommandId));
            Assert.Equal(InteractionInvocationResultTag.Completed, triggerResult.Tag);
            Assert.Contains("\"answer\":\"42\"", triggerResult.DataJson,
                StringComparison.Ordinal);
            Assert.Equal(3, provider.Calls);

            var existingTaskIds = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .Select(value => value.TaskId).ToArrayAsync();
            var recurringHost = InnerWorkerHost(application, state,
                "recurring-trigger-binding-command", deadline);
            var recurringTarget = TriggerProcedureWorkflowTarget.Create(recurringHost, selected,
                assignment, schema, TimeSpan.FromMinutes(2));
            var recurringDefinition = RecurringTriggerDefinition.Create(
                Application, "demo.runtime.recurring-inner-trigger", 1,
                RecurrencePattern.Daily(1,
                    new TimeOnly(triggerClock.UtcNow.Hour, triggerClock.UtcNow.Minute,
                        triggerClock.UtcNow.Second), "Etc/UTC"),
                misfirePolicy: TriggerMisfirePolicy.FireOnce,
                target: TriggerFireTarget.ProcedureWorkflow,
                procedureWorkflow: recurringTarget);
            var recurringAppend = await triggerStore.AppendRecurringTriggerAsync(recurringDefinition);
            var recurringReplay = await triggerStore.AppendRecurringTriggerAsync(recurringDefinition);
            var recurringWorker = new SqliteRecurringTriggerWorker(db, triggerClock,
                new SystemTaskTriggerTransactionParticipant(db,
                    new TriggerNotificationTransactionParticipant(db, triggerClock), durable,
                    resolver));

            var recurringFire = await recurringWorker.RunBatchAsync("recurring-inner-worker");
            var recurringDuplicate = await recurringWorker.RunBatchAsync("recurring-inner-worker-replay");

            Assert.Equal(TriggerSchedulingWriteDisposition.Appended, recurringAppend.Disposition);
            Assert.Equal(TriggerSchedulingWriteDisposition.Replay, recurringReplay.Disposition);
            Assert.Equal(1, recurringFire.Completed);
            Assert.Equal(0, recurringDuplicate.Completed);
            var recurringTask = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .SingleAsync(value => !existingTaskIds.Contains(value.TaskId));
            Assert.Equal(6L, await db.Set<SystemTaskAiCeilingRecord>().LongCountAsync());
            Assert.Single(await db.RecurringTriggerFireReceipts.AsNoTracking().ToArrayAsync());
            Assert.True(await worker.RunOnceAsync("inner-worker-recurring-trigger"));
            var recurringResult = await service.GetAsync(
                InnerWorkerHost(application, state, "recurring-trigger-result-read", deadline),
                new(recurringTask.TaskId, recurringTask.CommandId));
            Assert.Equal(InteractionInvocationResultTag.Completed, recurringResult.Tag);
            Assert.Contains("\"answer\":\"42\"", recurringResult.DataJson,
                StringComparison.Ordinal);
            Assert.Equal(4, provider.Calls);

            var revokedDependency = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, dependentInput,
                "inner-worker-revoked-dependency", "website-revoked-dependency");
            Assert.True(revokedDependency.Ok, revokedDependency.Error?.Message);
            await RevokeProcedureWorkerGrantAsync(db);
            Assert.True(await worker.RunOnceAsync("inner-worker-revoked-dependency"));
            Assert.Equal(4, provider.Calls);
            var revokedList = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerList, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    pageSize = 16
                }), null, "codex-list-revoked");
            Assert.True(revokedList.Ok, revokedList.Error?.Message);
            Assert.Equal("failed", revokedList.Data!.Value.GetProperty("tag").GetString());
            Assert.Equal("STANDING_GRANT_DENIED", revokedList.Data.Value.GetProperty("code").GetString());
            var revokedRead = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerRead, JsonSerializer.Serialize(new
                {
                    stateSpaceId = state.StateSpaceId,
                    taskId = handle.TaskId,
                    commandId = handle.CommandId
                }), null, "codex-revoked");
            Assert.True(revokedRead.Ok, revokedRead.Error?.Message);
            Assert.Equal("failed", revokedRead.Data!.Value.GetProperty("tag").GetString());
            Assert.Equal("STANDING_GRANT_DENIED", revokedRead.Data.Value.GetProperty("code").GetString());
            var revokedReplay = await gateway.InvokeAsync(host.Principal, Application,
                SystemCapabilityIds.InnerWorkerSubmit, transportInput, "inner-worker-gateway",
                "website-replay-revoked");
            Assert.True(revokedReplay.Ok, revokedReplay.Error?.Message);
            Assert.Equal("failed", revokedReplay.Data!.Value.GetProperty("tag").GetString());
            Assert.Equal("STANDING_GRANT_DENIED", revokedReplay.Data.Value.GetProperty("code").GetString());
            Assert.Equal(4, provider.Calls);
        }
    }

    [Fact]
    public void Procedure_worker_assignment_rejects_authority_and_unknown_grammar_fields()
    {
        var authority = Assert.Throws<InteractionContractException>(() => SystemInnerWorkerAssignmentV1.Parse("""
            {"format":"dantes-roleplay/inner-procedure-assignment/v1","instruction":"inspect","allowedTools":["root"]}
            """));
        Assert.Equal("INNER_WORKER_ASSIGNMENT_UNSUPPORTED", authority.Code);
        var version = Assert.Throws<InteractionContractException>(() => SystemInnerWorkerAssignmentV1.Parse("""
            {"format":"dantes-roleplay/inner-procedure-assignment/v2","instruction":"inspect"}
            """));
        Assert.Equal("INNER_WORKER_ASSIGNMENT_UNSUPPORTED", version.Code);
    }

    private static InteractionInvocationHost InnerWorkerHost(ApplicationRevision application,
        StateSpaceView state, string command, DateTime deadline, string? parentCommand = null) => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        application, state.StateSpaceId, "inner-grant@1", command, InteractionStateRevision.From(state),
        InteractionExecutionProfile.Workflow, new InteractionInvocationBudget(16, deadline),
        parentCommand);

    private static async Task SeedInnerWorkerGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("inner-grant@1", "inner-grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.StateSpace, "state",
            [StandingGrantCapability.Read, StandingGrantCapability.Execute, StandingGrantCapability.ReadTask, StandingGrantCapability.CancelTask],
            new(StandingGrantDefinitionMode.ApplicationOwned, [], [
                new("demo.runtime", true, [CatalogNamespaceKinds.Procedure]),
                new("demo.runtime.pure", false, [CatalogNamespaceKinds.Mechanic]),
                new("demo.runtime.query", false, [CatalogNamespaceKinds.Query])]),
            [], 16, DateTime.UtcNow.AddMinutes(10), false, "inner-grant-operation");
        grant = grant with { ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant) };
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
            Scope = "stateSpace", StateSpaceId = grant.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = grant.ContentFingerprint, MaximumOperations = grant.MaximumOperations,
            ExpiresAtUtc = grant.ExpiresAtUtc, Revoked = false, IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task RevokeProcedureWorkerGrantAsync(DantesRoleplayDbContext db)
    {
        var prior = SqliteStandingGrantPolicy.Parse(await db.Set<StandingGrantRevisionRecord>()
            .SingleAsync(value => value.GrantId == "inner-grant" && value.Revision == 1));
        var revoked = prior with
        {
            Revision = 2,
            GrantReference = "inner-grant@2",
            Revoked = true,
            IssuedByOperationId = "inner-grant-revoke"
        };
        db.Add(new Operation
        {
            Id = revoked.IssuedByOperationId,
            Timestamp = DateTime.UtcNow,
            Tool = "test"
        });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = revoked.GrantId,
            Revision = revoked.Revision,
            GrantReference = revoked.GrantReference,
            PrincipalReference = revoked.PrincipalReference,
            ApplicationId = revoked.ApplicationId.Value,
            Scope = "stateSpace",
            StateSpaceId = revoked.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(revoked),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(revoked),
            MaximumOperations = revoked.MaximumOperations,
            ExpiresAtUtc = revoked.ExpiresAtUtc,
            Revoked = true,
            IssuedByOperationId = revoked.IssuedByOperationId
        });
        (await db.Set<StandingGrantCurrentRecord>()
            .SingleAsync(value => value.GrantId == "inner-grant")).Revision = 2;
        await db.SaveChangesAsync();
    }

    private sealed class SuccessfulProcedureProvider(string activeInstructions, string activeConstraints) : IAiProvider
    {
        internal int Calls { get; private set; }
        internal int CallsWithPrerequisites { get; private set; }
        public AiProviderInfo Info { get; } = new("codex", "Controlled Codex fixture");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(InteractionRoleProfile.Inner.Model, request.Model);
            Assert.Empty(request.Tools);
            Assert.Null(request.ToolExecutor);
            Assert.NotEmpty(request.ResponseSchemaJson);
            var system = Assert.Single(request.Messages, value => value.Role == AiMessageRole.System).Content;
            Assert.Contains(activeInstructions, system, StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(activeConstraints))
                Assert.Contains(activeConstraints, system, StringComparison.Ordinal);
            Assert.DoesNotContain("obsolete manual instructions", system, StringComparison.Ordinal);
            var user = Assert.Single(request.Messages, value => value.Role == AiMessageRole.User).Content;
            if (user.Contains("\"prerequisites\"", StringComparison.Ordinal))
            {
                CallsWithPrerequisites++;
                using var prompt = JsonDocument.Parse(user);
                var prerequisite = Assert.Single(prompt.RootElement.GetProperty("prerequisites").EnumerateArray());
                Assert.Equal("priorAnswer", prerequisite.GetProperty("name").GetString());
                Assert.Equal("42", prerequisite.GetProperty("value").GetString());
                Assert.Equal(64, prerequisite.GetProperty("outputFingerprint").GetString()!.Length);
            }
            return Task.FromResult(new AiProviderResponse(true, null, "done",
                "{\"answer\":\"42\",\"summary\":\"Inspection completed.\"}", [],
                Usage: new(7, 2, 9, true)));
        }
    }

    private sealed class RuntimeTriggerClock(DateTimeOffset value) : TimeProvider, ITriggerClock
    {
        public DateTimeOffset UtcNow { get; } = value;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class UnusedReadModels : IApplicationReadModelService
    {
        public Task<ApplicationReadModelResult> ReadAsync(ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
            "The procedure-only fixture must not invent a query read model.");
    }

    private sealed class InnerWorkerDatabase(string path, DantesRoleplayDbContext db) : IAsyncDisposable
    {
        internal DantesRoleplayDbContext Db { get; } = db;

        internal static async Task<InnerWorkerDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"inner-worker-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + path).Options;
            var db = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new(path, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
