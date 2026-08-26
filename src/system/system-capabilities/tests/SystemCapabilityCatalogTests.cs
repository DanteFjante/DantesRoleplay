using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using DantesRoleplay.Web.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SystemCapabilities.Tests;

public sealed class SystemCapabilityCatalogTests
{
    [Fact]
    public void Generic_host_composition_registers_the_closed_system_task_capability_set()
    {
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Data Source=:memory:");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var descriptors = catalog.Discover(Context()).Capabilities;

        Assert.Equal([
            SystemCapabilityIds.ApplicationPreview,
            SystemCapabilityIds.ApplicationActivate,
            SystemCapabilityIds.ApplicationRegister,
            SystemCapabilityIds.Applications,
            SystemCapabilityIds.ComponentTypeRegister,
            SystemCapabilityIds.Dependencies,
            SystemCapabilityIds.SourceRegister,
            SystemCapabilityIds.Sources,
            SystemCapabilityIds.StateSpaceAdoptLegacy,
            SystemCapabilityIds.StateSpaceCreate,
            SystemCapabilityIds.StateSpaceUpgrade
        ], descriptors.Select(value => value.Id).ToArray());
        Assert.Equal(4, descriptors.Count(value => value.Mode == SystemCapabilityMode.Read));
        Assert.Equal(7, descriptors.Count(value => value.Mode == SystemCapabilityMode.Write));
    }

    [Fact]
    public async Task Application_descriptor_is_exact_stable_and_read_only()
    {
        var applications = new InMemoryApplicationRegistry();
        applications.Register(new(ApplicationIdentifier.Parse("fixture-app"), "Fixture", "Fixture application", []));
        var first = Catalog(new ApplicationsSystemCapabilityHandler(applications));
        var second = Catalog(new ApplicationsSystemCapabilityHandler(applications));

        var descriptor = Assert.Single(first.Discover(Context()).Capabilities);
        var repeated = Assert.Single(second.Discover(Context()).Capabilities);
        var result = await first.ReadAsync(
            SystemCapabilityIds.Applications,
            """{"limit":25}""",
            Context());
        var mutuallyExclusive = await first.ReadAsync(
            SystemCapabilityIds.Applications,
            """{"applicationId":"fixture-app","afterApplicationId":"fixture-app","limit":25}""",
            Context());
        var unknownApplication = await first.ReadAsync(
            SystemCapabilityIds.Applications,
            """{"applicationId":"missing-app","limit":25}""",
            Context());

        Assert.Equal(SystemCapabilityIds.Applications, descriptor.Id);
        Assert.Equal(1, descriptor.Version);
        Assert.Equal("application-registry", descriptor.Owner);
        Assert.Equal(SystemCapabilityMode.Read, descriptor.Mode);
        Assert.Equal("read", descriptor.ModeName);
        Assert.Equal(PrivateOperatorCapability.Read, descriptor.RequiredCapability);
        Assert.Equal("read", descriptor.RequiredCapabilityName);
        Assert.Equal(SystemCapabilitySensitivity.PrivateOperatorMetadata, descriptor.Sensitivity);
        Assert.Equal("private-operator-metadata", descriptor.SensitivityName);
        Assert.False(descriptor.RequiresConfirmation);
        Assert.False(descriptor.RequiresIdempotencyKey);
        Assert.Equal(["procedure.system.inspect"], descriptor.ProcedureIds);
        Assert.Equal("DCCDBAFDCCC8CAC4F8BC626F3523A3FDBF85BC29024F9443130C1FFD52CAC304",
            descriptor.InputSchemaHash);
        Assert.Equal("7AF9F7DEE4A62D913995933EACA4F6CC8007C6B20EEF4D369B78D10162598BB4",
            descriptor.OutputSchemaHash);
        Assert.Equal("34CC55EAEAA39DF3E86E581F8C22EF0CF574A269C4B9AF93AFA53975B2CC5A88",
            descriptor.Fingerprint);
        Assert.Equal(descriptor.Fingerprint, repeated.Fingerprint);
        Assert.True(result.Ok);
        Assert.Equal("fixture-app", result.Data!.Value.GetProperty("applications")[0]
            .GetProperty("id").GetString());
        Assert.Equal("SYSTEM_CAPABILITY_INPUT_INVALID", mutuallyExclusive.Error?.Code);
        Assert.Equal("APPLICATION_UNKNOWN", unknownApplication.Error?.Code);
    }

    [Fact]
    public async Task Authorization_unknown_and_input_failures_never_touch_a_handler()
    {
        var handler = new RecordingHandler("system.fixture", JsonDocument.Parse("{\"value\":1}").RootElement);
        var catalog = Catalog(handler);

        var denied = await catalog.ReadAsync("system.fixture", "{}", DeniedContext());
        var unknown = await catalog.ReadAsync("system.unknown", "{}", Context());
        var invalid = await catalog.ReadAsync("system.fixture", "{\"extra\":true}", Context());

        Assert.False(denied.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", denied.Error?.Code);
        Assert.Empty(denied.DescriptorFingerprint);
        Assert.False(unknown.Ok);
        Assert.Equal("SYSTEM_CAPABILITY_UNKNOWN", unknown.Error?.Code);
        Assert.False(invalid.Ok);
        Assert.Equal("SYSTEM_CAPABILITY_INPUT_INVALID", invalid.Error?.Code);
        Assert.NotEmpty(invalid.Error!.Diagnostics);
        Assert.Equal(0, handler.Calls);
        Assert.Empty(catalog.Discover(DeniedContext()).Capabilities);
    }

    [Fact]
    public async Task Duplicate_invalid_registration_bad_output_and_handler_exception_fail_closed()
    {
        var first = new RecordingHandler("system.fixture", JsonDocument.Parse("{\"value\":1}").RootElement);
        var duplicate = new RecordingHandler("system.fixture", JsonDocument.Parse("{\"value\":1}").RootElement);
        var duplicateError = Assert.Throws<SystemCapabilityConfigurationException>(() =>
            Catalog(first, duplicate));
        var invalidError = Assert.Throws<SystemCapabilityConfigurationException>(() =>
            Catalog(new RecordingHandler("fixture.invalid", JsonDocument.Parse("{\"value\":1}").RootElement)));
        var schemaError = Assert.Throws<SystemCapabilityConfigurationException>(() =>
            Catalog(new RecordingHandler(
                "system.invalid-schema",
                JsonDocument.Parse("{\"value\":1}").RootElement,
                inputSchema: "{\"unknownKeyword\":true}")));
        var modeError = Assert.Throws<SystemCapabilityConfigurationException>(() =>
            Catalog(new RecordingHandler(
                "system.invalid-mode",
                JsonDocument.Parse("{\"value\":1}").RootElement,
                mode: SystemCapabilityMode.Write)));
        var badOutput = Catalog(new RecordingHandler(
            "system.bad-output", JsonDocument.Parse("{\"other\":1}").RootElement));
        var throwing = Catalog(new RecordingHandler("system.throwing", default, throws: true));

        var invalidOutput = await badOutput.ReadAsync("system.bad-output", "{}", Context());
        var unavailable = await throwing.ReadAsync("system.throwing", "{}", Context());

        Assert.Equal("SYSTEM_CAPABILITY_DUPLICATE", duplicateError.Code);
        Assert.Equal("SYSTEM_CAPABILITY_ID_INVALID", invalidError.Code);
        Assert.Equal("SYSTEM_CAPABILITY_INPUT_SCHEMA_INVALID", schemaError.Code);
        Assert.Equal("SYSTEM_CAPABILITY_MODE_INVALID", modeError.Code);
        Assert.Equal("SYSTEM_CAPABILITY_OUTPUT_INVALID", invalidOutput.Error?.Code);
        Assert.Null(invalidOutput.Data);
        Assert.Equal("SYSTEM_CAPABILITY_UNAVAILABLE", unavailable.Error?.Code);
        Assert.DoesNotContain("secret exception", unavailable.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Web_and_mcp_application_reads_share_one_capability_handler_without_state_change()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var applications = new InMemoryApplicationRegistry();
        applications.Register(new(ApplicationIdentifier.Parse("alpha-app"), "Alpha", "First", []));
        applications.Register(new(ApplicationIdentifier.Parse("bravo-app"), "Bravo", "Second", []));
        var catalog = Catalog(new ApplicationsSystemCapabilityHandler(applications));
        var validator = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, validator);
        var explorer = new ControlStructureExplorer(
            applications,
            new SqliteStateSpaceRegistry(db, applications),
            types,
            new SqliteEntityComponentStore(db, types, validator),
            new EmptyPublicApplicationCatalogProvider(),
            catalog);
        var authorization = Authorization();

        var web = await explorer.ListApplicationsThroughCapabilitiesAsync(authorization.Evidence, null, "2");
        var mcp = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: SystemCapabilityIds.Applications,
            limit: 2,
            privateOperator: new AllowingAuthorizer(),
            systemCapabilities: catalog);
        var exactWeb = await explorer.GetApplicationThroughCapabilitiesAsync(
            authorization.Evidence, "bravo-app");
        var exactMcp = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: SystemCapabilityIds.Applications,
            applicationId: "bravo-app",
            privateOperator: new AllowingAuthorizer(),
            systemCapabilities: catalog);

        var mcpList = JsonSerializer.SerializeToElement(mcp.Data).GetProperty("Applications");
        var mcpExact = JsonSerializer.SerializeToElement(exactMcp.Data).GetProperty("Application");
        Assert.True(mcp.Ok);
        Assert.True(exactMcp.Ok);
        Assert.Equal(web.Items.Select(value => value.Id),
            mcpList.EnumerateArray().Select(value => value.GetProperty("id").GetString()));
        Assert.Equal(exactWeb!.Id, mcpExact.GetProperty("id").GetString());
        Assert.Equal(exactWeb.Description, mcpExact.GetProperty("description").GetString());
        Assert.Equal(2, await db.Operations.CountAsync());
        Assert.Equal(2, applications.List(100).Count);
    }

    [Fact]
    public async Task Mcp_adapter_never_falls_back_to_registry_semantics_when_catalog_is_missing()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var registry = new ThrowingApplicationRegistry();

        var result = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: SystemCapabilityIds.Applications,
            applications: registry,
            privateOperator: new AllowingAuthorizer(),
            systemCapabilities: null);

        Assert.False(result.Ok);
        Assert.Equal("SYSTEM_CAPABILITY_UNAVAILABLE", result.Error?.Code);
        Assert.False(registry.Touched);
    }

    private static SystemCapabilityCatalog Catalog(params ISystemReadCapabilityHandler[] handlers) =>
        new(handlers, new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy());

    private static SystemCapabilityInvocationContext Context() =>
        new(
            PrivateOperatorPrincipal.Create("test", "operator"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "capability-test");

    private static SystemCapabilityInvocationContext DeniedContext() =>
        new(
            TrustedPrincipalContext.Unauthenticated("TEST_UNAUTHENTICATED"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "capability-test");

    private static PrivateOperatorAuthorizationDecision Authorization() =>
        new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            PrivateOperatorPrincipal.Create("test", "operator"),
            PrivateOperatorCapability.ControlRead,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "web-test"));

    private sealed class RecordingHandler(
        string id,
        JsonElement output,
        bool throws = false,
        string? inputSchema = null,
        SystemCapabilityMode mode = SystemCapabilityMode.Read) : ISystemReadCapabilityHandler
    {
        public int Calls { get; private set; }

        public SystemCapabilityRegistration Registration { get; } = new(
            id,
            1,
            "test-owner",
            "Test system capability.",
            mode,
            inputSchema ?? """{"type":"object","additionalProperties":false}""",
            """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"integer"}}}""",
            ["procedure.system.inspect"],
            PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.PrivateOperatorMetadata,
            false,
            false);

        public Task<SystemCapabilityHandlerResult> ReadAsync(
            JsonElement input,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (throws) throw new InvalidOperationException("secret exception detail");
            return Task.FromResult(SystemCapabilityHandlerResult.Success(output));
        }
    }

    private sealed class AllowingAuthorizer : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability) =>
            new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                PrivateOperatorPrincipal.Create("test", "operator"),
                capability,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                "mcp-test"));
    }

    private sealed class ThrowingApplicationRegistry : IApplicationRegistry
    {
        public bool Touched { get; private set; }
        public ApplicationRevision Register(ApplicationRegistration registration) => throw Touch();
        public ApplicationRevision? Get(ApplicationIdentifier applicationId) => throw Touch();
        public ApplicationRegistration? Describe(ApplicationIdentifier applicationId) => throw Touch();
        public IReadOnlyList<ApplicationRegistration> List(int limit) => throw Touch();
        public ApplicationDiscoveryPage ListPage(string? afterApplicationId, int limit) => throw Touch();
        private Exception Touch()
        {
            Touched = true;
            return new InvalidOperationException("Registry fallback must not run.");
        }
    }
}
