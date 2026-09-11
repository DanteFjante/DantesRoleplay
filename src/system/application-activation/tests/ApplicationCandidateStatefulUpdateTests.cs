using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string StatefulMarkdownPath = "content/mechanics/stateful/mechanic.fixture.stateful.md";
    private const string StatefulSourcePath = "content/mechanics/stateful/mechanic.fixture.stateful.js";
    private const string StatefulMechanicId = "demo.runtime.mechanics.stateful";
    private const string StatefulSpace = "stateful-space";

    [Fact]
    public async Task Existing_stateful_body_runs_real_dry_run_publishes_and_rechecks_current_authority()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration("demo.runtime.mechanics", "human-domain-label",
            "Stateful mechanic fixture namespace.", [CatalogNamespaceKinds.Mechanic],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        WriteStatefulMechanic(StatefulSource(1));
        await ActivateAsync(setup);
        await SeedMechanicGrantAsync(db);
        await AllowStatefulAuthoringAsync(db);

        var active = setup.Activation.Current(Application)!;
        var revision = new ApplicationRevision(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []);
        var spaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var state = spaces.Create(new(StatefulSpace, revision, active.ActivationFingerprint, active.ResolutionFingerprint));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        await entities.CreateEntityAsync(StatefulSpace, "subject", "Subject");
        var edges = new SqliteStateSpaceEdgeStore(db, spaces);
        await SeedStatefulExecutionGrantAsync(db);
        db.ChangeTracker.Clear();

        var write = await Service(db, setup).WriteCandidateAsync(
            ApplicationHost(setup, "stateful-write", InteractionExecutionProfile.Atomic, "mechanic-grant@1"),
            MechanicRequest(active.ActivationFingerprint, new ApplicationCandidateDocumentInput(
                "file:" + StatefulSourcePath, "catalog", StatefulSourcePath, "text/javascript", StatefulSource(2))));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var candidate = await CandidateAsync(db);
        var selection = await new ApplicationCandidateSelectionReader(db, setup.Applications,
            setup.Activation, setup.Resolver).ReadAsync(AuthorHost(setup, "stateful-select"), candidate);
        var definition = new StandingGrantDefinitionReference(Assert.Single(selection!.Targets).DefinitionId,
            "mechanic", selection.Targets[0].Revision, selection.Targets[0].ContentFingerprint);

        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
            setup.Sources, setup.Roots, setup.Extensions);
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
            new CatalogCursorCodec(new byte[32]), setup.Activation);
        var projection = new ApplicationMechanicProjectionResolver(db, spaces);
        var mapping = new ApplicationMechanicProjectionMappingResolver(catalogs, spaces, types, edges);
        var evaluator = new ApplicationMechanicEvaluator(catalogs, projection, new JintMechanicEngine());
        var effectApplier = new ApplicationEcsEffectApplier(db, entities, spaces, new OperationLog(db), edges);
        var runner = new ApplicationActionRunner(catalogs, setup.Activation, spaces, types, entities, edges,
            mapping, evaluator, effectApplier, new OperationLog(db),
            new ApplicationEcsEffectBatchBuilder(types, entities, edges));
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var runtime = new ApplicationCandidateStatefulRuntimeValidator(db, setup.Activation, catalogs, spaces, mapping,
            evaluator, runner, effectApplier, new SqliteStandingGrantReadCandidateReader(db),
            setup.Resolver, policy);
        var manuals = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(catalogs), policy, setup.Resolver, setup.Activation, ["system"]);
        var service = new SqliteApplicationAuthoringService(db, setup.Applications, setup.Activation,
            setup.Activation, setup.Sources, policy, setup.Resolver, new OperationLog(db),
            null, manuals, null, null, runtime);
        var expectedEffects = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(new[]
        {
            new ApplicationEcsEffect { Type = ApplicationEcsEffectType.EntityCreate,
                EntityId = "created", Name = "Version 2" }
        }));
        var request = new ApplicationCandidateValidationRequest(candidate,
        [
            new(definition, "{\"entityId\":\"created\"}",
                "{\"entityId\":\"created\",\"version\":2}", StatefulSpace,
                InteractionStateRevision.From(state), new Dictionary<string, string> { ["subject"] = "subject" },
                expectedEffects)
        ]);
        var gateway = new ApplicationCandidateCapabilityGateway(CandidateCatalog(db, setup, service));
        var validationJson = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value, candidateId = candidate.CandidateId,
            revision = candidate.Revision, contentFingerprint = candidate.ContentFingerprint,
            samples = request.Samples.Select(sample => new
            {
                definition = new { definitionId = sample.Definition.DefinitionId,
                    kind = sample.Definition.Kind, revision = sample.Definition.Revision,
                    contentFingerprint = sample.Definition.ContentFingerprint },
                inputJson = sample.InputJson, expectedDataJson = sample.ExpectedDataJson,
                stateSpaceId = sample.StateSpaceId, stateRevision = sample.StateRevision,
                roleEntityIds = sample.RoleEntityIds, expectedEffectsJson = sample.ExpectedEffectsJson
            }).ToArray()
        });
        var validated = await gateway.InvokeAsync(AuthorHost(setup, "stateful-validate").Principal,
            Application, SystemCapabilityIds.ApplicationCandidateValidate,
            validationJson, "stateful-validate", "website");

        Assert.True(validated.Ok, validated.Error?.Code + ":" + validated.Error?.Message);
        Assert.Null(await entities.GetEntityAsync(StatefulSpace, "created"));
        var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.OperationId == validated.OperationId);
        Assert.True(validation.Outcome == "valid", validation.DiagnosticsJson);
        Assert.StartsWith(ApplicationCandidateStatefulRuntimeValidator.PolicyVersion + "@", validation.PreparationVersion);
        Assert.Contains("stateful-runtime-report", validation.PreparedEvidenceReference);
        Assert.Single(await db.Operations.AsNoTracking().Where(value =>
            value.Tool == ApplicationEcsExecutionIdentity.AuditTool && value.Summary.StartsWith("Validated ")).ToArrayAsync());

        var activationHost = AuthorHost(setup, "stateful-activate");
        var update = await new ApplicationCandidateStatefulUpdateReader(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Resolver).ReadAsync(activationHost, candidate, default);
        Assert.NotNull(update);
        var checkedOperation = await db.Operations.AsNoTracking().SingleAsync(value => value.Id == validation.OperationId);
        Assert.True(ApplicationCandidateOperationProof.TryReadStatefulRuntimeReport(checkedOperation, validation,
            candidate, [definition], out var retainedReport));
        Assert.NotNull(retainedReport);
        Assert.True(await ApplicationCandidateStatefulUpdateValidation.VerifyAsync(
            db, update!, retainedReport!, validation, default));
        Assert.True(await runtime.CurrentAsync(update!, retainedReport!, activationHost, default));

        var activated = await service.ActivateAsync(activationHost,
            new(candidate, validation.OperationId));
        Assert.True(activated.Tag == InteractionInvocationResultTag.Committed,
            activated.Code + ":" + activated.SafeMessage);
        Assert.Null(await entities.GetEntityAsync(StatefulSpace, "created"));
        Assert.True(catalogs.TryGet(Application, out var published));
        var publishedDefinition = published.Inspect(new(Application, Application.Value, StatefulMechanicId)).Summary;
        Assert.Equal(definition.ContentFingerprint, publishedDefinition.ContentFingerprint);
        var reboundState = spaces.Get(StatefulSpace)!;
        Assert.Equal(setup.Activation.Current(Application)!.ActivationFingerprint, reboundState.ManifestFingerprint);

        var execution = await runner.RunAsync(new(StatefulSpace, Application, StatefulMechanicId,
            publishedDefinition.Version, publishedDefinition.ContentFingerprint,
            new Dictionary<string, string> { ["subject"] = "subject" }, "{\"entityId\":\"created\"}", 42,
            new("1234567890abcdef1234567890abcdef", new string('E', 64))));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, execution.Disposition);
        Assert.Equal("Version 2", (await entities.GetEntityAsync(StatefulSpace, "created"))!.Name);

        var replay = await service.ActivateAsync(AuthorHost(setup, "stateful-activate"),
            new(candidate, validation.OperationId));
        Assert.True(replay.Receipt == activated.Receipt, replay.Code + ":" + replay.SafeMessage);

        await RevokeStatefulExecutionGrantAsync(db);
        db.ChangeTracker.Clear();
        Assert.False(await runtime.CurrentAsync(update, retainedReport,
            AuthorHost(setup, "stateful-current-revoked"), default));
        var denied = await gateway.InvokeAsync(AuthorHost(setup, "stateful-validate").Principal,
            Application, SystemCapabilityIds.ApplicationCandidateValidate,
            validationJson, "stateful-validate", "website");
        Assert.False(denied.Ok);
        Assert.Equal("APPLICATION_CANDIDATE_VALIDATION_EVIDENCE_INCONSISTENT", denied.Error?.Code);
    }

    [Fact]
    public void Pure_sample_canonical_command_is_unchanged_when_stateful_fields_are_absent()
    {
        using var db = fixture.CreateContext();
        var setup = Setup(db);
        var candidate = new ApplicationCandidateReference(Application, new string('a', 32), 1, new string('A', 64));
        var definition = new StandingGrantDefinitionReference("demo.runtime.mechanics.pure", "mechanic", 1, new string('B', 64));
        var legacy = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            principal = "principal." + new string('a', 64), authenticationMethod = "test", applicationId = Application.Value,
            CommandId = "pure-command", candidate,
            samples = new[] { new { Definition = definition, InputJson = "{}", ExpectedDataJson = "{}" } }
        }));
        var actual = ApplicationCandidateOperationProof.CanonicalValidation(
            AuthorHost(setup, "pure-command"), candidate,
            SqliteApplicationAuthoringService.NormalizeSamples([new(definition, "{}", "{}")]));
        Assert.Equal(legacy, actual);
    }

    private void WriteStatefulMechanic(string source)
    {
        var markdownPath = Path.Combine(root, StatefulMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
        File.WriteAllText(markdownPath, $$$$"""
            ---
            id: {{{{StatefulMechanicId}}}}
            category: fixture.stateful
            name: Stateful fixture
            scope: action
            status: active
            ---

            ## Description
            Create one fixture entity.

            ## Matches
            create fixture entity

            ## Requirements
            ```json
            {"roles":{"subject":{"components":[]}}}
            ```
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, StatefulSourcePath.Replace('/', Path.DirectorySeparatorChar)),
            source, new UTF8Encoding(false));
    }

    private static string StatefulSource(int version) =>
        $"return {{data:{{entityId:ctx.input.entityId,version:{version}}},effects:[{{type:'entity.create',entityId:ctx.input.entityId,name:'Version {version}'}}]}};";

    private static InteractionInvocationHost AuthorHost(SetupState setup, string command) =>
        InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new ApplicationRevision(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []),
            "mechanic-grant@1", command, InteractionExecutionProfile.Atomic,
            new InteractionInvocationBudget(8, DateTime.UtcNow.AddMinutes(1)));

    private static async Task AllowStatefulAuthoringAsync(DantesRoleplayDbContext db)
    {
        var row = await db.Set<StandingGrantRevisionRecord>().SingleAsync(value => value.GrantId == "mechanic-grant");
        var grant = SqliteStandingGrantPolicy.Parse(row) with
        {
            Capabilities = [StandingGrantCapability.Author, StandingGrantCapability.Validate,
                StandingGrantCapability.Read, StandingGrantCapability.Activate],
            EffectKinds = [ApplicationEcsEffectType.EntityCreate], MaximumOperations = 8
        };
        row.PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant);
        row.ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant);
        row.MaximumOperations = grant.MaximumOperations;
        await db.SaveChangesAsync();
    }

    private static async Task SeedStatefulExecutionGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("stateful-execute@1", "stateful-execute", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.StateSpace, StatefulSpace,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.runtime.mechanics", true, [CatalogNamespaceKinds.Mechanic])]),
            [ApplicationEcsEffectType.EntityCreate], 8, DateTime.UtcNow.AddMinutes(10), false,
            "stateful-execute-operation");
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
            Scope = "stateSpace", StateSpaceId = StatefulSpace,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            Revoked = false, ExpiresAtUtc = grant.ExpiresAtUtc, MaximumOperations = grant.MaximumOperations,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = grant.Revision });
        await db.SaveChangesAsync();
    }

    private static async Task RevokeStatefulExecutionGrantAsync(DantesRoleplayDbContext db)
    {
        var prior = SqliteStandingGrantPolicy.Parse(await db.Set<StandingGrantRevisionRecord>()
            .SingleAsync(value => value.GrantId == "stateful-execute" && value.Revision == 1));
        var revoked = prior with
        {
            Revision = 2, GrantReference = "stateful-execute@2", Revoked = true,
            IssuedByOperationId = "stateful-execute-revoke"
        };
        db.Add(new Operation { Id = revoked.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = revoked.GrantId, Revision = revoked.Revision, GrantReference = revoked.GrantReference,
            PrincipalReference = revoked.PrincipalReference, ApplicationId = revoked.ApplicationId.Value,
            Scope = "stateSpace", StateSpaceId = StatefulSpace,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(revoked),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(revoked),
            Revoked = true, ExpiresAtUtc = revoked.ExpiresAtUtc, MaximumOperations = revoked.MaximumOperations,
            IssuedByOperationId = revoked.IssuedByOperationId
        });
        (await db.Set<StandingGrantCurrentRecord>().SingleAsync(value =>
            value.GrantId == revoked.GrantId)).Revision = revoked.Revision;
        await db.SaveChangesAsync();
    }
}
