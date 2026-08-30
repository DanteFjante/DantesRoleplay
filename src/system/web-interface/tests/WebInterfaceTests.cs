using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Authorization;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.Ecs;
using DantesRoleplay.Events;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemConversations;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Live;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.Web.Security;
using DantesRoleplay.Web.Settings;
using DantesRoleplay.Web.Interactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Tests;

public sealed class WebInterfaceTests
{
    [Fact]
    public void Web_quotas_keep_writes_and_streams_tight_while_allowing_catalog_reads()
    {
        Assert.Equal(2_000, WebInterfaceSecurity.ReadRequestsPerMinute);
        Assert.Equal(10, WebInterfaceSecurity.UploadRequestsPerMinute);
        Assert.Equal(4, WebInterfaceSecurity.ConcurrentStreams);
    }

    [Fact]
    public async Task System_task_body_is_closed_and_allows_bounded_large_semantic_agendas()
    {
        var invalid = new DefaultHttpContext();
        invalid.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"operation\":\"resolve\",\"intent\":\"test\",\"agenda\":null,\"idempotencyKey\":\"task.test\",\"requestToken\":\"injected\"}"));
        invalid.Request.ContentLength = invalid.Request.Body.Length;

        var exception = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlSystemTaskExplorer.ReadBodyAsync<SystemTaskPrepareRequest>(invalid.Request));
        Assert.Equal("SYSTEM_TASK_BODY_INVALID", exception.Code);

        var largeJson = JsonSerializer.Serialize(new
        {
            operation = "submit",
            intent = "Register a large component schema",
            agenda = new[] { new { capabilityId = "system.component-type.register",
                input = new { applicationId = "fixture-app", qualifiedTypeId = "fixture-app.large",
                    schemaJson = new string('x', 20_000) } } },
            idempotencyKey = "task.large"
        });
        var accepted = new DefaultHttpContext();
        accepted.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(largeJson));
        accepted.Request.ContentLength = accepted.Request.Body.Length;

        var parsed = await ControlSystemTaskExplorer.ReadBodyAsync<SystemTaskPrepareRequest>(accepted.Request);
        Assert.Equal(SystemTaskOperations.Submit, parsed.Operation);
        Assert.True(accepted.Request.ContentLength > 16 * 1024);
        Assert.Single(parsed.Agenda!);
    }

    [Fact]
    public void System_workspace_surface_is_read_only_bounded_and_generic()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());
        var application = builder.Build();
        application.MapDantesRoleplayWeb();

        var route = Assert.Single(((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == "/components/system-workspace.js");

        Assert.Equal([HttpMethods.Get], route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Contains("customElements.define('system-navigation'", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.Contains("/api/control/structure/applications", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.Contains("url.searchParams.set('limit', '100')", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.Contains("page.nextCursor", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("MAXIMUM_PAGES = 10", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("MAXIMUM_APPLICATIONS = 1000", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent(application.id)", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.Contains("`/ui/${encodeURIComponent(application.id)}-play`", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("#/applications/${encodeURIComponent(application.id)}", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.Contains("No applications registered.", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("Applications are unavailable.", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_DISCOVERY_UNAVAILABLE", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.Contains("system-progress", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("system-error", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("bubbles: true, composed: true", SystemWorkspaceElement.Script, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('hashchange'", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.Contains("window.removeEventListener('hashchange'", SystemWorkspaceElement.Script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/mcp", SystemWorkspaceElement.Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sql", SystemWorkspaceElement.Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dnd", SystemWorkspaceElement.Script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registered_applications_have_direct_pages_and_authored_pages_take_precedence()
    {
        var connectionString = SharedMemoryConnectionString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var applications = new InMemoryApplicationRegistry();
        var quest = new ApplicationRegistration(ApplicationIdentifier.Parse("quest"), "Quest", "A new game.", []);
        var dnd = new ApplicationRegistration(ApplicationIdentifier.Parse("dnd2024"), "D&D 2024", "An authored game.", []);
        applications.Register(quest);
        applications.Register(dnd);

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IApplicationRegistry>(applications);
        builder.Services.AddDantesRoleplayWeb(connectionString, new ConfigurationBuilder().Build());
        var application = builder.Build();
        application.MapDantesRoleplayWeb();
        await using (var setup = application.Services.CreateAsyncScope())
        {
            var pages = setup.ServiceProvider.GetRequiredService<WebContentDbContext>();
            await pages.Database.EnsureCreatedAsync();
            await new WebPageStore(pages).SaveAndActivateAsync("dnd2024-play", "<h1>Authored D&D page</h1>");
        }

        var route = Assert.Single(((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), endpoint =>
                endpoint.RoutePattern.RawText == "/ui/{id}");
        await using var scope = application.Services.CreateAsyncScope();

        async Task<(int Status, string Html)> GetAsync(string pageId)
        {
            var context = RequestContext("localhost:6217", IPAddress.Loopback);
            context.RequestServices = scope.ServiceProvider;
            context.Request.Method = HttpMethods.Get;
            context.Request.RouteValues["id"] = pageId;
            context.Response.Body = new MemoryStream();
            await route.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            return (context.Response.StatusCode, await reader.ReadToEndAsync());
        }

        var generated = await GetAsync("quest-play");
        var authored = await GetAsync("dnd2024-play");
        var unknown = await GetAsync("missing-play");

        Assert.Equal(StatusCodes.Status200OK, generated.Status);
        Assert.Contains("<system-navigation application-id=\"quest\">", generated.Html, StringComparison.Ordinal);
        Assert.Contains("<h1>Quest</h1>", generated.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("control-center", generated.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StatusCodes.Status200OK, authored.Status);
        Assert.Equal("<h1>Authored D&D page</h1>", authored.Html);
        Assert.Equal(StatusCodes.Status404NotFound, unknown.Status);
        Assert.Empty(unknown.Html);
        Assert.Equal("quest-play", ApplicationPageId.For(quest.Id));
        Assert.True(ApplicationPageId.TryGetApplicationId("quest-play", out var parsed));
        Assert.Equal(quest.Id, parsed);
        Assert.False(ApplicationPageId.TryGetApplicationId("quest-admin", out _));
    }

    [Fact]
    public async Task System_chat_surface_has_closed_question_and_confirmed_task_routes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());
        var application = builder.Build();
        application.MapDantesRoleplayWeb();

        var routes = ((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith(
                "/api/control/system/conversations", StringComparison.Ordinal))
            .Select(endpoint => (endpoint.RoutePattern.RawText,
                Method: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single())).ToArray();
        Assert.Equal([
            ("/api/control/system/conversations", HttpMethods.Get),
            ("/api/control/system/conversations/{conversationId}", HttpMethods.Get),
            ("/api/control/system/conversations", HttpMethods.Post),
            ("/api/control/system/conversations/{conversationId}/turns", HttpMethods.Post),
            ("/api/control/system/conversations/{conversationId}/tasks", HttpMethods.Get),
            ("/api/control/system/conversations/{conversationId}/tasks", HttpMethods.Post)
        ], routes);

        var script = SystemWorkspaceElement.Script;
        var chatStart = script.IndexOf("class SystemChat", StringComparison.Ordinal);
        var chat = script[chatStart..];
        Assert.Contains("customElements.define('system-chat'", chat, StringComparison.Ordinal);
        Assert.Contains("/api/control/system/conversations", chat, StringComparison.Ordinal);
        Assert.Contains("system-progress", chat, StringComparison.Ordinal);
        Assert.Contains("system-error", chat, StringComparison.Ordinal);
        Assert.Contains("system-read-v1", chat, StringComparison.Ordinal);
        Assert.Contains("sourceReferences", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/applications/", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("/mcp", chat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plan task", chat, StringComparison.Ordinal);
        Assert.Contains("Confirm and run", chat, StringComparison.Ordinal);
        Assert.Contains("/tasks", chat, StringComparison.Ordinal);
        Assert.Contains("/confirmations", chat, StringComparison.Ordinal);
        Assert.Contains("/executions", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("provider:", chat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope:", chat, StringComparison.OrdinalIgnoreCase);

        var unknown = new DefaultHttpContext();
        unknown.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"message\":\"hello\",\"idempotencyKey\":\"web:system\",\"provider\":\"local\"}"));
        unknown.Request.ContentLength = unknown.Request.Body.Length;
        var invalid = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlAssistantExplorer.ReadBodyAsync<SystemConversationCreate>(unknown.Request));
        Assert.Equal("ASSISTANT_BODY_INVALID", invalid.Code);
    }

    [Fact]
    public void System_component_descriptor_route_is_authorized_exact_and_non_secret()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());
        var application = builder.Build();
        application.MapDantesRoleplayWeb();

        var route = Assert.Single(((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), endpoint =>
                endpoint.RoutePattern.RawText == "/api/control/system/capabilities/{capabilityId}");
        Assert.Equal([HttpMethods.Get], route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);

        var applications = new InMemoryApplicationRegistry();
        var catalog = new SystemCapabilityCatalog(
            [new ApplicationsSystemCapabilityHandler(applications), new SecretFixtureCapabilityHandler()],
            new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy());
        var explorer = new ControlSystemCapabilityExplorer(catalog);
        var descriptor = explorer.Get(CapabilityAuthorization(), SystemCapabilityIds.Applications);

        Assert.NotNull(descriptor);
        Assert.Equal(SystemCapabilityIds.Applications, descriptor!.Id);
        Assert.Equal("read", descriptor.Mode);
        Assert.Equal(JsonValueKind.Object, descriptor.InputSchema.ValueKind);
        Assert.Matches("^[0-9A-F]{64}$", descriptor.Fingerprint);
        Assert.Matches("^[0-9A-F]{64}$", descriptor.InputSchemaHash);
        Assert.False(descriptor.RequiresConfirmation);
        Assert.False(descriptor.RequiresIdempotencyKey);
        Assert.Null(explorer.Get(CapabilityAuthorization(), "system.secret-fixture"));
        Assert.Null(explorer.Get(CapabilityAuthorization(), "system.unknown-fixture"));
        Assert.Equal("SYSTEM_CAPABILITY_ID_INVALID", Assert.Throws<ControlAssistantException>(
            () => explorer.Get(CapabilityAuthorization(), "application.attack")).Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            Assert.Throws<ControlAssistantException>(() =>
                new ControlSystemCapabilityExplorer().Get(
                    CapabilityAuthorization(), SystemCapabilityIds.Applications)).StatusCode);

        var denied = new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("TEST_UNAUTHENTICATED"),
            PrivateOperatorCapability.ControlRead,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "web-capability-denied")).Evidence;
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.Throws<ControlAssistantException>(() =>
                explorer.Get(denied, SystemCapabilityIds.Applications)).StatusCode);

        var publicNames = typeof(ControlSystemCapabilityDocument).GetProperties()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("OutputSchema", publicNames);
        Assert.DoesNotContain("AuthorizationEvidence", publicNames);
        Assert.DoesNotContain("RequiredCapability", publicNames);
        Assert.DoesNotContain("Sensitivity", publicNames);
    }

    [Fact]
    public void System_action_and_form_components_use_schema_and_separate_confirmation()
    {
        var script = SystemWorkspaceElement.Script;
        var start = script.IndexOf("const SYSTEM_CAPABILITY_ENDPOINT", StringComparison.Ordinal);
        var controls = script[start..];

        Assert.Contains("customElements.define('system-action-button'", controls, StringComparison.Ordinal);
        Assert.Contains("customElements.define('system-form'", controls, StringComparison.Ordinal);
        Assert.Contains("/api/control/system/capabilities/", controls, StringComparison.Ordinal);
        Assert.Contains("capability-id", controls, StringComparison.Ordinal);
        Assert.Contains("input-json", controls, StringComparison.Ordinal);
        Assert.Contains("systemComponentClone", controls, StringComparison.Ordinal);
        Assert.Contains("SYSTEM_COMPONENT_MAXIMUM_INPUT_BYTES = 96 * 1024", controls, StringComparison.Ordinal);
        Assert.Contains("value.steps.length > 1", controls, StringComparison.Ordinal);
        Assert.Contains("System request not completed", controls, StringComparison.Ordinal);
        Assert.Contains("schema.additionalProperties !== false", controls, StringComparison.Ordinal);
        Assert.Contains("this._form.reportValidity()", controls, StringComparison.Ordinal);
        Assert.Contains("label.htmlFor = id", controls, StringComparison.Ordinal);
        Assert.Contains("aria-live", controls, StringComparison.Ordinal);
        Assert.Contains("role', error ? 'alert' : 'status'", controls, StringComparison.Ordinal);
        Assert.Contains("system-proposal", controls, StringComparison.Ordinal);
        Assert.Contains("system-receipt", controls, StringComparison.Ordinal);
        Assert.Contains("Confirm and run", controls, StringComparison.Ordinal);
        Assert.Contains("button.addEventListener('click', () => confirm(button))", controls, StringComparison.Ordinal);
        Assert.True(controls.IndexOf("system-proposal", StringComparison.Ordinal) <
            controls.IndexOf("/executions", StringComparison.Ordinal));
        Assert.Contains("SYSTEM_ACTION_CONVERSATION_REQUIRED", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation-id", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/applications/", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("provider:", controls, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expectedFingerprint", controls, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requestToken", controls, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Application_surface_is_exact_and_components_have_no_control_authority()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());
        var application = builder.Build();
        application.MapDantesRoleplayWeb();

        var routes = ((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/applications/", StringComparison.Ordinal)
                || endpoint.RoutePattern.RawText == "/components/application-conversation.js"
                || endpoint.RoutePattern.RawText == "/components/maps/{name}.png"
                || endpoint.RoutePattern.RawText == "/components/media/{name}"
                || endpoint.RoutePattern.RawText == "/components/{name}.js")
            .Select(endpoint => (endpoint.RoutePattern.RawText,
                Method: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single())).ToArray();
        Assert.Equal([
            ("/components/application-conversation.js", HttpMethods.Get),
            ("/components/maps/{name}.png", HttpMethods.Get),
            ("/components/media/{name}", HttpMethods.Get),
            ("/components/{name}.js", HttpMethods.Get),
            ("/api/applications/{applicationId}/catalog/browse", HttpMethods.Get),
            ("/api/applications/{applicationId}/catalog/records/{qualifiedId}", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}/prepare", HttpMethods.Post),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}/execute", HttpMethods.Post),
            ("/api/applications/{applicationId}/state-spaces", HttpMethods.Get),
            ("/api/applications/{applicationId}/campaigns/{campaignId}/knowledge", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/containments", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/relationships", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/containment", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components", HttpMethods.Get),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}", HttpMethods.Get),
            ("/api/applications/{applicationId}/conversations/{conversationId}", HttpMethods.Get),
            ("/api/applications/{applicationId}/conversations", HttpMethods.Post),
            ("/api/applications/{applicationId}/conversations/{conversationId}/turns", HttpMethods.Post),
            ("/api/applications/{applicationId}/conversations/{conversationId}/execute", HttpMethods.Post),
            ("/api/applications/{applicationId}/observations", HttpMethods.Post)
        ], routes);
        var applicationStateReads = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/state-spaces", StringComparison.Ordinal))
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/applications/", StringComparison.Ordinal))
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single() == HttpMethods.Get)
            .ToArray();
        Assert.Equal(9, applicationStateReads.Length);
        Assert.All(applicationStateReads, endpoint => Assert.Equal(
            WebInterfaceSecurity.ReadRateLimitPolicy,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName));
        var applicationActionWrites = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/{qualifiedMechanicId}/", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, applicationActionWrites.Length);
        Assert.All(applicationActionWrites, endpoint => Assert.Equal(
            WebInterfaceSecurity.UploadRateLimitPolicy,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName));
        Assert.Contains("customElements.define('application-conversation'", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("session-context-id", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("new CustomEvent", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("Remember this route", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("remember.checked = false", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/control", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("/mcp", ApplicationConversationElement.Script, StringComparison.Ordinal);
        var applicationScript = await BrowserComponentAssets.ReadAsync("application-workspace");
        Assert.NotNull(applicationScript);
        Assert.Contains("customElements.define('application-entity-picker'", applicationScript,
            StringComparison.Ordinal);
        Assert.Contains("customElements.define('application-action-button'", applicationScript,
            StringComparison.Ordinal);
        Assert.Contains("customElements.define('application-form'", applicationScript,
            StringComparison.Ordinal);
        Assert.Contains("MAXIMUM_ENTITY_PAGE = 100", applicationScript, StringComparison.Ordinal);
        Assert.Contains("url.searchParams.set('limit', String(MAXIMUM_ENTITY_PAGE))", applicationScript,
            StringComparison.Ordinal);
        Assert.Contains("application-entity-change", applicationScript, StringComparison.Ordinal);
        Assert.Contains("/mechanics/${encodeURIComponent(scope.mechanicId)}", applicationScript,
            StringComparison.Ordinal);
        Assert.Contains("/prepare", applicationScript, StringComparison.Ordinal);
        Assert.Contains("/execute", applicationScript, StringComparison.Ordinal);
        Assert.Contains("Confirm and execute", applicationScript, StringComparison.Ordinal);
        Assert.Contains("actionResults", applicationScript, StringComparison.Ordinal);
        Assert.Contains("result?.narration", applicationScript, StringComparison.Ordinal);
        Assert.Contains("schemaStatus !== 'authored'", applicationScript, StringComparison.Ordinal);
        Assert.Contains("empty input object", applicationScript, StringComparison.Ordinal);
        Assert.Contains("bubbles: true, composed: true", applicationScript, StringComparison.Ordinal);
        Assert.Contains("method: 'POST'", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("url.searchParams.set('cursor'", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/control", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("/mcp", applicationScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/conversations", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("method: 'PUT'", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("method: 'DELETE'", applicationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", applicationScript, StringComparison.Ordinal);

        Assert.Null(await BrowserComponentAssets.ReadAsync("missing-browser-component"));

        var assetRoute = Assert.Single(((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), endpoint =>
                endpoint.RoutePattern.RawText == "/components/{name}.js");
        var assetContext = RequestContext("localhost:6217", IPAddress.Loopback);
        assetContext.RequestServices = application.Services;
        assetContext.Request.Method = HttpMethods.Get;
        assetContext.Request.RouteValues["name"] = "dnd2024-workspace";
        assetContext.Response.Body = new MemoryStream();

        await assetRoute.RequestDelegate!(assetContext);

        Assert.Equal(StatusCodes.Status404NotFound, assetContext.Response.StatusCode);

        assetContext.Response.Clear();
        assetContext.Request.RouteValues["name"] = "application-workspace";
        assetContext.Response.Body = new MemoryStream();
        await assetRoute.RequestDelegate!(assetContext);

        Assert.Equal(StatusCodes.Status200OK, assetContext.Response.StatusCode);
        Assert.Equal("text/javascript; charset=utf-8", assetContext.Response.ContentType);
        assetContext.Response.Body.Position = 0;
        using var applicationReader = new StreamReader(assetContext.Response.Body);
        Assert.Contains("customElements.define('application-form'", await applicationReader.ReadToEndAsync(),
            StringComparison.Ordinal);

        Assert.Null(await BrowserMapAssets.ReadAsync("../thalos-world"));
        Assert.Null(await BrowserMapAssets.ReadAsync("missing-map"));
        Assert.NotNull(await BrowserMapAssets.ReadAsync("caldris-world"));
        Assert.NotNull(await BrowserMapAssets.ReadAsync("caldris-eredane"));
        var expectedMap = await BrowserMapAssets.ReadAsync("thalos-world");
        Assert.NotNull(expectedMap);

        var mapRoute = Assert.Single(((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), endpoint =>
                endpoint.RoutePattern.RawText == "/components/maps/{name}.png");
        var mapContext = RequestContext("localhost:6217", IPAddress.Loopback);
        mapContext.RequestServices = application.Services;
        mapContext.Request.Method = HttpMethods.Get;
        mapContext.Request.RouteValues["name"] = "thalos-world";
        mapContext.Response.Body = new MemoryStream();

        await mapRoute.RequestDelegate!(mapContext);

        Assert.Equal(StatusCodes.Status200OK, mapContext.Response.StatusCode);
        Assert.Equal("image/png", mapContext.Response.ContentType);
        Assert.Equal(expectedMap, ((MemoryStream)mapContext.Response.Body).ToArray());

        var missingMapContext = RequestContext("localhost:6217", IPAddress.Loopback);
        missingMapContext.RequestServices = application.Services;
        missingMapContext.Request.Method = HttpMethods.Get;
        missingMapContext.Request.RouteValues["name"] = "missing-map";
        missingMapContext.Response.Body = new MemoryStream();

        await mapRoute.RequestDelegate!(missingMapContext);

        Assert.Equal(StatusCodes.Status404NotFound, missingMapContext.Response.StatusCode);

        const string mediaName = "sha256.3ae0336e89155a4a00fb0d982ae903bf9ed1137cd292b097b252fd38c1501fa3.png";
        Assert.Null(await BrowserMediaAssets.ReadAsync("../" + mediaName));
        Assert.Null(await BrowserMediaAssets.ReadAsync("sha256.AAE0336e89155a4a00fb0d982ae903bf9ed1137cd292b097b252fd38c1501fa3.png"));
        Assert.Null(await BrowserMediaAssets.ReadAsync("sha256.3ae0336e89155a4a00fb0d982ae903bf9ed1137cd292b097b252fd38c1501fa3.gif"));
        var expectedMedia = await BrowserMediaAssets.ReadAsync(mediaName);
        Assert.NotNull(expectedMedia);
        Assert.Equal("image/png", expectedMedia!.ContentType);

        var mediaRoute = Assert.Single(((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), endpoint =>
                endpoint.RoutePattern.RawText == "/components/media/{name}");
        var mediaContext = RequestContext("localhost:6217", IPAddress.Loopback);
        mediaContext.RequestServices = application.Services;
        mediaContext.Request.Method = HttpMethods.Get;
        mediaContext.Request.RouteValues["name"] = mediaName;
        mediaContext.Response.Body = new MemoryStream();

        await mediaRoute.RequestDelegate!(mediaContext);

        Assert.Equal(StatusCodes.Status200OK, mediaContext.Response.StatusCode);
        Assert.Equal("image/png", mediaContext.Response.ContentType);
        Assert.Equal(expectedMedia.Content, ((MemoryStream)mediaContext.Response.Body).ToArray());
    }

    [Fact]
    public async Task Page_uploads_append_revisions_and_the_active_page_is_unchanged_html()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        const string first = "<!doctype html><title>First</title><script>window.first=true</script>";
        const string second = "<!doctype html><title>Second</title><style>body{color:gold}</style>";

        var revision1 = await store.SaveAndActivateAsync("character-sheet", first);
        var revision2 = await store.SaveAndActivateAsync("character-sheet", second);
        var active = await store.GetActiveAsync("character-sheet");

        Assert.Equal(1, revision1.Revision);
        Assert.Equal(2, revision2.Revision);
        Assert.NotNull(active);
        Assert.Equal(2, active!.Revision);
        Assert.Equal(second, active.Html);
        Assert.Equal(2, await db.PageRevisions.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../page")]
    [InlineData("page/name")]
    [InlineData(" page")]
    public void Page_ids_are_route_safe(string id)
    {
        Assert.False(WebPageId.IsValid(id));
    }

    [Fact]
    public async Task Invalid_page_inputs_do_not_create_a_revision()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAndActivateAsync("../page", "<p>content</p>"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAndActivateAsync("valid-page", "   "));

        Assert.Empty(await db.Pages.ToListAsync());
        Assert.Empty(await db.PageRevisions.ToListAsync());
    }

    [Fact]
    public async Task Dynamic_entity_data_preserves_unknown_components_and_fields()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("future.unknown", "Future", "Test data.");
        var entity = await world.CreateEntityAsync("Orban", "creature.orban");
        await world.SetComponentAsync(
            entity.Id,
            "future.unknown",
            """{"resonance":7,"nested":{"answer":42}}""");
        var reader = new DynamicDataReader(world);

        var result = await reader.ReadAsync("entity", entity.Id);

        Assert.NotNull(result);
        var component = result!.Json["components"]!["future.unknown"]!;
        Assert.Equal(7, component["resonance"]!.GetValue<int>());
        Assert.Equal(42, component["nested"]!["answer"]!.GetValue<int>());
    }

    [Fact]
    public async Task Dynamic_component_data_is_returned_as_the_raw_json_object()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("inventory", "Inventory", "Test data.");
        var entity = await world.CreateEntityAsync("Orban", "creature.orban");
        await world.SetComponentAsync(
            entity.Id,
            "inventory",
            """{"items":[{"id":"lantern","quantity":1}]}""");
        var reader = new DynamicDataReader(world);

        var result = await reader.ReadAsync("inventory", entity.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Revision);
        Assert.Equal("lantern", result.Json["items"]![0]!["id"]!.GetValue<string>());
        Assert.Null(await reader.ReadAsync("missing", entity.Id));
        Assert.Null(await reader.ReadAsync("inventory", "missing.entity"));
    }

    [Fact]
    public async Task Zip_bundle_materializes_and_serves_exact_active_revision_assets()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        var reader = new WebPageBundleReader();
        var css = Encoding.UTF8.GetBytes("body { color: gold; }");
        await using var zip = CreateZip(
            ("index.html", Encoding.UTF8.GetBytes("<!doctype html><link rel=\"stylesheet\" href=\"assets/site.css\"><h1>Home</h1>")),
            ("assets/site.css", css),
            ("assets/scripts/app.js", Encoding.UTF8.GetBytes("window.ready = true;")));

        var bundle = await reader.ReadAsync(zip, zip.Length);
        var saved = await store.SaveBundleAndActivateAsync("home", bundle);
        var active = await store.GetActiveAsync("home");
        var asset = await store.GetActiveAssetAsync("home", "assets/site.css");

        Assert.Equal(1, saved.Revision);
        Assert.Equal(2, bundle.Assets.Count);
        Assert.Contains("<h1>Home</h1>", active!.Html, StringComparison.Ordinal);
        Assert.NotNull(asset);
        Assert.Equal("text/css", asset!.ContentType);
        Assert.Equal(css, asset.Content);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(css)), asset.ContentHash);
    }

    [Fact]
    public async Task Later_revision_exposes_only_its_own_assets_and_retains_old_rows()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        var reader = new WebPageBundleReader();
        await using var firstZip = CreateZip(
            ("index.html", Encoding.UTF8.GetBytes("<h1>First</h1>")),
            ("assets/old.css", Encoding.UTF8.GetBytes("body{}")));
        await using var secondZip = CreateZip(
            ("index.html", Encoding.UTF8.GetBytes("<h1>Second</h1>")),
            ("assets/new.css", Encoding.UTF8.GetBytes("main{}")));

        await store.SaveBundleAndActivateAsync("home", await reader.ReadAsync(firstZip, firstZip.Length));
        await store.SaveBundleAndActivateAsync("home", await reader.ReadAsync(secondZip, secondZip.Length));

        Assert.Null(await store.GetActiveAssetAsync("home", "assets/old.css"));
        Assert.NotNull(await store.GetActiveAssetAsync("home", "assets/new.css"));
        Assert.Equal(2, await db.PageAssets.CountAsync());
        Assert.Equal(2, (await store.GetActiveAsync("home"))!.Revision);

        await store.SaveAndActivateAsync("home", "<h1>Plain HTML</h1>");
        Assert.Null(await store.GetActiveAssetAsync("home", "assets/new.css"));
        Assert.Equal(2, await db.PageAssets.CountAsync());
    }

    [Fact]
    public async Task Zip_boundary_rejects_missing_index_unsafe_duplicates_bad_utf8_and_oversize_html()
    {
        var reader = new WebPageBundleReader();
        var cases = new[]
        {
            CreateZip(("assets/site.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("../escape.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("assets/site%2fadmin.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("assets/site.css", Encoding.UTF8.GetBytes("body{}")),
                ("assets/site.css", Encoding.UTF8.GetBytes("main{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("site.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(("index.html", [0xC3, 0x28])),
            CreateZip(("index.html", new byte[WebPageBundleLimits.MaximumHtmlBytes + 1]))
        };

        try
        {
            foreach (var zip in cases)
            {
                await Assert.ThrowsAsync<WebPageBundleException>(
                    () => reader.ReadAsync(zip, zip.Length));
            }
        }
        finally
        {
            foreach (var zip in cases)
            {
                await zip.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Persistence_failure_rolls_back_revision_assets_and_active_pointer()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        await store.SaveAndActivateAsync("home", "<h1>Stable</h1>");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER reject_web_asset
            BEFORE INSERT ON web_page_asset
            BEGIN
                SELECT RAISE(ABORT, 'forced asset failure');
            END;
            """);

        var bundle = new WebPageBundle(
            "<h1>Rejected</h1>",
            [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]);
        await Assert.ThrowsAsync<DbUpdateException>(
            () => store.SaveBundleAndActivateAsync("home", bundle));

        await using var verificationDb = CreateWebContext(connection);
        var verificationStore = new WebPageStore(verificationDb);
        var active = await verificationStore.GetActiveAsync("home");
        Assert.Equal(1, active!.Revision);
        Assert.Contains("Stable", active.Html, StringComparison.Ordinal);
        Assert.Equal(1, await verificationDb.PageRevisions.CountAsync());
        Assert.Empty(await verificationDb.PageAssets.ToListAsync());
    }

    [Fact]
    public async Task Page_drafts_copy_exact_assets_without_activation_and_exact_revisions_publish_or_roll_back()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        var css = Encoding.UTF8.GetBytes("body{color:gold}");
        await store.SaveBundleAndActivateAsync(
            "control-center",
            new WebPageBundle(
                "<h1>Stable</h1>",
                [new WebPageAssetUpload("assets/site.css", css)]));

        var draft = await store.AppendDraftAsync(
            "control-center", 1, 1, "<h1>Draft</h1>");
        var afterDraft = await store.GetSummaryAsync("control-center");
        var activeAfterDraft = await store.GetActiveAsync("control-center");

        Assert.Equal(2, draft.Summary.Revision);
        Assert.False(draft.Summary.IsActive);
        Assert.Equal(css, Assert.Single(draft.Assets).Content);
        Assert.Equal(1, afterDraft!.ActiveRevision);
        Assert.Equal(2, afterDraft.LatestRevision);
        Assert.Contains("Stable", activeAfterDraft!.Html, StringComparison.Ordinal);
        Assert.Equal("PAGE_ALREADY_ACTIVE", (await Assert.ThrowsAsync<WebPageStoreException>(() =>
            store.ActivateRevisionAsync("control-center", 1, 1))).Code);
        var staleDraft = await Assert.ThrowsAsync<WebPageStoreException>(() =>
            store.AppendDraftAsync("control-center", 1, 1, "<h1>Replay</h1>"));
        Assert.Equal("PAGE_LATEST_STALE", staleDraft.Code);

        var published = await store.ActivateRevisionAsync("control-center", 2, 1);
        Assert.Equal(2, published.ActiveRevision);
        Assert.Contains("Draft", (await store.GetActiveAsync("control-center"))!.Html, StringComparison.Ordinal);
        var staleActivation = await Assert.ThrowsAsync<WebPageStoreException>(() =>
            store.ActivateRevisionAsync("control-center", 1, 1));
        Assert.Equal("PAGE_ACTIVE_STALE", staleActivation.Code);

        var rolledBack = await store.ActivateRevisionAsync("control-center", 1, 2);
        Assert.Equal(1, rolledBack.ActiveRevision);
        Assert.Contains("Stable", (await store.GetActiveAsync("control-center"))!.Html, StringComparison.Ordinal);
        Assert.Equal(2, await db.PageRevisions.CountAsync());
        Assert.Equal(2, await db.PageAssets.CountAsync());
    }

    [Fact]
    public async Task Page_and_revision_discovery_is_bounded_stable_and_exact()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        await store.SaveAndActivateAsync("bravo", "<h1>Bravo</h1>");
        await store.SaveAndActivateAsync("alpha", "<h1>Alpha 1</h1>");
        await store.AppendDraftAsync("alpha", 1, 1, "<h1>Alpha 2</h1>");
        await store.AppendDraftAsync("alpha", 2, 2, "<h1>Alpha 3</h1>");

        var firstPages = await store.ListPageAsync(null, 1);
        Assert.Equal(["alpha"], firstPages.Pages.Select(page => page.Id));
        Assert.Equal("alpha", firstPages.NextPageId);
        Assert.Equal(["bravo"], (await store.ListPageAsync(firstPages.NextPageId, 1)).Pages.Select(page => page.Id));
        Assert.Equal("CURSOR_STALE", (await Assert.ThrowsAsync<WebPageStoreException>(
            () => store.ListPageAsync("missing", 1))).Code);

        var firstRevisions = await store.ListRevisionsAsync("alpha", null, 2);
        Assert.Equal([3, 2], firstRevisions.Revisions.Select(revision => revision.Revision));
        Assert.Equal(2, firstRevisions.NextRevision);
        Assert.Equal([1], (await store.ListRevisionsAsync("alpha", firstRevisions.NextRevision, 2))
            .Revisions.Select(revision => revision.Revision));
        Assert.Equal("CURSOR_STALE", (await Assert.ThrowsAsync<WebPageStoreException>(
            () => store.ListRevisionsAsync("alpha", 99, 1))).Code);
        Assert.Contains("Alpha 2", (await store.GetRevisionAsync("alpha", 2))!.Html, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ListPageAsync(null, 101));
    }

    [Fact]
    public async Task Draft_and_activation_persistence_failures_roll_back_their_complete_transition()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using (var setup = CreateWebContext(connection))
        {
            await setup.Database.EnsureCreatedAsync();
            await new WebPageStore(setup).SaveBundleAndActivateAsync(
                "home",
                new WebPageBundle(
                    "<h1>Stable</h1>",
                    [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]));
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_draft_asset BEFORE INSERT ON web_page_asset BEGIN SELECT RAISE(ABORT, 'forced draft failure'); END;");
        }
        await using (var failingDraft = CreateWebContext(connection))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new WebPageStore(failingDraft)
                .AppendDraftAsync("home", 1, 1, "<h1>Rejected</h1>"));
        }
        await using (var verifyDraft = CreateWebContext(connection))
        {
            Assert.Equal(1, await verifyDraft.PageRevisions.CountAsync());
            Assert.Equal(1, (await new WebPageStore(verifyDraft).GetSummaryAsync("home"))!.ActiveRevision);
            await verifyDraft.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_draft_asset;");
        }
        await using (var setupActivation = CreateWebContext(connection))
        {
            await new WebPageStore(setupActivation).AppendDraftAsync("home", 1, 1, "<h1>Draft</h1>");
            await setupActivation.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_page_activation BEFORE UPDATE ON web_page BEGIN SELECT RAISE(ABORT, 'forced activation failure'); END;");
        }
        await using (var failingActivation = CreateWebContext(connection))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new WebPageStore(failingActivation)
                .ActivateRevisionAsync("home", 2, 1));
        }
        await using var verifyActivation = CreateWebContext(connection);
        var summary = await new WebPageStore(verifyActivation).GetSummaryAsync("home");
        Assert.Equal(1, summary!.ActiveRevision);
        Assert.Equal(2, summary.LatestRevision);
    }

    [Fact]
    public async Task Control_page_editor_projects_bounded_metadata_export_and_isolated_preview()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        await store.SaveBundleAndActivateAsync(
            "control-center",
            new WebPageBundle(
                "<script>fetch('/api/control/status')</script><h1>Editor</h1>",
                [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]));
        await store.SaveAndActivateAsync("home", "<h1>Home</h1>");
        var editor = new ControlPageEditor(store);

        var pages = await editor.ListPagesAsync(null, "1");
        var revisions = await editor.ListRevisionsAsync("control-center", null, null);
        var detail = await editor.GetRevisionAsync("control-center", 1);
        var bundle = await editor.ExportAsync("control-center", 1);
        var preview = await editor.PreviewHtmlAsync("control-center", 1);
        var asset = await editor.PreviewAssetAsync("control-center", 1, "site.css");

        Assert.Equal("control-center", Assert.Single(pages.Items).Id);
        Assert.Equal(1, Assert.Single(revisions.Items).Revision);
        Assert.Equal("assets/site.css", Assert.Single(detail!.Assets).Path);
        Assert.DoesNotContain("\"content\":", JsonSerializer.Serialize(detail.Assets), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fetch", preview, StringComparison.Ordinal);
        Assert.Equal("text/css", asset!.ContentType);
        using var archive = new ZipArchive(new MemoryStream(bundle!.Content), ZipArchiveMode.Read);
        Assert.Equal(["assets/site.css", "index.html"], archive.Entries.Select(entry => entry.FullName).Order());

        var response = new DefaultHttpContext().Response;
        WebInterfaceSecurity.ApplyHeaders(response);
        ControlPageEditor.ApplyPreviewHeaders(response, asset: false);
        Assert.Contains("connect-src 'none'", response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Equal("SAMEORIGIN", response.Headers.XFrameOptions);
        Assert.Equal("no-store", response.Headers.CacheControl);
        Assert.Equal("CURSOR_STALE", (await Assert.ThrowsAsync<ControlPageEditorException>(
            () => editor.ListRevisionsAsync("control-center", pages.NextCursor, "1"))).Code);

        var oversized = new DefaultHttpContext().Request;
        oversized.ContentLength = ControlPageEditor.MaximumJsonBodyBytes + 1;
        Assert.Equal("BODY_TOO_LARGE", (await Assert.ThrowsAsync<ControlPageEditorException>(
            () => ControlPageEditor.ReadBodyAsync<ControlPageDraftRequest>(oversized))).Code);
    }

    [Fact]
    public async Task Web_migrations_create_asset_storage_from_a_blank_database()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);

        await db.Database.MigrateAsync();

        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var schemaRows = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_schema WHERE type IN ('table', 'index')")
            .ToListAsync();
        Assert.Contains("web_page_asset", schemaRows);
        Assert.Contains("IX_web_page_asset_PageRevisionId_Path", schemaRows);

        var store = new WebPageStore(db);
        await store.SaveBundleAndActivateAsync(
            "home",
            new WebPageBundle(
                "<h1>Migrated</h1>",
                [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]));
        Assert.NotNull(await store.GetActiveAssetAsync("home", "assets/site.css"));
    }

    [Fact]
    public async Task Change_feed_starts_invalidated_and_reports_committed_page_revision()
    {
        var connectionString = SharedMemoryConnectionString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var observerDb = CreateWebContext(connectionString);
        await observerDb.Database.EnsureCreatedAsync();
        var feed = new SqliteWebChangeFeed(observerDb);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var changes = feed.WatchAsync(
                "home",
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromSeconds(10),
                timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(WebChangeKind.Invalidate, changes.Current.Kind);
        Assert.Equal("connected", changes.Current.Reason);
        Assert.Null(changes.Current.PageRevision);

        await using (var writerDb = CreateWebContext(connectionString))
        {
            await new WebPageStore(writerDb).SaveAndActivateAsync("home", "<h1>Live</h1>");
        }

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(WebChangeKind.Invalidate, changes.Current.Kind);
        Assert.Equal("database-commit", changes.Current.Reason);
        Assert.Equal(1, changes.Current.PageRevision);

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(WebChangeKind.PageRevision, changes.Current.Kind);
        Assert.Equal("home", changes.Current.PageId);
        Assert.Equal(1, changes.Current.PageRevision);
    }

    [Fact]
    public async Task Change_feed_does_not_report_a_rolled_back_write()
    {
        var connectionString = SharedMemoryConnectionString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var setupDb = CreateWebContext(connectionString))
        {
            await setupDb.Database.EnsureCreatedAsync();
            await new WebPageStore(setupDb).SaveAndActivateAsync("home", "<h1>Stable</h1>");
        }

        await using var observerDb = CreateWebContext(connectionString);
        var feed = new SqliteWebChangeFeed(observerDb);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await using var changes = feed.WatchAsync(
                "home",
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromSeconds(10),
                timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(1, changes.Current.PageRevision);

        await using (var writerDb = CreateWebContext(connectionString))
        {
            await using var transaction = await writerDb.Database.BeginTransactionAsync();
            await writerDb.Database.ExecuteSqlRawAsync(
                "UPDATE web_page SET UpdatedAt = {0} WHERE Id = {1}",
                DateTime.UtcNow,
                "home");
            await transaction.RollbackAsync();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await changes.MoveNextAsync().AsTask());
    }

    [Fact]
    public void Change_events_have_standard_single_line_sse_frames()
    {
        var invalidation = WebChangeSseFormatter.Format(
            new WebChange(WebChangeKind.Invalidate, "connected", 7, "home", 2));
        var pageRevision = WebChangeSseFormatter.Format(
            new WebChange(WebChangeKind.PageRevision, "page-activated", 8, "home", 3));

        Assert.StartsWith("event: invalidate\ndata: ", invalidation, StringComparison.Ordinal);
        Assert.Contains("\"databaseVersion\":7", invalidation, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', invalidation);
        Assert.EndsWith("\n\n", invalidation, StringComparison.Ordinal);
        Assert.Contains("event: page-revision", pageRevision, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"/ui/home/index.html\"", pageRevision, StringComparison.Ordinal);
        Assert.Equal(
            ": keep-alive\n\n",
            WebChangeSseFormatter.Format(
                new WebChange(WebChangeKind.KeepAlive, "keep-alive", 8)));
    }

    [Fact]
    public void Local_web_access_fails_closed_except_for_ipv4_and_ipv6_loopback()
    {
        Assert.True(WebInterfaceSecurity.IsLoopback(IPAddress.Loopback));
        Assert.True(WebInterfaceSecurity.IsLoopback(IPAddress.IPv6Loopback));
        Assert.False(WebInterfaceSecurity.IsLoopback(IPAddress.Parse("192.0.2.10")));
        Assert.False(WebInterfaceSecurity.IsLoopback(null));
    }

    [Fact]
    public void Local_web_responses_receive_the_closed_trusted_content_policy()
    {
        var context = new DefaultHttpContext();

        WebInterfaceSecurity.ApplyHeaders(context.Response);

        Assert.Equal(WebInterfaceSecurity.ContentSecurityPolicy, context.Response.Headers.ContentSecurityPolicy);
        Assert.Contains("connect-src 'self'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-src 'self'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("localhost:5173", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
    }

    [Fact]
    public void Direct_loopback_access_remains_local_without_remote_configuration()
    {
        var context = RequestContext("localhost", IPAddress.Loopback);
        var policy = AccessPolicy(new WebRemoteAccessOptions());

        var decision = policy.Evaluate(context);

        Assert.True(decision.Allowed);
        Assert.Equal(WebAccessMode.Local, decision.Mode);
        Assert.Null(decision.Login);
        Assert.True(WebAccessPolicy.CreatePrincipal(decision).Identity!.IsAuthenticated);
    }

    [Fact]
    public void Exact_tailscale_host_and_allowed_login_resolve_remote_identity()
    {
        var context = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "operator@example.com";
        var policy = AccessPolicy(new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "ROLEPLAY.example.ts.net.",
            AllowedLogins = ["operator@example.com"]
        });

        var decision = policy.Evaluate(context);
        var principal = WebAccessPolicy.CreatePrincipal(decision);

        Assert.True(decision.Allowed);
        Assert.Equal(WebAccessMode.Tailscale, decision.Mode);
        Assert.Equal("operator@example.com", decision.Login);
        Assert.Equal(WebAccessPolicy.TailscaleAuthenticationType, principal.Identity!.AuthenticationType);
        Assert.Equal("operator@example.com", principal.Identity.Name);
    }

    [Fact]
    public void Trusted_web_identity_becomes_a_stable_opaque_authorization_principal()
    {
        var first = WebTrustedPrincipalContextFactory.Create(new(
            true, WebAccessMode.Tailscale, "Operator@Example.com"));
        var second = WebTrustedPrincipalContextFactory.Create(new(
            true, WebAccessMode.Tailscale, "operator@example.com"));
        var local = WebTrustedPrincipalContextFactory.Create(new(true, WebAccessMode.Local));

        Assert.True(first.Verified);
        Assert.Equal(first.PrincipalId, second.PrincipalId);
        Assert.StartsWith("principal.", first.PrincipalId, StringComparison.Ordinal);
        Assert.DoesNotContain("operator", first.PrincipalId, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(first.PrincipalId, local.PrincipalId);
        Assert.Equal("tailscale-serve", first.AuthenticationMethod);
        Assert.Equal("local-loopback", local.AuthenticationMethod);
    }

    [Fact]
    public void Private_operator_guard_maps_http_reads_and_modifications_without_caller_authority()
    {
        var guard = OperatorGuard(new WebRemoteAccessOptions());
        var read = RequestContext("localhost", IPAddress.Loopback);
        read.Request.Method = HttpMethods.Get;
        var modify = RequestContext("localhost", IPAddress.Loopback);
        modify.Request.Method = HttpMethods.Put;

        var readDecision = guard.Evaluate(read);
        var modifyDecision = guard.Evaluate(modify);

        Assert.True(readDecision.Allowed);
        Assert.True(modifyDecision.Allowed);
        Assert.Equal("read", readDecision.Evidence.Capability);
        Assert.Equal("modify", modifyDecision.Evidence.Capability);
        Assert.Equal(PrivateOperatorAuthorizationPolicy.PrivateHostScope, readDecision.Evidence.Scope);
        Assert.DoesNotContain("local", readDecision.Evidence.PrincipalReference, StringComparison.OrdinalIgnoreCase);
        Assert.True(readDecision.Principal!.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Control_reads_and_local_same_origin_json_changes_use_the_server_selected_capability()
    {
        var guard = ControlGuard(new WebRemoteAccessOptions());
        var read = RequestContext("localhost:6217", IPAddress.Loopback);
        read.Request.Method = HttpMethods.Get;
        read.Request.Scheme = Uri.UriSchemeHttp;
        read.Request.Headers["X-Control-Capability"] = "control.codex.approve";
        var write = RequestContext("localhost:6217", IPAddress.IPv6Loopback);
        write.Request.Method = HttpMethods.Put;
        write.Request.Scheme = Uri.UriSchemeHttp;
        write.Request.ContentType = "application/json; charset=utf-8";
        write.Request.Headers.Origin = "http://localhost:6217";
        write.Request.Headers["X-Control-Capability"] = "control.codex.approve";

        var readDecision = guard.Evaluate(
            read,
            PrivateOperatorCapability.ControlRead,
            mutation: false);
        var writeDecision = guard.Evaluate(
            write,
            PrivateOperatorCapability.ControlPagesWrite,
            mutation: true);

        Assert.True(readDecision.Allowed);
        Assert.Equal("control.read", readDecision.Evidence.Capability);
        Assert.True(writeDecision.Allowed);
        Assert.Equal("control.pages.write", writeDecision.Evidence.Capability);
    }

    [Fact]
    public void Trigger_control_capabilities_are_server_selected_and_device_identity_grants_no_administration()
    {
        var guard = ControlGuard(new WebRemoteAccessOptions());
        var read = RequestContext("localhost:6217", IPAddress.Loopback);
        read.Request.Method = HttpMethods.Get;
        read.Request.Scheme = Uri.UriSchemeHttp;
        var write = RequestContext("localhost:6217", IPAddress.Loopback);
        write.Request.Method = HttpMethods.Post;
        write.Request.Scheme = Uri.UriSchemeHttp;
        write.Request.ContentType = "application/json";
        write.Request.Headers.Origin = "http://localhost:6217";
        write.Request.Headers[PhoneCompanionIdentity.CredentialHeader] =
            "phone-credential." + new string('a', 64);
        var remoteDevice = RequestContext("roleplay.example.ts.net", IPAddress.Parse("192.0.2.10"));
        remoteDevice.Request.Method = HttpMethods.Post;
        remoteDevice.Request.Scheme = Uri.UriSchemeHttps;
        remoteDevice.Request.ContentType = "application/json";
        remoteDevice.Request.Headers.Origin = "https://roleplay.example.ts.net";
        remoteDevice.Request.Headers[PhoneCompanionIdentity.CredentialHeader] =
            "phone-credential." + new string('a', 64);

        var readDecision = guard.Evaluate(read,
            PrivateOperatorCapability.TriggerAdministrationRead, mutation: false);
        var writeDecision = guard.Evaluate(write,
            PrivateOperatorCapability.TriggerAdministrationWrite, mutation: true);
        var wrongCapability = guard.Evaluate(write, PrivateOperatorCapability.Read, mutation: true);
        var deviceOnly = guard.Evaluate(remoteDevice,
            PrivateOperatorCapability.TriggerAdministrationWrite, mutation: true);

        Assert.True(readDecision.Allowed);
        Assert.Equal("trigger.admin.read", readDecision.Evidence.Capability);
        Assert.True(writeDecision.Allowed);
        Assert.Equal("trigger.admin.write", writeDecision.Evidence.Capability);
        Assert.False(wrongCapability.Allowed);
        Assert.False(deviceOnly.Allowed);
    }

    [Fact]
    public void Tailscale_control_change_requires_the_exact_https_public_origin()
    {
        var options = new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = ["operator@example.com"]
        };
        var accepted = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        accepted.Request.Method = HttpMethods.Post;
        accepted.Request.Scheme = Uri.UriSchemeHttp;
        accepted.Request.ContentType = "application/json";
        accepted.Request.Headers.Origin = "https://roleplay.example.ts.net";
        accepted.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "operator@example.com";
        var wrongScheme = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        wrongScheme.Request.Method = HttpMethods.Post;
        wrongScheme.Request.Scheme = Uri.UriSchemeHttp;
        wrongScheme.Request.ContentType = "application/json";
        wrongScheme.Request.Headers.Origin = "http://roleplay.example.ts.net";
        wrongScheme.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "operator@example.com";

        var acceptedDecision = ControlGuard(options).Evaluate(
            accepted,
            PrivateOperatorCapability.ControlAiMessage,
            mutation: true);
        var deniedDecision = ControlGuard(options).Evaluate(
            wrongScheme,
            PrivateOperatorCapability.ControlAiMessage,
            mutation: true);

        Assert.True(acceptedDecision.Allowed);
        Assert.Equal("control.ai.message", acceptedDecision.Evidence.Capability);
        Assert.False(deniedDecision.Allowed);
        Assert.Equal("CONTROL_ORIGIN_DENIED", deniedDecision.ErrorCode);
    }

    [Theory]
    [InlineData("localhost:6217", null, "application/json", 403, "CONTROL_ORIGIN_REQUIRED")]
    [InlineData("localhost:6217", "null", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("localhost:6217", "http://localhost:6218", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("localhost:6217", "https://localhost:6217", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("localhost:6217", "http://localhost:6217/path", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("evil.example:6217", "http://evil.example:6217", "application/json", 403, "CONTROL_HOST_DENIED")]
    [InlineData("localhost:6217", "http://localhost:6217", "text/plain", 415, "CONTROL_JSON_REQUIRED")]
    public void Invalid_control_mutation_inputs_fail_before_owner_invocation(
        string host,
        string? origin,
        string contentType,
        int statusCode,
        string errorCode)
    {
        var context = RequestContext(host, IPAddress.Loopback);
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = Uri.UriSchemeHttp;
        context.Request.ContentType = contentType;
        if (origin is not null)
            context.Request.Headers.Origin = origin;

        var decision = ControlGuard(new WebRemoteAccessOptions()).Evaluate(
            context,
            PrivateOperatorCapability.ControlSettingsWrite,
            mutation: true);

        Assert.False(decision.Allowed);
        Assert.Equal(statusCode, decision.StatusCode);
        Assert.Equal(errorCode, decision.ErrorCode);
    }

    [Fact]
    public void Multiple_origins_and_non_control_capabilities_fail_closed()
    {
        var multiple = RequestContext("localhost:6217", IPAddress.Loopback);
        multiple.Request.Method = HttpMethods.Put;
        multiple.Request.Scheme = Uri.UriSchemeHttp;
        multiple.Request.ContentType = "application/json";
        multiple.Request.Headers.Origin = new Microsoft.Extensions.Primitives.StringValues(
            ["http://localhost:6217", "http://localhost:6217"]);
        var wrongCapability = RequestContext("localhost:6217", IPAddress.Loopback);
        wrongCapability.Request.Method = HttpMethods.Put;
        wrongCapability.Request.Scheme = Uri.UriSchemeHttp;
        wrongCapability.Request.ContentType = "application/json";
        wrongCapability.Request.Headers.Origin = "http://localhost:6217";

        var multipleDecision = ControlGuard(new WebRemoteAccessOptions()).Evaluate(
            multiple,
            PrivateOperatorCapability.ControlSettingsWrite,
            mutation: true);
        var capabilityDecision = ControlGuard(new WebRemoteAccessOptions()).Evaluate(
            wrongCapability,
            PrivateOperatorCapability.Read,
            mutation: true);

        Assert.False(multipleDecision.Allowed);
        Assert.Equal("CONTROL_ORIGIN_REQUIRED", multipleDecision.ErrorCode);
        Assert.False(capabilityDecision.Allowed);
        Assert.Equal("CONTROL_CAPABILITY_DENIED", capabilityDecision.ErrorCode);
    }

    [Fact]
    public async Task Rejected_control_request_never_invokes_the_handler()
    {
        var context = RequestContext("localhost:6217", IPAddress.Loopback);
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = Uri.UriSchemeHttp;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Origin = "https://forwarded.example:6217";
        context.Request.Headers["X-Forwarded-Host"] = "forwarded.example:6217";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        var filter = new WebControlRequestFilter(ControlGuard(new WebRemoteAccessOptions()));
        var invoked = false;

        await filter.InvokeAsync(
            new TestFilterContext(context),
            _ =>
            {
                invoked = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            },
            PrivateOperatorCapability.ControlPagesWrite,
            mutation: true);

        Assert.False(invoked);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task Accepted_control_request_invokes_the_handler_once_with_an_authenticated_principal()
    {
        var context = RequestContext("127.0.0.1:6217", IPAddress.Loopback);
        context.Request.Method = HttpMethods.Put;
        context.Request.Scheme = Uri.UriSchemeHttp;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Origin = "http://127.0.0.1:6217";
        var filter = new WebControlRequestFilter(ControlGuard(new WebRemoteAccessOptions()));
        var invocations = 0;

        await filter.InvokeAsync(
            new TestFilterContext(context),
            _ =>
            {
                invocations++;
                return ValueTask.FromResult<object?>(Results.Ok());
            },
            PrivateOperatorCapability.ControlPagesWrite,
            mutation: true);

        Assert.Equal(1, invocations);
        Assert.True(context.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Observation_authorization_runs_before_the_handler_and_supplies_a_verified_principal()
    {
        var denied = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        denied.Request.Method = HttpMethods.Post;
        denied.Request.ContentType = "application/json";
        denied.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "intruder@example.com";
        var remoteOptions = new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = ["operator@example.com"]
        };
        var deniedFilter = new WebObservationRequestFilter(
            new WebObservationRequestGuard(OperatorGuard(remoteOptions)));
        var deniedInvoked = false;

        await deniedFilter.InvokeAsync(new TestFilterContext(denied), _ =>
        {
            deniedInvoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.False(deniedInvoked);

        var accepted = RequestContext("localhost:6217", IPAddress.Loopback);
        accepted.Request.Method = HttpMethods.Post;
        accepted.Request.ContentType = "application/json; charset=utf-8";
        var acceptedFilter = new WebObservationRequestFilter(
            new WebObservationRequestGuard(OperatorGuard(new WebRemoteAccessOptions())));
        TrustedPrincipalContext? supplied = null;

        await acceptedFilter.InvokeAsync(new TestFilterContext(accepted), invocation =>
        {
            supplied = WebObservationRequestFilter.GetPrincipal(invocation.HttpContext);
            return ValueTask.FromResult<object?>(Results.Accepted());
        });

        Assert.NotNull(supplied);
        Assert.True(supplied!.Verified);
        Assert.True(TrustedPrincipalContext.IsValidPrincipalId(supplied.PrincipalId));
    }

    [Theory]
    [InlineData("GET", "application/json", 405, "OBSERVATION_METHOD_DENIED")]
    [InlineData("POST", "text/plain", 415, "OBSERVATION_JSON_REQUIRED")]
    public void Observation_method_and_media_type_fail_closed(
        string method,
        string contentType,
        int statusCode,
        string errorCode)
    {
        var context = RequestContext("localhost:6217", IPAddress.Loopback);
        context.Request.Method = method;
        context.Request.ContentType = contentType;

        var decision = new WebObservationRequestGuard(
            OperatorGuard(new WebRemoteAccessOptions())).Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Equal(statusCode, decision.StatusCode);
        Assert.Equal(errorCode, decision.ErrorCode);
        Assert.Null(decision.Principal);
    }

    [Fact]
    public async Task Phone_observation_authenticates_route_and_credential_before_body_parsing()
    {
        var context = RequestContext("localhost:6217", IPAddress.Loopback);
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.RouteValues["applicationId"] = "quest";
        context.Request.Headers[PhoneCompanionIdentity.CredentialHeader] =
            "phone-credential.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("not-json"));
        var authenticator = new RecordingPhoneAuthenticator(allowed: true);
        var access = AccessPolicy(new WebRemoteAccessOptions());
        var guard = new WebObservationRequestGuard(
            new WebPrivateOperatorGuard(access, new PrivateOperatorAuthorizationPolicy()),
            access, authenticator);

        var decision = await guard.EvaluateAsync(context);

        Assert.True(decision.Allowed);
        Assert.Equal(1, authenticator.Calls);
        Assert.Equal("quest", authenticator.ApplicationId!.Value);
        Assert.Equal(0, context.Request.Body.Position);
        Assert.Equal(PhoneCompanionIdentity.AuthenticationMethod,
            decision.Principal!.AuthenticationMethod);
    }

    [Fact]
    public async Task Unknown_revoked_and_wrong_application_phone_credentials_share_one_denial()
    {
        var access = AccessPolicy(new WebRemoteAccessOptions());
        var guard = new WebObservationRequestGuard(
            new WebPrivateOperatorGuard(access, new PrivateOperatorAuthorizationPolicy()),
            access, new RecordingPhoneAuthenticator(allowed: false));
        var decisions = new List<WebObservationRequestDecision>();
        foreach (var application in new[] { "quest", "other" })
        {
            var context = RequestContext("localhost:6217", IPAddress.Loopback);
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json";
            context.Request.RouteValues["applicationId"] = application;
            context.Request.Headers[PhoneCompanionIdentity.CredentialHeader] =
                "phone-credential.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            decisions.Add(await guard.EvaluateAsync(context));
        }

        Assert.All(decisions, decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Equal(StatusCodes.Status403Forbidden, decision.StatusCode);
            Assert.Equal("PHONE_CREDENTIAL_DENIED", decision.ErrorCode);
            Assert.Equal("The phone credential was not accepted.", decision.ErrorMessage);
        });
    }

    [Fact]
    public async Task Observation_reader_accepts_only_the_exact_bounded_envelope()
    {
        var reader = new ObservationHttpRequestReader();
        var valid = ObservationRequest(ValidObservationJson());

        var submission = await reader.ReadAsync(valid);

        Assert.Equal("observation-request.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", submission.RequestId);
        Assert.Equal("phone.dante", submission.Source.Id);
        Assert.Equal("device.geofence.transition", submission.Structure.Id);
        Assert.Equal(1, submission.Structure.Version);
        Assert.Equal("2026-08-25T20:00:00.0000000+00:00", submission.ObservedAt.ToString("O"));
        Assert.Equal("{\"transition\":\"entered\"}", submission.Data.Json);

        var invalidBodies = new[]
        {
            ValidObservationJson().Replace("\"data\":", "\"unknown\":true,\"data\":", StringComparison.Ordinal),
            ValidObservationJson().Replace("\"source\":", "\"requestId\":\"observation-request.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"source\":", StringComparison.Ordinal),
            ValidObservationJson().Replace("2026-08-25T20:00:00Z", "2026-08-25T22:00:00+02:00", StringComparison.Ordinal),
            ValidObservationJson().Replace("{\"transition\":\"entered\"}", "[]", StringComparison.Ordinal)
        };
        foreach (var body in invalidBodies)
        {
            var exception = await Assert.ThrowsAsync<ObservationHttpRequestException>(
                () => reader.ReadAsync(ObservationRequest(body)));
            Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        }

        var oversized = new DefaultHttpContext().Request;
        oversized.Body = Stream.Null;
        oversized.ContentLength = TriggerSchedulingLimits.MaximumRequestBytes + 1L;
        var tooLarge = await Assert.ThrowsAsync<ObservationHttpRequestException>(
            () => reader.ReadAsync(oversized));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, tooLarge.StatusCode);

        var invalidUtf8 = new DefaultHttpContext().Request;
        invalidUtf8.Body = new MemoryStream([0xC3, 0x28]);
        invalidUtf8.ContentLength = 2;
        var encoding = await Assert.ThrowsAsync<ObservationHttpRequestException>(
            () => reader.ReadAsync(invalidUtf8));
        Assert.Equal("OBSERVATION_UTF8_INVALID", encoding.Code);

        var resourceBoundBodies = new[]
        {
            ObservationJsonWithData("{" + string.Join(',', Enumerable.Range(0, 247).Select(index => $"\"p{index}\":0")) + "}"),
            ObservationJsonWithData("{\"items\":[" + string.Join(',', Enumerable.Repeat("0", 257)) + "]}"),
            ObservationJsonWithData("{\"text\":\"" + new string('x', TriggerSchedulingLimits.MaximumStringBytes + 1) + "\"}"),
            ObservationJsonWithData(string.Concat(Enumerable.Repeat("{\"nest\":", 16)) + "{}" + new string('}', 16)),
            ObservationJsonWithData("{\"items\":[" + string.Join(',', Enumerable.Repeat("0", 256)) + "]," +
                string.Join(',', Enumerable.Range(0, 245).Select(index => $"\"p{index}\":0")) + "}")
        };
        for (var index = 0; index < resourceBoundBodies.Length; index++)
        {
            var failure = await Record.ExceptionAsync(
                () => reader.ReadAsync(ObservationRequest(resourceBoundBodies[index])));
            Assert.True(failure is ObservationHttpRequestException,
                $"Resource-bound request {index} was unexpectedly accepted.");
            var bounded = Assert.IsType<ObservationHttpRequestException>(failure,
                exactMatch: true);
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, bounded.StatusCode);
            Assert.Equal("OBSERVATION_REQUEST_BOUNDS", bounded.Code);
        }
    }

    [Fact]
    public async Task Observation_endpoint_returns_only_the_safe_acceptance_fields()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());
        builder.Services.AddSingleton<IObservationIngestionService, AcceptedObservationIngestion>();
        var application = builder.Build();
        application.MapDantesRoleplayWeb();
        var endpoint = ((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(value => value.RoutePattern.RawText == "/api/applications/{applicationId}/observations");
        Assert.Equal(WebInterfaceSecurity.UploadRateLimitPolicy,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName);
        var context = RequestContext("localhost:6217", IPAddress.Loopback);
        context.RequestServices = application.Services;
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.RouteValues["applicationId"] = "quest";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(ValidObservationJson()));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        var names = response.RootElement.EnumerateObject().Select(value => value.Name).Order().ToArray();
        Assert.Equal(["accepted", "duplicate", "observationId", "status"], names);
        Assert.True(response.RootElement.GetProperty("accepted").GetBoolean());
        Assert.False(response.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.Equal("recorded", response.RootElement.GetProperty("status").GetString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public void Control_route_helpers_are_closed_under_the_control_prefix()
    {
        var builder = WebApplication.CreateBuilder();
        var application = builder.Build();
        application.MapDantesRoleplayControlGet(
            "/status",
            new Func<IResult>(() => Results.Ok()));
        application.MapDantesRoleplayControlPost(
            "/messages",
            PrivateOperatorCapability.ControlAiMessage,
            new Func<IResult>(() => Results.Ok()));
        application.MapDantesRoleplayControlPost(
            "/confirmed-system-task",
            PrivateOperatorCapability.Modify,
            new Func<IResult>(() => Results.Ok()));

        var patterns = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/api/control/status", patterns);
        Assert.Contains("/api/control/messages", patterns);
        Assert.Contains("/api/control/confirmed-system-task", patterns);
        Assert.Throws<ArgumentException>(() => application.MapDantesRoleplayControlGet(
            "/api/escape",
            new Func<IResult>(() => Results.Ok())));
        Assert.Throws<ArgumentOutOfRangeException>(() => application.MapDantesRoleplayControlPut(
            "/settings",
            PrivateOperatorCapability.Read,
            new Func<IResult>(() => Results.Ok())));
    }

    [Fact]
    public void Control_center_status_is_bounded_and_derives_only_authenticated_access_identity()
    {
        var local = ControlCenterStatus.Create(WebAccessPolicy.CreatePrincipal(new(
            true,
            WebAccessMode.Local)));
        var tailscale = ControlCenterStatus.Create(WebAccessPolicy.CreatePrincipal(new(
            true,
            WebAccessMode.Tailscale,
            "operator@example.com")));
        var response = new DefaultHttpContext().Response;

        ControlCenterStatus.ApplyCacheHeaders(response);

        Assert.Equal("ready", local.Status);
        Assert.Equal("local", local.Access.Mode);
        Assert.Null(local.Access.Login);
        Assert.Equal("tailscale", tailscale.Access.Mode);
        Assert.Equal("operator@example.com", tailscale.Access.Login);
        Assert.Collection(
            local.Panels,
            panel =>
            {
                Assert.Equal("server-settings", panel.Id);
                Assert.Equal("ready", panel.State);
                Assert.False(string.IsNullOrWhiteSpace(panel.Message));
            },
            panel => AssertPanel(panel, "effect-history"),
            panel =>
            {
                Assert.Equal("trigger-scheduling", panel.Id);
                Assert.Equal("ready", panel.State);
                Assert.False(string.IsNullOrWhiteSpace(panel.Message));
            },
            panel =>
            {
                Assert.Equal("assistant", panel.Id);
                Assert.Equal("ready", panel.State);
                Assert.False(string.IsNullOrWhiteSpace(panel.Message));
            },
            panel => AssertPanel(panel, "ecs-explorer"),
            panel =>
            {
                Assert.Equal("site-editor", panel.Id);
                Assert.Equal("ready", panel.State);
                Assert.False(string.IsNullOrWhiteSpace(panel.Message));
            });
        Assert.Equal(ControlCenterStatus.CacheControl, response.Headers.CacheControl);
    }

    [Fact]
    public void Control_center_shell_keeps_one_workspace_inside_closed_persistent_navigation()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));

        Assert.Contains("class=\"app-shell\"", html, StringComparison.Ordinal);
        Assert.Contains("<aside class=\"sidebar\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"workspace-title\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/settings\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/effects\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/triggers\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/assistants\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/applications\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/site-editor\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-current", html, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"hashchange\"", html, StringComparison.Ordinal);
        Assert.Contains("history.replaceState", html, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 760px)", html, StringComparison.Ordinal);

        Assert.Equal(1, html.Split("<server-settings-panel", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<effect-history-panel", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<trigger-scheduling-panel", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<assistant-panel", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<ecs-explorer", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<site-editor", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Trigger_panel_previews_before_apply_and_displays_the_phone_secret_only_from_commit()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var start = html.IndexOf("class TriggerSchedulingPanel", StringComparison.Ordinal);
        var end = html.IndexOf("class SiteEditorPanel", start, StringComparison.Ordinal);
        var panel = html[start..end];

        Assert.Contains("/api/control/triggers/applications", panel, StringComparison.Ordinal);
        Assert.Contains("/api/control/triggers/commands/preview", panel, StringComparison.Ordinal);
        Assert.Contains("/api/control/triggers/commands", panel, StringComparison.Ordinal);
        Assert.Contains("this.pending.set(key, raw)", panel, StringComparison.Ordinal);
        Assert.Contains("apply.disabled = false", panel, StringComparison.Ordinal);
        Assert.Contains("will not be shown again", panel, StringComparison.Ordinal);
        Assert.Contains("crypto.getRandomValues", panel, StringComparison.Ordinal);
        Assert.Contains("Show derived principal", panel, StringComparison.Ordinal);
        Assert.Contains("phone.revoke", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/applications/", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("credentialVerifier", panel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Site_editor_saves_changed_html_as_a_draft_before_previewing_it()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));

        Assert.Contains("Save & preview draft", html, StringComparison.Ordinal);
        Assert.Contains("saveDraft(page, revision, textarea.value, body, state, true)", html,
            StringComparison.Ordinal);
        Assert.Contains("async saveDraft(page, baseRevision, html, body, state, previewAfterSave = false)", html,
            StringComparison.Ordinal);
        Assert.Contains("await this.selectRevision(current, detail.summary.revision, body)", html,
            StringComparison.Ordinal);
        Assert.Contains("this.preview(page.id, detail.summary.revision, body)", html, StringComparison.Ordinal);
        Assert.Contains("Preview saved revision", html, StringComparison.Ordinal);
        Assert.Contains("frame.setAttribute(\"sandbox\", \"allow-scripts\")", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_navigation_deep_links_into_the_existing_structure_owner_without_embedding_pages()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var routerStart = html.IndexOf("const workspaceRoutes", StringComparison.Ordinal);
        var panelStart = html.IndexOf("class EcsExplorerPanel", StringComparison.Ordinal);
        var panelEnd = html.IndexOf("class SiteEditorPanel", panelStart, StringComparison.Ordinal);

        Assert.True(routerStart > 0);
        Assert.True(panelStart > routerStart);
        var router = html[routerStart..panelStart];
        var panel = html[panelStart..panelEnd];
        Assert.Contains("#/applications/", router, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent(applicationId)", router, StringComparison.Ordinal);
        Assert.Contains("navigateToApplication(application.id)", panel, StringComparison.Ordinal);
        Assert.Contains("openApplicationFromRoute", panel, StringComparison.Ordinal);
        Assert.Contains("showApplicationListFromRoute", panel, StringComparison.Ordinal);
        Assert.Contains("decodeURIComponent", router, StringComparison.Ordinal);
        Assert.Contains("applicationId.length > 200", router, StringComparison.Ordinal);
        Assert.Contains("panelId: \"ecs-explorer\"", router, StringComparison.Ordinal);
        Assert.DoesNotContain("createElement(\"iframe\")", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("postMessage", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_setting_provider_freezes_allowlist_sources_and_inactive_runtime()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Knowledge:Completion:Enabled"] = "false",
                ["Knowledge:Completion:Endpoint"] = "http://127.0.0.1:11435",
                ["Knowledge:Completion:Timeout"] = "00:02:00"
            })
            .Build();

        var catalog = new ConfiguredHostSettingDefinitionProvider(configuration).GetCatalog();

        Assert.Equal(HostSettingRuntimeState.NotRegistered, catalog.Runtime.State);
        Assert.Equal(
        [
            "local-completion.enabled",
            "local-completion.endpoint",
            "local-completion.model",
            "local-completion.profile",
            "local-completion.max-output-tokens",
            "local-completion.timeout-seconds",
            "local-completion.max-concurrent-requests"
        ], catalog.Definitions.Select(definition => definition.Key));
        Assert.All(catalog.Definitions, definition =>
        {
            Assert.Equal(HostSettingSensitivity.PublicValue, definition.Sensitivity);
            Assert.Equal(HostSettingMutability.RestartRequired, definition.Mutability);
            Assert.Equal(HostSettingDisruption.HostRestart, definition.Disruption);
            Assert.Null(definition.EffectiveValue);
            Assert.Null(definition.PendingValue);
            Assert.False(definition.RestartRequired);
        });
        var enabled = catalog.Definitions[0];
        Assert.True(enabled.Configured);
        Assert.Equal("configuration", enabled.Source);
        Assert.False(enabled.Value!.Value.GetBoolean());
        Assert.Equal("default", catalog.Definitions[2].Source);
        Assert.Equal("qwen3:8b", catalog.Definitions[2].Value!.Value.GetString());
        Assert.Equal(120, catalog.Definitions[5].Value!.Value.GetInt32());
        Assert.Equal(8192, catalog.Definitions[4].Schema.GetProperty("maximum").GetInt32());
    }

    [Theory]
    [InlineData("Knowledge:Completion:Enabled", "sometimes")]
    [InlineData("Knowledge:Completion:Endpoint", "https://example.com")]
    [InlineData("Knowledge:Completion:MaxOutputTokens", "63")]
    [InlineData("Knowledge:Completion:Timeout", "00:00:00.500")]
    public void Host_setting_provider_rejects_invalid_startup_values(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => new ConfiguredHostSettingDefinitionProvider(configuration));
    }

    [Fact]
    public void Host_setting_provider_normalizes_and_applies_override_heads_without_activating_a_provider()
    {
        var provider = new ConfiguredHostSettingDefinitionProvider(new ConfigurationBuilder().Build());
        var normalized = provider.NormalizeOverride(
            "local-completion.profile", JsonSerializer.SerializeToElement("  focused  "));
        Assert.Equal("focused", normalized.GetString());
        Assert.Throws<InvalidOperationException>(() => provider.NormalizeOverride(
            "local-completion.max-concurrent-requests", JsonSerializer.SerializeToElement(9)));

        provider.ApplyStartupOverrides(new Dictionary<string, JsonElement?>
        {
            ["local-completion.profile"] = normalized,
            ["local-completion.enabled"] = null
        });

        var catalog = provider.GetCatalog();
        var profile = catalog.Definitions.Single(item => item.Key == "local-completion.profile");
        Assert.Equal("override", profile.Source);
        Assert.Equal("focused", profile.Value!.Value.GetString());
        Assert.False(profile.RestartRequired);
        Assert.Equal(HostSettingRuntimeState.NotRegistered, catalog.Runtime.State);
        Assert.Equal("default", catalog.Definitions.Single(item => item.Key == "local-completion.enabled").Source);
    }

    [Fact]
    public void Host_setting_provider_projects_applied_values_into_the_fixed_assistant_provider()
    {
        var provider = new ConfiguredHostSettingDefinitionProvider(new ConfigurationBuilder().Build());
        provider.ApplyStartupOverrides(new Dictionary<string, JsonElement?>
        {
            ["local-completion.enabled"] = JsonSerializer.SerializeToElement(true),
            ["local-completion.endpoint"] = JsonSerializer.SerializeToElement("http://127.0.0.1:11435/"),
            ["local-completion.model"] = JsonSerializer.SerializeToElement("fixture-model"),
            ["local-completion.profile"] = JsonSerializer.SerializeToElement("fixture-profile"),
            ["local-completion.max-output-tokens"] = JsonSerializer.SerializeToElement(512),
            ["local-completion.timeout-seconds"] = JsonSerializer.SerializeToElement(45),
            ["local-completion.max-concurrent-requests"] = JsonSerializer.SerializeToElement(2)
        });

        var options = provider.CreateCompletionOptions();
        provider.MarkProviderRegistered();

        Assert.True(options.Enabled);
        Assert.Equal(new Uri("http://127.0.0.1:11435/"), options.Endpoint);
        Assert.Equal("fixture-model", options.Model);
        Assert.Equal("fixture-profile", options.Profile);
        Assert.Equal(512, options.MaxOutputTokens);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Timeout);
        Assert.Equal(2, options.MaxConcurrentRequests);
        Assert.Equal(
            [
                AssistantConversationService.TaskClass,
                SystemConversationService.TaskClass,
                InteractionPlannerProtocol.TaskClass
            ],
            options.AllowedTaskClasses.Order(StringComparer.Ordinal));
        Assert.Equal(HostSettingRuntimeState.Ready, provider.GetCatalog().Runtime.State);
        Assert.All(provider.GetCatalog().Definitions, definition => Assert.Equal(definition.Value, definition.EffectiveValue));
    }

    [Fact]
    public void Host_setting_projection_redacts_configured_only_values_and_rejects_bad_catalogs()
    {
        var schema = JsonDocument.Parse("""{"type":"string"}""").RootElement.Clone();
        var secret = JsonSerializer.SerializeToElement("do-not-return");
        var definition = new HostSettingDefinition(
            "private.token",
            "Private token",
            "A configured-only test definition.",
            HostSettingSensitivity.ConfiguredOnly,
            HostSettingMutability.ReadOnly,
            HostSettingDisruption.None,
            "configuration",
            true,
            secret,
            secret,
            secret,
            true,
            schema);
        var explorer = new ControlSettingsExplorer(new StaticHostSettingProvider(new(
            new(HostSettingRuntimeState.Ready, "Ready."),
            [definition])));

        var summary = Assert.Single(explorer.List().Items);
        Assert.Equal("configured-only", summary.Sensitivity);
        Assert.True(summary.Configured);
        Assert.Null(summary.Value);
        Assert.Null(summary.EffectiveValue);
        Assert.Null(summary.PendingValue);
        Assert.Equal("string", explorer.Get("private.token")!.Schema.GetProperty("type").GetString());
        Assert.Null(explorer.Get("unknown.key"));
        Assert.Equal("INVALID_SETTING_KEY", Assert.Throws<ControlSettingsException>(
            () => explorer.Get("../private")).Code);

        var duplicates = new ControlSettingsExplorer(new StaticHostSettingProvider(new(
            new(HostSettingRuntimeState.Ready, "Ready."),
            [definition, definition])));
        Assert.Equal("HOST_SETTING_CATALOG_INVALID", Assert.Throws<ControlSettingsException>(
            duplicates.List).Code);
    }

    [Fact]
    public void Server_settings_panel_stages_versioned_changes_and_never_restarts_the_host()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var start = html.IndexOf("class ServerSettingsPanel", StringComparison.Ordinal);
        var end = html.IndexOf("class EffectHistoryPanel", start, StringComparison.Ordinal);
        var panel = html[start..end];

        Assert.Contains("/api/control/settings", panel, StringComparison.Ordinal);
        Assert.Contains("/api/control/settings/", panel, StringComparison.Ordinal);
        Assert.Contains("Stage change", panel, StringComparison.Ordinal);
        Assert.Contains("Reset to startup value", panel, StringComparison.Ordinal);
        Assert.Contains("/versions", panel, StringComparison.Ordinal);
        Assert.Contains("/rollback", panel, StringComparison.Ordinal);
        Assert.Contains("POST", panel, StringComparison.Ordinal);
        Assert.Contains("PUT", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("restart()", panel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Web_route_map_includes_the_versioned_settings_control_endpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb(
            "Data Source=:memory:",
            new ConfigurationBuilder().Build());
        var application = builder.Build();

        application.MapDantesRoleplayWeb();
        Assert.Equal(
            HostSettingRuntimeState.Unavailable,
            ((IHostSettingDefinitionProvider)application.Services.GetService(
                typeof(IHostSettingDefinitionProvider))!)
                .GetCatalog().Runtime.State);

        var endpoints = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(candidate => candidate.RoutePattern.RawText!.StartsWith("/api/control/", StringComparison.Ordinal))
            .ToList();
        var patterns = endpoints.Select(endpoint => endpoint.RoutePattern.RawText).ToList();

        Assert.Equal(
        [
            "/api/control/status",
            "/api/control/settings",
            "/api/control/settings/{key}",
            "/api/control/settings/{key}/versions",
            "/api/control/settings/{key}",
            "/api/control/settings/{key}/reset",
            "/api/control/settings/{key}/rollback",
            "/api/control/assistants/local/status",
            "/api/control/assistants/codex/status",
            "/api/control/conversations",
            "/api/control/conversations/{conversationId}",
            "/api/control/conversations",
            "/api/control/conversations/{conversationId}/turns",
            "/api/control/conversations/{conversationId}/turns/{turnId}/cancel",
            "/api/control/conversations/{conversationId}/turns/{turnId}/approvals/{approvalId}",
            "/api/control/system/conversations",
            "/api/control/system/conversations/{conversationId}",
            "/api/control/system/conversations",
            "/api/control/system/conversations/{conversationId}/turns",
            "/api/control/system/conversations/{conversationId}/tasks",
            "/api/control/system/conversations/{conversationId}/tasks",
            "/api/control/system/tasks/{taskId}",
            "/api/control/system/tasks/{taskId}/confirmations",
            "/api/control/system/tasks/{taskId}/executions",
            "/api/control/system/capabilities/{capabilityId}",
            "/api/control/effects",
            "/api/control/effects/{eventId}",
            "/api/control/triggers/applications",
            "/api/control/triggers/applications/{applicationId}",
            "/api/control/triggers/applications/{applicationId}/phone-principal/{deviceId}",
            "/api/control/triggers/commands/preview",
            "/api/control/triggers/commands",
            "/api/control/structure/applications",
            "/api/control/structure/applications/{applicationId}",
            "/api/control/structure/applications/{applicationId}/state-spaces",
            "/api/control/structure/applications/{applicationId}/component-types",
            "/api/control/structure/component-types/{qualifiedId}/versions/{version:int}",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities/{entityId}",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}",
            "/api/control/structure/applications/{applicationId}/catalog",
            "/api/control/structure/applications/{applicationId}/catalog/browse",
            "/api/control/structure/applications/{applicationId}/catalog/search",
            "/api/control/structure/applications/{applicationId}/catalog/records/{qualifiedId}",
            "/api/control/pages",
            "/api/control/pages/{pageId}",
            "/api/control/pages/{pageId}/revisions",
            "/api/control/pages/{pageId}/revisions/{revision:int}",
            "/api/control/pages/{pageId}/revisions/{revision:int}/bundle",
            "/api/control/pages/{pageId}/revisions/{revision:int}/preview/index.html",
            "/api/control/pages/{pageId}/revisions/{revision:int}/preview/assets/{**path}",
            "/api/control/pages/{pageId}/drafts",
            "/api/control/pages/{pageId}/active"
        ], patterns);
        Assert.All(endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Single() == HttpMethods.Get),
            endpoint => Assert.Equal(
                [HttpMethods.Get], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.Equal(
            [HttpMethods.Put],
            endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/control/settings/{key}" &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single() == HttpMethods.Put)
                .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.All(endpoints.Where(endpoint => endpoint.RoutePattern.RawText is
            "/api/control/settings/{key}/reset" or "/api/control/settings/{key}/rollback"),
            endpoint => Assert.Equal(
                [HttpMethods.Post], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.All(endpoints.Where(endpoint => endpoint.RoutePattern.RawText is
            "/api/control/conversations" or "/api/control/conversations/{conversationId}/turns" or
            "/api/control/conversations/{conversationId}/turns/{turnId}/cancel" or
            "/api/control/conversations/{conversationId}/turns/{turnId}/approvals/{approvalId}" or
            "/api/control/system/conversations" or
            "/api/control/system/conversations/{conversationId}/turns" or
            "/api/control/system/conversations/{conversationId}/tasks" or
            "/api/control/system/tasks/{taskId}/confirmations" or
            "/api/control/system/tasks/{taskId}/executions" &&
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single() == HttpMethods.Post),
            endpoint => Assert.Equal(
                [HttpMethods.Post], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.Equal(
            [HttpMethods.Post],
            endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/control/pages/{pageId}/drafts")
                .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(
            [HttpMethods.Put],
            endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/control/pages/{pageId}/active")
                .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
    }

    [Fact]
    public void Web_route_map_includes_a_read_only_root_home_entry()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb(
            "Data Source=:memory:",
            new ConfigurationBuilder().Build());
        var application = builder.Build();

        application.MapDantesRoleplayWeb();

        var endpoints = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        var root = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/");

        Assert.Equal([HttpMethods.Get], root.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.DoesNotContain(endpoints, endpoint => endpoint.RoutePattern.RawText == "/mcp");
    }

    [Fact]
    public void Home_and_site_editor_expose_direct_page_navigation()
    {
        var root = RepositoryRoot();
        var home = File.ReadAllText(Path.Combine(
            root, "src", "system", "web-interface", "examples", "home.html"));
        var controlCenter = File.ReadAllText(Path.Combine(
            root, "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var endpoints = File.ReadAllText(Path.Combine(
            root, "src", "system", "web-interface", "DantesRoleplay.Web", "Http", "WebInterfaceEndpoints.cs"));

        Assert.Contains("private const string HomePageId = \"home\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("GetPageAsync(HomePageId", endpoints, StringComparison.Ordinal);
        Assert.Contains("href=\"/ui/control-center/index.html\"", home, StringComparison.Ordinal);
        Assert.Contains("Open control center", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/ui/dnd2024-play\"", home, StringComparison.Ordinal);
        Assert.Contains("Enter D&amp;D 2024 game table", home, StringComparison.Ordinal);
        Assert.Contains("livePageLink(item.id)", controlCenter, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent(pageId)", controlCenter, StringComparison.Ordinal);
        Assert.Contains("Open live page", controlCenter, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_launcher_maps_reviewed_sources_and_enables_the_local_chat_providers()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "system", "web-interface",
            "scripts", "Start-PrivateWeb.ps1"));

        Assert.Contains("Sources__AllowedRoots__repository", script, StringComparison.Ordinal);
        Assert.Contains("Catalogs__PublishedApplications__0", script, StringComparison.Ordinal);
        Assert.Contains("Knowledge__Completion__Enabled', 'true'", script, StringComparison.Ordinal);
        Assert.Contains("InteractionOuter__Local__Enabled', 'true'", script, StringComparison.Ordinal);
        Assert.Contains("InteractionOuter__Local__Profile', 'outer'", script, StringComparison.Ordinal);
        Assert.Contains("$previousEnvironment", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_navigation_is_composed_once_by_system_and_application_pages()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "system", "web-interface", "examples");
        var home = File.ReadAllText(Path.Combine(examples, "home.html"));
        var controlCenter = File.ReadAllText(Path.Combine(examples, "control-center", "index.html"));
        var application = File.ReadAllText(Path.Combine(examples, "application-page.html"));

        foreach (var page in new[] { home, controlCenter, application })
        {
            Assert.Contains("<system-navigation", page, StringComparison.Ordinal);
            Assert.Contains("type=\"module\" src=\"/components/system-workspace.js\"", page,
                StringComparison.Ordinal);
            Assert.Equal(1, page.Split("<system-navigation", StringSplitOptions.None).Length - 1);
        }

        Assert.DoesNotContain("<nav class=\"nav\" aria-label=\"System navigation\"", home,
            StringComparison.Ordinal);
        Assert.Contains("application-id=\"dnd2024\"", application, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Control center functions\"", controlCenter, StringComparison.Ordinal);
        Assert.Contains("#/applications/", controlCenter, StringComparison.Ordinal);
    }

    [Fact]
    public void Authored_pages_compose_system_and_application_chat_without_crossing_scope()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "system", "web-interface", "examples");
        var home = File.ReadAllText(Path.Combine(examples, "home.html"));
        var controlCenter = File.ReadAllText(Path.Combine(examples, "control-center", "index.html"));
        var applicationPage = File.ReadAllText(Path.Combine(examples, "application-page.html"));

        Assert.Contains("General system chat", home, StringComparison.Ordinal);
        Assert.Contains("<system-chat aria-label=\"General system chat\"></system-chat>", home,
            StringComparison.Ordinal);
        Assert.Contains("document.createElement(\"application-conversation\")", home,
            StringComparison.Ordinal);
        Assert.Contains("conversation.setAttribute(\"application-id\", application.value)", home,
            StringComparison.Ordinal);
        Assert.Contains("conversation.setAttribute(\"state-space-id\", stateSpace.value)", home,
            StringComparison.Ordinal);

        var assistantStart = controlCenter.IndexOf("class AssistantPanel", StringComparison.Ordinal);
        var assistantEnd = controlCenter.IndexOf("class EcsExplorerPanel", assistantStart,
            StringComparison.Ordinal);
        var assistant = controlCenter[assistantStart..assistantEnd];
        Assert.Contains("document.createElement(\"system-chat\")", assistant, StringComparison.Ordinal);
        Assert.DoesNotContain("application-id", assistant, StringComparison.Ordinal);
        Assert.DoesNotContain("state-space-id", assistant, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/applications/", assistant, StringComparison.Ordinal);

        var explorerStart = assistantEnd;
        var explorerEnd = controlCenter.IndexOf("class SiteEditorPanel", explorerStart,
            StringComparison.Ordinal);
        var explorer = controlCenter[explorerStart..explorerEnd];
        Assert.Contains("this.renderApplicationChat(applicationId, spaces.items || [], body)", explorer,
            StringComparison.Ordinal);
        Assert.Contains("document.createElement(\"application-conversation\")", explorer,
            StringComparison.Ordinal);
        Assert.Contains("conversation.setAttribute(\"application-id\", applicationId)", explorer,
            StringComparison.Ordinal);
        Assert.Contains("conversation.setAttribute(\"state-space-id\", stateSpaceId)", explorer,
            StringComparison.Ordinal);
        Assert.Contains("conversation.setAttribute(\"session-context-id\"", explorer,
            StringComparison.Ordinal);
        Assert.Contains("select.addEventListener(\"change\", mount)", explorer, StringComparison.Ordinal);
        Assert.Contains("host.replaceChildren()", explorer, StringComparison.Ordinal);
        Assert.Contains("/components/application-conversation.js", controlCenter, StringComparison.Ordinal);

        Assert.Contains("application-id=\"dnd2024\" state-space-id=\"dnd2024-main\"", applicationPage,
            StringComparison.Ordinal);
        Assert.Contains("session-context-id=\"application-page.dnd2024\"", applicationPage,
            StringComparison.Ordinal);
        Assert.Contains("<system-chat aria-label=\"General system chat\"></system-chat>", applicationPage,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<system-chat application-id", applicationPage, StringComparison.Ordinal);
        Assert.Contains("/components/application-conversation.js", applicationPage, StringComparison.Ordinal);
    }

    [Fact]
    public void Dnd2024_play_page_mounts_the_same_origin_react_information_hub()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "dnd2024-play", "index.html"));

        Assert.Contains("<div id=\"root\">", html, StringComparison.Ordinal);
        Assert.Contains("type=\"module\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/ui/dnd2024-play/assets/", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/ui/dnd2024-play/assets/", html, StringComparison.Ordinal);
        Assert.Contains("Dante's Roleplay — World &amp; Campaign Reference", html, StringComparison.Ordinal);
        Assert.Contains("Dante's Roleplay", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dnd2024-workspace", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost:5173", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chatgpt.site", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<system-form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("application/json", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_dashboard_uses_existing_local_conversation_structure_and_browser_local_notes()
    {
        var home = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "home.html"));

        Assert.Contains("Local outer chat", home, StringComparison.Ordinal);
        Assert.Contains("/components/application-conversation.js", home, StringComparison.Ordinal);
        Assert.Contains("document.createElement(\"application-conversation\")", home, StringComparison.Ordinal);
        Assert.Contains("/api/control/structure/applications", home, StringComparison.Ordinal);
        Assert.Contains("/state-spaces", home, StringComparison.Ordinal);
        Assert.Contains("dantes.personal-dashboard.notes.v1", home, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem(storageKeys.notes", home, StringComparison.Ordinal);
        Assert.Contains("id=\"local-date-time\"", home, StringComparison.Ordinal);
        Assert.Contains("window.setInterval(updateClock, 1000)", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/ui/dnd2024-play\"", home, StringComparison.Ordinal);
        Assert.Contains("Enter D&amp;D 2024 game table", home, StringComparison.Ordinal);
        Assert.Contains("radial-gradient", home, StringComparison.Ordinal);
        Assert.DoesNotContain("api.openai.com", home, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InteractionOuter:Provider", home, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assistant_panel_keeps_codex_approvals_one_request_turn_scoped_and_closed()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var start = html.IndexOf("class AssistantPanel", StringComparison.Ordinal);
        var end = html.IndexOf("class EcsExplorerPanel", start, StringComparison.Ordinal);
        var panel = html[start..end];

        Assert.Contains("/api/control/assistants/\" + providerName + \"/status", panel, StringComparison.Ordinal);
        Assert.Contains("/api/control/conversations", panel, StringComparison.Ordinal);
        Assert.Contains("providerName === \"codex\"", panel, StringComparison.Ordinal);
        Assert.Contains("response.body.getReader()", panel, StringComparison.Ordinal);
        Assert.Contains("new TextDecoder()", panel, StringComparison.Ordinal);
        Assert.Contains("Cancel active Codex turn", panel, StringComparison.Ordinal);
        Assert.Contains("read-only/no-network baseline", panel, StringComparison.Ordinal);
        Assert.Contains("model \" + provider.model", panel, StringComparison.Ordinal);
        Assert.Contains("New conversations use the host-selected model", panel, StringComparison.Ordinal);
        Assert.Contains("Accept once", panel, StringComparison.Ordinal);
        Assert.Contains("Decline", panel, StringComparison.Ordinal);
        Assert.Contains("Cancel turn", panel, StringComparison.Ordinal);
        Assert.Contains("/approvals/", panel, StringComparison.Ordinal);
        Assert.Contains("There is no session-wide approval", panel, StringComparison.Ordinal);
        Assert.Contains("expectedRevision", panel, StringComparison.Ordinal);
        Assert.Contains("decision", panel, StringComparison.Ordinal);
        Assert.Contains("crypto.randomUUID", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("systemPrompt", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("responseSchema", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptForSession", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("workspaceWrite", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dangerFullAccess", panel, StringComparison.OrdinalIgnoreCase);

        var unknownField = new DefaultHttpContext();
        unknownField.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"provider\":\"local\",\"message\":\"hello\",\"idempotencyKey\":\"web:1\",\"prompt\":\"forbidden\"}"));
        var invalid = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlAssistantExplorer.ReadBodyAsync<AssistantConversationCreate>(unknownField.Request));
        Assert.Equal("ASSISTANT_BODY_INVALID", invalid.Code);

        var approvalField = new DefaultHttpContext();
        approvalField.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"expectedRevision\":1,\"decision\":\"accept\",\"scope\":\"session\"}"));
        var invalidApproval = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlAssistantExplorer.ReadBodyAsync<CodexApprovalDecisionInput>(approvalField.Request));
        Assert.Equal("ASSISTANT_BODY_INVALID", invalidApproval.Code);

        var oversized = new DefaultHttpContext();
        oversized.Request.ContentLength = 16 * 1024 + 1;
        var tooLarge = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlAssistantExplorer.ReadBodyAsync<AssistantConversationCreate>(oversized.Request));
        Assert.Equal("ASSISTANT_BODY_TOO_LARGE", tooLarge.Code);
    }

    [Fact]
    public async Task Structure_explorer_reads_exact_application_ecs_and_schema_evidence_without_publishing_catalog_files()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var applicationId = ApplicationIdentifier.Parse("fixture-app");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(applicationId, "Fixture", "Explorer fixture", []));
        var otherApplicationId = ApplicationIdentifier.Parse("other-app");
        var otherRevision = applications.Register(new(otherApplicationId, "Other", "Other fixture", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("fixture-space", revision, new string('A', 64)));
        stateSpaces.Create(new("other-space", otherRevision, new string('B', 64)));
        var validator = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, validator);
        var stats = types.Define(new(applicationId, "fixture-app.stats", "{\"type\":\"object\"}"));
        var entities = new SqliteEntityComponentStore(db, types, validator);
        await entities.CreateEntityAsync("fixture-space", "hero", "Hero");
        await entities.AddComponentAsync(new(
            "fixture-space", "hero",
            new(stats.QualifiedId, stats.Version, stats.SchemaHash),
            "{\"health\":12}", 0));
        await entities.CreateEntityAsync("fixture-space", "item-lantern", "Lantern");
        var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        await edges.MoveContainmentAsync("fixture-space", "item-lantern", "hero", "pack", 0);
        var changesBeforeReads = await SqliteTotalChangesAsync(db);
        var capabilities = ApplicationCapabilities(applications);
        var explorer = new ControlStructureExplorer(
            applications, stateSpaces, types, entities, new EmptyPublicApplicationCatalogProvider(), capabilities,
            edges);

        var appPage = await explorer.ListApplicationsThroughCapabilitiesAsync(CapabilityAuthorization(), null, "1");
        var app = await explorer.GetApplicationThroughCapabilitiesAsync(CapabilityAuthorization(), "fixture-app");
        var spaces = explorer.ListStateSpaces("fixture-app", null, null);
        var applicationSpaces = explorer.ListApplicationStateSpaces("fixture-app", null, null);
        var typePage = explorer.ListComponentTypes("fixture-app", null, null);
        var schema = explorer.GetComponentType("fixture-app.stats", 1);
        var entityPage = await explorer.ListEntitiesAsync("fixture-space", null, null);
        var componentPage = await explorer.ListComponentsAsync("fixture-space", "hero", null, null);
        var component = await explorer.GetComponentAsync("fixture-space", "hero", "fixture-app.stats");
        var applicationEntities = await explorer.ListApplicationEntitiesAsync(
            "fixture-app", "fixture-space", null, null);
        var applicationEntity = await explorer.GetApplicationEntityAsync(
            "fixture-app", "fixture-space", "hero");
        var applicationComponents = await explorer.ListApplicationComponentsAsync(
            "fixture-app", "fixture-space", "hero", null, null);
        var applicationComponent = await explorer.GetApplicationComponentAsync(
            "fixture-app", "fixture-space", "hero", "fixture-app.stats");
        var containments = await explorer.ListApplicationContainmentsAsync(
            "fixture-app", "fixture-space", "hero", null, "1");
        var directContainment = await explorer.GetApplicationContainmentAsync(
            "fixture-app", "fixture-space", "item-lantern");
        var noContainment = await explorer.GetApplicationContainmentAsync(
            "fixture-app", "fixture-space", "hero");
        var catalog = explorer.GetCatalog("fixture-app");

        Assert.Equal("fixture-app", Assert.Single(appPage.Items).Id);
        Assert.Equal("Explorer fixture", app!.Description);
        Assert.Equal("fixture-space", Assert.Single(spaces.Items).StateSpaceId);
        Assert.Equal("fixture-space", Assert.Single(applicationSpaces.Items).StateSpaceId);
        Assert.Equal(stats.SchemaHash, Assert.Single(typePage.Items).SchemaHash);
        Assert.Equal("{\"type\":\"object\"}", schema!.SchemaJson);
        Assert.Equal(["hero", "item-lantern"], entityPage.Items.Select(value => value.EntityId));
        Assert.Equal("fixture-app.stats", Assert.Single(componentPage.Items).QualifiedTypeId);
        Assert.Equal("{\"health\":12}", component!.ValueJson);
        Assert.Equal(stats.SchemaHash, component.SchemaHash);
        Assert.Equal(["hero", "item-lantern"], applicationEntities.Items.Select(value => value.EntityId));
        Assert.Equal("hero", applicationEntity!.EntityId);
        Assert.Equal("fixture-app.stats", Assert.Single(applicationComponents.Items).QualifiedTypeId);
        Assert.Equal("{\"health\":12}", applicationComponent!.ValueJson);
        var containment = Assert.Single(containments.Items);
        Assert.Equal("item-lantern", containment.ContainedEntityId);
        Assert.Equal("hero", containment.ContainerEntityId);
        Assert.Equal("pack", containment.Slot);
        Assert.Equal("item-lantern", directContainment.Containment!.ContainedEntityId);
        Assert.Equal("hero", directContainment.Containment.ContainerEntityId);
        Assert.Equal("pack", directContainment.Containment.Slot);
        Assert.Null(noContainment.Containment);
        Assert.Equal("unavailable", catalog.Status);
        Assert.Empty(catalog.Collections);
        Assert.Null(explorer.GetComponentType("fixture-app.stats", 2));
        Assert.Equal("STATE_SPACE_UNKNOWN", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.GetEntityAsync("missing-space", "hero"))).Code);
        Assert.Equal("ENTITY_UNKNOWN", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListComponentsAsync("fixture-space", "missing", null, null))).Code);
        Assert.Equal("STATE_SPACE_WRONG_APPLICATION", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListApplicationEntitiesAsync("fixture-app", "other-space", null, null))).Code);
        Assert.Equal("STATE_SPACE_WRONG_APPLICATION", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.GetApplicationEntityAsync("other-app", "fixture-space", "hero"))).Code);
        Assert.Equal("STATE_SPACE_WRONG_APPLICATION", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListApplicationComponentsAsync(
                "other-app", "fixture-space", "hero", null, null))).Code);
        Assert.Equal("STATE_SPACE_WRONG_APPLICATION", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.GetApplicationComponentAsync(
                "other-app", "fixture-space", "hero", "fixture-app.stats"))).Code);
        Assert.Equal("STATE_SPACE_WRONG_APPLICATION", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListApplicationContainmentsAsync(
                "other-app", "fixture-space", "hero", null, null))).Code);
        Assert.Equal("STATE_SPACE_WRONG_APPLICATION", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.GetApplicationContainmentAsync(
                "other-app", "fixture-space", "item-lantern"))).Code);
        Assert.Equal("ENTITY_UNKNOWN", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListApplicationContainmentsAsync(
                "fixture-app", "fixture-space", "missing", null, null))).Code);
        Assert.Equal("ENTITY_UNKNOWN", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.GetApplicationContainmentAsync(
                "fixture-app", "fixture-space", "missing"))).Code);
        var noEdges = new ControlStructureExplorer(
            applications, stateSpaces, types, entities, new EmptyPublicApplicationCatalogProvider(),
            capabilities);
        Assert.Equal("APPLICATION_CONTAINMENT_UNAVAILABLE",
            (await Assert.ThrowsAsync<ControlStructureException>(() =>
                noEdges.GetApplicationContainmentAsync(
                    "fixture-app", "fixture-space", "hero"))).Code);
        Assert.Equal(changesBeforeReads, await SqliteTotalChangesAsync(db));
    }

    [Fact]
    public void Structure_explorer_uses_only_the_explicit_public_catalog_provider()
    {
        using var fixture = new SqliteFixture();
        using var db = fixture.CreateContext();
        var applicationId = ApplicationIdentifier.Parse("fixture-app");
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(applicationId, "Fixture", "", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var entities = new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator());
        var emptyExplorer = new ControlStructureExplorer(
            applications, stateSpaces, types, entities,
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
            {
                [applicationId] = new EmptyCatalogNavigator()
            }));

        Assert.Equal("empty", emptyExplorer.GetCatalog("fixture-app").Status);

        const string content = "{\"kind\":\"contract\"}";
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var manifest = CatalogNavigationManifest.Create(
            applicationId,
            new string('A', 64),
            "catalog-v1",
            [new("contracts", "Contracts", "Public contracts")],
            [new("contracts", "", "Contracts", "Public contracts", CatalogDescriptionStatus.Authored)],
            [new(
                "contracts", "contract", "fixture-app.contract.hero", "Hero", "Hero contract",
                [], ["hero"], "", "active", 1, content, contentHash, "fixture", "catalog/hero.json")]);
        var navigator = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(new byte[32]));
        var explorer = new ControlStructureExplorer(
            applications, stateSpaces, types, entities,
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
            {
                [applicationId] = navigator
            }));

        var overview = explorer.GetCatalog("fixture-app");
        var browse = explorer.BrowseCatalog("fixture-app", "contracts", null, null, null);
        var search = explorer.SearchCatalog("fixture-app", "hero", null, null, [], [], null, null);
        var record = explorer.InspectCatalog("fixture-app", "contracts", "fixture-app.contract.hero");

        Assert.Equal("available", overview.Status);
        Assert.Equal("contracts", Assert.Single(overview.Collections).Id);
        Assert.Equal("fixture-app.contract.hero", Assert.Single(browse.Entries).Record!.QualifiedId);
        Assert.Equal("fixture-app.contract.hero", Assert.Single(search.Records).Record.QualifiedId);
        Assert.Equal(content, record.ContentJson);
        Assert.Equal("CURSOR_INVALID", Assert.Throws<ControlStructureException>(() => explorer.BrowseCatalog(
            "fixture-app", "contracts", null, new string('x', ControlStructureExplorer.MaximumCursorLength + 1), null)).Code);
    }

    [Fact]
    public async Task Structure_explorer_cursors_are_bounded_and_scope_bound()
    {
        var applications = new InMemoryApplicationRegistry();
        applications.Register(new(ApplicationIdentifier.Parse("alpha-app"), "Alpha", "", []));
        applications.Register(new(ApplicationIdentifier.Parse("bravo-app"), "Bravo", "", []));
        using var fixture = new SqliteFixture();
        using var db = fixture.CreateContext();
        var explorer = new ControlStructureExplorer(
            applications,
            new SqliteStateSpaceRegistry(db, applications),
            new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()),
            new SqliteEntityComponentStore(db, new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()), new BoundedJsonSchemaValidator()),
            new EmptyPublicApplicationCatalogProvider(),
            ApplicationCapabilities(applications));

        var authorization = CapabilityAuthorization();
        var first = await explorer.ListApplicationsThroughCapabilitiesAsync(authorization, null, "1");
        Assert.NotNull(first.NextCursor);
        Assert.Equal("bravo-app", Assert.Single((await explorer.ListApplicationsThroughCapabilitiesAsync(
            authorization, first.NextCursor, "1")).Items).Id);
        Assert.Equal("INVALID_LIMIT", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListApplicationsThroughCapabilitiesAsync(authorization, null, "101"))).Code);
        Assert.Equal("CURSOR_INVALID", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListApplicationsThroughCapabilitiesAsync(authorization, "not-a-cursor", "1"))).Code);
        Assert.Equal("CURSOR_STALE", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListApplicationsThroughCapabilitiesAsync(authorization, first.NextCursor, "2"))).Code);
    }

    [Fact]
    public async Task Committed_effect_history_groups_summaries_and_returns_exact_operation_context()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var timestamp = DateTime.UtcNow;
        db.Operations.Add(new Operation
        {
            Id = "operation-history",
            Timestamp = timestamp,
            Tool = "commit",
            Summary = "Created a record",
            Success = true,
            GuardEvidenceJson = "{\"accepted\":true}"
        });
        db.Events.Add(new EventRecord
        {
            Id = "event-history",
            TypeId = "world.entity.created",
            TypeVersion = 1,
            Scope = "world",
            PayloadJson = "{\"beforeJson\":null,\"afterJson\":{\"id\":\"hero\"}}",
            Timestamp = timestamp,
            CorrelationId = "operation-history",
            RootOperationId = "operation-history",
            Sequence = 0,
            Entities = [new EventEntity { EventId = "event-history", EntityId = "hero", Ordinal = 0 }]
        });
        await db.SaveChangesAsync();
        var history = new CommittedEffectHistory(new EventLedger(db), new OperationLog(db));

        var page = await history.ListAsync(null, null, null, null, "25");
        var detail = await history.GetAsync("event-history");

        var group = Assert.Single(page.Groups);
        Assert.Equal("operation-history", group.RootOperationId);
        Assert.Equal("event-history", Assert.Single(group.Events).Id);
        Assert.NotNull(detail);
        Assert.Contains("beforeJson", detail!.Event.PayloadJson, StringComparison.Ordinal);
        Assert.Equal("operation-history", detail.Operation!.Id);
        Assert.DoesNotContain("ProjectionJson", JsonSerializer.Serialize(detail));
    }

    [Fact]
    public async Task Committed_effect_history_rejects_invalid_bounds_and_missing_event()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var history = new CommittedEffectHistory(new EventLedger(db), new OperationLog(db));

        var limit = await Assert.ThrowsAsync<CommittedEffectHistoryException>(
            () => history.ListAsync(null, null, null, null, "101"));
        var cursor = await Assert.ThrowsAsync<CommittedEffectHistoryException>(
            () => history.ListAsync(null, null, null, "not a cursor", null));

        Assert.Equal("INVALID_LIMIT", limit.Code);
        Assert.Equal("INVALID_CURSOR", cursor.Code);
        Assert.Null(await history.GetAsync("missing-event"));
        Assert.Empty(await db.Events.ToListAsync());
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Denied_private_identity_never_invokes_the_web_handler()
    {
        var context = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        context.Request.Method = HttpMethods.Put;
        context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "intruder@example.com";
        var filter = new WebInterfaceSecurityFilter(OperatorGuard(new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = ["operator@example.com"]
        }));
        var invoked = false;

        await filter.InvokeAsync(new TestFilterContext(context), _ =>
        {
            invoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.False(invoked);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Theory]
    [InlineData(false, "roleplay.example.ts.net", "operator@example.com", "REMOTE_ACCESS_DENIED")]
    [InlineData(true, "other.example.ts.net", "operator@example.com", "REMOTE_ACCESS_DENIED")]
    [InlineData(true, "roleplay.example.ts.net", null, "REMOTE_IDENTITY_REQUIRED")]
    [InlineData(true, "roleplay.example.ts.net", "intruder@example.com", "REMOTE_ACCESS_DENIED")]
    public void Remote_access_fails_closed_for_disabled_host_missing_or_denied_identity(
        bool enabled,
        string host,
        string? login,
        string errorCode)
    {
        var context = RequestContext(host, IPAddress.IPv6Loopback);
        if (login is not null)
        {
            context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = login;
        }

        var decision = AccessPolicy(new WebRemoteAccessOptions
        {
            Enabled = enabled,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = ["operator@example.com"]
        }).Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Equal(errorCode, decision.ErrorCode);
    }

    [Fact]
    public void Remote_route_boundary_includes_only_the_web_surface()
    {
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/ui/home"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/components/system-workspace.js"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/components/application-conversation.js"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/components/application-workspace.js"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/pages/home"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/data/entity/hero"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/changes"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/session"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/control/status"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/applications/quest/observations"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/catalog/browse"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/catalog/records/quest.item.fixture.v1"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/mechanics/quest.mechanic.fixture"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/mechanics/quest.mechanic.fixture/prepare"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/mechanics/quest.mechanic.fixture/execute"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/applications/quest/state-spaces"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/entities"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/containments"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/entities/hero"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/entities/hero/containment"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/entities/hero/components"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/entities/hero/components/quest.stats"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/mcp"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/api/pages-other"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/api/control-other"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/components-other/system-workspace.js"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/api/applications/quest/conversations"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/api/applications/quest/observations/extra"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/catalog/browse/extra"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/catalog/records/quest.item.fixture.v1/extra"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/catalog/records/quest.item.fixture.v1/"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/catalog//records/quest.item.fixture.v1"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/mechanics/quest.mechanic.fixture/"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/mechanics/quest.mechanic.fixture/preview"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/mechanics/quest.mechanic.fixture/execute/extra"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/api/applications/quest/state-spaces/"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main//entities"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/entities/hero/components/quest.stats/extra"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/entities/hero/containment/extra"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/actions"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath(
            "/api/applications/quest/state-spaces/main/containments/extra"));
    }

    [Fact]
    public async Task Direct_html_reader_accepts_the_limit_and_rejects_oversize_or_invalid_utf8()
    {
        var reader = new WebHtmlReader();
        var maximum = Enumerable.Repeat((byte)'a', WebPageBundleLimits.MaximumHtmlBytes).ToArray();
        await using var valid = new MemoryStream(maximum);

        var html = await reader.ReadAsync(valid, valid.Length);

        Assert.Equal(WebPageBundleLimits.MaximumHtmlBytes, html.Length);
        await Assert.ThrowsAsync<WebHtmlUploadException>(() => reader.ReadAsync(
            Stream.Null,
            WebPageBundleLimits.MaximumHtmlBytes + 1L));
        await using var invalid = new MemoryStream([0xC3, 0x28]);
        var exception = await Assert.ThrowsAsync<WebHtmlUploadException>(
            () => reader.ReadAsync(invalid, invalid.Length));
        Assert.Equal("INVALID_HTML_ENCODING", exception.Code);
    }

    private static WebContentDbContext CreateWebContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(connection)
            .Options;
        return new WebContentDbContext(options);
    }

    private static async Task<long> SqliteTotalChangesAsync(DantesRoleplayDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT total_changes();";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static WebContentDbContext CreateWebContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new WebContentDbContext(options);
    }

    private static string SharedMemoryConnectionString() =>
        $"Data Source=web-change-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static WebAccessPolicy AccessPolicy(WebRemoteAccessOptions options) =>
        new(Options.Create(options));

    private static WebPrivateOperatorGuard OperatorGuard(WebRemoteAccessOptions options) =>
        new(AccessPolicy(options), new PrivateOperatorAuthorizationPolicy());

    private static SystemCapabilityCatalog ApplicationCapabilities(IApplicationRegistry applications) =>
        new(
            [new ApplicationsSystemCapabilityHandler(applications)],
            new BoundedJsonSchemaValidator(),
            new PrivateOperatorAuthorizationPolicy());

    private static AuthorizationAuditEvidence CapabilityAuthorization() =>
        new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            PrivateOperatorPrincipal.Create("test", "operator"),
            PrivateOperatorCapability.ControlRead,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "web-capability-test")).Evidence;

    private static WebControlRequestGuard ControlGuard(WebRemoteAccessOptions options) =>
        new(OperatorGuard(options));

    private sealed class SecretFixtureCapabilityHandler : ISystemReadCapabilityHandler
    {
        public SystemCapabilityRegistration Registration { get; } = new(
            "system.secret-fixture", 1, "web-interface", "Secret fixture.",
            SystemCapabilityMode.Read,
            "{\"type\":\"object\",\"additionalProperties\":false}",
            "{\"type\":\"object\",\"additionalProperties\":false}",
            ["procedure.system.use"], PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.Secret, false, false);

        public Task<SystemCapabilityHandlerResult> ReadAsync(
            JsonElement input, CancellationToken cancellationToken = default)
        {
            using var document = JsonDocument.Parse("{}");
            return Task.FromResult(SystemCapabilityHandlerResult.Success(document.RootElement.Clone()));
        }
    }

    private static HttpRequest ObservationRequest(string json)
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        request.ContentLength = request.Body.Length;
        request.ContentType = "application/json";
        return request;
    }

    private static string ValidObservationJson() =>
        """{"requestId":"observation-request.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","source":{"id":"phone.dante","instanceId":"android-primary","occurrenceId":"arrival.1"},"structure":{"id":"device.geofence.transition","version":1},"observedAt":"2026-08-25T20:00:00Z","data":{"transition":"entered"}}""";

    private static string ObservationJsonWithData(string data) =>
        ValidObservationJson().Replace("{\"transition\":\"entered\"}", data, StringComparison.Ordinal);

    private static void AssertPanel(ControlCenterPanelStatus panel, string id)
    {
        Assert.Equal(id, panel.Id);
        Assert.Equal("unavailable", panel.State);
        Assert.False(string.IsNullOrWhiteSpace(panel.Message));
        Assert.True(panel.Message.Length <= 128);
    }

    private static DefaultHttpContext RequestContext(string host, IPAddress address)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = address;
        return context;
    }

    private sealed class TestFilterContext(HttpContext context) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = context;
        public override IList<object?> Arguments { get; } = [];
        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }

    private sealed class AcceptedObservationIngestion : IObservationIngestionService
    {
        public Task<TriggerSchedulingWriteResult<StoredObservation>> SubmitAsync(
            TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId,
            ObservationSubmission submission,
            CancellationToken cancellationToken = default)
        {
            Assert.True(principal.Verified);
            var stored = new StoredObservation(
                "observation.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                applicationId,
                submission.RequestId,
                submission.Source.Id,
                1,
                submission.Source.InstanceId,
                submission.Source.OccurrenceId,
                submission.Structure.Id,
                submission.Structure.Version,
                new string('A', 64),
                submission.ObservedAt,
                submission.ObservedAt,
                submission.Data.Json,
                submission.Data.Hash,
                new string('A', 64),
                principal.PrincipalId);
            return Task.FromResult(TriggerSchedulingWriteResult<StoredObservation>.Appended(stored));
        }
    }

    private sealed class RecordingPhoneAuthenticator(bool allowed) : IPhoneCompanionAuthenticator
    {
        public int Calls { get; private set; }
        public ApplicationIdentifier? ApplicationId { get; private set; }

        public Task<PhoneCompanionAuthenticationResult> AuthenticateAsync(
            ApplicationIdentifier applicationId,
            string credential,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ApplicationId = applicationId;
            var principal = allowed
                ? TrustedPrincipalContext.VerifiedPrincipal(
                    PhoneCompanionIdentity.PrincipalId(applicationId,
                        "phone-device.0123456789abcdef0123456789abcdef"),
                    PhoneCompanionIdentity.AuthenticationMethod)
                : null;
            return Task.FromResult(new PhoneCompanionAuthenticationResult(allowed, principal));
        }
    }

    private sealed class EmptyCatalogNavigator : ICatalogNavigator
    {
        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) => [];
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => throw new NotSupportedException();
        public CatalogSearchResult Search(CatalogSearchRequest request) => throw new NotSupportedException();
        public CatalogRecordView Inspect(CatalogRecordRequest request) => throw new NotSupportedException();
    }

    private sealed class StaticHostSettingProvider(HostSettingCatalog catalog)
        : IHostSettingDefinitionProvider
    {
        public HostSettingCatalog GetCatalog() => catalog;
        public JsonElement NormalizeOverride(string key, JsonElement value) =>
            throw new KeyNotFoundException(key);
        public void ApplyStartupOverrides(IReadOnlyDictionary<string, JsonElement?> overrides) =>
            throw new InvalidOperationException();
    }

    private static MemoryStream CreateZip(params (string Path, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Path, CompressionLevel.Fastest);
                using var target = zipEntry.Open();
                target.Write(entry.Content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
