using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Gateway_intent_update_replays_then_validates_activates_and_refreshes_manual_discovery()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Read, StandingGrantCapability.Author,
            StandingGrantCapability.Validate, StandingGrantCapability.Activate]);
        await ExpandGrantBudgetAsync(db);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
            new OperationLog(db), preparation: null, manuals: Manuals(db, setup, policy));
        var associations = new IntentMatchAssociationService(setup.Activation,
            setup.Activation, setup.Resolver, policy, authoring);
        var gateway = new ApplicationCandidateCapabilityGateway(
            IntentUpdateCatalog(db, setup, authoring, associations));
        var target = await CurrentProcedureAsync(setup, "gateway-intent-current");
        var principal = AssociationHost(setup, "gateway-principal").Principal;
        var input = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value,
            candidateId = (string?)null,
            expectedCandidateRevision = 0,
            target = new
            {
                definitionId = target.DefinitionId,
                kind = target.Kind,
                revision = target.Revision,
                contentFingerprint = target.ContentFingerprint
            },
            matchPhrases = new[] { "  gateway   velvet  astrolabe  " }
        });

        var staged = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateIntentUpdate, input,
            "intent-gateway-stage", "website");
        var replay = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateIntentUpdate, input,
            "intent-gateway-stage", "codex");

        Assert.True(staged.Ok, staged.Error?.Code + ": " + staged.Error?.Message);
        Assert.True(replay.Ok, replay.Error?.Code + ": " + replay.Error?.Message);
        Assert.Equal(staged.OperationId, replay.OperationId);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = await LatestCandidateAsync(db);
        var validation = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                samples = Array.Empty<object>()
            }), "intent-gateway-validate", "codex");
        Assert.True(validation.Ok, validation.Error?.Code + ": " + validation.Error?.Message);
        var validationRow = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.OperationId == validation.OperationId);
        Assert.Equal("valid", validationRow.Outcome);
        Assert.Equal(ApplicationCandidateIntentMatchUpdateValidation.PreparationVersion,
            validationRow.PreparationVersion);

        var activated = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateActivate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                validationOperationId = validation.OperationId
            }), "intent-gateway-activate", "website");
        Assert.True(activated.Ok, activated.Error?.Code + ": " + activated.Error?.Message);
        AssertSelected(await DiscoverAsync(db, setup, policy,
            "gateway velvet astrolabe", "gateway-manual-refresh"), target.DefinitionId);

        var beforeStale = await db.Set<ApplicationCandidateRevisionRecord>().CountAsync();
        var stale = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateIntentUpdate, input,
            "intent-gateway-stale", "codex");
        Assert.False(stale.Ok);
        Assert.Equal("INTENT_ASSOCIATION_AUTHORITY_UNAVAILABLE", stale.Error?.Code);
        Assert.Equal(beforeStale, await db.Set<ApplicationCandidateRevisionRecord>().CountAsync());
    }

    [Fact]
    public async Task Gateway_intent_update_denies_a_grant_without_both_read_and_author()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Read]);
        await ExpandGrantBudgetAsync(db);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
            new OperationLog(db), preparation: null, manuals: Manuals(db, setup, policy));
        var associations = new IntentMatchAssociationService(setup.Activation,
            setup.Activation, setup.Resolver, policy, authoring);
        var gateway = new ApplicationCandidateCapabilityGateway(
            IntentUpdateCatalog(db, setup, authoring, associations));
        var target = await CurrentProcedureAsync(setup, "gateway-denied-current");

        var denied = await gateway.InvokeAsync(
            AssociationHost(setup, "gateway-denied-principal").Principal, Application,
            SystemCapabilityIds.ApplicationCandidateIntentUpdate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = (string?)null,
                expectedCandidateRevision = 0,
                target = new
                {
                    definitionId = target.DefinitionId,
                    kind = target.Kind,
                    revision = target.Revision,
                    contentFingerprint = target.ContentFingerprint
                },
                matchPhrases = new[] { "denied phrase" }
            }), "intent-gateway-denied", "website");

        Assert.False(denied.Ok);
        Assert.Equal("STANDING_GRANT_DENIED", denied.Error?.Code);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Match_association_create_disable_and_reenable_use_versioned_activation_and_refresh_discovery()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Read, StandingGrantCapability.Author,
            StandingGrantCapability.Validate, StandingGrantCapability.Activate]);
        await ExpandGrantBudgetAsync(db);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var manuals = Manuals(db, setup, policy);
        var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
            new OperationLog(db), preparation: null, manuals: manuals);
        var associations = new IntentMatchAssociationService(setup.Activation,
            setup.Activation, setup.Resolver, policy, authoring);
        var initial = await CurrentProcedureAsync(setup, "intent-current-0");
        var authorityHost = AssociationHost(setup, "intent-authority", InteractionExecutionProfile.Atomic);
        var authorityTarget = Assert.IsType<StandingGrantDefinitionTarget>(
            (await setup.Resolver.ResolveAsync(authorityHost, initial)).Target);
        var readDecision = await policy.EvaluateAsync(authorityHost,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, [authorityTarget], []));
        Assert.True(readDecision.Allowed, readDecision.Code);
        var createRequest = new IntentMatchAssociationWriteRequest(null, 0, initial,
            ["velvet astrolabe", "inspect with velvet"]);

        var created = await associations.WriteAsync(
            AssociationHost(setup, "intent-create", InteractionExecutionProfile.Atomic), createRequest);
        var replayed = await associations.WriteAsync(
            AssociationHost(setup, "intent-create", InteractionExecutionProfile.Atomic), createRequest);
        Assert.True(created.Tag == InteractionInvocationResultTag.Committed,
            created.Code);
        Assert.Equal(created.Receipt, replayed.Receipt);
        var firstCandidate = await LatestCandidateAsync(db);
        Assert.NotNull(await new ApplicationCandidateIntentMatchUpdateReader(db,
            setup.Applications, setup.Activation, setup.Activation).ReadAsync(firstCandidate));
        await ValidateActivateAsync(db, setup, authoring, firstCandidate, "intent-first");

        var firstDiscovery = await DiscoverAsync(db, setup, policy,
            "velvet astrolabe", "discover-first");
        AssertSelected(firstDiscovery, initial.DefinitionId);
        var firstActivation = setup.Activation.Current(Application)!;

        var stale = await associations.WriteAsync(
            AssociationHost(setup, "intent-stale", InteractionExecutionProfile.Atomic),
            new(firstCandidate.CandidateId, firstCandidate.Revision, initial, ["stale phrase"]));
        Assert.NotEqual(InteractionInvocationResultTag.Committed, stale.Tag);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());

        var enabled = await CurrentProcedureAsync(setup, "intent-current-1");
        var disabled = await associations.WriteAsync(
            AssociationHost(setup, "intent-disable", InteractionExecutionProfile.Atomic),
            new(firstCandidate.CandidateId, firstCandidate.Revision, enabled, []));
        Assert.Equal(InteractionInvocationResultTag.Committed, disabled.Tag);
        var disabledCandidate = await LatestCandidateAsync(db);
        Assert.Equal(2, disabledCandidate.Revision);
        await ValidateActivateAsync(db, setup, authoring, disabledCandidate, "intent-second");

        var disabledDiscovery = await DiscoverAsync(db, setup, policy,
            "velvet astrolabe", "discover-disabled");
        AssertNoSelection(disabledDiscovery);
        var disabledActivation = setup.Activation.Current(Application)!;
        Assert.NotEqual(firstActivation.ActivationFingerprint, disabledActivation.ActivationFingerprint);

        var current = await CurrentProcedureAsync(setup, "intent-current-2");
        var reenabled = await associations.WriteAsync(
            AssociationHost(setup, "intent-reenable", InteractionExecutionProfile.Atomic),
            new(disabledCandidate.CandidateId, disabledCandidate.Revision, current,
                ["azure sextant", "velvet astrolabe"]));
        Assert.Equal(InteractionInvocationResultTag.Committed, reenabled.Tag);
        var reenabledCandidate = await LatestCandidateAsync(db);
        Assert.Equal(3, reenabledCandidate.Revision);
        await ValidateActivateAsync(db, setup, authoring, reenabledCandidate, "intent-third");

        var reenabledDiscovery = await DiscoverAsync(db, setup, policy,
            "azure sextant", "discover-reenabled");
        AssertSelected(reenabledDiscovery, initial.DefinitionId);
        var reenabledActivation = setup.Activation.Current(Application)!;
        Assert.NotEqual(disabledActivation.ActivationFingerprint,
            reenabledActivation.ActivationFingerprint);
        Assert.Equal(4, setup.Activation.CurrentChange(Application)!.Revision);
        Assert.Equal(3, await db.Set<ApplicationCandidateRevisionRecord>().CountAsync());
        Assert.Equal(4, await db.Set<ApplicationActivationRevisionRecord>().CountAsync());
        Assert.NotNull(setup.Activation.ReadRevision(Application, 1));
        Assert.NotNull(setup.Activation.ReadRevision(Application, 2));
        Assert.NotNull(setup.Activation.ReadRevision(Application, 3));
    }

    [Fact]
    public async Task Match_only_proof_rejects_changes_outside_the_explicit_section()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Read, StandingGrantCapability.Author,
            StandingGrantCapability.Validate, StandingGrantCapability.Activate]);
        await ExpandGrantBudgetAsync(db);
        var active = setup.Activation.Current(Application)!;
        var changed = ProcedureIntentPhraseEditor.Edit(ProcedureText("Changed outside Matches."),
            ["velvet astrolabe"]);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
            new OperationLog(db), manuals: Manuals(db, setup, policy));
        var write = await authoring.WriteCandidateAsync(
            AssociationHost(setup, "intent-invalid-write", InteractionExecutionProfile.Atomic),
            new(null, 0, active.ActivationFingerprint, "runtime", null,
                "Attempt a mixed procedure edit.",
                [new("file:" + RelativePath, "catalog", RelativePath, "text/markdown", changed)]));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var candidate = await LatestCandidateAsync(db);

        var validation = await authoring.ValidateAsync(
            AssociationHost(setup, "intent-invalid-validate", InteractionExecutionProfile.Atomic), candidate);
        Assert.Equal(InteractionInvocationResultTag.Committed, validation.Tag);
        var row = await db.Set<ApplicationCandidateValidationRecord>().SingleAsync();
        Assert.Equal("unavailable", row.Outcome);
        Assert.NotEqual(ApplicationCandidateIntentMatchUpdateValidation.PreparationVersion,
            row.PreparationVersion);
    }

    [Fact]
    public async Task Match_association_cannot_stage_without_current_author_authority()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Read,
            StandingGrantCapability.Validate, StandingGrantCapability.Activate]);
        await ExpandGrantBudgetAsync(db);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
            new OperationLog(db), manuals: Manuals(db, setup, policy));
        var associations = new IntentMatchAssociationService(setup.Activation,
            setup.Activation, setup.Resolver, policy, authoring);
        var target = await CurrentProcedureAsync(setup, "intent-denied-current");

        var denied = await associations.WriteAsync(
            AssociationHost(setup, "intent-denied", InteractionExecutionProfile.Atomic),
            new(null, 0, target, ["unauthorized phrase"]));

        Assert.Equal(InteractionInvocationResultTag.Failed, denied.Tag);
        Assert.Equal("STANDING_GRANT_DENIED", denied.Code);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Mechanic_match_association_preserves_its_contract_and_javascript_sidecar()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        await ExpandGrantAsync(db, "mechanic-grant",
            [StandingGrantCapability.Read, StandingGrantCapability.Author,
                StandingGrantCapability.Validate, StandingGrantCapability.Activate]);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var manuals = Manuals(db, setup, policy);
        var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
            new OperationLog(db), preparation: null, manuals: manuals);
        var associations = new IntentMatchAssociationService(setup.Activation,
            setup.Activation, setup.Resolver, policy, authoring);
        var resolved = await setup.Resolver.ResolveCurrentAsync(
            AssociationHost(setup, "mechanic-current", grantReference: "mechanic-grant@1"),
            MechanicId, "mechanic");
        var target = Assert.IsType<StandingGrantDefinitionTarget>(resolved.Target);
        var selection = new StandingGrantDefinitionReference(target.DefinitionId,
            target.Kind, target.Revision, target.ContentFingerprint);

        var written = await associations.WriteAsync(
            AssociationHost(setup, "mechanic-intent-write", InteractionExecutionProfile.Atomic,
                "mechanic-grant@1"),
            new(null, 0, selection, ["silver compass"]));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var candidate = await LatestCandidateAsync(db);
        var proof = Assert.IsType<ApplicationCandidateIntentMatchUpdateEvidence>(
            await new ApplicationCandidateIntentMatchUpdateReader(db, setup.Applications,
                setup.Activation, setup.Activation).ReadAsync(candidate));
        Assert.Equal(MechanicId, proof.Successor.DefinitionId);
        Assert.Equal(selection.Revision, proof.Successor.Revision);
        await ValidateActivateAsync(db, setup, authoring, candidate,
            "mechanic-intent", "mechanic-grant@1");

        var discovered = await DiscoverAsync(db, setup, policy,
            "silver compass", "mechanic-discover", "mechanic-grant@1");
        AssertSelected(discovered, MechanicId);
        var current = setup.Activation.Current(Application)!;
        var sidecar = current.Winners.Single(value => value.RelativePath == MechanicSourcePath);
        var sidecarEvidence = setup.Activation.ReadDocumentEvidence(Application,
            current.ActivationRevision, sidecar.LogicalIdentity);
        Assert.Equal(MechanicSource("v1"),
            System.Text.Encoding.UTF8.GetString(sidecarEvidence!.RetainedBytes!));
    }

    private static async Task ExpandGrantBudgetAsync(DantesRoleplayDbContext db)
        => await ExpandGrantAsync(db, "grant", null);

    private static async Task ExpandGrantAsync(DantesRoleplayDbContext db, string grantId,
        IReadOnlyList<StandingGrantCapability>? capabilities)
    {
        var row = await db.Set<StandingGrantRevisionRecord>().SingleAsync(value => value.GrantId == grantId);
        var current = SqliteStandingGrantPolicy.Parse(row);
        var grant = current with
        {
            MaximumOperations = 16,
            Capabilities = capabilities ?? current.Capabilities
        };
        row.MaximumOperations = grant.MaximumOperations;
        row.PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant);
        row.ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static InteractionInvocationHost AssociationHost(SetupState setup, string command,
        InteractionExecutionProfile profile = InteractionExecutionProfile.ReadOnly,
        string grantReference = "grant@1") =>
        InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []),
            grantReference, command, profile,
            new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(1)));

    private static InteractionManualContextService Manuals(DantesRoleplayDbContext db,
        SetupState setup, IStandingGrantPolicy policy)
    {
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
            new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
                setup.Sources, setup.Roots, setup.Extensions),
            new CatalogCursorCodec(new byte[32]), setup.Activation);
        return new(new ProcedureStore(db), new InteractionFeatureRetriever(catalogs),
            policy, setup.Resolver, setup.Activation, ["system"]);
    }

    private static async Task<StandingGrantDefinitionReference> CurrentProcedureAsync(
        SetupState setup, string command)
    {
        var resolved = await setup.Resolver.ResolveCurrentAsync(
            AssociationHost(setup, command), "demo.runtime.inspect", "procedure");
        var target = Assert.IsType<StandingGrantDefinitionTarget>(resolved.Target);
        return new(target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint);
    }

    private static async Task<ApplicationCandidateReference> LatestCandidateAsync(
        DantesRoleplayDbContext db)
    {
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .OrderByDescending(value => value.Revision).FirstAsync();
        return new(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
    }

    private static async Task ValidateActivateAsync(DantesRoleplayDbContext db, SetupState setup,
        SqliteApplicationAuthoringService authoring, ApplicationCandidateReference candidate,
        string commandPrefix, string grantReference = "grant@1")
    {
        var validated = await authoring.ValidateAsync(
            AssociationHost(setup, commandPrefix + "-validate", InteractionExecutionProfile.Atomic,
                grantReference), candidate);
        Assert.Equal(InteractionInvocationResultTag.Committed, validated.Tag);
        var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.CandidateId == candidate.CandidateId
                && value.Revision == candidate.Revision);
        Assert.True(validation.Outcome == "valid", validation.DiagnosticsJson);
        Assert.Equal(ApplicationCandidateIntentMatchUpdateValidation.PreparationVersion,
            validation.PreparationVersion);
        var activated = await authoring.ActivateAsync(
            AssociationHost(setup, commandPrefix + "-activate", InteractionExecutionProfile.Atomic,
                grantReference),
            new(candidate, validation.OperationId));
        Assert.Equal(InteractionInvocationResultTag.Committed, activated.Tag);
        var replay = await authoring.ActivateAsync(
            AssociationHost(setup, commandPrefix + "-activate", InteractionExecutionProfile.Atomic,
                grantReference),
            new(candidate, validation.OperationId));
        Assert.Equal(activated.Receipt, replay.Receipt);
    }

    private static async Task<InteractionInvocationResult> DiscoverAsync(
        DantesRoleplayDbContext db, SetupState setup, IStandingGrantPolicy policy,
        string phrase, string command, string grantReference = "grant@1")
    {
        // A fresh provider and context service model process restart and force reconstruction from
        // the durable current activation rather than an in-memory pre-publication snapshot.
        var service = Manuals(db, setup, policy);
        return await service.DiscoverAsync(new(
            AssociationHost(setup, command, grantReference: grantReference), phrase));
    }

    private static void AssertSelected(InteractionInvocationResult result, string definitionId)
    {
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        using var json = JsonDocument.Parse(result.DataJson!);
        var candidate = Assert.Single(json.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Equal("exact-authored-match", candidate.GetProperty("reason").GetString());
        Assert.Equal(definitionId, candidate.GetProperty("reference")
            .GetProperty("qualifiedId").GetString());
    }

    private static void AssertNoSelection(InteractionInvocationResult result)
    {
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        using var json = JsonDocument.Parse(result.DataJson!);
        Assert.Empty(json.RootElement.GetProperty("candidates").EnumerateArray());
    }

    private static ISystemCapabilityCatalog IntentUpdateCatalog(
        DantesRoleplayDbContext db,
        SetupState setup,
        IApplicationAuthoringService authoring,
        IntentMatchAssociationService associations) => new SystemCapabilityCatalog(
        [new ApplicationCandidateInspectCapabilityHandler(db, setup.Applications, authoring)],
        new BoundedJsonSchemaValidator(),
        new PrivateOperatorAuthorizationPolicy(),
        new ISystemWriteCapabilityHandler[]
        {
            new ApplicationCandidateIntentUpdateCapabilityHandler(db, setup.Applications, associations),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateValidate,
                db, setup.Applications, authoring),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateActivate,
                db, setup.Applications, authoring)
        });
}
