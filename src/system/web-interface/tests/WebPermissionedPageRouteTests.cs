using System.Net;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Fixture = DantesRoleplay.Tests.WebPagePublicationSelectionTests.Fixture;

namespace DantesRoleplay.Tests;

[CollectionDefinition(nameof(WebPermissionedPageRouteCollection), DisableParallelization = true)]
public sealed class WebPermissionedPageRouteCollection;

[Collection(nameof(WebPermissionedPageRouteCollection))]
public sealed class WebPermissionedPageRouteTests
{
    private const string Target = "example.pages.home";

    [Fact]
    public async Task Pinned_http_routes_require_a_current_grant_and_serve_only_the_exact_retained_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        await PreparePinnedPageAsync(fixture);
        await using var application = BuildApplication(fixture);

        var denied = await GetAsync(application, "/ui/{id}", "/ui/example", new() { ["id"] = "example" });
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.Empty(denied.Body);

        var principal = WebTrustedPrincipalContextFactory.Create(new(true, WebAccessMode.Local)).PrincipalId;
        await SeedGrantAsync(fixture, "a-wrong", principal, "example.pages.other");
        await SeedGrantAsync(fixture, "z-allowed", principal, Target);

        var page = await GetAsync(application, "/ui/{id}", "/ui/example", new() { ["id"] = "example" });
        Assert.Equal(StatusCodes.Status200OK, page.StatusCode);
        Assert.Equal("private, no-store", page.CacheControl);
        Assert.Contains("/ui/example/content/example-content/revisions/2/assets/icon.bin",
            Encoding.UTF8.GetString(page.Body), StringComparison.Ordinal);

        var versioned = await GetAsync(application,
            "/ui/{id}/content/{pageId}/revisions/{revision:int}/{**path}",
            "/ui/example/content/example-content/revisions/2/assets/icon.bin",
            new() { ["id"] = "example", ["pageId"] = Fixture.ContentId, ["revision"] = "2",
                ["path"] = "assets/icon.bin" });
        Assert.Equal(StatusCodes.Status200OK, versioned.StatusCode);
        Assert.Equal("private, no-store", versioned.CacheControl);
        Assert.Equal(new byte[] { 2 }, versioned.Body);

        var compatibilityRoute = await GetAsync(application, "/ui/{id}/assets/{**path}",
            "/ui/example/assets/icon.bin", new() { ["id"] = "example", ["path"] = "icon.bin" });
        Assert.Equal(StatusCodes.Status200OK, compatibilityRoute.StatusCode);
        Assert.Equal("private, no-store", compatibilityRoute.CacheControl);
        Assert.Equal(new byte[] { 2 }, compatibilityRoute.Body);
        Assert.Equal(new byte[] { 1 },
            (await fixture.Content.GetActiveAssetAsync(Fixture.ContentId, "assets/icon.bin"))!.Content);

        foreach (var mismatch in new[]
        {
            await GetAsync(application, "/ui/{id}/content/{pageId}/revisions/{revision:int}/{**path}",
                "/ui/example/content/other/revisions/2/assets/icon.bin",
                new() { ["id"] = "example", ["pageId"] = "other", ["revision"] = "2",
                    ["path"] = "assets/icon.bin" }),
            await GetAsync(application, "/ui/{id}/content/{pageId}/revisions/{revision:int}/{**path}",
                "/ui/example/content/example-content/revisions/1/assets/icon.bin",
                new() { ["id"] = "example", ["pageId"] = Fixture.ContentId, ["revision"] = "1",
                    ["path"] = "assets/icon.bin" }),
            await GetAsync(application, "/ui/{id}/content/{pageId}/revisions/{revision:int}/{**path}",
                "/ui/example/content/example-content/revisions/2/assets/missing.bin",
                new() { ["id"] = "example", ["pageId"] = Fixture.ContentId, ["revision"] = "2",
                    ["path"] = "assets/missing.bin" })
        })
        {
            Assert.NotEqual(StatusCodes.Status200OK, mismatch.StatusCode);
            Assert.Empty(mismatch.Body);
        }

        await RevokeGrantAsync(fixture, "z-allowed", principal, Target);
        var revoked = await GetAsync(application, "/ui/{id}", "/ui/example", new() { ["id"] = "example" });
        Assert.Equal(StatusCodes.Status403Forbidden, revoked.StatusCode);
        Assert.Empty(revoked.Body);
    }

    [Fact]
    public async Task Legacy_unpinned_and_system_home_routes_keep_their_existing_behavior_without_grants()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.SaveAndActivateAsync("home", "<h1>System home</h1>");
        await using var application = BuildApplication(fixture);

        var legacy = await GetAsync(application, "/ui/{id}", "/ui/example", new() { ["id"] = "example" });
        var home = await GetAsync(application, "/", "/", []);

        Assert.Equal(StatusCodes.Status200OK, legacy.StatusCode);
        Assert.Equal("<p>Legacy active</p>", Encoding.UTF8.GetString(legacy.Body));
        Assert.Equal(StatusCodes.Status200OK, home.StatusCode);
        Assert.Equal("<h1>System home</h1>", Encoding.UTF8.GetString(home.Body));
    }

    [Fact]
    public async Task Ambiguous_slug_is_denied_before_any_page_payload_is_selected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var schemas = new BoundedJsonSchemaValidator();
        var type = new SqliteComponentTypeRegistry(fixture.Data, schemas).GetLatest(WebPageComponentTypes.Page)!;
        await using (var transaction = await fixture.Transactions.BeginAsync())
        {
            await fixture.Entities.CreateEntityAsync(Fixture.PublicationSpace, "web-page:collision", "Collision");
            await fixture.Entities.AddComponentAsync(new(Fixture.PublicationSpace, "web-page:collision",
                new(type.QualifiedId, type.Version, type.SchemaHash), JsonSerializer.Serialize(new
                {
                    title = "Collision", navigationLabel = "Collision", slug = "example", order = 1,
                    visibility = "public", activeContentReference = new { pageId = Fixture.ContentId }
                }), 0));
            await transaction.CommitAsync();
        }
        await using var application = BuildApplication(fixture);

        var response = await GetAsync(application, "/ui/{id}", "/ui/example", new() { ["id"] = "example" });

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.DoesNotContain("Legacy active", Encoding.UTF8.GetString(response.Body), StringComparison.Ordinal);
    }

    private static async Task PreparePinnedPageAsync(Fixture fixture)
    {
        var namespaces = new SqliteCatalogNamespaceRegistry(fixture.Data);
        foreach (var id in new[] { "example", "example.pages" })
            namespaces.Register(new CatalogNamespaceRegistration(id, "reviewed-domain", "Generic retained pages",
                [CatalogNamespaceKinds.WebPage], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
                ReviewNote: "Reviewed fixture."));
        fixture.Web.Add(new WebPageResourceIdentity
        {
            ContentPageId = Fixture.ContentId,
            QualifiedTargetId = Target,
            OwnerApplicationId = fixture.ApplicationId.Value,
            SourceOperationId = new string('a', 32),
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Web.SaveChangesAsync();
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2));
    }

    private static async Task SeedGrantAsync(Fixture fixture, string grantId, string principal, string target,
        int revision = 1, bool revoked = false)
    {
        var grant = new StandingGrantRevision($"{grantId}@{revision}", grantId, revision, new string('0', 64),
            principal, fixture.ApplicationId, StandingGrantScope.Application, null,
            [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ExactIds, [target], []), [], 1,
            DateTime.UtcNow.AddMinutes(10), revoked, $"{grantId}-operation-{revision}");
        fixture.Data.Add(new Operation
        {
            Id = grant.IssuedByOperationId,
            Timestamp = DateTime.UtcNow,
            Tool = "permissioned-http-fixture"
        });
        fixture.Data.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId,
            Revision = grant.Revision,
            GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference,
            ApplicationId = grant.ApplicationId.Value,
            Scope = "application",
            StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations,
            ExpiresAtUtc = grant.ExpiresAtUtc,
            Revoked = grant.Revoked,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        fixture.Data.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = revision });
        await fixture.Data.SaveChangesAsync();
    }

    private static async Task RevokeGrantAsync(Fixture fixture, string grantId, string principal, string target)
    {
        await SeedGrantRevisionAsync(fixture, grantId, principal, target, 2, revoked: true);
        var current = await fixture.Data.Set<StandingGrantCurrentRecord>().SingleAsync(value => value.GrantId == grantId);
        current.Revision = 2;
        await fixture.Data.SaveChangesAsync();
    }

    private static async Task SeedGrantRevisionAsync(Fixture fixture, string grantId, string principal, string target,
        int revision, bool revoked)
    {
        var grant = new StandingGrantRevision($"{grantId}@{revision}", grantId, revision, new string('0', 64),
            principal, fixture.ApplicationId, StandingGrantScope.Application, null,
            [StandingGrantCapability.Read], new(StandingGrantDefinitionMode.ExactIds, [target], []), [], 1,
            DateTime.UtcNow.AddMinutes(10), revoked, $"{grantId}-operation-{revision}");
        fixture.Data.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow,
            Tool = "permissioned-http-fixture" });
        fixture.Data.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
            Scope = "application", StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc,
            Revoked = grant.Revoked, IssuedByOperationId = grant.IssuedByOperationId
        });
        await fixture.Data.SaveChangesAsync();
    }

    private static WebApplication BuildApplication(Fixture fixture)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayDataAccess(fixture.Data.Database.GetConnectionString()!);
        builder.Services.AddDantesRoleplayWeb(fixture.Web.Database.GetConnectionString()!,
            new ConfigurationBuilder().Build());
        var application = builder.Build();
        application.MapDantesRoleplayWeb();
        return application;
    }

    private static async Task<HttpResponseSnapshot> GetAsync(WebApplication application, string pattern,
        string path, RouteValueDictionary values)
    {
        var route = Assert.Single(((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(), endpoint => endpoint.RoutePattern.RawText == pattern);
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = WebAccessPolicy.CreatePrincipal(new(true, WebAccessMode.Local))
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.Host = new HostString("localhost:6217");
        context.Request.RouteValues = values;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        await route.RequestDelegate!(context);
        return new(context.Response.StatusCode, context.Response.ContentType,
            context.Response.Headers.CacheControl.ToString(), ((MemoryStream)context.Response.Body).ToArray());
    }

    private sealed record HttpResponseSnapshot(int StatusCode, string? ContentType, string CacheControl, byte[] Body);
}
