using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SystemCapabilities.Tests;

public sealed class ApplicationCandidateSystemCapabilityTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("candidate-app");
    private static readonly ApplicationIdentifier OtherApplication = ApplicationIdentifier.Parse("other-app");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("test", "operator");
    private static readonly string Hash = new('A', 64);

    [Fact]
    public async Task Codex_capability_forwards_validation_samples_with_a_trusted_current_application_host_and_grant()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true);
        var context = Context(Application);
        var catalog = fixture.Scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var tool = SystemCapabilityAiTools.CreateTools(catalog, context, new Approval())
            .Single(value => value.Definition.Name == "system_application-candidate_validate");
        var definition = new
        {
            definitionId = "candidate-app.runtime.roll",
            kind = "mechanic",
            revision = 2,
            contentFingerprint = Hash
        };
        var input = JsonSerializer.SerializeToElement(new
        {
            applicationId = Application.Value,
            candidateId = new string('b', 32),
            revision = 3,
            contentFingerprint = Hash,
            samples = new[] { new { definition, inputJson = "{\"value\":1}", expectedDataJson = "{\"value\":2}",
                stateSpaceId = "candidate-state", stateRevision = "state-binding.1." + new string('a', 64),
                roleEntityIds = new Dictionary<string, string> { ["actor"] = "entity.actor" },
                expectedEffectsJson = "[]" } }
        });

        var result = await tool.InvokeAsync(new("call.1", tool.Definition.Name, input, AiRequestKind.Task));

        Assert.True(result.Ok, result.ErrorMessage);
        var call = Assert.Single(fixture.Authoring.ValidationCalls);
        Assert.Equal(Application, call.Request.Candidate.ApplicationId);
        var sample = Assert.Single(call.Request.Samples);
        Assert.Equal("candidate-app.runtime.roll", sample.Definition.DefinitionId);
        Assert.Equal("candidate-state", sample.StateSpaceId);
        Assert.Equal("entity.actor", sample.RoleEntityIds!["actor"]);
        Assert.Equal("[]", sample.ExpectedEffectsJson);
        Assert.Equal(Principal.PrincipalId, call.Host.Principal.PrincipalId);
        Assert.Equal("grant.validate@1", call.Host.GrantReference);
        Assert.Equal(InteractionExecutionProfile.Atomic, call.Host.Profile);
        Assert.Equal(3, call.Host.Budget.MaximumOperations);
        Assert.InRange(call.Host.Budget.DeadlineUtc, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(10));
        Assert.Contains("\"operationId\":\"0123456789abcdef0123456789abcdef\"", result.Content,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_forwards_only_the_trusted_host_and_exact_lookup()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true, capability: StandingGrantCapability.Read);
        var catalog = fixture.Scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var result = await catalog.ReadAsync(SystemCapabilityIds.ApplicationCandidateInspect,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = new string('c', 32),
                revision = 4,
                sourceOperationId = (string?)null
            }), Context(Application));

        Assert.True(result.Ok, result.Error?.Message);
        var call = Assert.Single(fixture.Authoring.InspectionCalls);
        Assert.Equal(new string('c', 32), call.Lookup.CandidateId);
        Assert.Equal(4, call.Lookup.Revision);
        Assert.Equal(Application, call.Host.ApplicationRevision.ApplicationId);
        Assert.Equal(InteractionExecutionProfile.ReadOnly, call.Host.Profile);
        Assert.Equal("grant.read@1", call.Host.GrantReference);
    }

    [Fact]
    public async Task Generic_system_task_context_can_select_an_application_as_bounded_input()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true, capability: StandingGrantCapability.Read);
        var catalog = fixture.Scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var context = new SystemCapabilityInvocationContext(
            Principal, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "system-task-candidate-test");

        var result = await catalog.ReadAsync(SystemCapabilityIds.ApplicationCandidateInspect,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value, candidateId = new string('c', 32), revision = 4,
                sourceOperationId = (string?)null
            }), context);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(Application, Assert.Single(fixture.Authoring.InspectionCalls).Host.ApplicationRevision.ApplicationId);
    }

    [Fact]
    public async Task Invalid_untrusted_or_ungranted_context_never_reaches_the_authoring_owner()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: false);
        var catalog = fixture.Scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var descriptor = catalog.Discover(Context(Application)).Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.ApplicationCandidateValidate);
        var input = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value,
            candidateId = new string('d', 32), revision = 1, contentFingerprint = Hash,
            samples = Array.Empty<object>()
        });
        var preflight = await catalog.PreflightWriteAsync(descriptor.Id, descriptor.Fingerprint,
            input, [], Context(Application));
        Assert.True(preflight.Ok, preflight.Error?.Message);

        var noGrant = await catalog.ExecuteWriteAsync(descriptor.Id, descriptor.Fingerprint, input,
            Execution(Context(Application), descriptor, preflight), default);
        var wrongApplication = await catalog.ExecuteWriteAsync(descriptor.Id, descriptor.Fingerprint, input,
            Execution(Context(OtherApplication), descriptor, preflight), default);
        var unauthenticated = await catalog.PreflightWriteAsync(descriptor.Id, descriptor.Fingerprint,
            input, [], new(TrustedPrincipalContext.Unauthenticated("TEST_UNAUTHENTICATED"),
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "candidate-test"));

        Assert.False(noGrant.Ok);
        Assert.Equal("STANDING_GRANT_DENIED", noGrant.Error?.Code);
        Assert.False(wrongApplication.Ok);
        Assert.Equal("APPLICATION_CONTEXT_REQUIRED", wrongApplication.Error?.Code);
        Assert.False(unauthenticated.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", unauthenticated.Error?.Code);
        Assert.Empty(fixture.Authoring.ValidationCalls);
    }

    [Fact]
    public async Task Every_mutating_candidate_contract_reaches_its_existing_typed_owner_method()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true,
            capabilities: [StandingGrantCapability.Author, StandingGrantCapability.Activate]);
        var context = Context(Application);
        var catalog = fixture.Scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();

        await ExecuteAsync(catalog, context, SystemCapabilityIds.ApplicationCandidateWrite,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value, candidateId = (string?)null,
                expectedCandidateRevision = 0, expectedActiveFingerprint = (string?)null,
                origin = "runtime", synchronizationEvidenceReference = (string?)null,
                newImplementationReason = "Add one retained mechanic.",
                documents = new[] { new
                {
                    logicalIdentity = "file:mechanics/test.md", sourceId = "runtime",
                    relativePath = "mechanics/test.md", mediaType = "text/markdown", text = "# Test"
                } }
            }));
        await ExecuteAsync(catalog, context, SystemCapabilityIds.ApplicationCandidateActivate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value, candidateId = new string('e', 32), revision = 2,
                contentFingerprint = Hash, validationOperationId = new string('f', 32)
            }));
        await ExecuteAsync(catalog, context, SystemCapabilityIds.ApplicationCandidateRecover,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value, activationRevision = 7, expectedActiveFingerprint = Hash
            }));

        Assert.Equal("Add one retained mechanic.", Assert.Single(fixture.Authoring.Writes).Request.NewImplementationReason);
        Assert.Equal(new string('f', 32), Assert.Single(fixture.Authoring.Activations).Request.ValidationOperationId);
        Assert.Equal((7, Hash), Assert.Single(fixture.Authoring.Recoveries).Request);
        Assert.All(fixture.Authoring.Writes.Select(value => value.Host)
            .Concat(fixture.Authoring.Recoveries.Select(value => value.Host)),
            host => Assert.Equal("grant.author@1", host.GrantReference));
        Assert.Equal("grant.activate@1", fixture.Authoring.Activations[0].Host.GrantReference);
    }

    [Fact]
    public async Task Validation_rejects_samples_that_cannot_fit_the_shared_sixteen_operation_ledger()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true);
        var context = Context(Application);
        var catalog = fixture.Scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var descriptor = catalog.Discover(context).Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.ApplicationCandidateValidate);
        var samples = Enumerable.Range(0, 15).Select(index => new
        {
            definition = new
            {
                definitionId = $"candidate-app.runtime.mechanic-{index}", kind = "mechanic",
                revision = 1, contentFingerprint = Hash
            },
            inputJson = "{}", expectedDataJson = "{}"
        }).ToArray();
        var input = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value, candidateId = new string('a', 32), revision = 1,
            contentFingerprint = Hash, samples
        });

        var result = await catalog.PreflightWriteAsync(descriptor.Id, descriptor.Fingerprint,
            input, [], context);

        Assert.False(result.Ok);
        Assert.Equal("INVOCATION_BUDGET_EXHAUSTED", result.Error?.Code);
        Assert.Empty(fixture.Authoring.ValidationCalls);
    }

    [Fact]
    public async Task Application_scoped_gateway_uses_verified_grantee_context_without_operator_confirmation()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true);
        var gateway = fixture.Scope.ServiceProvider.GetRequiredService<IApplicationCandidateCapabilityGateway>();
        var invited = TrustedPrincipalContext.VerifiedPrincipal(Principal.PrincipalId, "invited-test");
        var discovery = gateway.Discover(invited, Application, "web-request");
        var input = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value,
            candidateId = new string('d', 32), revision = 1, contentFingerprint = Hash,
            samples = Array.Empty<object>()
        });

        var allowed = await gateway.InvokeAsync(invited, Application,
            SystemCapabilityIds.ApplicationCandidateValidate, input, "candidate-validation-1", "web-request");
        var unauthenticated = await gateway.InvokeAsync(
            TrustedPrincipalContext.Unauthenticated("TEST_UNAUTHENTICATED"), Application,
            SystemCapabilityIds.ApplicationCandidateValidate, input, "candidate-validation-2", "web-request");

        Assert.True(discovery.Ok, discovery.Error?.Message);
        Assert.Equal(13, discovery.Capabilities.Count);
        Assert.DoesNotContain(discovery.Capabilities, value =>
            value.Id == SystemCapabilityIds.StandingGrantAdmin || value.RequiresConfirmation);
        Assert.Equal([StandingGrantCapability.Validate], discovery.Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.ApplicationCandidateValidate).RequiredStandingGrantCapabilities);
        var reviewSubmit = discovery.Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.ApplicationCandidateReviewSubmit);
        Assert.Equal([StandingGrantCapability.Read, StandingGrantCapability.Validate],
            reviewSubmit.RequiredStandingGrantCapabilities);
        Assert.True(reviewSubmit.RequiresIdempotencyKey);
        var reviewRead = discovery.Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.ApplicationCandidateReviewRead);
        Assert.Equal([StandingGrantCapability.Read], reviewRead.RequiredStandingGrantCapabilities);
        Assert.False(reviewRead.RequiresIdempotencyKey);
        Assert.Equal([StandingGrantCapability.Execute], discovery.Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.InnerWorkerSubmit).RequiredStandingGrantCapabilities);
        Assert.Equal([StandingGrantCapability.ReadTask], discovery.Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.InnerWorkerRead).RequiredStandingGrantCapabilities);
        Assert.Equal([StandingGrantCapability.CancelTask], discovery.Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.InnerWorkerCancel).RequiredStandingGrantCapabilities);
        Assert.True(allowed.Ok, allowed.Error?.Message);
        Assert.Equal("grant.validate@1", Assert.Single(fixture.Authoring.ValidationCalls).Host.GrantReference);
        Assert.False(unauthenticated.Ok);
        Assert.Equal("APPLICATION_AUTHORING_UNAUTHENTICATED", unauthenticated.Error?.Code);
    }

    [Fact]
    public async Task Candidate_owner_can_reject_a_narrow_first_grant_and_authorize_with_a_later_current_grant()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: false);
        var db = fixture.Scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
        await Fixture.AddGrantAsync(db, StandingGrantCapability.Validate, "a-narrow");
        await Fixture.AddGrantAsync(db, StandingGrantCapability.Validate, "b-valid");
        fixture.Authoring.DeniedGrantReference = "grant.a-narrow@1";
        var gateway = fixture.Scope.ServiceProvider.GetRequiredService<IApplicationCandidateCapabilityGateway>();
        var input = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value,
            candidateId = new string('d', 32), revision = 1, contentFingerprint = Hash,
            samples = Array.Empty<object>()
        });

        var result = await gateway.InvokeAsync(Principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate, input, "candidate-validation-3", "codex-call");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(["grant.a-narrow@1", "grant.b-valid@1"],
            fixture.Authoring.ValidationCalls.Select(value => value.Host.GrantReference).ToArray());
        Assert.Same(fixture.Authoring.ValidationCalls[0].Host.Budget,
            fixture.Authoring.ValidationCalls[1].Host.Budget);
    }

    [Fact]
    public async Task Revoked_grant_is_immediately_unavailable_to_the_application_gateway()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true);
        var db = fixture.Scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
        var row = await db.Set<StandingGrantRevisionRecord>().SingleAsync(value =>
            value.GrantId == "grant.validate");
        row.Revoked = true;
        await db.SaveChangesAsync();
        var gateway = fixture.Scope.ServiceProvider.GetRequiredService<IApplicationCandidateCapabilityGateway>();
        var input = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value,
            candidateId = new string('d', 32), revision = 1, contentFingerprint = Hash,
            samples = Array.Empty<object>()
        });

        var result = await gateway.InvokeAsync(Principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate, input, "candidate-validation-4", "web-request");

        Assert.False(result.Ok);
        Assert.Equal("STANDING_GRANT_DENIED", result.Error?.Code);
        Assert.Empty(fixture.Authoring.ValidationCalls);
    }

    [Fact]
    public async Task Selected_application_ai_gets_grant_backed_candidate_tools_without_an_approval_gate()
    {
        await using var fixture = await Fixture.CreateAsync(withGrant: true);
        var gateway = fixture.Scope.ServiceProvider.GetRequiredService<IApplicationCandidateCapabilityGateway>();
        var source = new ApplicationCandidateCapabilityAiToolSource(gateway);
        var context = Context(Application);
        var tools = source.CreateTools(new(
            new("test", "Test", "Test application authoring."),
            new("test", "model", [new(AiMessageRole.User, "Validate the candidate")], AiRequestKind.Task),
            context,
            null,
            null,
            () => []));
        var tool = tools.Single(value => value.Definition.Name == "system_application-candidate_validate");
        var arguments = JsonSerializer.SerializeToElement(new
        {
            idempotencyKey = "candidate-validation-ai-1",
            input = new
            {
                applicationId = Application.Value,
                candidateId = new string('d', 32), revision = 1, contentFingerprint = Hash,
                samples = Array.Empty<object>()
            }
        });

        var result = await tool.InvokeAsync(new("call.1", tool.Definition.Name, arguments, AiRequestKind.Task));

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Single(fixture.Authoring.ValidationCalls);
    }

    [Fact]
    public async Task Default_data_access_composition_resolves_the_real_authoring_owner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Data Source=:memory:");
        services.AddDbContext<DantesRoleplayDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<SqliteApplicationAuthoringService>(
            scope.ServiceProvider.GetRequiredService<IApplicationAuthoringService>());
        Assert.Contains(scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>()
            .Discover(Context(Application)).Capabilities,
            value => value.Id == SystemCapabilityIds.ApplicationCandidateWrite);
    }

    private static SystemCapabilityInvocationContext Context(ApplicationIdentifier application) => new(
        Principal, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "candidate-test")
    {
        ApplicationId = application
    };

    private static SystemCapabilityWriteExecutionContext Execution(
        SystemCapabilityInvocationContext context, SystemCapabilityDescriptor descriptor,
        SystemCapabilityWritePreflightResult preflight) => new(
        context, "0123456789abcdef0123456789abcdef", "Validate the retained candidate.",
        descriptor.ProcedureIds, preflight.AuthorizationEvidence,
        preflight.Preflight!.ExecutionEvidenceJson);

    private static async Task ExecuteAsync(ISystemCapabilityCatalog catalog,
        SystemCapabilityInvocationContext context, string capabilityId, string input)
    {
        var descriptor = catalog.Discover(context).Capabilities.Single(value => value.Id == capabilityId);
        var preflight = await catalog.PreflightWriteAsync(capabilityId, descriptor.Fingerprint, input, [], context);
        Assert.True(preflight.Ok, preflight.Error?.Message);
        var result = await catalog.ExecuteWriteAsync(capabilityId, descriptor.Fingerprint, input,
            Execution(context, descriptor, preflight));
        Assert.True(result.Ok, result.Error?.Message);
    }

    private sealed class Approval : ISystemCapabilityAiWriteApprovalGate
    {
        public Task<SystemCapabilityAiApprovalDecision> ConfirmAsync(
            SystemCapabilityAiApprovalRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemCapabilityAiApprovalDecision(true,
                "0123456789abcdef0123456789abcdef", "Validate the retained candidate."));
    }

    private sealed class RecordingAuthoringService : IApplicationAuthoringService
    {
        public string? DeniedGrantReference { get; set; }
        public List<(ApplicationCandidateValidationRequest Request, InteractionInvocationHost Host)> ValidationCalls { get; } = [];
        public List<(ApplicationCandidateLookup Lookup, InteractionInvocationHost Host)> InspectionCalls { get; } = [];
        public List<(ApplicationCandidateWriteRequest Request, InteractionInvocationHost Host)> Writes { get; } = [];
        public List<(ApplicationCandidateActivationRequest Request, InteractionInvocationHost Host)> Activations { get; } = [];
        public List<((int Revision, string? Fingerprint) Request, InteractionInvocationHost Host)> Recoveries { get; } = [];

        public Task<InteractionInvocationResult> WriteCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateWriteRequest request, CancellationToken cancellationToken = default)
        {
            Writes.Add((request, host));
            return Task.FromResult(Committed());
        }

        public Task<InteractionInvocationResult> InspectAsync(InteractionInvocationHost host,
            ApplicationCandidateLookup candidate, CancellationToken cancellationToken = default)
        {
            InspectionCalls.Add((candidate, host));
            return Task.FromResult(InteractionInvocationResult.CompletedComputation(
                "{\"candidate\":true}", "candidate-inspection.1"));
        }

        public Task<InteractionInvocationResult> ValidateAsync(InteractionInvocationHost host,
            ApplicationCandidateReference candidate, CancellationToken cancellationToken = default) =>
            ValidateAsync(new(candidate, []), host, cancellationToken);

        public Task<InteractionInvocationResult> ValidateAsync(ApplicationCandidateValidationRequest request,
            InteractionInvocationHost host, CancellationToken cancellationToken = default)
        {
            ValidationCalls.Add((request, host));
            return Task.FromResult(host.GrantReference == DeniedGrantReference
                ? InteractionInvocationResult.Failed("STANDING_GRANT_TARGET_DENIED", "The selected grant does not cover the exact target.")
                : Committed());
        }

        public Task<InteractionInvocationResult> ActivateAsync(InteractionInvocationHost host,
            ApplicationCandidateActivationRequest request, CancellationToken cancellationToken = default)
        {
            Activations.Add((request, host));
            return Task.FromResult(Committed());
        }

        public Task<InteractionInvocationResult> RecoverAsync(InteractionInvocationHost host, int activationRevision,
            string? expectedActiveFingerprint, CancellationToken cancellationToken = default)
        {
            Recoveries.Add(((activationRevision, expectedActiveFingerprint), host));
            return Task.FromResult(Committed());
        }

        private static InteractionInvocationResult Committed() => InteractionInvocationResult.Committed(new(
            "0123456789abcdef0123456789abcdef", Hash, []));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        public AsyncServiceScope Scope { get; }
        public RecordingAuthoringService Authoring { get; }

        private Fixture(SqliteConnection connection, ServiceProvider provider,
            AsyncServiceScope scope, RecordingAuthoringService authoring)
        {
            this.connection = connection;
            this.provider = provider;
            Scope = scope;
            Authoring = authoring;
        }

        public static async Task<Fixture> CreateAsync(bool withGrant,
            StandingGrantCapability capability = StandingGrantCapability.Validate,
            IReadOnlyList<StandingGrantCapability>? capabilities = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var authoring = new RecordingAuthoringService();
            var services = new ServiceCollection();
            services.AddDantesRoleplayDataAccess("Data Source=:memory:");
            services.AddDbContext<DantesRoleplayDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<IApplicationAuthoringService>(_ => authoring);
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
            await db.Database.EnsureCreatedAsync();
            scope.ServiceProvider.GetRequiredService<IApplicationRegistry>().Register(
                new(Application, "Candidate", "Candidate application", []));
            scope.ServiceProvider.GetRequiredService<IApplicationRegistry>().Register(
                new(OtherApplication, "Other", "Other application", []));
            if (withGrant)
            {
                foreach (var value in capabilities ?? [capability])
                    await AddGrantAsync(db, value);
            }
            return new(connection, provider, scope, authoring);
        }

        internal static async Task AddGrantAsync(DantesRoleplayDbContext db,
            StandingGrantCapability capability, string? grantId = null)
        {
            var name = capability.ToString().ToLowerInvariant();
            grantId ??= name;
            var operationId = "grant-issuer-" + grantId;
            var grant = new StandingGrantRevision($"grant.{grantId}@1", $"grant.{grantId}", 1,
                new string('0', 64), Principal.PrincipalId, Application, StandingGrantScope.Application, null,
                [capability], new(StandingGrantDefinitionMode.ExactIds, [], []), [], 16,
                DateTime.UtcNow.AddMinutes(5), false, operationId);
            grant = grant with { ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant) };
            db.Add(new Operation { Id = operationId, Timestamp = DateTime.UtcNow, Tool = "test" });
            db.Add(new StandingGrantRevisionRecord
            {
                GrantId = grant.GrantId, Revision = 1, GrantReference = grant.GrantReference,
                PrincipalReference = grant.PrincipalReference, ApplicationId = Application.Value,
                Scope = "application", StateSpaceId = null,
                PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
                ContentFingerprint = grant.ContentFingerprint, MaximumOperations = 16,
                ExpiresAtUtc = grant.ExpiresAtUtc, Revoked = false, IssuedByOperationId = operationId
            });
            db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = 1 });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
