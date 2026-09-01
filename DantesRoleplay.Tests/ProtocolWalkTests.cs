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

        _app = builder.Build();
        await _app.Services.InitialiseDantesRoleplayAsync();
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
    /// The acceptance walk from VERB_MIGRATION.md B3, in one test because the steps depend on each
    /// other: the component has to exist before an effect can name it, and the entity has to exist
    /// before it can be read back.
    /// </summary>
    [Fact]
    public async Task A_session_can_orient_read_commit_and_confirm_without_leaving_the_three_verbs()
    {
        // 1. Orient. It must state what exists and hand back calls that can be made verbatim.
        var orient = await ToolAsync("orient", new { });

        Assert.True(orient.Ok, orient.Raw);
        Assert.Contains("query(kind: \"procedures\"", string.Join(" ", orient.NextSteps));
        AssertEveryStepIsCallable(orient);

        var capabilities = orient.Data.GetProperty("capabilities");
        Assert.True(capabilities.TryGetProperty("query", out _));
        Assert.True(capabilities.TryGetProperty("commit", out _));

        // 2. The catalog. This is what a session reads instead of guessing a payload shape.
        var catalog = await ToolAsync("query", new { kind = "capabilities" });

        Assert.True(catalog.Ok, catalog.Raw);
        Assert.Equal(
            DantesRoleplay.MCPServer.Mcp.McpVerbCatalog.QueryKindNames.Order(StringComparer.Ordinal),
            catalog.Data.GetProperty("query").EnumerateArray()
                .Select(value => value.GetProperty("name").GetString()).Order(StringComparer.Ordinal));

        // 3. The world, before changing it.
        var world = await ToolAsync("query", new { kind = "world" });
        Assert.True(world.Ok, world.Raw);

        // 4. Declare a component, then use it. Note the payload is a JSON string, as advertised.
        var component = await ToolAsync("commit", new
        {
            kind = "component",
            payload = """{"id":"walk.note","name":"Note","description":"A line of text about something."}""",
            intent = "declare a component for the protocol walk"
        });

        Assert.True(component.Ok, component.Raw);

        // 5. A rejection has to be recoverable. An effect naming an entity that does not exist is
        //    the ordinary mistake, and the fix must be a call, not advice.
        var rejected = await ToolAsync("commit", new
        {
            kind = "effects",
            payload = """{"effects":[{"type":"component.set","entityId":"nobody","definitionId":"walk.note","data":"{}"}]}""",
            dryRun = true
        });

        Assert.False(rejected.Ok);
        Assert.Equal("INVALID_EFFECTS", rejected.Error.GetProperty("code").GetString());
        AssertIsCall(rejected.Error.GetProperty("fix").GetString()!);

        // 6. Dry run first, exactly as every contract insists.
        // The seeded threshold mechanic advertises fixture.legacy.stats. Nothing seeds that
        // definition, so a fresh database cannot resolve the action until someone declares the
        // exact component contract it read from the mechanic.
        var stats = await ToolAsync("commit", new
        {
            kind = "component",
            payload = """{"id":"fixture.legacy.stats","name":"Stats","description":"Numbers about an entity, e.g. vigour."}"""
        });

        Assert.True(stats.Ok, stats.Raw);

        const string effects =
            """
            {"effects":[
              {"type":"entity.create","entityId":"walk.orban","name":"Orban"},
              {"type":"component.set","entityId":"walk.orban","definitionId":"walk.note","data":"{\"text\":\"carries a lantern\"}"},
              {"type":"component.set","entityId":"walk.orban","definitionId":"fixture.legacy.stats","data":"{\"vigour\":6}"}
            ]}
            """;

        var dryRun = await ToolAsync("commit", new { kind = "effects", payload = effects, dryRun = true });

        Assert.True(dryRun.Ok, dryRun.Raw);
        Assert.False(dryRun.Data.GetProperty("applied").GetBoolean());

        // 7. The identical payload, committed.
        var applied = await ToolAsync("commit", new
        {
            kind = "effects",
            payload = effects,
            intent = "create the walk's one entity",
            proceduresUsed = new[] { "procedure.world.change" }
        });

        Assert.True(applied.Ok, applied.Raw);
        Assert.Equal(3, applied.Data.GetProperty("count").GetInt32());
        Assert.NotEmpty(applied.OperationId);

        // 8. Confirm by reading back, which is what the contracts ask for and what makes the
        //    reported outcome something other than a claim.
        // Exact unscoped IDs are now reserved for application-state inspection. The generic world
        // search remains the correct read-back route for an entity created through generic effects.
        var entities = await ToolAsync("query", new { kind = "entities", nameQuery = "Orban" });

        Assert.True(entities.Ok, entities.Raw);
        Assert.Contains("walk.orban", entities.Raw);

        var graph = await ToolAsync("query", new
        {
            kind = "graph",
            id = "walk.orban",
            componentIds = new[] { "walk.note" },
            containmentDepth = 0,
            relationshipKinds = Array.Empty<string>(),
            relationshipDepth = 0
        });

        Assert.True(graph.Ok, graph.Raw);
        Assert.Contains("carries a lantern", graph.Raw);
        Assert.Equal("walk.orban", graph.Data.GetProperty("rootId").GetString());
        Assert.Single(graph.Data.GetProperty("nodes").EnumerateArray());

        // 9. A rule, read before it is used, exactly as procedure.mechanic.run asks.
        var rules = await ToolAsync("query", new { kind = "mechanics", query = "can they manage it" });

        Assert.True(rules.Ok, rules.Raw);

        var rule = rules.Data.GetProperty("mechanics").EnumerateArray().First();
        var ruleId = rule.GetProperty("id").GetString();

        // 10. An action, the one commit kind with a whole subsystem behind it. Selection is by
        //     intent — there is no way to name the rule — so the roles have to be filled from what
        //     the rule declares. Getting that wrong is recoverable, and this is where a session
        //     most often does.
        foreach (var payload in new[]
                 {
                     "{",
                     "[]",
                     "{}"
                 })
        {
            var malformedAction = await ToolAsync("commit", new { kind = "action", payload });
            Assert.False(malformedAction.Ok);
            Assert.Equal("INVALID_PAYLOAD", malformedAction.Error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(malformedAction.Error.GetProperty("why").GetString()));
            Assert.DoesNotContain("the rule is broken, not your arguments", malformedAction.Error.GetProperty("fix").GetString(), StringComparison.OrdinalIgnoreCase);
            AssertIsCall(malformedAction.Error.GetProperty("fix").GetString()!);
        }

        var missingRole = await ToolAsync("commit", new
        {
            kind = "action",
            payload = """{"intent":"can they manage it"}"""
        });

        Assert.False(missingRole.Ok);
        Assert.Equal("PROJECTION_FAILED", missingRole.Error.GetProperty("code").GetString());
        Assert.Contains("subject", missingRole.Error.GetProperty("why").GetString());
        Assert.DoesNotContain("the rule is broken, not your arguments", missingRole.Error.GetProperty("fix").GetString(), StringComparison.OrdinalIgnoreCase);
        AssertIsCall(missingRole.Error.GetProperty("fix").GetString()!);

        // 11. The same action with the role supplied. This runs AI-written JavaScript in the
        //     sandbox and applies what it proposes, which is the sentence the whole system exists
        //     to make true.
        var ran = await ToolAsync("commit", new
        {
            kind = "action",
            payload = """
                {"intent":"can they manage it","roleEntityIds":{"subject":"walk.orban"},"input":"{\"field\":\"vigour\",\"threshold\":5}","seed":7}
                """,
            intent = "resolve whether Orban manages it",
            proceduresUsed = new[] { "procedure.mechanic.run" }
        });

        Assert.True(ran.Ok, ran.Raw);
        Assert.Equal(ruleId, ran.Data.GetProperty("mechanic").GetProperty("id").GetString());
        Assert.NotEqual(0, ran.Data.GetProperty("seed").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(ran.Data.GetProperty("narration").GetString()));

        // 12. History records the public verbs, not the handler names behind them.
        var history = await ToolAsync("query", new { kind = "history", limit = 50 });

        Assert.True(history.Ok, history.Raw);

        var tools = history.Data.GetProperty("operations")
            .EnumerateArray()
            .Select(o => o.GetProperty("tool").GetString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.All(tools, tool => Assert.Contains(tool,
            new[] { "orient", "query", "commit", "apply_effects", "define_component" }));
    }

    [Fact]
    public async Task Binary_content_is_a_resource_template_not_a_fourth_tool()
    {
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
        var queryKinds = capabilities.Data.GetProperty("query").EnumerateArray()
            .Select(value => value.GetProperty("name").GetString()).ToArray();
        var commitKinds = capabilities.Data.GetProperty("commit").EnumerateArray()
            .Select(value => value.GetProperty("name").GetString()).ToArray();
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

        var badPayload = await ToolAsync("commit", new { kind = "component", payload = "{\"name\":\"No id\"}" });

        Assert.False(badPayload.Ok);
        Assert.Equal("INVALID_PAYLOAD", badPayload.Error.GetProperty("code").GetString());

        // The shape travels with the failure: the reason names every field the payload needed.
        var why = badPayload.Error.GetProperty("why").GetString()!;
        Assert.Contains("component requires id, name, and description", why);

        var fix = badPayload.Error.GetProperty("fix").GetString()!;
        AssertIsCall(fix);

        // And the fix is not merely well formed — sending it back gets a different, better answer.
        var retry = await ToolAsync("commit", new
        {
            kind = "component",
            payload = """{"id":"walk.retry","name":"Retry","description":"Written by following the fix."}"""
        });

        Assert.True(retry.Ok, retry.Raw);

        var dryRunUnsupported = await ToolAsync("commit", new
        {
            kind = "component",
            payload = """{"id":"walk.retry","name":"Retry","description":"..."}""",
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

    private sealed record ToolResult(
        bool Ok,
        JsonElement Data,
        JsonElement Error,
        IReadOnlyList<string> NextSteps,
        string OperationId,
        string Raw);
}
