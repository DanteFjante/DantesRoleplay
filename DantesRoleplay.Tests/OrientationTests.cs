using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class OrientationTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Orientation_is_built_from_authorized_live_descriptors_and_runtime_scope()
    {
        await using var db = fixture.CreateContext();
        var applications = new InMemoryApplicationRegistry();
        var app = ApplicationIdentifier.Parse("fixture");
        var revision = applications.Register(new(app, "Fixture", "Fixture application.", []));
        var states = new States(new("space.fixture", app, revision.Revision, revision.Fingerprint,
            new string('A', 64), 2, new string('B', 64), null, null));
        var authorization = new Authorizer();

        var result = await new OrientMcpTool().OrientAsync(
            new OperationLog(db), privateOperator: authorization, applications: applications,
            stateSpaces: states);

        Assert.True(result.Ok, JsonSerializer.Serialize(result));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        var root = document.RootElement;
        var principal = root.GetProperty("Principal");
        Assert.True(principal.GetProperty("CanRead").GetBoolean());
        Assert.True(principal.GetProperty("CanModify").GetBoolean());
        Assert.StartsWith("principal.", principal.GetProperty("Reference").GetString());

        var application = Assert.Single(root.GetProperty("Applications").GetProperty("Items").EnumerateArray());
        Assert.Equal("fixture", application.GetProperty("Id").GetString());
        Assert.Equal("space.fixture", Assert.Single(application.GetProperty("StateSpaces").EnumerateArray())
            .GetProperty("Id").GetString());

        var families = root.GetProperty("CapabilityFamilies").EnumerateArray().ToArray();
        Assert.Contains(families, value => value.GetProperty("Id").GetString() == "read-query");
        Assert.Contains(families, value => value.GetProperty("Id").GetString() == "direct-execution");
        Assert.Contains(families, value => value.GetProperty("Id").GetString() == "planned-interaction");
        Assert.DoesNotContain(families, value => value.GetProperty("Id").GetString() == "draft-authoring");
        var activeIds = families.SelectMany(value => value.GetProperty("Capabilities").EnumerateArray())
            .Where(value => value.GetProperty("Interface").GetString() == "mcp")
            .Select(value => value.GetProperty("Id").GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(McpVerbCatalog.Descriptors.Select(value => value.Id).ToHashSet(StringComparer.Ordinal), activeIds);
        Assert.Contains(activeIds, value => value == "mcp.commit.application.action.execute");
        Assert.DoesNotContain("mcp.commit.action", activeIds);
        Assert.All(families.SelectMany(value => value.GetProperty("Capabilities").EnumerateArray()), value =>
        {
            Assert.Matches("^[0-9A-F]{64}$", value.GetProperty("Fingerprint").GetString());
            Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("Schemas").GetProperty("ReadFrom").GetString()));
        });

        Assert.Empty(root.GetProperty("Limitations")
            .GetProperty("DeprecatedCapabilities").EnumerateArray());
        Assert.All(result.NextSteps, step => Assert.DoesNotContain("commit(kind: \"action\")", step));

        Assert.NotEmpty(result.NextActions);
        var validator = new BoundedJsonSchemaValidator();
        foreach (var action in result.NextActions)
        {
            var descriptor = Assert.Single(McpVerbCatalog.Descriptors,
                value => value.Id == action.CapabilityId);
            Assert.Equal(descriptor.Fingerprint, action.CapabilityFingerprint);
            Assert.Equal(descriptor.Input.SchemaHash, action.InputSchemaHash);
            Assert.Equal(SchemaValueStatus.Valid,
                validator.Validate(descriptor.Input.SchemaJson, action.Arguments.ToJsonString()).Status);

            var known = action.KnownArguments.Select(value => value.Key).ToHashSet(StringComparer.Ordinal);
            var missing = action.MissingArguments.Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Empty(known.Intersect(missing, StringComparer.Ordinal));
            Assert.All(action.RequiredArguments,
                value => Assert.True(known.Contains(value) || missing.Contains(value), value));
            Assert.Equal(action.MissingArguments.Count == 0, action.Ready);
            Assert.All(action.KnownArguments, value =>
                Assert.True(JsonNode.DeepEquals(value.Value, action.Arguments[value.Key])));
        }

        var plan = Assert.Single(result.NextActions,
            value => value.CapabilityId == "mcp.query.system.interaction-plan");
        Assert.Equal("fixture", plan.KnownArguments["applicationId"]!.GetValue<string>());
        Assert.Equal("request", Assert.Single(plan.MissingArguments).Name);
        Assert.False(plan.Ready);
    }

    [Fact]
    public async Task Orientation_does_not_advertise_protected_capabilities_or_state_spaces_when_read_is_denied()
    {
        await using var db = fixture.CreateContext();
        var applications = new InMemoryApplicationRegistry();
        var app = ApplicationIdentifier.Parse("fixture");
        var revision = applications.Register(new(app, "Fixture", "Fixture application.", []));
        var result = await new OrientMcpTool().OrientAsync(new OperationLog(db),
            privateOperator: new Authorizer(allowed: false), applications: applications,
            stateSpaces: new States(new("space.fixture", app, revision.Revision, revision.Fingerprint,
                new string('A', 64), 1, new string('B', 64), null, null)));

        Assert.True(result.Ok);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        var root = document.RootElement;
        Assert.Empty(root.GetProperty("CapabilityFamilies").EnumerateArray());
        Assert.Equal("denied", root.GetProperty("Applications").GetProperty("Status").GetString());
        Assert.Empty(root.GetProperty("Applications").GetProperty("Items").EnumerateArray());
        Assert.Empty(result.NextSteps);
        Assert.Empty(result.NextActions);
    }

    [Fact]
    public async Task Direct_application_action_returns_a_current_schema_valid_next_action()
    {
        await using var db = fixture.CreateContext();
        var result = await new ApplicationActionExecutionHandler().ExecuteAsync(
            new SuccessfulActionRunner(), new Authorizer(), new OperationLog(db),
            JsonSerializer.Serialize(new
            {
                idempotencyKey = "execute.fixture.1",
                applicationId = "fixture",
                stateSpaceId = "space.fixture",
                qualifiedMechanicId = "fixture.mechanic.test",
                mechanicVersion = 1,
                contentFingerprint = new string('A', 64),
                roleEntityIds = new { },
                input = new { }
            }), "test exact action", null, CancellationToken.None);

        Assert.True(result.Ok, JsonSerializer.Serialize(result));
        var action = Assert.Single(result.NextActions);
        Assert.Equal("mcp.query.entities", action.CapabilityId);
        Assert.True(action.Ready);
        var descriptor = Assert.Single(McpVerbCatalog.Descriptors,
            value => value.Id == action.CapabilityId);
        var validator = new BoundedJsonSchemaValidator();
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(descriptor.Input.SchemaJson, action.Arguments.ToJsonString()).Status);

        var execute = Assert.Single(McpVerbCatalog.Descriptors,
            value => value.Id == "mcp.commit.application.action.execute");
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(execute.Output.SchemaJson,
                JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))).Status);
    }

    private sealed class Authorizer(bool allowed = true) : IPrivateOperatorRequestAuthorizer
    {
        private readonly TrustedPrincipalContext principal = PrivateOperatorPrincipal.Create("test", "orientation");

        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
        {
            PrivateOperatorCapabilityNames.TryGetAuditName(capability, out var name);
            var evidence = new AuthorizationAuditEvidence(principal.PrincipalId, principal.AuthenticationMethod,
                name, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "orientation", allowed,
                allowed ? "PRIVATE_OPERATOR_ALLOWED" : "PRIVATE_OPERATOR_DENIED");
            return new(allowed, evidence.ReasonCode, allowed ? "" : "Authenticate and retry.", evidence);
        }
    }

    private sealed class States(StateSpaceBindingSummary value) : IStateSpaceAdministrationReader
    {
        public StateSpaceBindingSummary? Get(string stateSpaceId) =>
            stateSpaceId == value.StateSpaceId ? value : null;

        public IReadOnlyList<StateSpaceBindingSummary> List(ApplicationIdentifier applicationId, int limit) =>
            applicationId == value.ApplicationId ? [value] : [];
    }

    private sealed class SuccessfulActionRunner : IApplicationActionRunner
    {
        public Task<ApplicationActionExecutionResult> RunAsync(
            ApplicationActionExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationActionExecutionResult(
                ApplicationActionExecutionDisposition.Succeeded,
                request.ExecutionIdentity.OperationId,
                request.QualifiedMechanicId,
                request.ContentFingerprint,
                request.Seed,
                "Executed.",
                0,
                [])
            {
                MechanicVersion = request.MechanicVersion,
                AffectedEntityIds = ["entity.fixture"]
            });
    }
}
