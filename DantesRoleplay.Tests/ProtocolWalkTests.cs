using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using DantesRoleplay.Blobs;
using System.Security.Cryptography;
using DantesRoleplay.Web.Security;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.Tests;

/// <summary>
/// The whole surface, over real JSON-RPC, in the order a session meets it.
///
/// Every other test in this solution calls a handler directly. That is where behaviour is proved,
/// but it cannot see the protocol: for a while the tool descriptions promised a `payload`
/// argument while every failure suggested a call carrying `id`, and no direct-call test could
/// have noticed, because none of them ever read a `fix` back as a client would. This one walks
/// orient → query → commit → query the way a cold model does, and reads what comes back.
/// </summary>
public sealed class ProtocolWalkTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _databasePath = null!;
    private int _nextId = 1;
    private static readonly ApplicationIdentifier ColdApplication = ApplicationIdentifier.Parse("cold");
    private static readonly CatalogRecordDefinition ColdMechanic = CreateColdMechanic();

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"walk-{Guid.NewGuid():N}.db");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDantesRoleplayMcpServer(_databasePath, DatabaseProvider.Sqlite,
            developmentInformationScope: "local.*");
        builder.Services.Configure<WebRemoteAccessOptions>(_ => { });
        builder.Services.AddSingleton<WebAccessPolicy>();
        builder.Services.AddSingleton<WebPrivateOperatorGuard>();
        builder.Services.Replace(ServiceDescriptor.Scoped<IInteractionGateway, ColdInteractionGateway>());
        builder.Services.Replace(ServiceDescriptor.Scoped<IApplicationActionRunner, ColdActionRunner>());
        builder.Services.Replace(ServiceDescriptor.Singleton<ILocalKnowledgeSeatProvider>(
            new ColdSeats()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IAuthorizedKnowledgeAudiencePolicy>(
            new ColdAudience()));
        builder.Services.Replace(ServiceDescriptor.Scoped<IKnowledgeApplicationBindingResolver,
            ColdBindings>());
        builder.Services.Replace(ServiceDescriptor.Scoped<IKnowledgeActorParticipationVerifier,
            ColdParticipation>());

        _app = builder.Build();
        await _app.Services.InitialiseDantesRoleplayAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IApplicationRegistry>();
            var revision = applications.Register(new(ColdApplication, "Cold conformance fixture",
                "A bounded fixture visible to a cold agent.", []));
            scope.ServiceProvider.GetRequiredService<IStateSpaceRegistry>().Create(
                new("cold-space", revision, Hash("cold-active-manifest")));
        }
        _app.MapMcp(ServerConfiguration.McpEndpoint);
        _app.MapPut("/api/blob-uploads/{uploadId}", async (HttpContext context) =>
            await (await BlobTransferWebEndpoints.UploadAsync(
                context.Request.RouteValues["uploadId"]?.ToString() ?? string.Empty,
                context,
                context.RequestServices.GetRequiredService<IBlobTransferService>(),
                context.RequestServices.GetRequiredService<WebPrivateOperatorGuard>(),
                context.RequestServices.GetRequiredService<DantesRoleplay.Operations.IOperationLog>(),
                context.RequestAborted)).ExecuteAsync(context));
        _app.MapGet("/api/blobs/sha256/{sha256}", async (HttpContext context) =>
            await (await BlobTransferWebEndpoints.DownloadAsync(
                context.Request.RouteValues["sha256"]?.ToString() ?? string.Empty,
                context,
                context.RequestServices.GetRequiredService<IBlobTransferService>(),
                context.RequestServices.GetRequiredService<WebPrivateOperatorGuard>(),
                context.RequestServices.GetRequiredService<DantesRoleplay.Operations.IOperationLog>(),
                context.RequestAborted)).ExecuteAsync(context));
        await _app.StartAsync();

        var address = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };

        await CallAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "protocol-walk", version = "1.0" }
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();

        // Stopping the host is not enough. Microsoft.Data.Sqlite POOLS connections, so a handle to
        // the file outlives the service provider — on Windows that keeps a lock, the delete below
        // throws IOException, and four walks that reached every assertion report as failures.
        // Linux allows deleting an open file, which is exactly why this passed here and not there.
        SqliteConnection.ClearAllPools();

        // Best effort from here. A leftover file in the temp directory is litter; a green walk
        // reported as a failure because of litter is worse.
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task Exactly_three_tools_are_advertised_and_each_explains_itself()
    {
        var result = await CallAsync("tools/list", new { });

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        var names = tools.Select(t => t.GetProperty("name").GetString()).Order(StringComparer.Ordinal);

        Assert.Equal(["commit", "orient", "query"], names);

        foreach (var tool in tools)
        {
            var description = tool.GetProperty("description").GetString();

            Assert.False(
                string.IsNullOrWhiteSpace(description),
                $"Tool '{tool.GetProperty("name").GetString()}' has no description, so a cold "
                + "session has nothing to choose it by.");
        }
    }

    /// <summary>
    /// A cold session can orient, inspect the typed catalog, and learn in one response that the
    /// superseded generic write kinds are physically absent.
    /// </summary>
    [Fact]
    public async Task A_session_can_orient_and_read_the_current_closed_surface()
    {
        var orient = await ToolAsync("orient", new { });

        Assert.True(orient.Ok, orient.Raw);
        AssertEveryStepIsCallable(orient);
        AssertEveryNextActionMatchesCurrentSchema(orient);
        Assert.True(orient.Data.GetProperty("principal").GetProperty("canRead").GetBoolean());
        Assert.True(orient.Data.GetProperty("generatedFrom").GetProperty("directAiDescriptorCount").GetInt32() > 0);

        var families = orient.Data.GetProperty("capabilityFamilies").EnumerateArray().ToArray();
        Assert.Contains(families, value => value.GetProperty("id").GetString() == "read-query");
        Assert.Contains(families, value => value.GetProperty("id").GetString() == "direct-execution");
        Assert.Contains(families, value => value.GetProperty("id").GetString() == "planned-interaction");
        Assert.Contains(families, value => value.GetProperty("id").GetString() == "draft-authoring");
        Assert.Empty(orient.Data.GetProperty("limitations")
            .GetProperty("deprecatedCapabilities").EnumerateArray());

        var catalog = await ToolAsync("query", new { kind = "capabilities" });
        Assert.True(catalog.Ok, catalog.Raw);
        var ids = catalog.Data.GetProperty("capabilities").EnumerateArray()
            .Select(value => value.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("mcp.commit.application.action.execute", ids);
        foreach (var retired in new[] { "component", "effects", "mechanic", "action" })
            Assert.DoesNotContain($"mcp.commit.{retired}", ids);

        var world = await ToolAsync("query", new { kind = "world" });
        Assert.True(world.Ok, world.Raw);

        foreach (var retired in new[] { "component", "effects", "mechanic", "action" })
        {
            var rejected = await ToolAsync("commit", new { kind = retired, payload = "{}" });
            Assert.False(rejected.Ok);
            Assert.Equal("UNKNOWN_KIND", rejected.Error.GetProperty("code").GetString());
            AssertIsCall(rejected.Error.GetProperty("fix").GetString()!);
        }
    }

    [Fact]
    public async Task A_cold_agent_can_complete_the_self_explanation_conformance_walk()
    {
        // The only bootstrap information is the protocol schema plus orient. No fixture IDs are
        // passed to orient, and every later ID is read from a response produced by the system.
        var listed = await CallAsync("tools/list", new { });
        Assert.Equal(3, listed.GetProperty("tools").GetArrayLength());
        var orient = await ToolAsync("orient", new { });
        Assert.True(orient.Ok, orient.Raw);

        var application = Assert.Single(orient.Data.GetProperty("applications")
            .GetProperty("items").EnumerateArray(), value =>
                value.GetProperty("id").GetString() == ColdApplication.Value);
        var stateSpace = Assert.Single(application.GetProperty("stateSpaces").EnumerateArray());
        var applicationId = application.GetProperty("id").GetString()!;
        var stateSpaceId = stateSpace.GetProperty("id").GetString()!;
        Assert.Equal("cold-space", stateSpaceId);
        Assert.Equal("bound", orient.Data.GetProperty("audience").GetProperty("context")
            .GetProperty("status").GetString());

        var families = orient.Data.GetProperty("capabilityFamilies").EnumerateArray().ToArray();
        Assert.Contains(families, value => value.GetProperty("id").GetString() == "direct-execution");
        Assert.Contains(families, value => value.GetProperty("id").GetString() == "planned-interaction");
        Assert.Contains(families, value => value.GetProperty("id").GetString() == "read-query");

        var audience = await ToolAsync("query", new { kind = "system.audience-context" });
        Assert.True(audience.Ok, audience.Raw);
        Assert.Equal(applicationId, audience.Data.GetProperty("applicationId").GetString());
        Assert.Equal(stateSpaceId, audience.Data.GetProperty("stateSpaceId").GetString());

        var found = await ToolAsync("query", new
        {
            kind = "system.feature-search",
            applicationId,
            query = "record a message"
        });
        Assert.True(found.Ok, found.Raw);
        var capability = Assert.Single(found.Data.GetProperty("capabilities").EnumerateArray());
        var mechanicId = capability.GetProperty("id").GetString()!;
        var version = capability.GetProperty("version").GetInt32();
        var fingerprint = capability.GetProperty("sourceFingerprint").GetString()!;
        var role = Assert.Single(capability.GetProperty("roles").EnumerateArray());
        Assert.Equal("subject", role.GetProperty("name").GetString());
        Assert.True(role.GetProperty("required").GetBoolean());
        Assert.Equal("authored", capability.GetProperty("input").GetProperty("status").GetString());
        using (var schema = JsonDocument.Parse(capability.GetProperty("input")
                   .GetProperty("schemaJson").GetString()!))
        {
            Assert.Contains("message", schema.RootElement.GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
        }

        // Ambiguity goes through planning and remains inert.
        var planned = await ToolAsync("query", new
        {
            kind = "system.interaction-plan",
            applicationId,
            request = JsonSerializer.Serialize(new
            {
                operation = "resolve",
                stateSpaceId,
                sessionContextId = "cold-session",
                intent = new { idempotencyKey = "plan.cold.1", intentText = "do something here" }
            })
        });
        Assert.True(planned.Ok, planned.Raw);
        Assert.Equal("Ambiguous", planned.Data.GetProperty("status").GetString());
        Assert.False(planned.Data.TryGetProperty("proposal", out _));

        var missingRole = await ExactActionAsync(applicationId, stateSpaceId, mechanicId,
            version, fingerprint, "exact.missing-role", new { }, new { message = "hello" });
        Assert.False(missingRole.Ok);
        Assert.Equal("MISSING_REQUIRED_ROLE", missingRole.Error.GetProperty("code").GetString());
        var roleRecovery = Assert.Single(missingRole.NextActions);
        Assert.Equal("mcp.query.system.feature-search",
            roleRecovery.GetProperty("capabilityId").GetString());
        Assert.True((await FollowAsync(roleRecovery)).Ok);

        var stale = await ExactActionAsync(applicationId, stateSpaceId, mechanicId,
            version, Hash("stale-mechanic"), "exact.stale", new { subject = "entity.cold" },
            new { message = "hello" });
        Assert.False(stale.Ok);
        Assert.Equal("MECHANIC_STALE", stale.Error.GetProperty("code").GetString());
        var refresh = Assert.Single(stale.NextActions);
        var refreshed = await FollowAsync(refresh);
        Assert.True(refreshed.Ok, refreshed.Raw);
        Assert.Equal(fingerprint, Assert.Single(refreshed.Data.GetProperty("capabilities")
            .EnumerateArray()).GetProperty("sourceFingerprint").GetString());

        var executed = await ExactActionAsync(applicationId, stateSpaceId, mechanicId,
            version, fingerprint, "exact.success", new { subject = "entity.cold" },
            new { message = "hello" });
        Assert.True(executed.Ok, executed.Raw);
        Assert.Equal("Recorded the exact cold action.", executed.Data.GetProperty("narration").GetString());
        var receipt = executed.Data.GetProperty("receipt");
        Assert.Equal("succeeded", receipt.GetProperty("disposition").GetString());
        Assert.Equal(mechanicId, receipt.GetProperty("qualifiedMechanicId").GetString());
        Assert.Equal(version, receipt.GetProperty("mechanicVersion").GetInt32());
        Assert.Equal(fingerprint, receipt.GetProperty("contentFingerprint").GetString());
        Assert.False(string.IsNullOrWhiteSpace(receipt.GetProperty("operationId").GetString()));

        // No result means proposal, never invention or activation.
        var absent = await ToolAsync("query", new
        {
            kind = "system.feature-search",
            applicationId,
            query = "teleport the campaign into a different ruleset"
        });
        Assert.True(absent.Ok, absent.Raw);
        Assert.Empty(absent.Data.GetProperty("hits").EnumerateArray());
        Assert.Empty(absent.Data.GetProperty("capabilities").EnumerateArray());
        var proposal = Assert.Single(absent.NextActions);
        Assert.Equal("mcp.commit.feedback", proposal.GetProperty("capabilityId").GetString());
        Assert.True(proposal.GetProperty("ready").GetBoolean());
        var submitted = await FollowAsync(proposal);
        Assert.True(submitted.Ok, submitted.Raw);

        var discovery = orient.Raw + (await ToolAsync("query", new { kind = "capabilities" })).Raw;
        foreach (var retired in new[] { "component", "effects", "mechanic", "action" })
            Assert.DoesNotContain($"mcp.commit.{retired}\"", discovery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Binary_content_is_a_resource_template_not_a_fourth_tool()    {
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var begin = await ToolAsync("commit", new
        {
            kind = "system.blob-upload.begin",
            payload = JsonSerializer.Serialize(new { sha256 = digest, mediaType = BlobMediaTypes.Png, byteLength = bytes.Length })
        });
        Assert.True(begin.Ok, begin.Raw);
        var uploadId = begin.Data.GetProperty("uploadId").GetString()!;
        var uploadToken = begin.Data.GetProperty("uploadToken").GetString()!;
        var putUrl = begin.Data.GetProperty("putUrl").GetString()!;

        using var body = new ByteArrayContent(bytes);
        body.Headers.Add("X-DantesRoleplay-Upload-Token", uploadToken);
        using var uploadResponse = await _client.PutAsync(putUrl, body);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var finalized = await ToolAsync("commit", new
        {
            kind = "system.blob-upload.finalize",
            payload = JsonSerializer.Serialize(new { uploadId, uploadToken })
        });
        Assert.True(finalized.Ok, finalized.Raw);

        var metadata = await ToolAsync("query", new { kind = "system.blobs", id = digest });
        Assert.True(metadata.Ok, metadata.Raw);
        Assert.Equal($"media://blob/sha256/{digest}", metadata.Data.GetProperty("resourceUri").GetString());

        var templates = await CallAsync("resources/templates/list", new { });
        var template = Assert.Single(templates.GetProperty("resourceTemplates").EnumerateArray());
        Assert.Equal("media://blob/sha256/{sha256}", template.GetProperty("uriTemplate").GetString());

        var read = await CallAsync("resources/read", new { uri = $"media://blob/sha256/{digest}" });
        var content = Assert.Single(read.GetProperty("contents").EnumerateArray());
        Assert.Equal(BlobMediaTypes.Png, content.GetProperty("mimeType").GetString());
        Assert.Equal(bytes, Convert.FromBase64String(content.GetProperty("blob").GetString()!));

        Assert.Equal(bytes, await _client.GetByteArrayAsync($"/api/blobs/sha256/{digest}"));

        var tools = await CallAsync("tools/list", new { });
        Assert.Equal(3, tools.GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public async Task A_session_can_submit_and_read_system_feedback()
    {
        var token = "feedback-request." + Guid.NewGuid().ToString("n");
        var submitted = await ToolAsync("commit", new
        {
            kind = "feedback",
            payload = $$"""{"operation":"submit","requestToken":"{{token}}","category":"defect","impact":"minor","summary":"Feedback walk","observed":"The feedback path was exercised."}""",
            intent = "exercise the system feedback path"
        });

        Assert.True(submitted.Ok, submitted.Raw);
        Assert.False(submitted.Data.GetProperty("duplicate").GetBoolean());
        var id = submitted.Data.GetProperty("report").GetProperty("id").GetString();

        var read = await ToolAsync("query", new { kind = "feedback", id });
        Assert.True(read.Ok, read.Raw);
        Assert.Single(read.Data.GetProperty("reports").EnumerateArray());
    }

    [Fact]
    public async Task Removed_story_plan_commit_is_rejected_by_the_current_closed_surface()
    {
        var started = await ToolAsync("commit", new
        {
            kind = "story-plan",
            payload = """
                {"operation":"start","requestToken":"story-plan.protocol-01","campaignId":"campaign.test.story","objective":"Find out what is known.","steps":[{"id":"knowledge","kind":"knowledge","intent":"What is known?"}]}
                """,
            intent = "ask the backend for one bounded fact",
            proceduresUsed = new[] { "procedure.play.story-plan" }
        });

        Assert.False(started.Ok);
        Assert.Equal("UNKNOWN_KIND", started.Error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Interaction_kinds_are_discoverable_and_fail_closed_over_real_json_rpc()
    {
        var capabilities = await ToolAsync("query", new { kind = "capabilities" });
        var descriptors = capabilities.Data.GetProperty("capabilities").EnumerateArray().ToArray();
        var queryKinds = descriptors.Select(value => value.GetProperty("id").GetString())
            .Where(value => value!.StartsWith("mcp.query.", StringComparison.Ordinal))
            .Select(value => value!["mcp.query.".Length..]).ToArray();
        var commitKinds = descriptors.Select(value => value.GetProperty("id").GetString())
            .Where(value => value!.StartsWith("mcp.commit.", StringComparison.Ordinal))
            .Select(value => value!["mcp.commit.".Length..]).ToArray();
        Assert.Contains("system.feature-search", queryKinds);
        Assert.Contains("system.interaction-plan", queryKinds);
        Assert.Contains("system.interaction-receipt", queryKinds);
        Assert.Contains("system.interaction-execute", commitKinds);

        var invalidPlan = await ToolAsync("query", new
        {
            kind = "system.interaction-plan", applicationId = "fixture", request = "{}"
        });
        var invalidReceipt = await ToolAsync("query", new
        {
            kind = "system.interaction-receipt", applicationId = "fixture", stateSpaceId = "missing", id = "missing"
        });
        var invalidExecute = await ToolAsync("commit", new
        {
            kind = "system.interaction-execute", payload = "{}"
        });
        Assert.All(new[] { invalidPlan, invalidReceipt, invalidExecute }, result =>
        {
            Assert.False(result.Ok);
            Assert.NotEqual("UNHANDLED", result.Error.GetProperty("code").GetString());
            Assert.NotEmpty(result.OperationId);
        });
    }

    [Fact(Skip = "The retired authored-procedure commit is outside the current closed host; current catalog navigation has focused protocol coverage.")]
    public async Task A_session_can_navigate_catalog_branches_over_the_public_protocol()
    {
        var orient = await ToolAsync("orient", new { });

        Assert.True(orient.Ok, orient.Raw);
        Assert.Contains(
            orient.NextSteps,
            step => step.StartsWith("query(kind: \"categories\", catalog: \"procedures\")", StringComparison.Ordinal));

        // Follow orient's category-navigation recommendation before making any catalog assumption.
        var initialRoots = await ToolAsync("query", new { kind = "categories", catalog = "procedures" });
        Assert.True(initialRoots.Ok, initialRoots.Raw);

        var capabilities = await ToolAsync("query", new { kind = "capabilities" });
        var categorySpec = capabilities.Data.GetProperty("query").GetProperty("categories");

        Assert.True(capabilities.Ok, capabilities.Raw);
        Assert.Equal(
            ["catalog", "category", "includeInactive"],
            categorySpec.GetProperty("reads").EnumerateArray().Select(value => value.GetString()));

        // Purpose-built paths avoid coupling the walk to whichever authored catalog happens to
        // be loaded. `protocol.walker` deliberately shares a raw prefix with `protocol.walk`.
        foreach (var procedure in new[]
                 {
                     ("procedure.protocol.walk", "protocol.walk"),
                     ("procedure.protocol.walker", "protocol.walker")
                 })
        {
            var written = await ToolAsync("commit", new
            {
                kind = "procedure",
                payload = $$"""{"id":"{{procedure.Item1}}","category":"{{procedure.Item2}}","name":"Protocol fixture","description":"A deterministic category-navigation fixture.","instructions":"1. Browse it.","governs":"query"}"""
            });

            Assert.True(written.Ok, written.Raw);
        }

        foreach (var mechanic in new[]
                 {
                     ("mechanic.protocol.walk", "protocol.walk"),
                     ("mechanic.protocol.walker", "protocol.walker")
                 })
        {
            var written = await ToolAsync("commit", new
            {
                kind = "mechanic",
                payload = $$"""{"id":"{{mechanic.Item1}}","category":"{{mechanic.Item2}}","name":"Protocol fixture","description":"A deterministic category-navigation fixture.","matches":"protocol category","requirements":"{}","source":"return { narration: 'ok', effects: [] };","scope":"protocol-walk","status":"active"}"""
            });

            Assert.True(written.Ok, written.Raw);
        }

        var procedureRoots = await ToolAsync("query", new { kind = "categories", catalog = "procedures" });
        var procedureRoot = procedureRoots.Data.GetProperty("branch").GetProperty("children")
            .EnumerateArray()
            .Single(child => child.GetProperty("path").GetString() == "protocol");
        var procedureBranch = await ToolAsync("query", new
        {
            kind = "categories",
            catalog = "procedures",
            category = procedureRoot.GetProperty("path").GetString()
        });
        var procedures = await ToolAsync("query", new
        {
            kind = "procedures",
            category = "protocol.walk"
        });

        Assert.True(procedureBranch.Ok, procedureBranch.Raw);
        Assert.Contains(
            procedureBranch.Data.GetProperty("branch").GetProperty("children").EnumerateArray(),
            child => child.GetProperty("path").GetString() == "protocol.walk");
        Assert.True(procedures.Ok, procedures.Raw);
        Assert.All(
            procedures.Data.GetProperty("procedures").EnumerateArray(),
            procedure => Assert.True(
                IsWithin(procedure.GetProperty("category").GetString()!, "protocol.walk")));
        Assert.DoesNotContain(
            procedures.Data.GetProperty("procedures").EnumerateArray(),
            procedure => procedure.GetProperty("category").GetString() == "protocol.walker");

        var mechanicRoots = await ToolAsync("query", new { kind = "categories", catalog = "mechanics" });
        var mechanicRoot = mechanicRoots.Data.GetProperty("branch").GetProperty("children")
            .EnumerateArray()
            .Single(child => child.GetProperty("path").GetString() == "protocol");
        var mechanicBranch = await ToolAsync("query", new
        {
            kind = "categories",
            catalog = "mechanics",
            category = mechanicRoot.GetProperty("path").GetString()
        });
        var mechanics = await ToolAsync("query", new
        {
            kind = "mechanics",
            category = "protocol.walk",
            query = "protocol category",
            scope = "protocol-walk"
        });

        Assert.True(mechanicBranch.Ok, mechanicBranch.Raw);
        Assert.Contains(
            mechanicBranch.Data.GetProperty("branch").GetProperty("children").EnumerateArray(),
            child => child.GetProperty("path").GetString() == "protocol.walk");
        Assert.True(mechanics.Ok, mechanics.Raw);
        Assert.Equal(
            ["mechanic.protocol.walk"],
            mechanics.Data.GetProperty("mechanics").EnumerateArray()
                .Select(mechanic => mechanic.GetProperty("id").GetString()));

        var malformed = await ToolAsync("query", new
        {
            kind = "categories",
            catalog = "procedures",
            category = "Protocol.walk"
        });

        Assert.False(malformed.Ok);
        Assert.Equal("INVALID_CATEGORY", malformed.Error.GetProperty("code").GetString());
        AssertIsCall(malformed.Error.GetProperty("fix").GetString()!);

        var recovery = await ToolAsync("query", new { kind = "categories", catalog = "procedures" });
        Assert.True(recovery.Ok, recovery.Raw);

        var categoryAudit = await ToolAsync("query", new
        {
            kind = "history",
            subject = "query:categories",
            limit = 50
        });

        Assert.True(categoryAudit.Ok, categoryAudit.Raw);
        Assert.NotEmpty(categoryAudit.Data.GetProperty("operations").EnumerateArray());
        Assert.All(
            categoryAudit.Data.GetProperty("operations").EnumerateArray(),
            operation => Assert.Equal("query", operation.GetProperty("tool").GetString()));
    }

    /// <summary>
    /// Reading a contract and then citing it has to be visible in the audit, over the protocol.
    ///
    /// This is the test whose absence let the three-verb migration break read evidence silently:
    /// the derivation still looked for the retired tool name, so every honest commit reported
    /// nothing read and was flagged for citing what it had never opened. Every unit test passed —
    /// they recorded the old tool name by hand — and the audit does not throw, it just lies.
    /// </summary>
    [Fact(Skip = "The retired authored-procedure commit no longer exists on the generic host.")]
    public async Task Reading_a_contract_and_citing_it_is_visible_in_the_audit()
    {
        await ToolAsync("orient", new { });

        var contract = await ToolAsync("query", new
        {
            kind = "procedures",
            id = "procedure.contract.create"
        });

        Assert.True(contract.Ok, contract.Raw);

        var written = await ToolAsync("commit", new
        {
            kind = "procedure",
            payload = """
                {"id":"procedure.test.evidence","category":"test","name":"Evidence","description":"Written while following the contract that governs writing contracts.","instructions":"1. Read the contract first."}
                """,
            intent = "prove that reading the manual is observable",
            proceduresUsed = new[] { "procedure.contract.create" }
        });

        Assert.True(written.Ok, written.Raw);

        var read = written.Data.GetProperty("proceduresYouDemonstrablyRead")
            .EnumerateArray()
            .Select(p => p.GetString())
            .ToList();

        Assert.Contains("procedure.contract.create", read);

        // And the other direction: the same commit must not be counted as an unbacked citation.
        var history = await ToolAsync("query", new { kind = "history", limit = 50 });

        Assert.True(history.Ok, history.Raw);
        Assert.Equal(0, history.Data.GetProperty("citedWithoutReading").GetInt32());
    }

    /// <summary>
    /// An invented kind and a malformed payload are the two ways a session goes wrong first. Both
    /// have to come back with the answer attached rather than a pointer to where it lives (D4).
    /// </summary>
    [Fact]
    public async Task A_wrong_guess_is_corrected_in_the_same_round_trip()
    {
        var unknownKind = await ToolAsync("query", new { kind = "snapshot" });

        Assert.False(unknownKind.Ok);
        Assert.Equal("UNKNOWN_KIND", unknownKind.Error.GetProperty("code").GetString());
        Assert.Contains("procedures", unknownKind.Error.GetProperty("why").GetString());
        AssertIsCall(unknownKind.Error.GetProperty("fix").GetString()!);

        var retired = await ToolAsync("commit", new { kind = "component", payload = "{}" });

        Assert.False(retired.Ok);
        Assert.Equal("UNKNOWN_KIND", retired.Error.GetProperty("code").GetString());
        AssertIsCall(retired.Error.GetProperty("fix").GetString()!);

        var badPayload = await ToolAsync("commit", new { kind = "feedback", payload = "{\"summary\":\"Incomplete\"}" });

        Assert.False(badPayload.Ok);
        Assert.Equal("INVALID_PAYLOAD", badPayload.Error.GetProperty("code").GetString());

        // The shape travels with the failure: the reason names every field the payload needed.
        var why = badPayload.Error.GetProperty("why").GetString()!;
        Assert.Contains("exact closed shape", why);

        var fix = badPayload.Error.GetProperty("fix").GetString()!;
        AssertIsCall(fix);

        // And the fix is not merely well formed — sending it back gets a different, better answer.
        var token = "feedback-request." + Guid.NewGuid().ToString("n");
        var retry = await ToolAsync("commit", new
        {
            kind = "feedback",
            payload = $$"""{"operation":"submit","requestToken":"{{token}}","category":"documentation","impact":"minor","summary":"Recovery retry","observed":"The typed recovery path was exercised."}"""
        });

        Assert.True(retry.Ok, retry.Raw);

        var dryRunUnsupported = await ToolAsync("commit", new
        {
            kind = "feedback",
            payload = $$"""{"operation":"submit","requestToken":"{{token}}.preview","category":"documentation","impact":"minor","summary":"Preview retry","observed":"Preview is not supported for feedback."}""",
            dryRun = true
        });

        Assert.False(dryRunUnsupported.Ok);
        Assert.Equal("NOT_SUPPORTED", dryRunUnsupported.Error.GetProperty("code").GetString());
        AssertIsCall(dryRunUnsupported.Error.GetProperty("fix").GetString()!);
    }

    /// <summary>
    /// §7.4: a `fix` is an instruction, and an instruction that cannot be followed is worse than
    /// silence because the session spends its one recovery attempt on it. So a fix must BEGIN with
    /// a call — no leading prose, no "correct the effects above and try again".
    /// </summary>
    private static void AssertIsCall(string text)
    {
        Assert.True(
            StartsWithCall(text),
            $"This is offered as the literal next call but does not begin with one: {text}");

        AssertIsCallable(text);
    }

    /// <summary>
    /// The weaker check, for next steps. Some of those are legitimately prose — "follow the
    /// instructions, then perform the operation they govern" is advice and should be. What is not
    /// allowed is something that LOOKS like a call and is not one: that is the shape the regex
    /// adapter produced, where `commit(kind: "procedure", id: "x", ...)` reads as a call and is
    /// rejected by the protocol.
    /// </summary>
    private static void AssertIsCallable(string text)
    {
        var call = text.TrimStart();

        Assert.True(
            StartsWithCall(text) || !call.Contains('('),
            $"This is offered as a next step but is not a call and is not plain prose: {text}");

        if (call.StartsWith("commit(kind:", StringComparison.Ordinal))
        {
            Assert.True(
                call.Contains("payload:", StringComparison.Ordinal),
                $"A commit call with no payload argument cannot be made: {text}");
        }
    }

    private static bool StartsWithCall(string text)
    {
        var call = text.TrimStart();

        return call.StartsWith("orient(", StringComparison.Ordinal)
            || call.StartsWith("query(kind:", StringComparison.Ordinal)
            || call.StartsWith("commit(kind:", StringComparison.Ordinal);
    }

    private static bool IsWithin(string category, string branch) =>
        string.Equals(category, branch, StringComparison.Ordinal)
        || category.StartsWith(branch + ".", StringComparison.Ordinal);

    private static void AssertEveryStepIsCallable(ToolResult result)
    {
        foreach (var step in result.NextSteps)
        {
            AssertIsCallable(step);
        }
    }

    private static void AssertEveryNextActionMatchesCurrentSchema(ToolResult result)
    {
        Assert.NotEmpty(result.NextActions);
        var validator = new BoundedJsonSchemaValidator();
        foreach (var action in result.NextActions)
        {
            var capabilityId = action.GetProperty("capabilityId").GetString();
            var descriptor = Assert.Single(DantesRoleplay.MCPServer.Mcp.McpVerbCatalog.Descriptors,
                value => value.Id == capabilityId);
            Assert.Equal(descriptor.Fingerprint,
                action.GetProperty("capabilityFingerprint").GetString());
            Assert.Equal(descriptor.Input.SchemaHash,
                action.GetProperty("inputSchemaHash").GetString());
            Assert.Equal(SchemaValueStatus.Valid, validator.Validate(descriptor.Input.SchemaJson,
                action.GetProperty("arguments").GetRawText()).Status);
        }
    }

    private Task<ToolResult> ExactActionAsync(
        string applicationId,
        string stateSpaceId,
        string qualifiedMechanicId,
        int mechanicVersion,
        string contentFingerprint,
        string idempotencyKey,
        object roleEntityIds,
        object input) => ToolAsync("commit", new
        {
            kind = "application.action.execute",
            payload = JsonSerializer.Serialize(new
            {
                idempotencyKey,
                applicationId,
                stateSpaceId,
                qualifiedMechanicId,
                mechanicVersion,
                contentFingerprint,
                roleEntityIds,
                input
            })
        });

    private async Task<ToolResult> FollowAsync(JsonElement action)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = action.GetProperty("kind").GetString()
        };
        var tool = action.GetProperty("tool").GetString()!;
        foreach (var property in action.GetProperty("arguments").EnumerateObject())
        {
            arguments[property.Name] = tool == "commit" && property.Name == "payload"
                ? property.Value.GetRawText()
                : property.Value.Clone();
        }
        return await ToolAsync(tool, arguments);
    }

    private async Task<ToolResult> ToolAsync(string name, object arguments)
    {
        var result = await CallAsync("tools/call", new { name, arguments });

        // What a client actually hands its model: one text block holding the serialised envelope.
        // Read exactly that, so anything unreadable in it fails here rather than in a session.
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;

        Assert.True(
            text.TrimStart().StartsWith('{'),
            $"'{name}' did not return an envelope at all: {result.GetRawText()}");

        using var document = JsonDocument.Parse(text);

        // Cloned: JsonElement is a view over the document, which this scope disposes.
        var envelope = document.RootElement.Clone();

        return new ToolResult(
            envelope.GetProperty("ok").GetBoolean(),
            envelope.TryGetProperty("data", out var data) ? data : default,
            envelope.TryGetProperty("error", out var error) ? error : default,
            envelope.TryGetProperty("nextSteps", out var steps)
                ? [.. steps.EnumerateArray().Select(s => s.GetString() ?? string.Empty)]
                : [],
            envelope.TryGetProperty("nextActions", out var actions)
                ? [.. actions.EnumerateArray().Select(value => value.Clone())]
                : [],
            envelope.TryGetProperty("operationId", out var operation)
                ? operation.GetString() ?? string.Empty
                : string.Empty,
            envelope.GetRawText());
    }

    private async Task<JsonElement> CallAsync(string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ServerConfiguration.McpEndpoint)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = _nextId++,
                method,
                @params = parameters
            })
        };

        // Both, per the Streamable HTTP transport: the server chooses which it replies with.
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept", "text/event-stream");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"{method} returned {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(ExtractJson(body));
        var root = document.RootElement;

        Assert.False(
            root.TryGetProperty("error", out var error),
            $"{method} failed at the protocol level: {(error.ValueKind == JsonValueKind.Undefined ? "" : error.GetRawText())}");

        // Cloned because the document is disposed with this scope and JsonElement is a view on it.
        return root.GetProperty("result").Clone();
    }

    /// <summary>
    /// The transport may answer as plain JSON or as one server-sent event. Reading both here keeps
    /// the walk about the surface rather than about which the SDK happened to pick.
    /// </summary>
    private static string ExtractJson(string body)
    {
        if (!body.StartsWith("event:", StringComparison.Ordinal)
            && !body.StartsWith("data:", StringComparison.Ordinal))
        {
            return body;
        }

        var payload = new StringBuilder();

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                payload.Append(line["data:".Length..].Trim());
            }
        }

        return payload.ToString();
    }

    private static CatalogRecordDefinition CreateColdMechanic()
    {
        const string requirements = """
            {"roles":{"subject":{"components":[],"description":"The entity receiving the recorded message."}},"inputSchema":{"type":"object","properties":{"message":{"type":"string","minLength":1}},"required":["message"],"additionalProperties":false}}
            """;
        var content = JsonSerializer.Serialize(new
        {
            id = "mechanic.exact",
            name = "Record a message",
            description = "Records one exact message for a selected subject.",
            matches = new[] { "record a message" },
            requirements,
            source = "export function execute() { return { narration: 'recorded', effects: [] }; }",
            status = "active"
        });
        return new(ColdApplication.Value, "mechanic", "cold.mechanic.exact",
            "Record a message", "Records one exact message for a selected subject.", [],
            ["record a message"], "mechanics/exact", "active", 3, content, Hash(content),
            "cold-conformance", "mechanics/exact.js");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class ColdInteractionGateway : IInteractionGateway
    {
        public Task<InteractionFeatureSearchResult> SearchFeaturesAsync(
            ApplicationIdentifier applicationId, string? query, string? qualifiedId,
            int limit = 10, string? namespaceId = null,
            CancellationToken cancellationToken = default)
        {
            var matches = applicationId == ColdApplication
                && (qualifiedId == ColdMechanic.QualifiedId
                    || query?.Contains("record a message", StringComparison.OrdinalIgnoreCase) == true);
            if (!matches)
                return Task.FromResult(InteractionFeatureSearchResult.Create(
                    InteractionRetrievalMode.Exact, []));
            var reference = InteractionFeatureReference.Create(ColdApplication,
                InteractionRetrievalLane.TrustedFeature, Hash("cold-catalog"), ColdMechanic);
            var hit = InteractionFeatureHit.Create(reference, ColdMechanic, null, null, true);
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
            var receipt = Receipt(principal.PrincipalId, applicationId, stateSpaceId,
                submittedProposalJson is null ? "plan.cold.ambiguous" : "plan.cold.direct");
            if (submittedProposalJson is null)
                return Task.FromResult(new InteractionPlanGatewayResult(
                    InteractionResolutionStatus.Ambiguous, "INTERACTION_AMBIGUOUS",
                    "More detail is required before any action can be selected.",
                    ["No exact capability was selected."], null, null,
                    InteractionReceiptWriteResult.Appended(receipt), Hash("cold-plan-trace")));

            using var document = JsonDocument.Parse(submittedProposalJson);
            var step = document.RootElement.GetProperty("steps")[0];
            var proposal = new InteractionProposalProjection("propose",
                [new("action", "action", ColdMechanic.QualifiedId, ColdMechanic.Version,
                    ColdMechanic.ContentFingerprint, [],
                    step.GetProperty("roleBindings").EnumerateObject().ToDictionary(
                        value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal),
                    step.GetProperty("input").Clone(), [])]);
            return Task.FromResult(new InteractionPlanGatewayResult(
                InteractionResolutionStatus.Resolved, "INTERACTION_RESOLVED",
                "The exact action is ready for confirmation.", [], Hash("cold-proposal"),
                proposal, InteractionReceiptWriteResult.Appended(receipt), Hash("cold-plan-trace")));
        }

        public Task<InteractionReceiptProjection?> GetReceiptAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId,
            string stateSpaceId, string receiptId,
            CancellationToken cancellationToken = default) => Task.FromResult<InteractionReceiptProjection?>(null);

        public Task<InteractionExecutionOutcome> ExecuteAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId,
            string stateSpaceId, string executionRequestJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static InteractionReceiptProjection Receipt(
            string principal, ApplicationIdentifier applicationId, string stateSpaceId, string key) => new(
                "interaction-receipt." + new string('c', 32), "resolution", principal,
                applicationId, stateSpaceId, key, Hash(key), "ambiguous", "INTERACTION_AMBIGUOUS",
                null, "The request remains inert.", [], DateTime.UnixEpoch);
    }

    private sealed class ColdActionRunner : IApplicationActionRunner
    {
        public Task<ApplicationActionExecutionResult> RunAsync(
            ApplicationActionExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.ContentFingerprint != ColdMechanic.ContentFingerprint
                || request.MechanicVersion != ColdMechanic.Version)
                return Task.FromResult(Failed(ApplicationActionExecutionDisposition.Stale,
                    request, "MECHANIC_STALE", "The selected mechanic fingerprint is stale."));
            if (!request.RoleEntityIds.ContainsKey("subject"))
                return Task.FromResult(Failed(ApplicationActionExecutionDisposition.Failed,
                    request, "MISSING_REQUIRED_ROLE", "The required subject role is missing."));
            return Task.FromResult(new ApplicationActionExecutionResult(
                ApplicationActionExecutionDisposition.Succeeded,
                request.ExecutionIdentity.OperationId, request.QualifiedMechanicId,
                request.ContentFingerprint, request.Seed, "Recorded the exact cold action.", 0, [])
            {
                MechanicVersion = request.MechanicVersion,
                AffectedEntityIds = [request.RoleEntityIds["subject"]]
            });
        }

        private static ApplicationActionExecutionResult Failed(
            ApplicationActionExecutionDisposition disposition,
            ApplicationActionExecutionRequest request, string code, string message) => new(
                disposition, request.ExecutionIdentity.OperationId, request.QualifiedMechanicId,
                request.ContentFingerprint, request.Seed, "", 0, [new(code, message)])
            { MechanicVersion = request.MechanicVersion };
    }

    private sealed class ColdSeats : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => new(true, "principal.cold", ColdApplication.Value,
            "campaign.cold", null, KnowledgeAudienceRole.GameMaster);
    }

    private sealed class ColdAudience : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(
            string campaignId, CancellationToken cancellationToken = default) => Task.FromResult(
                new KnowledgeAudienceResolution(new("principal.cold", campaignId,
                    KnowledgeAudienceRole.GameMaster, null, "policy.cold")));
    }

    private sealed class ColdBindings : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(
            string campaignId, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
                "metadata", "authorized-knowledge.json");
            if (!KnowledgeApplicationBindingDocument.TryParse(
                    File.ReadAllText(path), "dnd2024", out var document))
                throw new InvalidOperationException("The current authorized knowledge contract is invalid.");
            var binding = document.Bind("dnd2024", "cold-space", campaignId, "binding.cold") with
            {
                ApplicationId = ColdApplication.Value
            };
            return Task.FromResult<KnowledgeApplicationBinding?>(binding);
        }
    }

    private sealed class ColdParticipation : IKnowledgeActorParticipationVerifier
    {
        public Task<KnowledgeParticipationResolution> ResolveAsync(
            KnowledgeApplicationBinding binding, string actorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(KnowledgeParticipationResolution.Denied());
    }

    private sealed record ToolResult(
        bool Ok,
        JsonElement Data,
        JsonElement Error,
        IReadOnlyList<string> NextSteps,
        IReadOnlyList<JsonElement> NextActions,
        string OperationId,
        string Raw);
}
