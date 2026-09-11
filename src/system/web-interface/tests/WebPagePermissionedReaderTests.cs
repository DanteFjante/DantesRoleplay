using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Fixture = DantesRoleplay.Tests.WebPagePublicationSelectionTests.Fixture;

namespace DantesRoleplay.Tests;

public sealed class WebPagePermissionedReaderTests
{
    private const string Target = "example.pages.home";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Current_application_read_grant_serves_exact_composition_and_assets_with_one_operation_each()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = await PrepareAsync(fixture);
        var host = Host(fixture);
        var result = await reader.ReadPageAsync(host, Fixture.EntityId);
        Assert.Null(result.Failure);
        Assert.Contains("/ui/example/content/example-content/revisions/2/assets/icon.bin", result.Html);
        Assert.Equal(2, result.Content!.Revision);
        Assert.Equal(0, host.Budget.RemainingOperations);
        var exhausted = await reader.ReadPageAsync(host, Fixture.EntityId);
        Assert.Equal("INVOCATION_BUDGET_EXHAUSTED", exhausted.Failure!.Code);
        Assert.Null(exhausted.Html);

        var asset = await reader.ReadAssetAsync(Host(fixture), Fixture.EntityId, Fixture.ContentId, 2, "assets/icon.bin");
        Assert.Null(asset.Failure);
        Assert.Equal(new byte[] { 2 }, asset.Asset!.Content);
        var mutable = await fixture.Content.GetActiveAssetAsync(Fixture.ContentId, "assets/icon.bin");
        Assert.Equal(new byte[] { 1 }, mutable!.Content);
        var wrongRevision = await reader.ReadAssetAsync(Host(fixture), Fixture.EntityId, Fixture.ContentId, 1, "assets/icon.bin");
        Assert.NotNull(wrongRevision.Failure);
        Assert.Null(wrongRevision.Asset);
        Assert.Null(wrongRevision.Content);
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("scope")]
    [InlineData("target")]
    [InlineData("revoked")]
    [InlineData("namespace")]
    public async Task Current_authority_failures_return_no_content_or_assets(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = await PrepareAsync(fixture, failure);
        var host = Host(fixture, failure == "principal" ? "principal.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" : Principal);
        var result = await reader.ReadPageAsync(host, Fixture.EntityId);
        Assert.NotNull(result.Failure);
        Assert.Null(result.Html);
        Assert.Null(result.Content);
        Assert.Null(result.Asset);
        var asset = await reader.ReadAssetAsync(Host(fixture, host.Principal.PrincipalId), Fixture.EntityId,
            Fixture.ContentId, 2, "assets/icon.bin");
        Assert.NotNull(asset.Failure);
        Assert.Null(asset.Asset);
    }

    [Fact]
    public async Task Revocation_after_a_successful_read_is_reloaded_in_the_same_service_scope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = await PrepareAsync(fixture);
        Assert.Null((await reader.ReadPageAsync(Host(fixture), Fixture.EntityId)).Failure);
        await SeedGrantAsync(fixture, "revoked", 2);
        var result = await reader.ReadPageAsync(Host(fixture), Fixture.EntityId);
        Assert.NotNull(result.Failure);
        Assert.Null(result.Html);
    }

    [Fact]
    public async Task Declared_bindings_stay_unavailable_and_legacy_HTML_remains_readable_only_with_a_pin()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = await PrepareAsync(fixture);
        const string composition = """{"formatVersion":1,"generation":"bound","queries":[{"name":"records"}],"components":[],"root":{"kind":"text","text":"hidden"}}""";
        await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, 2,
            new WebPageBundle("", []) { ContentFormat = WebPageContentFormat.Composition, CompositionJson = composition });
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 3));
        var unavailable = await reader.ReadPageAsync(Host(fixture), Fixture.EntityId);
        Assert.Equal(InteractionInvocationResultTag.Unavailable, unavailable.Failure!.Tag);
        Assert.Equal("COMPOSITION_BINDINGS_UNAVAILABLE", unavailable.Failure.Code);
        Assert.Null(unavailable.Html);

        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 1));
        var html = await reader.ReadPageAsync(Host(fixture), Fixture.EntityId);
        Assert.Null(html.Failure);
        Assert.Equal("<p>Legacy active</p>", html.Html);
    }

    [Fact]
    public async Task State_scoped_host_is_rejected_before_consuming_the_read_budget()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = await PrepareAsync(fixture);
        var host = new InteractionInvocationHost(TrustedPrincipalContext.VerifiedPrincipal(Principal, "test"),
            fixture.Applications.Get(fixture.ApplicationId)!, Fixture.PublicationSpace, "web-read@1", Guid.NewGuid().ToString("N"),
            "publication@1", InteractionExecutionProfile.ReadOnly, new(1, DateTime.UtcNow.AddMinutes(1)));
        var result = await reader.ReadPageAsync(host, Fixture.EntityId);
        Assert.Equal("WEB_APPLICATION_SCOPE_REQUIRED", result.Failure!.Code);
        Assert.Null(result.Html);
        Assert.Equal(1, host.Budget.RemainingOperations);
    }

    private static InteractionInvocationHost Host(Fixture fixture, string principal = Principal) =>
        InteractionInvocationHost.ForApplication(TrustedPrincipalContext.VerifiedPrincipal(principal, "test"),
            fixture.Applications.Get(fixture.ApplicationId)!, "web-read@1", Guid.NewGuid().ToString("N"),
            InteractionExecutionProfile.ReadOnly, new(1, DateTime.UtcNow.AddMinutes(1)));

    private static async Task<WebPagePermissionedReader> PrepareAsync(Fixture fixture, string? failure = null)
    {
        var namespaces = new SqliteCatalogNamespaceRegistry(fixture.Data);
        foreach (var id in new[] { "example", "example.pages" })
            namespaces.Register(new CatalogNamespaceRegistration(id, "reviewed-domain", "Generic retained pages", [CatalogNamespaceKinds.WebPage],
                ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        fixture.Web.Add(new WebPageResourceIdentity
        {
            ContentPageId = Fixture.ContentId, QualifiedTargetId = Target, OwnerApplicationId = fixture.ApplicationId.Value,
            SourceOperationId = new string('a', 32), CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Web.SaveChangesAsync();
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2));
        await SeedGrantAsync(fixture, failure);
        if (failure == "namespace") namespaces.SetEnabled("example.pages", false);

        // All invoked ownership, grant, content and publication services are production owners.
        // The catalog resolver has no source roots/activation: no missing dependency is made successful.
        var sources = new SqliteSourceRegistry(fixture.Data);
        var extensions = new SqliteApplicationExtensionRegistry(fixture.Data, sources);
        var roots = new EmptyAllowedSourceRootResolver();
        var previews = new ApplicationPreviewService(fixture.Applications, sources,
            new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()), new SourceOverlayResolver());
        var activation = new ApplicationActivationService(fixture.Data, previews, extensions, sources, roots,
            new ProjectionImpactService(fixture.Applications, new SqliteProjectionImpactSnapshotReader(fixture.Data)), new OperationLog(fixture.Data));
        var catalog = new ActivatedApplicationCatalogMaterializer(fixture.Applications, activation, sources, roots, extensions);
        var definitions = new SqliteStandingGrantTargetResolver(fixture.Data, fixture.Applications,
            activation, activation, sources, extensions, namespaces, catalog);
        var owner = new WebPageStandingGrantResourceTargetOwner(fixture.Web, fixture.Content, fixture.Applications, namespaces);
        var resolver = new ResourceStandingGrantTargetResolver(definitions, [owner]);
        return new(fixture.Publication, fixture.Web, resolver, new SqliteStandingGrantPolicy(fixture.Data, resolver));
    }

    private static async Task SeedGrantAsync(Fixture fixture, string? failure = null, int revision = 1)
    {
        var grant = new StandingGrantRevision($"web-read@{revision}", "web-read", revision, new string('0', 64), Principal,
            fixture.ApplicationId, failure == "scope" ? StandingGrantScope.StateSpace : StandingGrantScope.Application,
            failure == "scope" ? Fixture.PublicationSpace : null, [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ExactIds, [failure == "target" ? "example.pages.other" : Target], []), [],
            1, DateTime.UtcNow.AddMinutes(10), failure == "revoked", $"grant-operation-{revision}");
        fixture.Data.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test-fixture" });
        fixture.Data.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
            Scope = failure == "scope" ? "stateSpace" : "application", StateSpaceId = grant.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc,
            Revoked = grant.Revoked, IssuedByOperationId = grant.IssuedByOperationId
        });
        if (revision == 1) fixture.Data.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = revision });
        else (await fixture.Data.Set<StandingGrantCurrentRecord>().SingleAsync()).Revision = revision;
        await fixture.Data.SaveChangesAsync();
    }
}
