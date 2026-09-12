using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class WebPageStandingGrantResourceTargetOwnerTests
{
    [Fact]
    public async Task Exact_selection_uses_the_requested_retained_revision_while_current_uses_latest()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.SaveBundleAndActivateAsync("page", new("<p>one</p>", [new("assets/a.bin", [1])]));
        var first = (await fixture.Content.GetRevisionAsync("page", 1))!;
        var second = await fixture.Content.AppendBundleDraftAsync("page", 1, new("<p>two</p>", [new("assets/a.bin", [2])]));
        await fixture.MapAsync("page", "web.pages.example", fixture.App.Value);
        var owner = fixture.Owner();

        var exact = await owner.ResolveAsync(fixture.Host(), new("web.pages.example", CatalogNamespaceKinds.WebPage,
            1, first.CompositionHash ?? first.Summary.ContentHash));
        var current = await owner.ResolveCurrentAsync(fixture.Host(), "web.pages.example");

        Assert.True(exact.Status == StandingGrantTargetResolutionStatus.Available, exact.Code);
        Assert.Equal(1, exact.Target!.Revision);
        Assert.Equal(first.Summary.ContentHash, exact.Target.ContentFingerprint);
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, current.Status);
        Assert.Equal(2, current.Target!.Revision);
        Assert.Equal(second.Summary.ContentHash, current.Target.ContentFingerprint);
    }

    [Fact]
    public async Task Unmapped_owner_mismatch_and_unreviewed_namespace_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.SaveBundleAndActivateAsync("page", new("<p>one</p>", []));
        var owner = fixture.Owner();
        Assert.Equal(StandingGrantTargetResolutionStatus.Unavailable,
            (await owner.ResolveCurrentAsync(fixture.Host(), "web.pages.missing")).Status);

        await fixture.MapAsync("page", "web.pages.example", "other");
        Assert.Equal("STANDING_GRANT_WEB_PAGE_OWNER_MISMATCH",
            (await owner.ResolveCurrentAsync(fixture.Host(), "web.pages.example")).Code);
        await using var reviewed = await Fixture.CreateAsync();
        await reviewed.Content.SaveBundleAndActivateAsync("page", new("<p>one</p>", []));
        await reviewed.MapAsync("page", "web.pages.example", reviewed.App.Value);
        reviewed.Namespaces.SetReview("web.pages", CatalogNamespaceReviewStatuses.NeedsReview, "fixture");
        Assert.Equal("STANDING_GRANT_NAMESPACE_UNREVIEWED",
            (await reviewed.Owner().ResolveCurrentAsync(reviewed.Host(), "web.pages.example")).Code);
    }

    [Fact]
    public async Task Corrupt_retained_asset_bytes_are_denied()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.SaveBundleAndActivateAsync("page", new("<p>one</p>", [new("assets/a.bin", [1])]));
        await fixture.MapAsync("page", "web.pages.example", fixture.App.Value);
        await fixture.Web.Database.ExecuteSqlRawAsync("UPDATE web_page_asset_content SET Content = X'02'");
        var result = await fixture.Owner().ResolveCurrentAsync(fixture.Host(), "web.pages.example");
        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal("STANDING_GRANT_WEB_PAGE_CONTENT_CORRUPT", result.Code);
    }

    [Fact]
    public async Task State_context_does_not_change_resource_ownership_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.SaveBundleAndActivateAsync("page", new("<p>one</p>", []));
        await fixture.MapAsync("page", "web.pages.example", fixture.App.Value);
        var host = new InteractionInvocationHost(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            fixture.Applications.Get(fixture.App)!, "state", "grant@1", "command", "state@1", InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));
        var applicationOnly = await fixture.Owner().ResolveCurrentAsync(fixture.Host(), "web.pages.example");
        var withState = await fixture.Owner().ResolveCurrentAsync(host, "web.pages.example");
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, withState.Status);
        Assert.Equal(applicationOnly.Target, withState.Target);
    }

    [Fact]
    public async Task Stale_generation_disabled_namespace_and_changed_namespace_metadata_are_not_reusable_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.SaveBundleAndActivateAsync("page", new("<p>one</p>", []));
        await fixture.MapAsync("page", "web.pages.example", fixture.App.Value);
        var owner = fixture.Owner();
        var initial = await owner.ResolveCurrentAsync(fixture.Host(), "web.pages.example");
        fixture.Namespaces.SetReview("web.pages", CatalogNamespaceReviewStatuses.Reviewed, "new reviewed metadata");
        var changed = await owner.ResolveCurrentAsync(fixture.Host(), "web.pages.example");
        Assert.NotEqual(initial.Target!.OwnershipEvidenceReference, changed.Target!.OwnershipEvidenceReference);
        fixture.Namespaces.SetEnabled("web.pages", false);
        Assert.Equal("STANDING_GRANT_NAMESPACE_UNREVIEWED",
            (await owner.ResolveCurrentAsync(fixture.Host(), "web.pages.example")).Code);
        fixture.Namespaces.SetEnabled("web.pages", true);
        var oldHost = fixture.Host();
        var current = fixture.Applications.Get(fixture.App)!;
        fixture.Applications.ReviseBaseApplications(fixture.App, [ApplicationIdentifier.Parse("other")], current.Revision, current.Fingerprint);
        Assert.Equal("STANDING_GRANT_APPLICATION_STALE",
            (await owner.ResolveCurrentAsync(oldHost, "web.pages.example")).Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required DantesRoleplayDbContext Data { get; init; }
        public required WebContentDbContext Web { get; init; }
        public required SqliteApplicationRegistry Applications { get; init; }
        public required SqliteCatalogNamespaceRegistry Namespaces { get; init; }
        public required WebPageStore Content { get; init; }
        public ApplicationIdentifier App { get; } = ApplicationIdentifier.Parse("example");

        public WebPageStandingGrantResourceTargetOwner Owner() => new(Web, Content, Applications, Namespaces);
        public InteractionInvocationHost Host() => InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            Applications.Get(App)!, "grant@1", "command", InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

        public async Task MapAsync(string page, string target, string app)
        {
            Web.Add(new WebPageResourceIdentity { ContentPageId = page, QualifiedTargetId = target,
                OwnerApplicationId = app, SourceOperationId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CreatedAtUtc = DateTime.UtcNow });
            await Web.SaveChangesAsync();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Worktree root unavailable.");
            var directory = Path.Combine(root.FullName, ".tmp", "web-page-standing-grant-owner", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string Connection(string name) => new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, name), Pooling = false }.ToString();
            var data = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(Connection("kernel.db")).Options);
            var web = new WebContentDbContext(new DbContextOptionsBuilder<WebContentDbContext>().UseSqlite(Connection("web.db")).Options);
            await data.Database.MigrateAsync(); await web.Database.MigrateAsync();
            var apps = new SqliteApplicationRegistry(data);
            apps.Register(new(ApplicationIdentifier.Parse("example"), "Example", "Fixture.", []));
            apps.Register(new(ApplicationIdentifier.Parse("other"), "Other", "Fixture.", []));
            var namespaces = new SqliteCatalogNamespaceRegistry(data);
            namespaces.Register(new CatalogNamespaceRegistration("web", "domain", "Fixture.", [CatalogNamespaceKinds.WebPage], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed));
            namespaces.Register(new CatalogNamespaceRegistration("web.pages", "domain", "Fixture.", [CatalogNamespaceKinds.WebPage], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed));
            return new() { Data = data, Web = web, Applications = apps, Namespaces = namespaces, Content = new WebPageStore(web) };
        }

        public async ValueTask DisposeAsync() { await Web.DisposeAsync(); await Data.DisposeAsync(); }
    }
}
