using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.Tests;
using DantesRoleplay.Sources;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Projections;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.LegacyStateAdoption;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.McpProtocol.Tests;

public sealed class SystemRegistryAuthorizationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Administrative_query_denies_before_identifier_parsing_or_registry_lookup()
    {
        await using var db = _fixture.CreateContext();
        var registry = new ThrowingRegistry();
        var authorizer = new DenyingAuthorizer();

        var result = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: "system.applications", applicationId: "system", applications: registry,
            privateOperator: authorizer);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.False(registry.Touched);
        Assert.Equal(1, authorizer.Calls);
        var operation = await db.Operations.AsNoTracking().SingleAsync();
        Assert.Equal("query:system.applications", operation.Subject);
        Assert.Contains("PRIVATE_OPERATOR_UNAUTHENTICATED", operation.GuardEvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("system", result.Error!.Why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mcp_adapter_allows_direct_loopback_and_denies_remote_markers_or_missing_context()
    {
        var accessor = new HttpContextAccessor();
        var adapter = new McpPrivateOperatorAuthorizer(accessor, new PrivateOperatorAuthorizationPolicy());

        accessor.HttpContext = Context("localhost");
        var local = adapter.Authorize(PrivateOperatorCapability.Read);
        accessor.HttpContext = Context("roleplay.example.ts.net", tailscaleLogin: "operator@example.com");
        var remote = adapter.Authorize(PrivateOperatorCapability.Read);
        accessor.HttpContext = null;
        var missing = adapter.Authorize(PrivateOperatorCapability.Read);

        Assert.True(local.Allowed);
        Assert.StartsWith("principal.", local.Evidence.PrincipalReference, StringComparison.Ordinal);
        Assert.False(remote.Allowed);
        Assert.False(missing.Allowed);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", remote.Code);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", missing.Code);
    }

    [Fact]
    public async Task Application_activation_denies_before_invalid_json_or_service_access()
    {
        await using var db = _fixture.CreateContext();
        var activations = new ThrowingActivation();
        var authorization = new DenyingAuthorizer();

        var result = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.application.activate", payload: "not-json", dryRun: true,
            applicationActivations: activations, privateOperator: authorization);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.False(activations.Touched);
        Assert.Equal(PrivateOperatorCapability.Modify, authorization.LastCapability);
        Assert.Contains("PRIVATE_OPERATOR_UNAUTHENTICATED",
            (await db.Operations.AsNoTracking().SingleAsync()).GuardEvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task State_space_creation_denies_before_invalid_json_or_service_access()
    {
        await using var db = _fixture.CreateContext();
        var stateSpaces = new ThrowingStateSpaceAdministration();
        var authorization = new DenyingAuthorizer();

        var result = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.state-space.create", payload: "not-json", dryRun: true,
            stateSpaceAdministration: stateSpaces, privateOperator: authorization);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.False(stateSpaces.Touched);
        Assert.Equal(PrivateOperatorCapability.Modify, authorization.LastCapability);
        Assert.Contains("PRIVATE_OPERATOR_UNAUTHENTICATED",
            (await db.Operations.AsNoTracking().SingleAsync()).GuardEvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task State_space_upgrade_denies_before_invalid_json_or_service_access()
    {
        await using var db = _fixture.CreateContext();
        var stateSpaces = new ThrowingStateSpaceAdministration();
        var authorization = new DenyingAuthorizer();

        var result = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.state-space.upgrade", payload: "not-json", dryRun: true,
            stateSpaceAdministration: stateSpaces, privateOperator: authorization);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.False(stateSpaces.Touched);
        Assert.Equal(PrivateOperatorCapability.Modify, authorization.LastCapability);
    }

    [Fact]
    public async Task Legacy_adoption_denies_before_invalid_json_or_service_access()
    {
        await using var db = _fixture.CreateContext();
        var adoption = new ThrowingLegacyAdoption();
        var authorization = new DenyingAuthorizer();

        var result = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.state-space.adopt-legacy", payload: "not-json", dryRun: true,
            legacyStateAdoption: adoption, privateOperator: authorization);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.False(adoption.Touched);
        Assert.Equal(PrivateOperatorCapability.Modify, authorization.LastCapability);
    }

    [Fact]
    public async Task Administrative_commit_denies_before_invalid_json_parsing_and_uses_modify_capability()
    {
        await using var db = _fixture.CreateContext();
        var authorizer = new DenyingAuthorizer();

        var result = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.application.register", payload: "not-json", dryRun: true,
            registryAdministration: null, privateOperator: authorizer);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.Equal(1, authorizer.Calls);
        Assert.Equal(PrivateOperatorCapability.Modify, authorizer.LastCapability);
        var operation = await db.Operations.AsNoTracking().SingleAsync();
        Assert.Contains("PRIVATE_OPERATOR_UNAUTHENTICATED", operation.GuardEvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON", result.Error!.Why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Component_type_registration_denies_before_invalid_json_or_service_access()
    {
        await using var db = _fixture.CreateContext();
        var authorization = new DenyingAuthorizer();
        var result = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.component-type.register", payload: "not-json", dryRun: true,
            privateOperator: authorization);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.Equal(PrivateOperatorCapability.Modify, authorization.LastCapability);
    }

    [Fact]
    public async Task Administrative_commit_rejects_extra_fields_and_unsafe_paths_without_registration()
    {
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var administration = new RegistryAdministrationService(
            db, applications, sources, new OperationLog(db));
        var authorization = new AllowingAuthorizer();
        var app = ApplicationIdentifier.Parse("fixture-app");
        applications.Register(new(app, "Fixture", "", []));
        var extra = """{"requestToken":"7123456789abcdef0123456789abcdef","applicationId":"extra-app","displayName":"Extra","description":"","baseApplications":[],"expectedFingerprint":null,"principal":"admin"}""";
        var unsafeSource = """{"requestToken":"8123456789abcdef0123456789abcdef","applicationId":"fixture-app","sourceId":"unsafe","allowedRootId":"workspace","relativePathOrGlob":"../outside/**/*.json","trust":"trusted","precedence":1,"logicalIdentity":"catalog","expectedFingerprint":null}""";

        var extraResult = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.application.register", payload: extra, dryRun: true,
            registryAdministration: administration, privateOperator: authorization);
        var unsafeResult = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.source.register", payload: unsafeSource, dryRun: true,
            registryAdministration: administration, privateOperator: authorization);

        Assert.Equal("INVALID_PAYLOAD", extraResult.Error?.Code);
        Assert.Equal("INVALID_SOURCE", unsafeResult.Error?.Code);
        Assert.Null(applications.Get(ApplicationIdentifier.Parse("extra-app")));
        Assert.Empty(sources.For(app));
        Assert.Equal(2, authorization.Calls);
    }

    [Fact]
    public async Task Dependency_query_denies_before_identifier_parsing_or_registry_lookup()
    {
        await using var db = _fixture.CreateContext();
        var impacts = new ThrowingImpact();
        var authorization = new DenyingAuthorizer();

        var result = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: "system.dependencies", applicationId: "system", id: "invalid",
            projectionImpacts: impacts, privateOperator: authorization);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.False(impacts.Touched);
        Assert.Equal(PrivateOperatorCapability.Read, authorization.LastCapability);
        Assert.Contains("PRIVATE_OPERATOR_UNAUTHENTICATED",
            (await db.Operations.AsNoTracking().SingleAsync()).GuardEvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dependency_query_bounds_details_without_changing_full_counts_or_fingerprint()
    {
        await using var db = _fixture.CreateContext();
        var impacts = new StaticImpact();
        var authorization = new AllowingAuthorizer();

        var one = await QueryImpactAsync(db, impacts, authorization, 1);
        var two = await QueryImpactAsync(db, impacts, authorization, 2);
        var oneData = JsonSerializer.SerializeToElement(one.Data);
        var twoData = JsonSerializer.SerializeToElement(two.Data);

        Assert.True(one.Ok); Assert.True(two.Ok);
        Assert.Equal(2, oneData.GetProperty("Counts").GetProperty("Nodes").GetInt32());
        Assert.Single(oneData.GetProperty("Nodes").EnumerateArray());
        Assert.Equal(2, twoData.GetProperty("Nodes").GetArrayLength());
        Assert.True(oneData.GetProperty("Truncated").GetBoolean());
        Assert.False(oneData.GetProperty("Coverage").GetProperty("Complete").GetBoolean());
        Assert.Equal(oneData.GetProperty("GraphFingerprint").GetString(),
            twoData.GetProperty("GraphFingerprint").GetString());
    }

    [Fact]
    public async Task Application_preview_denies_before_identifier_parsing_or_scanning()
    {
        await using var db = _fixture.CreateContext();
        var preview = new ThrowingPreview();
        var authorization = new DenyingAuthorizer();

        var result = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: "system.application-preview", applicationId: "system",
            applicationPreviews: preview, privateOperator: authorization);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.False(preview.Touched);
        Assert.Equal(PrivateOperatorCapability.Read, authorization.LastCapability);
        Assert.Contains("PRIVATE_OPERATOR_UNAUTHENTICATED",
            (await db.Operations.AsNoTracking().SingleAsync()).GuardEvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_preview_bounds_details_without_changing_full_counts_or_fingerprint()
    {
        await using var db = _fixture.CreateContext();
        var preview = new StaticPreview();
        var authorization = new AllowingAuthorizer();

        var one = await QueryPreviewAsync(db, preview, authorization, 1);
        var two = await QueryPreviewAsync(db, preview, authorization, 2);
        var oneData = JsonSerializer.SerializeToElement(one.Data);
        var twoData = JsonSerializer.SerializeToElement(two.Data);

        Assert.True(one.Ok); Assert.True(two.Ok);
        Assert.Equal(2, oneData.GetProperty("Counts").GetProperty("Winners").GetInt32());
        Assert.Single(oneData.GetProperty("Winners").EnumerateArray());
        Assert.Equal(2, twoData.GetProperty("Winners").GetArrayLength());
        Assert.True(oneData.GetProperty("Truncated").GetBoolean());
        Assert.Equal(oneData.GetProperty("PreviewFingerprint").GetString(),
            twoData.GetProperty("PreviewFingerprint").GetString());
    }

    public void Dispose() => _fixture.Dispose();

    private static DefaultHttpContext Context(string host, string? tailscaleLogin = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Path = ServerConfiguration.McpEndpoint;
        context.Request.Host = new HostString(host);
        if (tailscaleLogin is not null) context.Request.Headers["Tailscale-User-Login"] = tailscaleLogin;
        return context;
    }

    private sealed class DenyingAuthorizer : IPrivateOperatorRequestAuthorizer
    {
        public int Calls { get; private set; }
        public PrivateOperatorCapability? LastCapability { get; private set; }

        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
        {
            Calls++;
            LastCapability = capability;
            return new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
                capability,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                "denied-test"));
        }
    }

    private sealed class ThrowingRegistry : IApplicationRegistry
    {
        public bool Touched { get; private set; }
        public ApplicationRevision Register(ApplicationRegistration registration) => throw Touch();
        public ApplicationRevision? Get(ApplicationIdentifier applicationId) => throw Touch();
        public ApplicationRegistration? Describe(ApplicationIdentifier applicationId) => throw Touch();
        public IReadOnlyList<ApplicationRegistration> List(int limit) => throw Touch();
        public ApplicationDiscoveryPage ListPage(string? afterApplicationId, int limit) => throw Touch();
        private Exception Touch() { Touched = true; return new InvalidOperationException("Registry must not be reached."); }
    }

    private sealed class AllowingAuthorizer : IPrivateOperatorRequestAuthorizer
    {
        public int Calls { get; private set; }

        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
        {
            Calls++;
            return new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                PrivateOperatorPrincipal.Create("test", "operator"),
                capability,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                "allowed-test"));
        }
    }

    private sealed class ThrowingPreview : IApplicationPreviewService
    {
        public bool Touched { get; private set; }

        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            CancellationToken cancellationToken = default)
        {
            Touched = true;
            throw new InvalidOperationException("Preview must not be reached.");
        }

        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            IReadOnlyList<string> sourceIds,
            CancellationToken cancellationToken = default)
        {
            Touched = true;
            throw new InvalidOperationException("Preview must not be reached.");
        }
    }

    private sealed class ThrowingImpact : IProjectionImpactService
    {
        public bool Touched { get; private set; }

        public ProjectionImpactReport Analyze(
            ApplicationIdentifier applicationId,
            string? rootId = null,
            bool transitive = true)
        {
            Touched = true;
            throw new InvalidOperationException("Dependency registry must not be reached.");
        }
    }

    private sealed class ThrowingActivation : IApplicationActivationService
    {
        public bool Touched { get; private set; }
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) => throw Touch();
        public Task<ApplicationActivationPreview> PreviewAsync(
            ApplicationActivationRequest request,
            ApplicationActivationContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        public Task<ApplicationActivationReceipt> ActivateAsync(
            ApplicationActivationRequest request,
            ApplicationActivationContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        private Exception Touch()
        {
            Touched = true;
            return new InvalidOperationException("Activation service must not be reached.");
        }
    }

    private sealed class ThrowingStateSpaceAdministration : IStateSpaceAdministrationService
    {
        public bool Touched { get; private set; }
        public StateSpaceBindingSummary? Get(string stateSpaceId) => throw Touch();
        public IReadOnlyList<StateSpaceBindingSummary> List(ApplicationIdentifier applicationId, int limit) => throw Touch();
        public Task<StateSpaceCreationPreview> PreviewCreateAsync(
            StateSpaceCreationRequest request,
            StateSpaceCreationContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        public Task<StateSpaceCreationReceipt> CreateAsync(
            StateSpaceCreationRequest request,
            StateSpaceCreationContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        public Task<StateSpaceUpgradePreview> PreviewUpgradeAsync(
            StateSpaceUpgradeRequest request,
            StateSpaceUpgradeContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        public Task<StateSpaceUpgradeReceipt> UpgradeAsync(
            StateSpaceUpgradeRequest request,
            StateSpaceUpgradeContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        private Exception Touch()
        {
            Touched = true;
            return new InvalidOperationException("State-space service must not be reached.");
        }
    }

    private sealed class ThrowingLegacyAdoption : ILegacyStateAdoptionService
    {
        public bool Touched { get; private set; }
        public Task<LegacyStateAdoptionPreview> PreviewAsync(
            LegacyStateAdoptionRequest request,
            LegacyStateAdoptionContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        public Task<LegacyStateAdoptionReceipt> AdoptAsync(
            LegacyStateAdoptionRequest request,
            LegacyStateAdoptionContext context,
            CancellationToken cancellationToken = default) => throw Touch();
        private Exception Touch()
        {
            Touched = true;
            return new InvalidOperationException("Legacy adoption must not be reached.");
        }
    }

    private static Task<ToolEnvelope> QueryImpactAsync(
        DantesRoleplayDbContext db,
        IProjectionImpactService impacts,
        IPrivateOperatorRequestAuthorizer authorization,
        int limit) => new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: "system.dependencies", applicationId: "fixture-app", limit: limit,
            projectionImpacts: impacts, privateOperator: authorization);

    private sealed class StaticImpact : IProjectionImpactService
    {
        public ProjectionImpactReport Analyze(
            ApplicationIdentifier applicationId,
            string? rootId = null,
            bool transitive = true)
        {
            var nodes = new[]
            {
                new ProjectionImpactNode("component:fixture-app.stats@1#/score", "component-field",
                    "fixture-app.stats", 1, new string('A', 64), "/score"),
                new ProjectionImpactNode("projection:fixture-app.view@1", "projection",
                    "fixture-app.view", 1, new string('B', 64), null)
            };
            var edges = new[]
            {
                new ProjectionImpactEdge(nodes[0].Id, nodes[1].Id, "reads-component-field"),
                new ProjectionImpactEdge(nodes[1].Id, nodes[1].Id, "depends-on-projection")
            };
            return new(applicationId, new string('F', 64), null, transitive,
                nodes, edges, []);
        }
    }

    private static Task<ToolEnvelope> QueryPreviewAsync(
        DantesRoleplayDbContext db,
        IApplicationPreviewService preview,
        IPrivateOperatorRequestAuthorizer authorization,
        int limit) => new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: "system.application-preview", applicationId: "fixture-app", limit: limit,
            applicationPreviews: preview, privateOperator: authorization);

    private sealed class StaticPreview : IApplicationPreviewService
    {
        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            CancellationToken cancellationToken = default)
        {
            var winners = new[]
            {
                new EffectiveSourceDocument("file:a.txt", "source", SourceTrust.Trusted, 1,
                    "a.txt", "text/plain", new string('A', 64), 1, true),
                new EffectiveSourceDocument("file:b.txt", "source", SourceTrust.Trusted, 1,
                    "b.txt", "text/plain", new string('B', 64), 1, true)
            };
            return Task.FromResult(new ApplicationPreviewResult(
                applicationId, 1, new string('C', 64), new string('D', 64), new string('E', 64), new string('F', 64),
                true, [], winners, [], []));
        }

        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            IReadOnlyList<string> sourceIds,
            CancellationToken cancellationToken = default) => PreviewAsync(applicationId, cancellationToken);
    }
}
