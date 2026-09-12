using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
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
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            setup.Resolver, policy, types);
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
            EffectKinds = [ApplicationEcsEffectType.EntityCreate], MaximumOperations = 16
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

    [Fact]
    public async Task New_stateful_atomic_action_requires_retained_review_then_dry_runs_publishes_executes_and_replays()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"candidate-new-stateful-{Guid.NewGuid():N}.db");
        try
        {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite("Filename=" + databasePath).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.EnsureCreatedAsync();
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

        const string id = "demo.runtime.mechanics.new-stateful";
        const string markdownPath = "content/mechanics/stateful/mechanic.fixture.new-stateful.md";
        const string javascriptPath = "content/mechanics/stateful/mechanic.fixture.new-stateful.js";
        var markdown = $$$$$"""
            ---
            id: {{{{{id}}}}}
            category: fixture.stateful
            name: New reviewed stateful fixture
            scope: action
            status: active
            ---

            ## Description
            Create one fixture entity through a reviewed new Atomic action.

            ## Requirements
            ```json
            {"roles":{"subject":{"components":[]}},"inputSchema":{"type":"object","required":["entityId"],"properties":{"entityId":{"type":"string"}}}}
            ```
            """;
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
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
        var runtime = new ApplicationCandidateStatefulRuntimeValidator(db, setup.Activation, catalogs, spaces,
            mapping, evaluator, runner, effectApplier, new SqliteStandingGrantReadCandidateReader(db),
            setup.Resolver, policy);
        var features = new InteractionFeatureRetriever(catalogs, namespaces: setup.Namespaces, changes: setup.Activation);
        var manuals = new InteractionManualContextService(new ProcedureStore(db), features, policy,
            setup.Resolver, setup.Activation, ["system"]);
        var closureReader = new ApplicationCandidateStatefulReviewClosureReader(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Resolver, catalogs);
        var gate = new SystemTaskApplicationValidationGate(db, setup.Applications, setup.Activation,
            setup.Resolver, policy, TimeProvider.System, PureRuntimeClosure(db, setup), manuals, features,
            [closureReader]);
        var receiptReader = new ApplicationCandidateReviewedClosureReceiptReader(db, setup.Applications, gate);
        var reviewedReader = new ApplicationCandidateReviewedStatefulUpdateReader(receiptReader);
        var authoring = new SqliteApplicationAuthoringService(db, setup.Applications, setup.Activation,
            setup.Activation, setup.Sources, policy, setup.Resolver, new OperationLog(db), null, manuals,
            null, null, runtime, closureReader, reviewedReader);
        var reviews = new SystemTaskApplicationValidationService(db, gate, TimeProvider.System);
        var gateway = new ApplicationCandidateCapabilityGateway(CandidateCatalog(db, setup, authoring, reviews));
        var principal = AuthorHost(setup, "new-stateful-principal").Principal;

        var write = await gateway.InvokeAsync(principal, Application, SystemCapabilityIds.ApplicationCandidateWrite,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value, candidateId = (string?)null, expectedCandidateRevision = 0,
                expectedActiveFingerprint = active.ActivationFingerprint, origin = "runtime",
                synchronizationEvidenceReference = (string?)null,
                newImplementationReason = "Add one reviewed Atomic stateful action without changing component schemas.",
                documents = new[]
                {
                    new { logicalIdentity = "file:" + markdownPath, sourceId = "catalog", relativePath = markdownPath,
                        mediaType = "text/markdown", text = markdown },
                    new { logicalIdentity = "file:" + javascriptPath, sourceId = "catalog", relativePath = javascriptPath,
                        mediaType = "text/javascript", text = StatefulSource(3) }
                }
            }), "new-stateful-write", "website");
        Assert.True(write.Ok, write.Error?.Code + ":" + write.Error?.Message);
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .SingleAsync(value => value.SourceOperationId == write.OperationId);
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var selection = await new ApplicationCandidateSelectionReader(db, setup.Applications,
            setup.Activation, setup.Resolver).ReadAsync(AuthorHost(setup, "new-stateful-select"), candidate);
        var definition = new StandingGrantDefinitionReference(selection!.Targets[0].DefinitionId, "mechanic",
            selection.Targets[0].Revision, selection.Targets[0].ContentFingerprint);
        var sourceOperation = await db.Operations.AsNoTracking().SingleAsync(value => value.Id == row.SourceOperationId);
        var retainedCandidate = await new ApplicationCandidateRetainedReader(db, setup.Applications)
            .ReadMetadataAsync(Application, candidate.CandidateId, candidate.Revision);
        Assert.NotNull(retainedCandidate);
        Assert.True(ApplicationCandidateOperationProof.WriteMatches(
            sourceOperation, retainedCandidate, [definition], out var writeCommand));
        var reviewInput = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value, candidateId = candidate.CandidateId, revision = candidate.Revision,
            contentFingerprint = candidate.ContentFingerprint, authoringOperationId = row.SourceOperationId,
            authoringCommandId = writeCommand!.CommandId
        });
        var submitted = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateReviewSubmit, reviewInput, "new-stateful-review", "website");
        Assert.True(submitted.Ok, submitted.Error?.Code + ":" + submitted.Error?.Message);

        SystemTaskValidationAuthority prepared;
        await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default))
            prepared = await gate.CheckAsync(InteractionInvocationHost.ForApplication(principal, revision,
                "mechanic-grant@1", "new-stateful-provider", InteractionExecutionProfile.ReadOnly,
                new(16, DateTime.UtcNow.AddMinutes(2))), candidate, true);
        Assert.IsType<ApplicationCandidateStatefulReviewClosureEvidence>(prepared.ReviewClosure);
        var provider = new RetainedReviewProvider(prepared.ReviewInput!, "justifiedNew");
        var invoker = new SystemInnerWorkerValidationInvoker(new AiService([provider]), TimeProvider.System);
        var services = new ServiceCollection().AddSingleton(db).AddSingleton(gate)
            .AddSingleton<TimeProvider>(TimeProvider.System).BuildServiceProvider();
        await using (services)
        {
            var lifecycles = new SystemTaskAiInvocationLifecycleFactory(
                services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            Assert.True(await reviews.RunNextAsync("new-stateful-reviewer", invoker,
                ReviewerConfiguration(), lifecycles));
        }
        Assert.Equal(1, provider.Calls);

        var expectedEffects = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(new[]
        {
            new ApplicationEcsEffect { Type = ApplicationEcsEffectType.EntityCreate,
                EntityId = "new-created", Name = "Version 3" }
        }));
        var validationInput = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value, candidateId = candidate.CandidateId, revision = candidate.Revision,
            contentFingerprint = candidate.ContentFingerprint,
            samples = new[] { new { definition = new { definitionId = definition.DefinitionId, kind = definition.Kind,
                    revision = definition.Revision, contentFingerprint = definition.ContentFingerprint },
                inputJson = "{\"entityId\":\"new-created\"}",
                expectedDataJson = "{\"entityId\":\"new-created\",\"version\":3}",
                stateSpaceId = StatefulSpace, stateRevision = InteractionStateRevision.From(state),
                roleEntityIds = new Dictionary<string,string> { ["subject"] = "subject" },
                expectedEffectsJson = expectedEffects } }
        });
        var validated = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate, validationInput, "new-stateful-validate", "website");
        Assert.True(validated.Ok, validated.Error?.Code + ":" + validated.Error?.Message);
        var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.OperationId == validated.OperationId);
        Assert.Equal("valid", validation.Outcome);
        Assert.StartsWith("validation.result.", validation.ReuseEvidenceReference);

        ApplicationCandidateReviewedStatefulUpdateEvidence? reviewedProof;
        await using (var reviewTransaction = await db.Database.BeginTransactionAsync())
        {
            reviewedProof = await reviewedReader.ReadAsync(
                AuthorHost(setup, "new-stateful-activate"), candidate);
            Assert.NotNull(reviewedProof);
            await reviewTransaction.RollbackAsync();
            db.ChangeTracker.Clear();
        }

        var activated = await authoring.ActivateAsync(AuthorHost(setup, "new-stateful-activate"),
            new(candidate, validated.OperationId!));
        Assert.Equal(InteractionInvocationResultTag.Committed, activated.Tag);
        Assert.Null(await entities.GetEntityAsync(StatefulSpace, "new-created"));
        Assert.True(catalogs.TryGet(Application, out var published));
        var publishedDefinition = published.Inspect(new(Application, Application.Value, id)).Summary;
        var executed = await runner.RunAsync(new(StatefulSpace, Application, id,
            publishedDefinition.Version, publishedDefinition.ContentFingerprint,
            new Dictionary<string, string> { ["subject"] = "subject" },
            "{\"entityId\":\"new-created\"}", 43,
            new("fedcba0987654321fedcba0987654321", new string('D', 64))));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, executed.Disposition);
        Assert.Equal("Version 3", (await entities.GetEntityAsync(StatefulSpace, "new-created"))!.Name);
        var replay = await authoring.ActivateAsync(AuthorHost(setup, "new-stateful-activate"),
            new(candidate, validated.OperationId!));
        Assert.Equal(activated.Receipt, replay.Receipt);
        Assert.Equal(1, provider.Calls);
        var validationOperation = await db.Operations.AsNoTracking()
            .SingleAsync(value => value.Id == validation.OperationId);
        Assert.True(ApplicationCandidateOperationProof.TryReadStatefulRuntimeReport(
            validationOperation, validation, candidate, [definition], out var retainedReport));
        await RevokeStatefulExecutionGrantAsync(db);
        db.ChangeTracker.Clear();
        Assert.False(await runtime.CurrentAsync(reviewedProof!.Closure, retainedReport!,
            AuthorHost(setup, "new-stateful-revoked"), default));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
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
