using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.Web.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Local_installation_operator_adopts_an_unmapped_source_with_a_durable_owner_receipt()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedUnmappedSourceAsync(db, "source.adopt");
        using var administration = OwnershipAdministration(db, setup, RemoteConfiguration());
        var request = OwnershipRequest("source.adopt", "demo.info.adopt", 0, null);

        var result = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-adopt",
            PrivateOperatorPrincipal.Create("local-loopback", "local-operator")), request);

        Assert.Equal(InteractionInvocationResultTag.Committed, result.Tag);
        Assert.NotNull(result.Receipt);
        var owner = await InformationSourceOwnership.ReadAsync(db, "source.adopt", default);
        Assert.NotNull(owner);
        Assert.Equal(Application.Value, owner!.Owner.ApplicationId);
        Assert.Equal("demo.info.adopt", owner.Owner.QualifiedTargetId);
        Assert.Equal(1, owner.Owner.Revision);
        Assert.Single(await db.Set<Operation>().Where(value => value.Tool == "information-source-owner").ToArrayAsync());
    }

    [Fact]
    public async Task Ownership_move_requires_the_exact_current_owner_and_keeps_the_old_target_identity_assigned()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedUnmappedSourceAsync(db, "source.move");
        await SeedUnmappedSourceAsync(db, "source.other");
        using var administration = OwnershipAdministration(db, setup, RemoteConfiguration());
        var principal = PrivateOperatorPrincipal.Create("local-loopback-mcp", "local-operator");
        var first = OwnershipRequest("source.move", "demo.info.first", 0, null);

        Assert.Equal(InteractionInvocationResultTag.Committed, (await administration.Service.BindAsync(
            OwnershipHost(setup, "ownership-first", principal), first)).Tag);
        var owner = (await InformationSourceOwnership.ReadAsync(db, "source.move", default))!.Owner;
        var moved = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-move", principal),
            OwnershipRequest("source.move", "demo.info.second", owner.Revision, owner.ContentFingerprint));
        var stale = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-stale", principal),
            OwnershipRequest("source.move", "demo.info.third", 1, owner.ContentFingerprint));
        var reassigned = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-reassign", principal),
            OwnershipRequest("source.other", "demo.info.first", 0, null));

        Assert.Equal(InteractionInvocationResultTag.Committed, moved.Tag);
        Assert.Equal(InteractionInvocationResultTag.Failed, stale.Tag);
        Assert.Equal("INFORMATION_OWNER_REVISION_CONFLICT", stale.Code);
        Assert.Equal(InteractionInvocationResultTag.Failed, reassigned.Tag);
        Assert.Equal("INFORMATION_TARGET_ALREADY_ASSIGNED", reassigned.Code);
        var current = (await InformationSourceOwnership.ReadAsync(db, "source.move", default))!.Owner;
        Assert.Equal(2, current.Revision);
        Assert.Equal("demo.info.second", current.QualifiedTargetId);
        Assert.Equal(owner.ContentFingerprint, current.PreviousFingerprint);
        Assert.Null(await InformationSourceOwnership.ReadAsync(db, "source.other", default));
        var identity = await db.Set<InformationSourceTargetIdentityRecord>().SingleAsync(value => value.QualifiedTargetId == "demo.info.first");
        Assert.Equal("source.move", identity.SourceId);
    }

    [Fact]
    public async Task Ownership_replay_returns_its_original_receipt_after_a_later_move()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedUnmappedSourceAsync(db, "source.replay");
        using var administration = OwnershipAdministration(db, setup, RemoteConfiguration());
        var principal = PrivateOperatorPrincipal.Create("local-loopback", "local-operator");
        var firstRequest = OwnershipRequest("source.replay", "demo.info.before", 0, null);
        var first = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-replay", principal), firstRequest);
        var owner = (await InformationSourceOwnership.ReadAsync(db, "source.replay", default))!.Owner;
        _ = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-later-move", principal),
            OwnershipRequest("source.replay", "demo.info.after", owner.Revision, owner.ContentFingerprint));

        var replay = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-replay", principal), firstRequest);

        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal(2, (await InformationSourceOwnership.ReadAsync(db, "source.replay", default))!.Owner.Revision);
        Assert.Equal(2, await db.Set<InformationSourceOwnerRevisionRecord>().CountAsync(value => value.SourceId == "source.replay"));
    }

    [Fact]
    public async Task Removed_remote_operator_membership_denies_a_receipt_replay_without_writing_a_new_owner_revision()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedUnmappedSourceAsync(db, "source.remote");
        using var administration = OwnershipAdministration(db, setup,
            RemoteConfiguration(allowedLogin: "operator@example.com"));
        var principal = PrivateOperatorPrincipal.Create("tailscale-serve", "operator@example.com");
        var request = OwnershipRequest("source.remote", "demo.info.remote", 0, null);
        var first = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-remote", principal), request);

        administration.Configuration[WebRemoteAccessOptions.SectionName + ":Enabled"] = "false";
        administration.Configuration.Reload();
        var replay = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-remote", principal), request);

        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        Assert.Equal(InteractionInvocationResultTag.Failed, replay.Tag);
        Assert.Equal("INSTALLATION_OPERATOR_DENIED", replay.Code);
        Assert.Single(await db.Set<InformationSourceOwnerRevisionRecord>().Where(value => value.SourceId == "source.remote").ToArrayAsync());
        Assert.Single(await db.Set<Operation>().Where(value => value.Tool == "information-source-owner").ToArrayAsync());
    }

    [Fact]
    public async Task Verified_invited_identity_cannot_adopt_a_source_or_create_an_audit_record()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedUnmappedSourceAsync(db, "source.invited");
        using var administration = OwnershipAdministration(db, setup,
            RemoteConfiguration(invitedLogin: "guest@example.com"));

        var result = await administration.Service.BindAsync(OwnershipHost(setup, "ownership-invited",
            PrivateOperatorPrincipal.Create("tailscale-invited-web", "guest@example.com")),
            OwnershipRequest("source.invited", "demo.info.invited", 0, null));

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INSTALLATION_OPERATOR_DENIED", result.Code);
        Assert.Null(await InformationSourceOwnership.ReadAsync(db, "source.invited", default));
        Assert.Empty(await db.Set<Operation>().Where(value => value.Tool == "information-source-owner").ToArrayAsync());
    }

    private static async Task SeedUnmappedSourceAsync(DantesRoleplayDbContext db, string sourceId) =>
        _ = await new InformationStore(db).WriteSourceAsync(new(sourceId, "demo.info", "Fixture source"));

    private static InformationSourceOwnershipWriteRequest OwnershipRequest(string sourceId, string targetId,
        int expectedRevision, string? expectedFingerprint) => new(sourceId, Application, targetId, expectedRevision, expectedFingerprint);

    private static InteractionInvocationHost OwnershipHost(SetupState setup, string command,
        TrustedPrincipalContext principal) => InteractionInvocationHost.ForApplication(principal,
        new ApplicationRevision(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []),
        "operator@1", command, InteractionExecutionProfile.Atomic,
        new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

    private static OwnershipAdministrationFixture OwnershipAdministration(DantesRoleplayDbContext db, SetupState setup,
        IConfigurationRoot configuration)
    {
        var services = new ServiceCollection();
        services.AddOptions<WebRemoteAccessOptions>().Bind(configuration.GetSection(WebRemoteAccessOptions.SectionName));
        var provider = services.BuildServiceProvider();
        var catalog = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation, setup.Sources, setup.Roots,
            setup.Extensions).UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority());
        return new(configuration, provider, new SqliteInformationSourceOwnershipAdministration(db,
            new PlatformInstallationOperatorMembershipPolicy(provider.GetRequiredService<IOptionsMonitor<WebRemoteAccessOptions>>()),
            setup.Applications, setup.Namespaces, setup.Activation, catalog, new OperationLog(db)));
    }

    private static IConfigurationRoot RemoteConfiguration(string? allowedLogin = null, string? invitedLogin = null)
    {
        var values = new Dictionary<string, string?>
        {
            [WebRemoteAccessOptions.SectionName + ":Enabled"] = "true",
            [WebRemoteAccessOptions.SectionName + ":TailscaleHost"] = "roleplay.example.ts.net"
        };
        if (allowedLogin is not null) values[WebRemoteAccessOptions.SectionName + ":AllowedLogins:0"] = allowedLogin;
        if (invitedLogin is not null) values[WebRemoteAccessOptions.SectionName + ":InvitedLogins:0"] = invitedLogin;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class OwnershipAdministrationFixture(IConfigurationRoot configuration, ServiceProvider provider,
        SqliteInformationSourceOwnershipAdministration service) : IDisposable
    {
        public IConfigurationRoot Configuration { get; } = configuration;
        public SqliteInformationSourceOwnershipAdministration Service { get; } = service;
        public void Dispose() => provider.Dispose();
    }
}
