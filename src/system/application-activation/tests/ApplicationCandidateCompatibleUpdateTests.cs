using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Compatible_body_publication_executes_the_new_active_javascript_and_revocation_denies_it()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db,
            "return { data: { count: ctx.input.count + 7 } };");
        await AllowPurePublicationAsync(db, execute: true);
        db.ChangeTracker.Clear();
        var before = data.Setup.Activation.Current(Application)!;
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
            new ActivatedApplicationCatalogMaterializer(data.Setup.Applications,
                data.Setup.Activation, data.Setup.Sources, data.Setup.Roots, data.Setup.Extensions),
            new CatalogCursorCodec(new byte[32]), data.Setup.Activation);
        Assert.True(catalogs.TryGet(Application, out var oldCatalog));
        var oldRecord = oldCatalog.Inspect(new(Application, Application.Value,
            data.Definition.DefinitionId)).Summary;
        var manuals = new InteractionManualContextService(
            new ProcedureStore(db), new InteractionFeatureRetriever(catalogs),
            new SqliteStandingGrantPolicy(db, data.Setup.Resolver), data.Setup.Resolver,
            data.Setup.Activation, ["system"]);
        var service = new SqliteApplicationAuthoringService(
            db, data.Setup.Applications, data.Setup.Activation, data.Setup.Activation,
            data.Setup.Sources, new SqliteStandingGrantPolicy(db, data.Setup.Resolver),
            data.Setup.Resolver, new OperationLog(db), PureRuntimeValidator(db, data.Setup), manuals);
        var validationHost = PureRuntimeHost(data.Setup, operations: 3);
        var validated = await service.ValidateAsync(new ApplicationCandidateValidationRequest(
            data.Candidate,
            [new(data.Definition, "{\"count\":1}", "{\"count\":8}")]), validationHost);
        Assert.Equal(InteractionInvocationResultTag.Committed, validated.Tag);
        var validation = await db.Set<ApplicationCandidateValidationRecord>()
            .AsNoTracking().SingleAsync();
        Assert.Equal("valid", validation.Outcome);
        Assert.True(validation.DependenciesComplete);
        Assert.NotNull(validation.ManualPacketResultFingerprint);

        var activationRequest = new ApplicationCandidateActivationRequest(
            data.Candidate, validation.OperationId);
        var activated = await service.ActivateAsync(
            PureRuntimeHost(data.Setup), activationRequest);
        Assert.Equal(InteractionInvocationResultTag.Committed, activated.Tag);
        var published = data.Setup.Activation.Current(Application)!;
        Assert.Equal(before.ActivationRevision + 1, published.ActivationRevision);
        Assert.True(catalogs.TryGet(Application, out var activeCatalog));
        var activeRecord = activeCatalog.Inspect(new(Application, Application.Value,
            data.Definition.DefinitionId)).Summary;
        Assert.NotEqual(oldRecord.ContentFingerprint, activeRecord.ContentFingerprint);
        Assert.Equal(data.Definition.ContentFingerprint, activeRecord.ContentFingerprint);

        var action = PureActionAdapter(db, data.Setup, catalogs);
        var executionHost = PureRuntimeHost(data.Setup);
        var executed = await action.ExecuteAsync(new(
            executionHost, activeRecord.QualifiedId, activeRecord.Version,
            activeRecord.ContentFingerprint, new Dictionary<string, string>(),
            "{\"count\":5}"));
        Assert.Equal(InteractionInvocationResultTag.Completed, executed.Tag);
        Assert.Equal("{\"count\":12}", executed.DataJson);
        Assert.Null(executed.Receipt);

        var replay = await service.ActivateAsync(
            PureRuntimeHost(data.Setup), activationRequest);
        Assert.Equal(activated.Receipt, replay.Receipt);
        Assert.Equal(published.ActivationRevision,
            data.Setup.Activation.Current(Application)!.ActivationRevision);
        Assert.Single(await db.Set<ApplicationCandidatePublicationRecord>()
            .AsNoTracking().ToArrayAsync());
        Assert.Single(await db.Operations.AsNoTracking()
            .Where(value => value.Tool == "application-candidate-activation")
            .ToArrayAsync());

        await RevokeGrantAsync(db);
        db.ChangeTracker.Clear();
        var revokedHost = PureRuntimeHost(data.Setup);
        var denied = await action.ExecuteAsync(new(
            revokedHost, activeRecord.QualifiedId, activeRecord.Version,
            activeRecord.ContentFingerprint, new Dictionary<string, string>(),
            "{\"count\":5}"));
        Assert.Equal(InteractionInvocationResultTag.Failed, denied.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", denied.Code);
        Assert.Equal(1, revokedHost.Budget.RemainingOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compatible_update_authoring_validates_publishes_replays_and_recovers_retained_content(bool failPublication)
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: { count: ctx.input.count + 1 } };");
        await AllowPurePublicationAsync(db);
        db.ChangeTracker.Clear();
        var before = data.Setup.Activation.Current(Application)!;
        var materializer = new ActivatedApplicationCatalogMaterializer(data.Setup.Applications, data.Setup.Activation,
            data.Setup.Sources, data.Setup.Roots, data.Setup.Extensions);
        var catalogs = new ActivatedApplicationCatalogProvider(new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
            materializer, new CatalogCursorCodec(new byte[32]), data.Setup.Activation);
        Assert.True(catalogs.TryGetSnapshot(Application, out var oldSnapshot));
        var manuals = new InteractionManualContextService(new ProcedureStore(db), new InteractionFeatureRetriever(catalogs),
            new SqliteStandingGrantPolicy(db, data.Setup.Resolver), data.Setup.Resolver, data.Setup.Activation, ["system"]);
        var service = new SqliteApplicationAuthoringService(db, data.Setup.Applications, data.Setup.Activation,
            data.Setup.Activation, data.Setup.Sources, new SqliteStandingGrantPolicy(db, data.Setup.Resolver),
            data.Setup.Resolver, new OperationLog(db), PureRuntimeValidator(db, data.Setup), manuals);
        var validated = await service.ValidateAsync(new ApplicationCandidateValidationRequest(data.Candidate,
            [new(data.Definition, "{\"count\":1}", "{\"count\":2}")]), PureRuntimeHost(data.Setup, operations: 3));
        Assert.Equal(InteractionInvocationResultTag.Committed, validated.Tag);
        var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking().SingleAsync();
        Assert.True(validation.Outcome == "valid", validation.DiagnosticsJson);
        Assert.Equal(before.ActivationFingerprint, data.Setup.Activation.Current(Application)!.ActivationFingerprint);

        var request = new ApplicationCandidateActivationRequest(data.Candidate, validation.OperationId);
        if (failPublication)
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_candidate_publication BEFORE INSERT ON system_application_candidate_publication
                BEGIN SELECT RAISE(ABORT, 'Injected publication failure'); END;
                """);
            var failed = await service.ActivateAsync(PureRuntimeHost(data.Setup), request);
            Assert.Equal(InteractionInvocationResultTag.Unavailable, failed.Tag);
            Assert.Equal(before.ActivationFingerprint, data.Setup.Activation.Current(Application)!.ActivationFingerprint);
            Assert.Empty(await db.Set<ApplicationCandidatePublicationRecord>().AsNoTracking().ToArrayAsync());
            Assert.Single(await db.Set<ApplicationActivationRevisionRecord>().AsNoTracking().ToArrayAsync());
            Assert.Empty(await db.Operations.AsNoTracking().Where(value => value.Tool == "application-candidate-activation").ToArrayAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_candidate_publication;");
        }
        var activated = await service.ActivateAsync(PureRuntimeHost(data.Setup), request);
        Assert.True(activated.Tag == InteractionInvocationResultTag.Committed, activated.Code);
        var current = data.Setup.Activation.Current(Application)!;
        Assert.Equal(before.ActivationRevision + 1, current.ActivationRevision);
        Assert.NotEqual(before.ActivationFingerprint, current.ActivationFingerprint);
        Assert.Single(await db.Set<ApplicationCandidatePublicationRecord>().ToArrayAsync());
        Assert.True(catalogs.TryGetSnapshot(Application, out var updatedSnapshot));
        Assert.NotEqual(oldSnapshot.EffectiveSetFingerprint, updatedSnapshot.EffectiveSetFingerprint);
        Assert.Equal(data.Definition.ContentFingerprint,
            Assert.Single(updatedSnapshot.Manifest.Records, value => value.QualifiedId == data.Definition.DefinitionId).ContentFingerprint);

        var replay = await service.ActivateAsync(PureRuntimeHost(data.Setup), request);
        Assert.Equal(activated.Receipt, replay.Receipt);
        Assert.Equal(current.ActivationRevision, data.Setup.Activation.Current(Application)!.ActivationRevision);
        Assert.Single(await db.Set<ApplicationCandidatePublicationRecord>().ToArrayAsync());

        var recoveryHost = InteractionInvocationHost.ForApplication(PureRuntimeHost(data.Setup).Principal,
            PureRuntimeHost(data.Setup).ApplicationRevision, "grant@1", "recover-body", InteractionExecutionProfile.Atomic,
            new(1, DateTime.UtcNow.AddMinutes(1)));
        var recovered = await service.RecoverAsync(recoveryHost, before.ActivationRevision, current.ActivationFingerprint);
        Assert.Equal(InteractionInvocationResultTag.Committed, recovered.Tag);
        var recoveryRow = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .SingleAsync(value => value.SourceOperationId == recovered.Receipt!.OperationId);
        var retainedRecovery = await new ApplicationCandidateRetainedReader(db, data.Setup.Applications)
            .ReadAsync(Application, recoveryRow.CandidateId, recoveryRow.Revision);
        Assert.Equal(before.Winners.Single(value => value.RelativePath == PureJavaScriptPath).ContentFingerprint,
            retainedRecovery!.Documents.Single(value => value.Document.RelativePath == PureJavaScriptPath).Document.ContentFingerprint);
        Assert.Equal(current.ActivationFingerprint, data.Setup.Activation.Current(Application)!.ActivationFingerprint);

        await RevokeGrantAsync(db);
        db.ChangeTracker.Clear();
        var denied = await service.ActivateAsync(PureRuntimeHost(data.Setup), request);
        Assert.NotEqual(InteractionInvocationResultTag.Committed, denied.Tag);
        Assert.Equal(current.ActivationFingerprint, data.Setup.Activation.Current(Application)!.ActivationFingerprint);
    }

    private static async Task AllowPurePublicationAsync(
        DantesRoleplayDbContext db,
        bool execute = false)
    {
        var row = await db.Set<StandingGrantRevisionRecord>().SingleAsync(value => value.GrantId == "grant");
        var grant = SqliteStandingGrantPolicy.Parse(row);
        grant = grant with
        {
            Capabilities = execute
                ? [.. grant.Capabilities, StandingGrantCapability.Activate, StandingGrantCapability.Execute]
                : [.. grant.Capabilities, StandingGrantCapability.Activate]
        };
        row.PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant);
        row.ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Compatible_update_proves_existing_body_replacement_and_retains_the_original_generation()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: { updated: true } };");
        var before = data.Setup.Activation.Current(Application)!;
        var reader = new ApplicationCandidateCompatibleUpdateReader(db, data.Setup.Applications,
            data.Setup.Activation, data.Setup.Activation, data.Setup.Resolver,
            new ApplicationCandidatePureMechanicClassifier(new BoundedJsonSchemaValidator()));
        var update = await reader.ReadAsync(PureRuntimeHost(data.Setup), data.Candidate);

        Assert.NotNull(update);
        Assert.Equal(before.ActivationFingerprint, update.Basis.ActivationFingerprint);
        Assert.Equal(before.Winners, update.Basis.Winners);
        var previous = Assert.Single(update.Predecessors);
        Assert.Equal(data.Definition.DefinitionId, previous.DefinitionId);
        Assert.NotEqual(data.Definition.ContentFingerprint, previous.ContentFingerprint);
        Assert.Equal(before.ActivationFingerprint, data.Setup.Activation.Current(Application)!.ActivationFingerprint);
        Assert.Equal(update.Fingerprint, (await reader.ReadAsync(PureRuntimeHost(data.Setup), data.Candidate))!.Fingerprint);
    }

    [Fact]
    public async Task Compatible_update_refuses_a_contract_change_even_when_the_pure_runtime_accepts_its_grammar()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        var written = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "changed-contract",
                Interactions.InteractionExecutionProfile.Atomic, "mechanic-grant@1"),
            MechanicRequest(setup.Activation.Current(Application)!.ActivationFingerprint,
                new ApplicationCandidateDocumentInput("file:" + MechanicMarkdownPath, "catalog", MechanicMarkdownPath, "text/markdown",
                    MechanicMarkdown("Changed contract"))));
        Assert.Equal(Interactions.InteractionInvocationResultTag.Committed, written.Tag);
        var candidate = await CandidateAsync(db);
        var reader = new ApplicationCandidateCompatibleUpdateReader(db, setup.Applications, setup.Activation,
            setup.Activation, setup.Resolver, new(new BoundedJsonSchemaValidator()));
        Assert.Null(await reader.ReadAsync(ApplicationHost(setup, "check", grantReference: "mechanic-grant@1"), candidate));
    }
}
