using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Play.Tests;

public sealed class ConversationMemoryDreamRuntimeIntegrationTests
{
    private const string Principal =
        "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ProcedureId = "dream-fixture.procedure.conversation-dream";
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("dream-fixture");

    [Fact]
    public async Task Real_durable_submit_terminal_readback_and_derive_retain_one_source_pinned_candidate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "conversation-dream-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite(connectionString).Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var applications = new SqliteApplicationRegistry(db);
            var application = applications.Register(new(Application, "Dream Fixture", "", []));
            var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
            var state = stateSpaces.Create(new("dream-state", application, Hash, Hash));
            var scope = new ConversationMemoryScope(Principal, Application.Value, state.StateSpaceId, "session.dream");
            var binding = new ConversationMemoryBinding(scope, "codex", "project.dream",
                "C:\\repo\\dream", "thread.dream");
            var memory = new ApplicationConversationMemoryStore(db);
            memory.Connect(binding);
            var captured = memory.AppendTurn(new(binding, "turn.dream", [
                new("message.user", ConversationMemoryRoles.User,
                    ConversationMemoryMessageKinds.UserPrompt, "Remember the lantern.", DateTime.UtcNow),
                new("message.assistant", ConversationMemoryRoles.Assistant,
                    ConversationMemoryMessageKinds.AssistantFinal, "The lantern is lit.", DateTime.UtcNow)
            ], "codex-app-server/turn-items@0.153.4", DateTime.UtcNow, "capture.dream"));
            var sourceIds = captured.Messages.Select(value => value.SourceMessageId).ToArray();
            var candidate = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                sourceRevision = captured.Journal.Revision,
                sourceMessageIds = sourceIds,
                summary = "The lantern is lit."
            }));
            const string resultSchema =
                "{\"additionalProperties\":false,\"properties\":{\"sourceMessageIds\":{\"items\":{\"type\":\"string\"},\"type\":\"array\"},\"sourceRevision\":{\"type\":\"integer\"},\"summary\":{\"type\":\"string\"}},\"required\":[\"sourceRevision\",\"sourceMessageIds\",\"summary\"],\"type\":\"object\"}";
            var host = new InteractionInvocationHost(
                TrustedPrincipalContext.VerifiedPrincipal(Principal, "test"), application,
                state.StateSpaceId, "dream-grant@1", "dream.command", InteractionStateRevision.From(state),
                InteractionExecutionProfile.Workflow,
                new InteractionInvocationBudget(4, DateTime.UtcNow.AddMinutes(2)));
            var selected = new SystemTaskSelectedDefinition(ProcedureId, 1, Hash);
            var worker = new SystemInnerWorkerRequest(host, selected,
                "{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"Consolidate the selected exact messages.\"}",
                resultSchema);
            var profile = new AiAgentProfile("web.inner", "Dream worker",
                "Perform the selected conversation dream.", "Return only the required structured result.");
            var request = new AiRequest("codex", InteractionRoleProfile.Inner.Model, [], AiRequestKind.Task,
                AiReasoningEffort.Low, ResponseSchemaJson: resultSchema, AllowedTools: [],
                MaximumToolRounds: 0, MaximumToolCalls: 0, MaximumDuration: TimeSpan.FromMinutes(2));
            var prepared = new SystemInnerWorkerPreparedRequest(profile, request, Hash, Hash,
                Fingerprint(resultSchema), [], 1);
            var preparation = new SystemInnerWorkerProcedurePreparationResult(worker, prepared,
                new("web.inner", 1, Fingerprint(InteractionCanonicalJson.CanonicalizeObject(
                    JsonSerializer.Serialize(new { profile.Id, profile.Name, profile.Identity, profile.Instructions })))),
                [], new("manual.conversation-dream", Hash), new SystemInnerWorkerAiBudget(toolCalls: 0),
                new(host.Principal, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "dream-runtime")
                {
                    ApplicationId = Application,
                    StateSpaceId = state.StateSpaceId,
                    ResolutionFingerprint = state.ResolutionFingerprint
                }, []);
            var origin = new StandingGrantActivationOrigin(1, Hash, application.Revision, application.Fingerprint);
            var authority = new DreamAuthority(host, selected, origin);
            var durable = new SqliteSystemTaskDurableService(db, authority, authority, stateSpaces, TimeProvider.System);

            var submitted = await durable.SubmitInnerWorkerAsync(preparation);
            Assert.True(submitted.Tag == InteractionInvocationResultTag.Pending,
                submitted.Code + ": " + submitted.SafeMessage);
            var handle = Assert.IsType<SystemTaskDurableHandle>(submitted.TaskHandle);
            var store = new SqliteSystemTaskLifecycleStore(connectionString, TimeProvider.System);
            var lease = Assert.IsType<SystemTaskLease>(await store.ClaimNextWorkflowAsync(
                "dream-runtime", TimeSpan.FromMinutes(1)));
            var resolved = preparation.BindAuthority(new("dream-grant@1", "1", authority.Grant.ContentFingerprint));
            string reservationReference;
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
                var reservation = await store.StageReserveAiBudgetAsync(new(
                    host, handle, lease.Attempt, "dream.provider.1", resolved.AiBudget, 16, 0),
                    connection, transaction);
                Assert.True(reservation.Accepted, reservation.Code);
                reservationReference = reservation.Reservation!.RecordReference;
                var providerRequest = new AiProviderRequest(InteractionRoleProfile.Inner.Model,
                    [new(AiMessageRole.User, "Consolidate the exact source selection.")],
                    AiRequestKind.Task, AiReasoningEffort.Low, resultSchema, [], null, 2_048);
                var dispatch = await store.StageRecordAiProviderDispatchAsync(reservationReference,
                    lease.Attempt, resolved, new("codex", 0, providerRequest), connection, transaction);
                Assert.True(dispatch.Accepted, dispatch.Code);
                var outcome = await store.StageRecordAiProviderOutcomeAsync(reservationReference,
                    lease.Attempt, new(AiDispatchCompletionKind.Returned,
                        new(true, null, "done", candidate, [], Usage: new(5, 3, 8, true))),
                    connection, transaction);
                Assert.True(outcome.Accepted, outcome.Code);
                await transaction.CommitAsync();
            }
            var evidence = SystemInnerWorkerCompletionEvidence.For(lease);
            Assert.True(await store.CompleteAsync(lease,
                new(candidate, evidence, ["manual.conversation-dream"])));

            var read = await durable.GetAsync(ReadHost(host, host.Principal, "dream.read"), handle);
            Assert.Equal(InteractionInvocationResultTag.Completed, read.Tag);
            Assert.Equal(candidate, read.DataJson);
            Assert.Equal(evidence, read.CompletionEvidenceReference);
            var recorder = new ConversationMemoryDreamRecorder(db, memory,
                new DurableReadGateway(durable, host), TimeProvider.System);
            var retained = await recorder.RetainCompletedAsync(host.Principal,
                new(scope, handle, captured.Journal.Revision, sourceIds, "derive.dream"), "derive.dream");

            Assert.Equal(candidate, retained.CandidateJson);
            Assert.Equal(evidence, retained.CompletionEvidenceReference);
            Assert.Equal(Fingerprint(resultSchema), retained.ResultSchemaFingerprint);
            Assert.Equal(sourceIds, retained.SourceMessageIds);
            Assert.Single(memory.GetDerivedCandidates(scope));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static InteractionInvocationHost ReadHost(
        InteractionInvocationHost source, TrustedPrincipalContext principal, string command) => new(
        principal, source.ApplicationRevision, source.StateSpaceId!, source.GrantReference, command,
        source.StateRevision!, source.Profile, source.Budget, source.ParentCommandId);

    private sealed class DreamAuthority(
        InteractionInvocationHost admittedHost,
        SystemTaskSelectedDefinition selected,
        StandingGrantActivationOrigin origin) : IStandingGrantPolicy, IStandingGrantTargetResolver
    {
        internal StandingGrantRevision Grant { get; } = CreateGrant(admittedHost, selected);

        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
            StandingGrantRequirement requirement, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandingGrantDecision(true, "STANDING_GRANT_ALLOWED", Grant,
                new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, "dream-runtime",
                    host.StateSpaceId!, host.CommandId, true, "STANDING_GRANT_ALLOWED")));

        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(selection, origin));

        public Task<StandingGrantTargetResolution> ResolveRetainedAsync(InteractionInvocationHost host,
            StandingGrantActivationOrigin retained, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(retained == origin ? Result(selection, null, retained)
                : new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Denied,
                    "STANDING_GRANT_RETAINED_ORIGIN_STALE", null));

        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
            string exactDefinitionId, string kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(new(exactDefinitionId, kind, selected.Version, selected.Fingerprint), origin));

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static StandingGrantTargetResolution Result(StandingGrantDefinitionReference selection,
            StandingGrantActivationOrigin? current, StandingGrantActivationOrigin? retained = null) => new(
            StandingGrantTargetResolutionStatus.Available, "STANDING_GRANT_TARGET_AVAILABLE",
            new(selection.DefinitionId, selection.Kind, Application,
                CatalogNamespaceIdentity.NamespaceOf(selection.DefinitionId), "dream-fixture",
                selection.Revision, selection.ContentFingerprint, RetainedActivation: retained), current);

        private static StandingGrantRevision CreateGrant(
            InteractionInvocationHost host, SystemTaskSelectedDefinition definition)
        {
            var provisional = new StandingGrantRevision(
                host.GrantReference, "dream-grant", 1, Hash, Principal, Application,
                StandingGrantScope.StateSpace, host.StateSpaceId,
                [StandingGrantCapability.Execute, StandingGrantCapability.ReadTask],
                new(StandingGrantDefinitionMode.ExactIds, [definition.ExactDefinitionId], []), [],
                4, host.Budget.DeadlineUtc, false, "dream-grant-operation");
            return provisional with
            {
                ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(provisional)
            };
        }
    }

    private sealed class DurableReadGateway(
        SqliteSystemTaskDurableService durable,
        InteractionInvocationHost host) : IApplicationCandidateCapabilityGateway
    {
        public ApplicationCandidateCapabilityDiscoveryResult Discover(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId, string correlationId) =>
            throw new NotSupportedException();

        public async Task<ApplicationCandidateCapabilityInvocationResult> InvokeAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId, string capabilityId,
            string inputJson, string? idempotencyKey, string correlationId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(SystemCapabilityIds.InnerWorkerRead, capabilityId);
            using var input = JsonDocument.Parse(inputJson);
            var result = await durable.GetAsync(ReadHost(host, principal,
                "dream.readback." + correlationId), new(input.RootElement.GetProperty("taskId").GetString()!,
                input.RootElement.GetProperty("commandId").GetString()!), cancellationToken);
            return new(result.Tag == InteractionInvocationResultTag.Completed, capabilityId, "read",
                JsonSerializer.SerializeToElement(new
                {
                    tag = result.Tag.ToString().ToLowerInvariant(),
                    dataJson = result.DataJson,
                    completionEvidenceReference = result.CompletionEvidenceReference
                }), "", "", result.Tag == InteractionInvocationResultTag.Completed ? null
                    : new(result.Code, result.SafeMessage, "Inspect the durable task.", []));
        }
    }
}
