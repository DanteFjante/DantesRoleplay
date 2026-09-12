using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
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
    private const string ProcedureActionId = "demo.runtime.action.sample";
    private const string ProcedureActionMarkdownPath = "content/mechanics/action.md";
    private const string ProcedureActionJavaScriptPath = "content/mechanics/action.js";

    [Theory]
    [InlineData("query(kind: \"runtime.inspect\")")]
    [InlineData("query(kind: \"demo.runtime.missing\"")]
    public async Task Procedure_review_closure_rejects_unresolved_or_malformed_explicit_references(
        string governs)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author,
            StandingGrantCapability.Read, StandingGrantCapability.Validate]);
        var before = setup.Activation.Current(Application)!;
        var text = ProcedureText("Changed instruction.").Replace(
            "query(kind: \"runtime.inspect\")", governs, StringComparison.Ordinal);
        var written = await Service(db, setup).WriteCandidateAsync(
            ApplicationHost(setup, "procedure-invalid-reference", InteractionExecutionProfile.Atomic),
            new(null, 0, before.ActivationFingerprint, "runtime", null,
                "Extend demo.runtime.inspect.",
                [new("file:" + RelativePath, "catalog", RelativePath, "text/markdown", text)]));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleAsync();
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId,
            row.Revision, row.ContentFingerprint);
        var materializer = new ActivatedApplicationCatalogMaterializer(
            setup.Applications, setup.Activation, setup.Sources, setup.Roots, setup.Extensions)
            .UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(),
                new ActivatedApplicationCatalogCacheAuthority());
        var snapshots = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
            new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)), setup.Activation);
        var closure = new ApplicationCandidateProcedureClosureReader(
            setup.Activation, setup.Activation, setup.Resolver, snapshots);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var host = ApplicationHost(setup, "procedure-invalid-reference-review");
        var selection = await new ApplicationCandidateSelectionReader(
            db, setup.Applications, setup.Activation, setup.Resolver).ReadAsync(host, candidate);

        var result = await closure.ReadAsync(host, candidate, selection!);

        Assert.Equal(ApplicationCandidateReviewClosureReadStatus.Rejected, result.Status);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task Candidate_gateway_publishes_reviewed_procedure_and_inner_selects_its_exact_action()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"procedure-publication-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + databasePath).Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var setup = Setup(db);
            setup.Namespaces.Register(new CatalogNamespaceRegistration(
                "demo.runtime.action", "human-domain-label", "Reviewed fixture actions.",
                [CatalogNamespaceKinds.Mechanic],
                ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
                ReviewNote: "Reviewed procedure publication fixture."));
            WriteProcedureAction();
            await ActivateAsync(setup);
            await SeedProcedurePublicationGrantAsync(db);
            db.ChangeTracker.Clear();

            var before = setup.Activation.Current(Application)!;
            var materializer = new ActivatedApplicationCatalogMaterializer(
                setup.Applications, setup.Activation, setup.Sources, setup.Roots, setup.Extensions)
                .UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(),
                    new ActivatedApplicationCatalogCacheAuthority());
            var snapshots = new ActivatedApplicationCatalogProvider(
                new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
                new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)), setup.Activation);
            var features = new InteractionFeatureRetriever(snapshots,
                namespaces: setup.Namespaces, changes: setup.Activation);
            var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
            var manuals = new InteractionManualContextService(new ProcedureStore(db), features,
                policy, setup.Resolver, setup.Activation, ["system"]);
            var procedureClosure = new ApplicationCandidateProcedureClosureReader(
                setup.Activation, setup.Activation, setup.Resolver, snapshots);
            var gate = new SystemTaskApplicationValidationGate(db, setup.Applications,
                setup.Activation, setup.Resolver, policy, TimeProvider.System,
                pureClosures: null, manuals, features, [procedureClosure]);
            var reviews = new SystemTaskApplicationValidationService(db, gate, TimeProvider.System);
            var reviewed = new ApplicationCandidateReviewedProcedureUpdateReader(
                db, setup.Applications, setup.Activation, setup.Resolver, gate);
            var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
                setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
                new OperationLog(db), preparation: null, manuals, reviewedPureUpdates: null,
                reviewedProcedureUpdates: reviewed);
            var gateway = new ApplicationCandidateCapabilityGateway(
                CandidateCatalog(db, setup, authoring, reviews));
            var principal = TrustedPrincipalContext.VerifiedPrincipal(
                "principal." + new string('a', 64), "test");

            const string writeKey = "procedure-publication-write";
            var written = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateWrite,
                JsonSerializer.Serialize(new
                {
                    applicationId = Application.Value,
                    candidateId = (string?)null,
                    expectedCandidateRevision = 0,
                    expectedActiveFingerprint = before.ActivationFingerprint,
                    origin = "runtime",
                    synchronizationEvidenceReference = (string?)null,
                    newImplementationReason = "Extend demo.runtime.inspect to govern the existing action.",
                    documents = new[]
                    {
                        new
                        {
                            logicalIdentity = "file:" + RelativePath,
                            sourceId = "catalog",
                            relativePath = RelativePath,
                            mediaType = "text/markdown",
                            text = PublishedProcedureText()
                        }
                    }
                }), writeKey, "website");
            Assert.True(written.Ok, written.Error?.Code + ": " + written.Error?.Message);
            var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
                .SingleAsync(value => value.SourceOperationId == written.OperationId);
            var candidate = new ApplicationCandidateReference(Application, row.CandidateId,
                row.Revision, row.ContentFingerprint);

            var submitted = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateReviewSubmit,
                JsonSerializer.Serialize(new
                {
                    applicationId = Application.Value,
                    candidateId = candidate.CandidateId,
                    revision = candidate.Revision,
                    contentFingerprint = candidate.ContentFingerprint,
                    authoringOperationId = row.SourceOperationId,
                    authoringCommandId = GatewayCommandId(
                        principal, SystemCapabilityIds.ApplicationCandidateWrite, writeKey)
                }), "procedure-publication-review", "website");
            Assert.True(submitted.Ok, submitted.Error?.Code + ": " + submitted.Error?.Message);

            SystemTaskValidationAuthority prepared;
            await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(
                db, TimeProvider.System, false, default))
                prepared = await gate.CheckAsync(PureReviewHost(setup, "procedure-review-provider"),
                    candidate, true);
            Assert.Null(SystemTaskApplicationValidationGate.ExecutionPrerequisite(prepared));
            var closure = Assert.IsType<ApplicationCandidateProcedureReviewClosureEvidence>(
                prepared.ReviewClosure);
            Assert.Equal(ProcedureActionId, Assert.Single(closure.Dependencies).DefinitionId);
            var provider = new RetainedReviewProvider(prepared.ReviewInput!, "extendExisting");
            var invoker = new SystemInnerWorkerValidationInvoker(
                new AiService([provider]), TimeProvider.System);
            var lifecycleServices = new ServiceCollection()
                .AddSingleton(db).AddSingleton(gate)
                .AddSingleton<TimeProvider>(TimeProvider.System).BuildServiceProvider();
            await using (lifecycleServices)
            {
                var lifecycles = new SystemTaskAiInvocationLifecycleFactory(
                    lifecycleServices.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
                Assert.True(await reviews.RunNextAsync("procedure-publication-reviewer", invoker,
                    ReviewerConfiguration(), lifecycles));
            }

            var validated = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateValidate,
                JsonSerializer.Serialize(new
                {
                    applicationId = Application.Value,
                    candidateId = candidate.CandidateId,
                    revision = candidate.Revision,
                    contentFingerprint = candidate.ContentFingerprint,
                    samples = Array.Empty<object>()
                }), "procedure-publication-validate", "codex");
            Assert.True(validated.Ok, validated.Error?.Code + ": " + validated.Error?.Message);
            var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
                .SingleAsync(value => value.OperationId == validated.OperationId);
            Assert.True(validation.Outcome == "valid", validation.DiagnosticsJson);
            Assert.Equal(ApplicationCandidateReviewedProcedureUpdateValidation.PreparationVersion,
                validation.PreparationVersion);

            var activationArguments = JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                validationOperationId = validated.OperationId
            });
            var activated = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateActivate, activationArguments,
                "procedure-publication-activate", "website");
            Assert.True(activated.Ok, activated.Error?.Code + ": " + activated.Error?.Message);
            var current = setup.Activation.Current(Application)!;
            Assert.Equal(before.ActivationRevision + 1, current.ActivationRevision);
            Assert.Equal(before.Winners.Count, current.Winners.Count);
            var replayed = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateActivate, activationArguments,
                "procedure-publication-activate", "website");
            Assert.True(replayed.Ok, replayed.Error?.Code + ": " + replayed.Error?.Message);
            Assert.Equal(activated.OperationId, replayed.OperationId);
            Assert.Equal(current.ActivationFingerprint,
                setup.Activation.Current(Application)!.ActivationFingerprint);

            Assert.True(snapshots.TryGetSnapshot(Application, out var snapshot));
            var procedure = snapshot.Documents.Single(value =>
                value.Record.QualifiedId == "demo.runtime.inspect").Record;
            Assert.Contains("Run the exact governed action", procedure.ContentJson, StringComparison.Ordinal);
            Assert.Contains(ProcedureActionId, procedure.ContentJson, StringComparison.Ordinal);
            var manual = await manuals.DiscoverAsync(new(
                PureReviewHost(setup, "procedure-publication-manual"), "demo.runtime.inspect"));
            Assert.Equal(InteractionInvocationResultTag.Completed, manual.Tag);
            Assert.Contains("Run the exact governed action", manual.DataJson, StringComparison.Ordinal);

            var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
            var state = stateSpaces.Create(new("procedure-publication-state",
                setup.Applications.Get(Application)!, current.ActivationFingerprint,
                current.ResolutionFingerprint));
            await SeedProcedureInnerGrantAsync(db, state.StateSpaceId);
            db.ChangeTracker.Clear();
            var privateAuthority = new PrivateHostInteractionAuthorizationPolicy(stateSpaces);
            var contexts = new InteractionTaskContextMaterializer(
                privateAuthority, features, snapshots, new UnusedReadModels());
            var resolver = new SystemInnerWorkerProcedureResolver(
                new InteractionEnvelopeFactory(setup.Applications, setup.Activation,
                    stateSpaces, privateAuthority), contexts, snapshots,
                new SystemInnerWorkerPreparation(new ProcedureStore(db), contexts),
                new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
                    new("web.inner", "Inner AI", "Perform the selected procedure.")
                ])), TimeProvider.System);
            var innerHost = new InteractionInvocationHost(principal,
                setup.Applications.Get(Application)!, state.StateSpaceId, "inner-procedure@1",
                "procedure-publication-inner", InteractionStateRevision.From(state),
                InteractionExecutionProfile.Workflow,
                new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(2)));
            var resolved = await resolver.ResolveAsync(new(innerHost,
                new(procedure.QualifiedId, procedure.Version, procedure.ContentFingerprint),
                JsonSerializer.Serialize(new
                {
                    format = SystemInnerWorkerAssignmentV1.Format,
                    instruction = "Run the governed fixture action."
                }), "{\"type\":\"object\"}"));
            var selectedAction = Assert.Single(resolved.ApplicationTools);
            Assert.Equal(ProcedureActionId,
                selectedAction.Binding.CapabilityVersion.ExactDefinitionId);
            Assert.Equal(SystemInnerWorkerToolKind.ApplicationAction, selectedAction.Binding.Kind);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private void WriteProcedureAction()
    {
        var directory = Path.Combine(root, "content", "mechanics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(root,
            ProcedureActionMarkdownPath.Replace('/', Path.DirectorySeparatorChar)), """
            ---
            id: demo.runtime.action.sample
            category: runtime.action
            name: Procedure action
            scope: action
            status: active
            ---

            ## Description
            Execute one generic procedure action.

            ## Requirements
            ```json
            {}
            ```
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root,
            ProcedureActionJavaScriptPath.Replace('/', Path.DirectorySeparatorChar)),
            "return { data: { selected: true } };", new UTF8Encoding(false));
    }

    private static string PublishedProcedureText() => $$"""
        ---
        id: demo.runtime.inspect
        category: runtime.inspect
        name: Inspect runtime
        governs: execute {{ProcedureActionId}}
        status: active
        ---

        ## Description
        Inspect the active runtime definition.

        ## Instructions
        1. Run the exact governed action.

        ## Constraints
        - Preserve it.
        """;

    private static string GatewayCommandId(TrustedPrincipalContext principal,
        string capabilityId, string key) => Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("dantes-roleplay/application-authoring-request/v1\n"
                + principal.PrincipalId + "\n" + Application.Value + "\n" + capabilityId + "\n" + key)))[..32];

    private static async Task SeedProcedurePublicationGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("grant@1", "grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.Application, null,
            [StandingGrantCapability.Author, StandingGrantCapability.Read,
                StandingGrantCapability.Validate, StandingGrantCapability.Activate],
            new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.runtime", true,
                    [CatalogNamespaceKinds.Procedure, CatalogNamespaceKinds.Mechanic])]),
            [], 16, DateTime.UtcNow.AddMinutes(10), false, "procedure-publication-grant");
        await PersistGrantAsync(db, grant, "application");
    }

    private static async Task SeedProcedureInnerGrantAsync(
        DantesRoleplayDbContext db, string stateSpaceId)
    {
        var grant = new StandingGrantRevision("inner-procedure@1", "inner-procedure", 1,
            new string('0', 64), "principal." + new string('a', 64), Application,
            StandingGrantScope.StateSpace, stateSpaceId,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.runtime", true,
                    [CatalogNamespaceKinds.Procedure, CatalogNamespaceKinds.Mechanic])]),
            [], 16, DateTime.UtcNow.AddMinutes(10), false, "procedure-inner-grant");
        await PersistGrantAsync(db, grant, "stateSpace");
    }

    private static async Task PersistGrantAsync(
        DantesRoleplayDbContext db, StandingGrantRevision grant, string scope)
    {
        grant = grant with { ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant) };
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision,
            GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference,
            ApplicationId = grant.ApplicationId.Value, Scope = scope,
            StateSpaceId = grant.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = grant.ContentFingerprint,
            MaximumOperations = grant.MaximumOperations,
            ExpiresAtUtc = grant.ExpiresAtUtc, Revoked = grant.Revoked,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = grant.Revision });
        await db.SaveChangesAsync();
    }
}
