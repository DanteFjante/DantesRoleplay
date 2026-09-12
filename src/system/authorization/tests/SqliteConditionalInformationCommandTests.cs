using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Application_owned_information_source_create_returns_receipt_and_retains_owner_history()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedInformationGrantAsync(db, ["demo.info"]);
        var service = InformationService(db, setup);

        var result = await service.WriteSourceAsync(ApplicationHost(setup, "information-create", InteractionExecutionProfile.Atomic),
            new(new("source.notes", "demo.info", "Notes"), 0, "demo.info.notes"));

        Assert.Equal(InteractionInvocationResultTag.Committed, result.Tag);
        Assert.NotNull(result.Receipt);
        var owner = await InformationSourceOwnership.ReadAsync(db, "source.notes", default);
        Assert.NotNull(owner);
        Assert.Equal(Application.Value, owner!.Owner.ApplicationId);
        Assert.Equal("demo.info.notes", owner.Owner.QualifiedTargetId);
        Assert.Equal(1, owner.Owner.Revision);
        Assert.Single(await db.Set<InformationContentRevisionRecord>().Where(value => value.Kind == "source").ToArrayAsync());
    }

    [Fact]
    public async Task Information_source_stale_cas_preserves_the_current_source()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedInformationGrantAsync(db, ["demo.info"]);
        var service = InformationService(db, setup);
        _ = await service.WriteSourceAsync(ApplicationHost(setup, "information-first", InteractionExecutionProfile.Atomic),
            new(new("source.notes", "demo.info", "Notes"), 0, "demo.info.notes"));

        var stale = await service.WriteSourceAsync(ApplicationHost(setup, "information-stale", InteractionExecutionProfile.Atomic),
            new(new("source.notes", "demo.info", "Changed notes"), 0, "demo.info.notes"));

        Assert.Equal(InteractionInvocationResultTag.Failed, stale.Tag);
        Assert.Equal("INFORMATION_REVISION_CONFLICT", stale.Code);
        Assert.Equal("Notes", (await db.Set<InformationSource>().SingleAsync(value => value.Id == "source.notes")).Name);
    }

    [Fact]
    public async Task Unmapped_existing_information_source_is_unavailable_and_content_scope_is_not_authority()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedInformationGrantAsync(db, ["demo.info"]);
        var raw = new InformationStore(db);
        _ = await raw.WriteSourceAsync(new("source.unmapped", "demo.info", "Unmapped"));

        var result = await InformationService(db, setup).WriteSourceAsync(ApplicationHost(setup, "information-unmapped", InteractionExecutionProfile.Atomic),
            new(new("source.unmapped", "demo.info", "Changed"), 1, "demo.info.unmapped"));

        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Equal("INFORMATION_SOURCE_OWNER_UNAVAILABLE", result.Code);
        Assert.Equal("Unmapped", (await db.Set<InformationSource>().SingleAsync(value => value.Id == "source.unmapped")).Name);
    }

    [Fact]
    public async Task Denied_information_source_create_rolls_back_source_owner_identity_and_audit()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedInformationGrantAsync(db, ["demo.info"], [StandingGrantCapability.Read]);

        var result = await InformationService(db, setup).WriteSourceAsync(ApplicationHost(setup, "information-denied", InteractionExecutionProfile.Atomic),
            new(new("source.denied", "demo.info", "Denied"), 0, "demo.info.denied"));

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Empty(await db.Set<InformationSource>().Where(value => value.Id == "source.denied").ToArrayAsync());
        Assert.Empty(await db.Set<InformationSourceTargetIdentityRecord>().Where(value => value.SourceId == "source.denied").ToArrayAsync());
        Assert.Empty(await db.Operations.Where(value => value.Tool == "information-conditional").ToArrayAsync());
    }

    [Fact]
    public async Task Incompatible_source_metadata_schema_preserves_existing_source_and_records()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedInformationGrantAsync(db, ["demo.info"]);
        var service = InformationService(db, setup);
        _ = await service.WriteSourceAsync(ApplicationHost(setup, "information-schema-source", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema"), 0, "demo.info.schema"));
        _ = await new InformationStore(db).WriteRecordAsync(new("record.schema", "source.schema", "Existing", "Keep", "{}"));

        var rejected = await service.WriteSourceAsync(ApplicationHost(setup, "information-schema-reject", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema", MetadataSchemaJson: """{"type":"object","required":["rank"]}"""), 1, "demo.info.schema"));

        Assert.Equal(InteractionInvocationResultTag.Failed, rejected.Tag);
        Assert.Equal("{}", (await db.Set<InformationSource>().SingleAsync(value => value.Id == "source.schema")).MetadataSchemaJson);
        Assert.Equal("Keep", (await db.Set<InformationRecord>().SingleAsync(value => value.Id == "record.schema")).Content);
    }

    [Fact]
    public async Task Information_source_replay_returns_its_prior_receipt_after_a_later_revision_and_revocation_denies_replay()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedInformationGrantAsync(db, ["demo.info"]);
        var service = InformationService(db, setup);
        var firstRequest = new InformationSourceConditionalWriteRequest(new("source.replay", "demo.info", "Original"), 0, "demo.info.replay");
        var first = await service.WriteSourceAsync(ApplicationHost(setup, "information-replay", InteractionExecutionProfile.Atomic), firstRequest);
        _ = await service.WriteSourceAsync(ApplicationHost(setup, "information-revision", InteractionExecutionProfile.Atomic),
            new(new("source.replay", "demo.info", "Revision two"), 1, "demo.info.replay"));

        var replay = await service.WriteSourceAsync(ApplicationHost(setup, "information-replay", InteractionExecutionProfile.Atomic), firstRequest);
        await RevokeGrantAsync(db);
        var revoked = await service.WriteSourceAsync(ApplicationHost(setup, "information-replay", InteractionExecutionProfile.Atomic), firstRequest);

        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal(InteractionInvocationResultTag.Failed, revoked.Tag);
        Assert.Equal("STANDING_GRANT_NOT_CURRENT", revoked.Code);
    }

    [Fact]
    public async Task Record_source_move_requires_authority_for_both_the_old_and_new_source()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.alpha"); AddInformationNamespace(setup, "demo.beta");
        await SeedInformationGrantAsync(db, ["demo.alpha", "demo.beta"]);
        var service = InformationService(db, setup);
        _ = await service.WriteSourceAsync(ApplicationHost(setup, "source-alpha", InteractionExecutionProfile.Atomic),
            new(new("source.alpha", "demo.alpha", "Alpha"), 0, "demo.alpha.source"));
        _ = await service.WriteSourceAsync(ApplicationHost(setup, "source-beta", InteractionExecutionProfile.Atomic),
            new(new("source.beta", "demo.beta", "Beta"), 0, "demo.beta.source"));
        _ = await new InformationStore(db).WriteRecordAsync(new("record.move", "source.alpha", "Original", "Keep", "{}"));
        await ReplaceInformationGrantNamespacesAsync(db, ["demo.alpha"]);

        var moved = await service.WriteRecordAsync(ApplicationHost(setup, "record-move", InteractionExecutionProfile.Atomic,
            grantReference: "grant@2"), new(new("record.move", "source.beta", "Moved", "Changed", "{}"), 1));

        Assert.Equal(InteractionInvocationResultTag.Failed, moved.Tag);
        Assert.Equal("STANDING_GRANT_TARGET_DENIED", moved.Code);
        var record = await db.Set<InformationRecord>().SingleAsync(value => value.Id == "record.move");
        Assert.Equal("source.alpha", record.SourceId);
        Assert.Equal("Keep", record.Content);
    }

    private static SqliteConditionalInformationStore InformationService(DantesRoleplayDbContext db, SetupState setup,
        InformationStore? store = null)
    {
        var catalog = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation, setup.Sources, setup.Roots,
            setup.Extensions).UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority());
        return new(db, store ?? new InformationStore(db), setup.Applications, setup.Namespaces, setup.Activation, catalog,
            new SqliteStandingGrantPolicy(db, setup.Resolver), setup.Resolver, new OperationLog(db));
    }

    private static void AddInformationNamespace(SetupState setup, string id) => setup.Namespaces.Register(
        new CatalogNamespaceRegistration(id, "human-domain-label", "Information fixture namespace.",
            [CatalogNamespaceKinds.InformationSource], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed fixture."));

    private static async Task SeedInformationGrantAsync(DantesRoleplayDbContext db, IReadOnlyList<string> namespaces,
        IReadOnlyList<StandingGrantCapability>? capabilities = null)
    {
        var grant = new StandingGrantRevision("grant@1", "grant", 1, new string('0', 64), "principal." + new string('a', 64), Application,
            StandingGrantScope.Application, null, capabilities ?? [StandingGrantCapability.Author],
            new(StandingGrantDefinitionMode.ApplicationOwned, [], namespaces.Select(value =>
                new StandingGrantNamespaceAllowance(value, true, [CatalogNamespaceKinds.InformationSource])).ToArray()), [], 1,
            DateTime.UtcNow.AddMinutes(10), false, "grant-operation");
        db.Add(new Operation { Id = "grant-operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord { GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value, Scope = "application", StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant), ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc, Revoked = false, IssuedByOperationId = "grant-operation" });
        db.Add(new StandingGrantCurrentRecord { GrantId = "grant", Revision = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task ReplaceInformationGrantNamespacesAsync(DantesRoleplayDbContext db,
        IReadOnlyList<string> namespaces)
    {
        var prior = SqliteStandingGrantPolicy.Parse(await db.Set<StandingGrantRevisionRecord>()
            .SingleAsync(value => value.GrantId == "grant" && value.Revision == 1));
        var replacement = prior with
        {
            Revision = 2,
            GrantReference = "grant@2",
            Definitions = new(StandingGrantDefinitionMode.ApplicationOwned, [], namespaces.Select(value =>
                new StandingGrantNamespaceAllowance(value, true, [CatalogNamespaceKinds.InformationSource])).ToArray()),
            IssuedByOperationId = "grant-replace"
        };
        db.Add(new Operation { Id = "grant-replace", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord { GrantId = replacement.GrantId, Revision = replacement.Revision,
            GrantReference = replacement.GrantReference, PrincipalReference = replacement.PrincipalReference,
            ApplicationId = replacement.ApplicationId.Value, Scope = "application", StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(replacement),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(replacement),
            MaximumOperations = replacement.MaximumOperations, ExpiresAtUtc = replacement.ExpiresAtUtc,
            Revoked = false, IssuedByOperationId = "grant-replace" });
        (await db.Set<StandingGrantCurrentRecord>().SingleAsync(value => value.GrantId == "grant")).Revision = 2;
        await db.SaveChangesAsync();
    }
}
