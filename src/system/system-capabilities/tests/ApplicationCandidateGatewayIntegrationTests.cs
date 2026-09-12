using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Application_gateway_cancels_exact_candidate_review_and_denies_foreign_handle()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"candidate-review-cancel-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + databasePath).Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var data = await PureRuntimeFixtureAsync(db,
                "return { data: { count: ctx.input.count + 1 } };");
            var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleAsync();
            var reviews = new SystemTaskApplicationValidationService(
                db, PureValidationGate(db, data.Setup), TimeProvider.System);
            var gateway = new ApplicationCandidateCapabilityGateway(
                CandidateCatalog(db, data.Setup, Service(db, data.Setup), reviews));
            var principal = PureRuntimeHost(data.Setup).Principal;
            var submitted = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewSubmit,
                JsonSerializer.Serialize(new
                {
                    applicationId = Application.Value,
                    candidateId = data.Candidate.CandidateId,
                    revision = data.Candidate.Revision,
                    contentFingerprint = data.Candidate.ContentFingerprint,
                    authoringOperationId = row.SourceOperationId,
                    authoringCommandId = "pure-runtime"
                }), "gateway-review-cancel-target", "website");
            Assert.True(submitted.Ok, submitted.Error?.Code + ": " + submitted.Error?.Message);
            var task = submitted.Data!.Value.GetProperty("task");
            var taskId = task.GetProperty("taskId").GetString()!;
            var commandId = task.GetProperty("commandId").GetString()!;

            var foreign = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewCancel,
                JsonSerializer.Serialize(new { taskId = new string('f', 32), commandId }),
                "gateway-review-cancel-foreign", "website");
            Assert.False(foreign.Ok);
            Assert.Equal("INNER_VALIDATION_NOT_AUTHORIZED", foreign.Error?.Code);

            var cancelled = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewCancel,
                JsonSerializer.Serialize(new { taskId, commandId }),
                "gateway-review-cancel", "website");
            Assert.True(cancelled.Ok, cancelled.Error?.Code + ": " + cancelled.Error?.Message);
            Assert.Equal("cancelled", cancelled.Data!.Value.GetProperty("status").GetString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Application_gateway_submits_runs_and_reads_actual_candidate_reuse_review()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"candidate-review-gateway-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + databasePath).Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var data = await PureRuntimeFixtureAsync(db,
                "return { data: { count: ctx.input.count + 1 } };");
            await AllowPurePublicationAsync(db);
            db.ChangeTracker.Clear();
            var before = data.Setup.Activation.Current(Application)!;
            const string markdown = """
            ---
            id: demo.runtime.pure.sample
            category: runtime.pure
            name: Reviewed pure sample
            status: active
            ---

            ## Description
            Calculate the reviewed state-free result.

            ## Requirements
            ```json
            {"inputSchema":{"type":"object","required":["count"],"properties":{"count":{"type":"integer"}}}}
            ```
            """;
            var written = await Service(db, data.Setup).WriteCandidateAsync(
                PureAtomicHost(data.Setup, "gateway-reviewed-write", 1),
                new(null, 0, before.ActivationFingerprint, "runtime", null,
                    "Extend the existing pure calculation with reviewed retained behavior.",
                    [
                        new("file:" + PureMarkdownPath, "catalog", PureMarkdownPath, "text/markdown", markdown),
                        new("file:" + PureJavaScriptPath, "catalog", PureJavaScriptPath, "text/javascript",
                            "return { data: { count: ctx.input.count + 2 } };")
                    ]));
            Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
            var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
                .SingleAsync(value => value.SourceOperationId == written.Receipt!.OperationId);
            var candidate = new ApplicationCandidateReference(Application, row.CandidateId,
                row.Revision, row.ContentFingerprint);
            var gate = PureValidationGate(db, data.Setup);
            var reviews = new SystemTaskApplicationValidationService(db, gate, TimeProvider.System);
            var gateway = new ApplicationCandidateCapabilityGateway(
                CandidateCatalog(db, data.Setup, Service(db, data.Setup), reviews));
            var principal = PureRuntimeHost(data.Setup).Principal;
            var submitInput = JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                authoringOperationId = row.SourceOperationId,
                authoringCommandId = "gateway-reviewed-write"
            });
            var submitted = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewSubmit,
                submitInput, "gateway-review-submit", "website");

            Assert.True(submitted.Ok, submitted.Error?.Code + ": " + submitted.Error?.Message);
            Assert.Equal("pending", submitted.Data!.Value.GetProperty("status").GetString());
            var task = submitted.Data.Value.GetProperty("task");
            var handle = new SystemTaskDurableHandle(
                task.GetProperty("taskId").GetString()!, task.GetProperty("commandId").GetString()!);
            var replayed = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewSubmit,
                submitInput, "gateway-review-submit", "website");
            Assert.True(replayed.Ok, replayed.Error?.Code + ": " + replayed.Error?.Message);
            Assert.Equal(handle.TaskId,
                replayed.Data!.Value.GetProperty("task").GetProperty("taskId").GetString());

            SystemTaskValidationAuthority prepared;
            await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(
                db, TimeProvider.System, false, default))
                prepared = await gate.CheckAsync(PureReviewHost(data.Setup, "provider-probe"), candidate, true);
            Assert.Null(SystemTaskApplicationValidationGate.ExecutionPrerequisite(prepared));
            var provider = new RetainedReviewProvider(prepared.ReviewInput!, "extendExisting");
            var invoker = new SystemInnerWorkerValidationInvoker(new AiService([provider]), TimeProvider.System);
            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton(gate);
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            await using var serviceProvider = services.BuildServiceProvider();
            var lifecycles = new SystemTaskAiInvocationLifecycleFactory(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            Assert.True(await reviews.RunNextAsync("gateway-review-worker", invoker,
                ReviewerConfiguration(), lifecycles));

            var readInput = JsonSerializer.Serialize(new
            { taskId = handle.TaskId, commandId = handle.CommandId });
            var read = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewRead, readInput, null, "codex");
            Assert.True(read.Ok, read.Error?.Code + ": " + read.Error?.Message);
            var status = read.Data!.Value.GetProperty("data");
            Assert.True(status.GetProperty("completionAvailable").GetBoolean());
            Assert.Equal("dantes-roleplay/application-candidate-reuse-review-result/v1",
                status.GetProperty("result").GetProperty("format").GetString());
            Assert.Equal(1, provider.Calls);

            var reviewed = new ApplicationCandidateReviewedPureUpdateReader(db, data.Setup.Applications,
                data.Setup.Activation, data.Setup.Activation, data.Setup.Resolver, gate);
            var authoring = new SqliteApplicationAuthoringService(db, data.Setup.Applications,
                data.Setup.Activation, data.Setup.Activation, data.Setup.Sources,
                new SqliteStandingGrantPolicy(db, data.Setup.Resolver), data.Setup.Resolver,
                new OperationLog(db), PureRuntimeValidator(db, data.Setup), null, reviewed);
            var publishingGateway = new ApplicationCandidateCapabilityGateway(
                CandidateCatalog(db, data.Setup, authoring, reviews));
            var definition = Assert.Single(prepared.PureClosure!.Definitions).Plan.Definition;
            var validated = await publishingGateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateValidate,
                JsonSerializer.Serialize(new
                {
                    applicationId = Application.Value,
                    candidateId = candidate.CandidateId,
                    revision = candidate.Revision,
                    contentFingerprint = candidate.ContentFingerprint,
                    samples = new[]
                    {
                        new
                        {
                            definition = new
                            {
                                definitionId = definition.DefinitionId, kind = definition.Kind,
                                revision = definition.Revision, contentFingerprint = definition.ContentFingerprint
                            },
                            inputJson = "{\"count\":1}", expectedDataJson = "{\"count\":3}"
                        }
                    }
                }), "gateway-reviewed-validate", "website");
            Assert.True(validated.Ok, validated.Error?.Code + ": " + validated.Error?.Message);
            var validationRow = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
                .SingleAsync(value => value.OperationId == validated.OperationId);
            Assert.True(validationRow.Outcome == "valid", validationRow.DiagnosticsJson);
            Assert.True(validationRow.DependenciesComplete, validationRow.DiagnosticsJson);
            Assert.NotNull(validationRow.ReuseEvidenceReference);
            var activated = await publishingGateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateActivate,
                JsonSerializer.Serialize(new
                {
                    applicationId = Application.Value,
                    candidateId = candidate.CandidateId,
                    revision = candidate.Revision,
                    contentFingerprint = candidate.ContentFingerprint,
                    validationOperationId = validated.OperationId
                }), "gateway-reviewed-activate", "website");
            Assert.True(activated.Ok, activated.Error?.Code + ": " + activated.Error?.Message);
            Assert.Equal(before.ActivationRevision + 1,
                data.Setup.Activation.Current(Application)!.ActivationRevision);

            var foreign = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewRead,
                JsonSerializer.Serialize(new { taskId = new string('f', 32), commandId = handle.CommandId }), null, "codex");
            Assert.False(foreign.Ok);
            Assert.Equal("INNER_VALIDATION_NOT_AUTHORIZED", foreign.Error?.Code);

            await RevokeGrantAsync(db);
            var revoked = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewRead, readInput, null, "codex");
            Assert.False(revoked.Ok);
            Assert.Equal("STANDING_GRANT_DENIED", revoked.Error?.Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Application_gateway_authors_validates_and_activates_through_actual_owners()
    {
        await using var db = fixture.CreateContext();
        var initial = await PureRuntimeFixtureAsync(db,
            "return { data: { count: ctx.input.count + 1 } };");
        await AllowPurePublicationAsync(db);
        db.ChangeTracker.Clear();
        var before = initial.Setup.Activation.Current(Application)!;
        var materializer = new ActivatedApplicationCatalogMaterializer(initial.Setup.Applications,
            initial.Setup.Activation, initial.Setup.Sources, initial.Setup.Roots, initial.Setup.Extensions);
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
            new CatalogCursorCodec(new byte[32]), initial.Setup.Activation);
        var manuals = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(catalogs),
            new SqliteStandingGrantPolicy(db, initial.Setup.Resolver), initial.Setup.Resolver,
            initial.Setup.Activation, ["system"]);
        var service = new SqliteApplicationAuthoringService(db, initial.Setup.Applications,
            initial.Setup.Activation, initial.Setup.Activation, initial.Setup.Sources,
            new SqliteStandingGrantPolicy(db, initial.Setup.Resolver), initial.Setup.Resolver,
            new OperationLog(db), PureRuntimeValidator(db, initial.Setup), manuals);
        var catalog = CandidateCatalog(db, initial.Setup, service);
        var gateway = new ApplicationCandidateCapabilityGateway(catalog);
        var principal = PureRuntimeHost(initial.Setup).Principal;

        var write = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateWrite,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = initial.Candidate.CandidateId,
                expectedCandidateRevision = initial.Candidate.Revision,
                expectedActiveFingerprint = before.ActivationFingerprint,
                origin = "runtime",
                synchronizationEvidenceReference = (string?)null,
                newImplementationReason = "Exercise the application-scoped authoring gateway.",
                documents = new[]
                {
                    new
                    {
                        logicalIdentity = "file:" + PureJavaScriptPath,
                        sourceId = "catalog",
                        relativePath = PureJavaScriptPath,
                        mediaType = "text/javascript",
                        text = "return { data: { count: ctx.input.count + 2 } };"
                    }
                }
            }), "gateway-write", "website");
        Assert.True(write.Ok, write.Error?.Message);

        var candidateRow = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .SingleAsync(value => value.CandidateId == initial.Candidate.CandidateId && value.Revision == 2);
        var candidate = new ApplicationCandidateReference(
            Application, candidateRow.CandidateId, candidateRow.Revision, candidateRow.ContentFingerprint);
        var metadata = await new ApplicationCandidateRetainedReader(db, initial.Setup.Applications)
            .ReadMetadataAsync(Application, candidate.CandidateId, candidate.Revision);
        var documents = await new ApplicationCandidateRetainedReader(db, initial.Setup.Applications)
            .ReadSelectedAsync(metadata!, [PureMarkdownPath, PureJavaScriptPath]);
        var definition = Assert.Single(SqliteApplicationAuthoringService.Definitions(
            Application, documents, [PureJavaScriptPath]));

        var validation = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                samples = new[]
                {
                    new
                    {
                        definition = new
                        {
                            definitionId = definition.DefinitionId,
                            kind = definition.Kind,
                            revision = definition.Revision,
                            contentFingerprint = definition.ContentFingerprint
                        },
                        inputJson = "{\"count\":1}",
                        expectedDataJson = "{\"count\":3}"
                    }
                }
            }), "gateway-validate", "codex");
        Assert.True(validation.Ok, validation.Error?.Message);
        var validationRow = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.OperationId == validation.OperationId);
        Assert.True(validationRow.Outcome == "valid", validationRow.DiagnosticsJson);

        var activation = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateActivate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                validationOperationId = validation.OperationId
            }), "gateway-activate", "website");

        Assert.True(activation.Ok, activation.Error?.Code + ": " + activation.Error?.Message);
        Assert.Equal(before.ActivationRevision + 1,
            initial.Setup.Activation.Current(Application)!.ActivationRevision);
        Assert.Single(await db.Set<ApplicationCandidatePublicationRecord>().AsNoTracking()
            .Where(value => value.CandidateId == candidate.CandidateId
                && value.Revision == candidate.Revision).ToArrayAsync());
    }

    private static ISystemCapabilityCatalog CandidateCatalog(
        DantesRoleplayDbContext db,
        SetupState setup,
        IApplicationAuthoringService service,
        SystemTaskApplicationValidationService? reviews = null)
    {
        reviews ??= new SystemTaskApplicationValidationService(db,
            PureValidationGate(db, setup), TimeProvider.System);
        return new SystemCapabilityCatalog(
        [new ApplicationCandidateInspectCapabilityHandler(db, setup.Applications, service),
            new ApplicationCandidateReviewReadCapabilityHandler(db, setup.Applications, reviews)],
        new BoundedJsonSchemaValidator(),
        new PrivateOperatorAuthorizationPolicy(),
        new ISystemWriteCapabilityHandler[]
        {
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateWrite,
                db, setup.Applications, service),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateValidate,
                db, setup.Applications, service),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateActivate,
                db, setup.Applications, service),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateRecover,
                db, setup.Applications, service),
            new ApplicationCandidateReviewWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateReviewSubmit,
                db, setup.Applications, reviews),
            new ApplicationCandidateReviewWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateReviewCancel,
                db, setup.Applications, reviews)
        });
    }
}
