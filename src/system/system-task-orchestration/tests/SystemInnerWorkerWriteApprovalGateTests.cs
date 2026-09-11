using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.Authorization.Tests;

public sealed class SystemInnerWorkerWriteApprovalGateTests
{
    private static readonly string Hash = new('A', 64);

    [Fact]
    public async Task Approves_only_the_exact_admitted_write_and_consumes_its_approval()
    {
        var fixture = Create();
        fixture.Gate.RecordAdmittedTool(fixture.Dispatch);

        var approved = await fixture.Gate.ConfirmAsync(fixture.Request);
        var replay = await fixture.Gate.ConfirmAsync(fixture.Request);

        Assert.True(approved.Approved);
        Assert.Equal(32, approved.RequestToken.Length);
        Assert.All(approved.RequestToken, value => Assert.True(char.IsAsciiHexDigitLower(value)));
        Assert.False(replay.Approved);
        Assert.Equal(2, fixture.Reauthorizations());
    }

    [Fact]
    public async Task Fake_provider_executes_the_permitted_write_through_the_real_tool_adapter()
    {
        var fixture = Create();
        var catalog = new WriteCatalog(fixture.Descriptor);
        var provider = new ToolProvider(fixture.Dispatch.Invocation.Arguments.GetRawText());
        var ai = new SystemAiAgentService([new SystemCapabilityAiToolSource(catalog)],
            new AiService([provider]));
        var request = new AiRequest("fixture", "fixture-model", [new(AiMessageRole.User, "write")],
            AiRequestKind.Task, AiReasoningEffort.Low,
            AllowedTools: [fixture.Dispatch.Definition.Name], MaximumToolRounds: 1,
            ResponseSchemaJson: "{\"type\":\"object\"}");

        var response = await ai.SendAsync(fixture.Profile.Profile, request, fixture.ToolContext,
            new AdmittingLifecycle(fixture.Gate), fixture.Gate);

        Assert.True(response.Ok, response.ErrorMessage);
        Assert.Equal(1, catalog.ExecuteCalls);
        Assert.True(provider.ToolResult?.Ok);
    }

    [Fact]
    public async Task Tampered_input_or_context_and_revoked_authority_never_approve()
    {
        var fixture = Create();
        fixture.Gate.RecordAdmittedTool(fixture.Dispatch);
        var changedArguments = fixture.Request with
        {
            Arguments = JsonSerializer.SerializeToElement(new { value = "changed" })
        };
        Assert.False((await fixture.Gate.ConfirmAsync(changedArguments)).Approved);
        var changedContext = fixture.Request with
        {
            Invocation = fixture.Request.Invocation with { StateSpaceId = "other-state" }
        };
        Assert.False((await fixture.Gate.ConfirmAsync(changedContext)).Approved);
        var changedCapability = fixture.Request with
        {
            Capability = fixture.Request.Capability with { Fingerprint = new string('B', 64) }
        };
        Assert.False((await fixture.Gate.ConfirmAsync(changedCapability)).Approved);
        fixture.Revoke();
        var denied = await Assert.ThrowsAsync<AiLifecycleException>(() =>
            fixture.Gate.ConfirmAsync(fixture.Request));
        Assert.Equal("INNER_WORKER_AUTHORITY_STALE", denied.Code);
    }

    private static Fixture Create()
    {
        var principal = TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test");
        var application = new ApplicationRevision(ApplicationIdentifier.Parse("demo"), 1, Hash, []);
        var host = new InteractionInvocationHost(principal, application, "state", "grant@1", "command",
            "state@1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(2)));
        var procedure = new SystemTaskSelectedDefinition("system.runtime.inspect", 1, Hash);
        const string resultSchema = "{\"type\":\"object\"}";
        var worker = new SystemInnerWorkerRequest(host, procedure, "{}", resultSchema);
        var definition = new AiToolDefinition("system_fixture_write",
            "Write through the in-process system capability 'system.fixture.write'. Trusted confirmation and an idempotency token are required. Write fixture.",
            "{\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[\"value\"],\"type\":\"object\"}");
        var schemas = new BoundedJsonSchemaValidator();
        var inputSchema = schemas.Compile(definition.InputSchemaJson);
        var outputSchema = schemas.Compile(resultSchema);
        var capability = new SystemCapabilityDescriptor("system.fixture.write", 1, Hash,
            "fixture", "Write fixture.", SystemCapabilityMode.Write, "write", inputSchema.ProfileId,
            inputSchema.NormalizedSchema, inputSchema.SchemaHash, outputSchema.ProfileId,
            outputSchema.NormalizedSchema, outputSchema.SchemaHash,
            ["procedure." + procedure.ExactDefinitionId], PrivateOperatorCapability.Modify, "modify",
            SystemCapabilitySensitivity.PrivateOperatorMetadata, "private-operator-metadata", true, true);
        var binding = new SystemInnerWorkerToolBinding(definition,
            new(capability.Id, capability.Version, capability.Fingerprint), SystemCapabilityMode.Write,
            SystemInnerWorkerToolKind.SystemCapability);
        var profileDefinition = new AiAgentProfile("web.inner", "Inner", "Focused worker.");
        var profile = new SystemInnerWorkerResolvedProfile(worker,
            new(profileDefinition.Id, 1, Sha256("profile")), profileDefinition, Sha256(resultSchema),
            [binding], [], new("context.fixture", Hash), new("grant@1", "1", Hash));
        var storedInvocation = new SystemTaskStoredInvocation(principal.PrincipalId, principal.AuthenticationMethod,
            application.ApplicationId.Value, 1, Hash, "[]", "state", "grant@1", "state@1",
            InteractionExecutionProfile.Workflow, "command", null, 16, host.Budget.DeadlineUtc);
        var handle = new SystemTaskDurableHandle("task.fixture", "command");
        var lease = new SystemTaskLease(new(handle, storedInvocation, procedure, "{}", [], false),
            new("command", "attempt.fixture", "lease.fixture", 1, DateTime.UtcNow.AddMinutes(1)),
            1, null, null, false);
        var toolContext = new SystemCapabilityInvocationContext(principal, "inner.procedure", "inner:fixture")
        {
            ApplicationId = application.ApplicationId,
            StateSpaceId = "state",
            ResolutionFingerprint = Hash
        };
        var arguments = JsonSerializer.SerializeToElement(new { value = "exact" });
        var invocation = new AiToolInvocation("call.1", definition.Name, arguments, AiRequestKind.Task);
        var dispatch = new AiToolDispatchDescriptor(1, definition, invocation);
        var request = new SystemCapabilityAiApprovalRequest(capability, arguments,
            SystemCapabilityWritePreflight.Ready(Hash, "Write fixture.", ["fixture"]),
            toolContext with { CorrelationId = "ai-tool:call.1" });
        var revoked = false;
        var calls = 0;
        var gate = new SystemInnerWorkerWriteApprovalGate(lease, profile, toolContext, _ =>
        {
            calls++;
            return revoked
                ? Task.FromException(new AiLifecycleException("INNER_WORKER_AUTHORITY_STALE", "Revoked."))
                : Task.CompletedTask;
        });
        return new(gate, dispatch, request, profile, toolContext, capability,
            () => calls, () => revoked = true);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record Fixture(SystemInnerWorkerWriteApprovalGate Gate,
        AiToolDispatchDescriptor Dispatch, SystemCapabilityAiApprovalRequest Request,
        SystemInnerWorkerResolvedProfile Profile, SystemCapabilityInvocationContext ToolContext,
        SystemCapabilityDescriptor Descriptor,
        Func<int> Reauthorizations, Action Revoke);

    private sealed class AdmittingLifecycle(SystemInnerWorkerWriteApprovalGate gate) : IAiInvocationLifecycle
    {
        public ValueTask<IAiProviderCallScope> AdmitProviderCallAsync(AiProviderCallDescriptor call,
            CancellationToken cancellationToken) => ValueTask.FromResult<IAiProviderCallScope>(new Scope(gate));

        private sealed class Scope(SystemInnerWorkerWriteApprovalGate gate) : IAiProviderCallScope
        {
            public ValueTask RecordProviderOutcomeAsync(AiProviderCallObservation outcome) => ValueTask.CompletedTask;
            public ValueTask<IAiToolDispatchScope> AdmitToolDispatchAsync(AiToolDispatchDescriptor dispatch,
                CancellationToken cancellationToken)
            {
                gate.RecordAdmittedTool(dispatch);
                return ValueTask.FromResult<IAiToolDispatchScope>(new ToolScope());
            }
        }

        private sealed class ToolScope : IAiToolDispatchScope
        {
            public ValueTask RecordToolOutcomeAsync(AiToolDispatchObservation outcome) => ValueTask.CompletedTask;
        }
    }

    private sealed class ToolProvider(string arguments) : IAiProvider
    {
        public AiToolResult? ToolResult { get; private set; }
        public AiProviderInfo Info { get; } = new("fixture", "Fixture");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([]);
        public async Task<AiProviderResponse> SendAsync(AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            ToolResult = await request.ToolExecutor!(new("call.1", request.Tools[0].Name, arguments), cancellationToken);
            return new(true, null, "done", "{}", [], Usage: new(1, 1, 2, true));
        }
    }

    private sealed class WriteCatalog(SystemCapabilityDescriptor descriptor) : ISystemCapabilityCatalog
    {
        public int ExecuteCalls { get; private set; }
        public SystemCapabilityDiscoveryResult Discover(SystemCapabilityInvocationContext context) =>
            new(true, [descriptor], null, Evidence(context));
        public Task<SystemCapabilityReadResult> ReadAsync(string capabilityId, string inputJson,
            SystemCapabilityInvocationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<SystemCapabilityWritePreflightResult> PreflightWriteAsync(string capabilityId,
            string descriptorFingerprint, string inputJson, IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
            SystemCapabilityInvocationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemCapabilityWritePreflightResult(true, descriptor.Id, descriptor.Fingerprint,
                SystemCapabilityWritePreflight.Ready(Hash, "Write fixture.", ["fixture"]), null, Evidence(context)));
        public Task<SystemCapabilityWriteResult> ExecuteWriteAsync(string capabilityId, string descriptorFingerprint,
            string inputJson, SystemCapabilityWriteExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(new SystemCapabilityWriteResult(true, descriptor.Id, descriptor.Fingerprint,
                JsonSerializer.SerializeToElement(new { value = "written" }), context.RequestToken, Hash, null,
                context.AuthorizationEvidence));
        }
        private static AuthorizationAuditEvidence Evidence(SystemCapabilityInvocationContext context) => new(
            context.Principal.PrincipalId, context.Principal.AuthenticationMethod, "modify", context.Scope,
            context.CorrelationId, true, "PRIVATE_OPERATOR_ALLOWED");
    }
}
