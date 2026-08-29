using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.Web.Interactions;
using DantesRoleplay.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class ApplicationMechanicWebServiceTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("fixture");
    private static readonly TrustedPrincipalContext Principal =
        PrivateOperatorPrincipal.Create("local-loopback", "web-action-test");
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Exact_descriptor_prepares_inert_direct_proposal_and_confirmed_execute_delegates()
    {
        var fixture = new Fixture();

        var descriptor = await fixture.Service.DescribeAsync(App, "space.1", fixture.Record.QualifiedId);

        Assert.Equal("mechanic.fixture", descriptor.AuthoritativeId);
        Assert.True(descriptor.RequiresConfirmation);
        Assert.Equal("not-authored", descriptor.Input.SchemaStatus);
        var role = Assert.Single(descriptor.Roles);
        Assert.Equal("subject", role.Name);
        Assert.True(role.Required);
        var component = Assert.Single(role.Components);
        Assert.Equal("stats", component.LocalId);
        Assert.Equal("fixture.stats", component.QualifiedId);
        Assert.Equal(Fixture.Schema, component.SchemaJson);
        Assert.DoesNotContain("fixture-secret-source", JsonSerializer.Serialize(descriptor, WebJson),
            StringComparison.Ordinal);

        var prepared = await fixture.Service.PrepareAsync(Principal, App, "space.1",
            fixture.Record.QualifiedId, new("prepare.1",
                new() { ["subject"] = "entity.hero" }, Element("{\"bonus\":2}")));

        Assert.True(prepared.Ready);
        Assert.True(prepared.RequiresConfirmation);
        Assert.Equal(InteractionAiRole.Direct, fixture.Gateway.PlanRole);
        Assert.NotNull(fixture.Gateway.SubmittedProposalJson);
        using (var submitted = JsonDocument.Parse(fixture.Gateway.SubmittedProposalJson!))
        {
            var step = submitted.RootElement.GetProperty("steps")[0];
            Assert.Equal(fixture.Record.QualifiedId, step.GetProperty("qualifiedId").GetString());
            Assert.Equal(fixture.Record.ContentFingerprint, step.GetProperty("fingerprint").GetString());
            Assert.Equal("entity.hero", step.GetProperty("roleBindings").GetProperty("subject").GetString());
            Assert.Equal(2, step.GetProperty("input").GetProperty("bonus").GetInt32());
        }
        Assert.Equal(0, fixture.Gateway.ExecuteCalls);

        var outcome = await fixture.Service.ExecuteAsync(Principal, App, "space.1",
            fixture.Record.QualifiedId, new(prepared.Receipt.Id, prepared.ProposalFingerprint,
                "execute.1", JsonSerializer.SerializeToElement(prepared.Proposal, WebJson)));

        Assert.True(outcome.Successful);
        Assert.Equal(1, fixture.Gateway.ExecuteCalls);
        using var execution = JsonDocument.Parse(fixture.Gateway.ExecutionJson!);
        Assert.False(execution.RootElement.GetProperty("learn").GetBoolean());
        Assert.True(execution.RootElement.GetProperty("stopOnFailure").GetBoolean());
        Assert.Equal(prepared.Receipt.Id,
            execution.RootElement.GetProperty("resolutionReceiptId").GetString());
    }

    [Fact]
    public async Task Stale_scope_invalid_input_and_tampered_route_fail_before_execution()
    {
        var stale = new Fixture(staleActivation: true);
        var staleError = await Assert.ThrowsAsync<ApplicationMechanicWebException>(() =>
            stale.Service.DescribeAsync(App, "space.1", stale.Record.QualifiedId));
        Assert.Equal("STATE_SPACE_ACTIVATION_STALE", staleError.Code);
        Assert.Equal(0, stale.Gateway.SearchCalls);

        var fixture = new Fixture();
        var inputError = await Assert.ThrowsAsync<ApplicationMechanicWebException>(() =>
            fixture.Service.PrepareAsync(Principal, App, "space.1", fixture.Record.QualifiedId,
                new("prepare.invalid", [], Element("[]"))));
        Assert.Equal("APPLICATION_ACTION_INPUT_INVALID", inputError.Code);
        Assert.Equal(0, fixture.Gateway.PlanCalls);

        var prepared = await fixture.Service.PrepareAsync(Principal, App, "space.1",
            fixture.Record.QualifiedId, new("prepare.valid",
                new() { ["subject"] = "entity.hero" }, Element("{}")));
        var proposal = JsonSerializer.SerializeToElement(prepared.Proposal, WebJson);
        var tamper = await Assert.ThrowsAsync<ApplicationMechanicWebException>(() =>
            fixture.Service.ExecuteAsync(Principal, App, "space.1", "fixture.mechanic.other",
                new(prepared.Receipt.Id, prepared.ProposalFingerprint, "execute.tampered", proposal)));
        Assert.Equal("APPLICATION_ACTION_PROPOSAL_SCOPE_MISMATCH", tamper.Code);
        Assert.Equal(0, fixture.Gateway.ExecuteCalls);
    }

    [Fact]
    public async Task Event_mechanic_is_not_projected_as_a_direct_action()
    {
        var fixture = new Fixture(requirements:
            "{\"roles\":{},\"event\":{\"mode\":\"guard\",\"types\":[\"fixture.event\"],\"components\":[],\"includeContents\":false}}");

        var error = await Assert.ThrowsAsync<ApplicationMechanicWebException>(() =>
            fixture.Service.DescribeAsync(App, "space.1", fixture.Record.QualifiedId));

        Assert.Equal("EVENT_MECHANIC_NOT_DIRECT", error.Code);
        Assert.Equal(422, error.StatusCode);
    }

    [Theory]
    [InlineData("{\"idempotencyKey\":\"prepare.closed\",\"roleEntityIds\":{},\"input\":{},\"unexpected\":true}")]
    [InlineData("{\"idempotencyKey\":\"prepare.duplicate\",\"idempotencyKey\":\"prepare.other\",\"roleEntityIds\":{},\"input\":{}}")]
    [InlineData("{\"idempotencyKey\":\"prepare.case\",\"roleEntityIds\":{},\"input\":{},\"Input\":{\"changed\":true}}")]
    [InlineData("[]")]
    public async Task Prepare_route_rejects_non_closed_or_non_object_json_before_planning(string body)
    {
        var fixture = new Fixture();

        var response = await InvokePrepareRouteAsync(fixture, Encoding.UTF8.GetBytes(body));

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl);
        Assert.Equal(0, fixture.Gateway.PlanCalls);
    }

    [Fact]
    public async Task Prepare_route_bounds_chunked_bodies_before_planning()
    {
        var fixture = new Fixture();

        var response = await InvokePrepareRouteAsync(fixture, new byte[(64 * 1024) + 1]);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal("INTERACTION_REQUEST_TOO_LARGE",
            document.RootElement.GetProperty("error").GetString());
        Assert.Equal(0, fixture.Gateway.PlanCalls);
    }

    private static async Task<HttpResponse> InvokePrepareRouteAsync(Fixture fixture, byte[] body)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb(
            "Data Source=:memory:", new ConfigurationBuilder().Build());
        builder.Services.AddSingleton(fixture.Service);
        await using var application = builder.Build();
        application.MapDantesRoleplayWeb();
        var route = Assert.Single(((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), endpoint =>
                endpoint.RoutePattern.RawText ==
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}/prepare");
        var context = new DefaultHttpContext
        {
            RequestServices = application.Services
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Host = new HostString("localhost", 6217);
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Request.RouteValues["applicationId"] = App.Value;
        context.Request.RouteValues["stateSpaceId"] = "space.1";
        context.Request.RouteValues["qualifiedMechanicId"] = fixture.Record.QualifiedId;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        await route.RequestDelegate!(context);
        return context.Response;
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Fixture
    {
        public const string Schema = "{\"type\":\"object\",\"properties\":{\"score\":{\"type\":\"integer\"}},\"additionalProperties\":false}";

        public Fixture(bool staleActivation = false, string? requirements = null)
        {
            requirements ??= "{\"roles\":{\"subject\":{\"components\":[\"stats\"],\"description\":\"The selected fixture subject.\"}}}";
            var content = JsonSerializer.Serialize(new
            {
                id = "mechanic.fixture",
                name = "Fixture action",
                description = "Runs one fixture action.",
                requirements,
                source = "fixture-secret-source",
                status = "active"
            });
            Record = new(App.Value, "mechanic", "fixture.mechanic.fixture", "Fixture action",
                "Runs one fixture action.", [], [], "mechanics/fixture", "active", 1,
                content, Hash(content), "fixture-source", "mechanics/fixture.md");
            var revision = new ApplicationRevision(App, 1, Hash("application"), []);
            var activationFingerprint = Hash("activation");
            var state = new StateSpaceView("space.1", revision, activationFingerprint, 1,
                DateTime.UnixEpoch, DateTime.UnixEpoch);
            var activation = new ActiveApplicationManifest(App, 1, revision.Revision,
                revision.Fingerprint, Hash("preview"), Hash("scan"), Hash("candidate"),
                Hash("dependencies"), staleActivation ? Hash("changed") : activationFingerprint,
                "coverage-v1", true, [], [], "operation.activation", DateTime.UnixEpoch);
            Gateway = new Gateway(Record);
            Service = new(new Spaces(state), new Activation(activation), new Types(), Gateway);
        }

        public CatalogRecordDefinition Record { get; }
        public Gateway Gateway { get; }
        public ApplicationMechanicWebService Service { get; }
    }

    private sealed class Spaces(StateSpaceView state) : IStateSpaceRegistry
    {
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == state.StateSpaceId ? state : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId,
            string? afterStateSpaceId, int limit) => new([state], null);
    }

    private sealed class Activation(ActiveApplicationManifest activation) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == activation.ApplicationId ? activation : null;
    }

    private sealed class Types : IApplicationComponentTypeRegistry
    {
        private readonly RegisteredComponentTypeVersion value = new(App, "fixture.stats", 3,
            "system-json-schema-draft-2020-12-v1", Fixture.Schema, Hash(Fixture.Schema), DateTime.UnixEpoch);
        public RegisteredComponentTypeVersion Define(ComponentTypeDefinition definition) => throw new NotSupportedException();
        public RegisteredComponentTypeVersion? Get(string qualifiedId, int version) =>
            qualifiedId == value.QualifiedId && version == value.Version ? value : null;
        public RegisteredComponentTypeVersion? GetLatest(string qualifiedId) =>
            qualifiedId == value.QualifiedId ? value : null;
        public RegisteredComponentTypeVersion? GetBySchemaHash(string qualifiedId, string profileId, string schemaHash) =>
            qualifiedId == value.QualifiedId && profileId == value.ProfileId && schemaHash == value.SchemaHash ? value : null;
        public ComponentTypeDiscoveryPage ListLatestPage(ApplicationIdentifier owner,
            string? afterQualifiedId, int limit) => new([value], null);
    }

    private sealed class Gateway(CatalogRecordDefinition record) : IInteractionGateway
    {
        private readonly string catalogFingerprint = Hash("catalog");
        public int SearchCalls { get; private set; }
        public int PlanCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public InteractionAiRole? PlanRole { get; private set; }
        public string? SubmittedProposalJson { get; private set; }
        public string? ExecutionJson { get; private set; }

        public Task<InteractionFeatureSearchResult> SearchFeaturesAsync(
            ApplicationIdentifier applicationId, string? query, string? qualifiedId,
            int limit = 10, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            var reference = InteractionFeatureReference.Create(App,
                InteractionRetrievalLane.TrustedFeature, catalogFingerprint, record);
            var hit = InteractionFeatureHit.Create(reference, record, null, null, true);
            return Task.FromResult(InteractionFeatureSearchResult.Create(
                InteractionRetrievalMode.Exact, [hit]));
        }

        public Task<InteractionPlanGatewayResult> PlanAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId,
            string stateSpaceId, string sessionContextId, string intentJson,
            string? submittedProposalJson = null, string? conversationId = null,
            InteractionAiRole role = InteractionAiRole.Outer, string? parentDelegationId = null,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            PlanRole = role;
            SubmittedProposalJson = submittedProposalJson;
            using var document = JsonDocument.Parse(submittedProposalJson!);
            var step = document.RootElement.GetProperty("steps")[0];
            var proposal = new InteractionProposalProjection("propose",
                [new("action", "action", record.QualifiedId, record.Version,
                    record.ContentFingerprint, [], new Dictionary<string, string>
                    {
                        ["subject"] = step.GetProperty("roleBindings").GetProperty("subject").GetString()!
                    }, step.GetProperty("input").Clone(), [])]);
            var receipt = ResolutionReceipt("prepare.1");
            return Task.FromResult(new InteractionPlanGatewayResult(InteractionResolutionStatus.Resolved,
                "INTERACTION_RESOLVED", "The exact action is ready for confirmation.", [],
                Hash("proposal"), proposal, InteractionReceiptWriteResult.Appended(receipt),
                Hash("trace")));
        }

        public Task<InteractionReceiptProjection?> GetReceiptAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId,
            string stateSpaceId, string receiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InteractionReceiptProjection?>(null);

        public Task<InteractionExecutionOutcome> ExecuteAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId,
            string stateSpaceId, string executionRequestJson,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            ExecutionJson = executionRequestJson;
            var receipt = ResolutionReceipt("execute.1") with
            {
                Kind = "execution",
                Status = "succeeded",
                Code = "INTERACTION_EXECUTION_SUCCEEDED"
            };
            return Task.FromResult(new InteractionExecutionOutcome(
                InteractionExecutionReceiptDisposition.Succeeded,
                "INTERACTION_EXECUTION_SUCCEEDED", "The verified interaction completed.", [],
                InteractionReceiptWriteResult.Appended(receipt), Hash("execution")));
        }

        private static InteractionReceiptProjection ResolutionReceipt(string key) => new(
            "interaction-receipt." + new string('a', 32), "resolution", Principal.PrincipalId,
            App, "space.1", key, Hash(key), "resolved", "INTERACTION_RESOLVED",
            Hash("proposal"), "The exact action is ready for confirmation.", [], DateTime.UnixEpoch);
    }
}
