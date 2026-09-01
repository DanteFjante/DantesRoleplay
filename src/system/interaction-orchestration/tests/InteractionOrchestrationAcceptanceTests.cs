using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionOrchestrationAcceptanceTests
{
    [Fact]
    public async Task Gateway_feature_search_carries_the_namespace_filter_to_retrieval()
    {
        var fixture = new Fixture();
        var gateway = fixture.Gateway(new DisabledPlanner(), new RecipeStore(), out _, out _);

        await gateway.SearchFeaturesAsync(fixture.Application, "neutral fixture", null,
            namespaceId: "sample-app.mechanic");

        Assert.Equal("sample-app.mechanic", fixture.Features.LastInput?.NamespaceId);
    }

    [Fact]
    public async Task Direct_role_requires_an_exact_submitted_proposal_and_never_calls_a_planner()
    {
        var fixture = new Fixture();
        var planner = new DisabledPlanner();
        var gateway = fixture.Gateway(planner, new RecipeStore(), out var actions, out _);
        var intent = fixture.Intent("plan.direct", "entity.current-actor", "automatic");
        var proposal = fixture.ProposalJson("entity.current-actor");

        var error = await Assert.ThrowsAsync<InteractionContractException>(() => gateway.PlanAsync(
            fixture.Principal, fixture.Application, fixture.State.StateSpaceId,
            "session.direct", intent, role: InteractionAiRole.Direct));
        Assert.Equal("DIRECT_PROPOSAL_REQUIRED", error.Code);

        var planned = await gateway.PlanAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId, "session.direct", intent,
            proposal, role: InteractionAiRole.Direct);

        Assert.Equal(InteractionResolutionStatus.Resolved, planned.Status);
        Assert.Equal(0, planner.Calls);
        var execution = await gateway.ExecuteAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId,
            ExecutionJson(planned, proposal, intent, "execute.direct", learn: false));
        Assert.True(execution.Successful);
        Assert.Equal(1, actions.Calls);

        var missingRoleProposal = JsonSerializer.Serialize(new
        {
            command = "propose",
            steps = new[]
            {
                new
                {
                    stepId = "step.1", kind = "action", qualifiedId = fixture.Record.QualifiedId,
                    version = fixture.Record.Version, fingerprint = fixture.Record.ContentFingerprint,
                    dependsOn = Array.Empty<string>(), roleBindings = new Dictionary<string, string>(),
                    input = new { }
                }
            }
        });
        var missing = await gateway.PlanAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId, "session.direct",
            fixture.Intent("plan.direct.missing", "entity.current-actor", "automatic"),
            missingRoleProposal, role: InteractionAiRole.Direct);
        Assert.Equal(InteractionResolutionStatus.NeedsInput, missing.Status);
        Assert.Contains("role:actor", missing.Evidence);
        Assert.Equal(0, planner.Calls);

        var impersonating = await gateway.PlanAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId, "session.direct.impersonating",
            fixture.Intent("plan.direct.impersonating", "entity.current-actor", "automatic"),
            fixture.ProposalJson("entity.other-actor"), role: InteractionAiRole.Direct);
        Assert.Equal(InteractionResolutionStatus.Unsafe, impersonating.Status);
        Assert.Equal("ROLE_HINT_BINDING_MISMATCH", impersonating.Code);
        Assert.Null(impersonating.Proposal);
        Assert.Equal(0, planner.Calls);
    }

    [Fact]
    public async Task Remote_closed_proposal_executes_and_learns_without_local_or_vector_services()
    {
        var fixture = new Fixture();
        var recipes = new RecipeStore();
        var disabledPlanner = new DisabledPlanner();
        var gateway = fixture.Gateway(disabledPlanner, recipes, out var actions, out _);
        var intent = fixture.Intent("plan.remote", "entity.current-actor", "remote");
        var proposal = fixture.ProposalJson("entity.current-actor");

        var planned = await gateway.PlanAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId, "session.remote", intent, proposal);

        Assert.Equal(InteractionResolutionStatus.Resolved, planned.Status);
        Assert.Equal(0, disabledPlanner.Calls);
        var execution = await gateway.ExecuteAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId,
            ExecutionJson(planned, proposal, intent, "execute.remote", learn: true));

        Assert.True(execution.Successful);
        Assert.Equal(1, actions.Calls);
        Assert.Equal(InteractionRecipeLearningDisposition.Created, execution.Learning!.Disposition);
        Assert.NotNull(recipes.Candidate);
        Assert.DoesNotContain("entity.current-actor", recipes.Candidate!.Template.CanonicalJson,
            StringComparison.Ordinal);
        Assert.Contains("actor", recipes.Candidate.Template.CanonicalJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verified_recipe_rebinds_current_roles_revalidates_and_records_execution_use()
    {
        var fixture = new Fixture();
        var recipes = new RecipeStore();
        var sourceCommand = fixture.Command("entity.previous-actor");
        var template = InteractionRecipeTemplate.FromProposal(fixture.Application, sourceCommand);
        var reference = new InteractionRecipeReference(
            InteractionRecipeIds.Create(fixture.Application, template.Fingerprint), 2,
            template.Fingerprint);
        recipes.Verified = new(reference, fixture.Application, InteractionRecipeStatus.Verified,
            template, 1, [Hash("evidence")], DateTime.UnixEpoch, DateTime.UnixEpoch,
            fixture.Revision.Revision, fixture.Revision.Fingerprint,
            fixture.Activation.ActivationFingerprint);
        var verifier = fixture.Verifier();
        var resolver = new DantesRoleplay.DataAccess.Composition.VerifiedInteractionRecipeResolver(
            recipes, fixture.Snapshots, verifier);
        var planner = new DantesRoleplay.DataAccess.Composition.InteractionPlanner(
            fixture.Authorization, fixture.Features, fixture.Snapshots, verifier, resolver,
            fixture.Receipts, []);
        var gateway = fixture.Gateway(planner, recipes, out var actions, out var receipts);
        var intent = fixture.Intent("plan.recipe", "entity.current-actor", "automatic");

        var planned = await gateway.PlanAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId, "session.recipe", intent);

        Assert.Equal(InteractionResolutionStatus.Resolved, planned.Status);
        Assert.Equal(reference, planned.RecipeReference);
        Assert.Equal("entity.current-actor", planned.Proposal!.Steps[0].RoleBindings["actor"]);
        var proposal = ProposalJson(planned.Proposal);
        var execution = await gateway.ExecuteAsync(fixture.Principal, fixture.Application,
            fixture.State.StateSpaceId,
            ExecutionJson(planned, proposal, intent, "execute.recipe", learn: false));

        Assert.True(execution.Successful);
        Assert.Equal("entity.current-actor", actions.LastRequest!.RoleEntityIds["actor"]);
        Assert.NotNull(recipes.UseEvidence);
        Assert.Equal(reference, recipes.UseEvidence!.Recipe);
        Assert.True(recipes.UseEvidence.Successful);
        Assert.Equal(receipts.LastResolution!.Id, recipes.UseEvidence.ResolutionReceiptId);
        Assert.Equal(receipts.LastExecution!.Id, recipes.UseEvidence.ExecutionReceiptId);
    }

    private static string ExecutionJson(
        InteractionPlanGatewayResult planned,
        string proposalJson,
        string intentJson,
        string idempotencyKey,
        bool learn)
    {
        using var proposal = JsonDocument.Parse(proposalJson);
        using var intent = JsonDocument.Parse(intentJson);
        return learn
            ? JsonSerializer.Serialize(new
            {
                resolutionReceiptId = planned.Receipt.Receipt!.Id,
                proposalFingerprint = planned.ProposalFingerprint,
                idempotencyKey,
                proposal = proposal.RootElement,
                stopOnFailure = true,
                learn = true,
                learningIntent = intent.RootElement
            })
            : JsonSerializer.Serialize(new
            {
                resolutionReceiptId = planned.Receipt.Receipt!.Id,
                proposalFingerprint = planned.ProposalFingerprint,
                idempotencyKey,
                proposal = proposal.RootElement,
                stopOnFailure = true,
                learn = false
            });
    }

    private static string ProposalJson(InteractionProposalProjection proposal) =>
        JsonSerializer.Serialize(new
        {
            command = proposal.Command,
            steps = proposal.Steps.Select(step => new
            {
                stepId = step.StepId,
                kind = step.Kind,
                qualifiedId = step.QualifiedId,
                version = step.Version,
                fingerprint = step.Fingerprint,
                dependsOn = step.DependsOn,
                roleBindings = step.RoleBindings,
                input = step.Input
            })
        });

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Fixture
    {
        public Fixture()
        {
            Application = ApplicationIdentifier.Parse("sample-app");
            var registry = new InMemoryApplicationRegistry();
            Revision = registry.Register(new(Application, "Sample", "Neutral acceptance fixture.", []));
            Applications = registry;
            var content = JsonSerializer.Serialize(new
            {
                id = "mechanic.fixture",
                description = "Apply one neutral fixture action.",
                requirements = "{\"roles\":{\"actor\":{\"components\":[]}}}",
                source = "return { effects: [] };"
            });
            Record = new(Application.Value, "mechanic", Application.Value + ".mechanic.fixture",
                "Fixture action", "Apply one neutral fixture action.", [], ["apply fixture"],
                "mechanics", "active", 1, content, Hash(content), "trusted-source",
                "mechanics/mechanic.fixture.md");
            var manifest = CatalogNavigationManifest.Create(Application, Hash("catalog"),
                "catalog-lexical-v1", [new(Application.Value, "Sample", "Neutral fixture.")],
                [new(Application.Value, "", "Sample", "Neutral fixture.", CatalogDescriptionStatus.Authored),
                 new(Application.Value, "mechanics", "Mechanics", "Neutral actions.", CatalogDescriptionStatus.Authored)],
                [Record]);
            var snapshot = new ActiveCatalogFeatureSnapshot(manifest,
                [new ActiveCatalogFeatureDocument(Record, SourceTrust.Trusted)]);
            Snapshots = new SnapshotProvider(Application, snapshot);
            Activation = new(Application, 1, Revision.Revision, Revision.Fingerprint,
                Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"),
                Hash("activation"), "coverage-v1", true, [], [], "operation.activation",
                DateTime.UnixEpoch);
            Activations = new ActivationReader(Activation);
            State = new("state.acceptance", Revision, Activation.ActivationFingerprint, 1,
                DateTime.UnixEpoch, DateTime.UnixEpoch);
            StateSpaces = new Spaces(State);
            Principal = PrivateOperatorPrincipal.Create("local-loopback", "acceptance");
            Authorization = new AllowPolicy();
            Features = new LexicalFeatures(InteractionFeatureHit.Create(
                InteractionFeatureReference.Create(Application,
                    InteractionRetrievalLane.TrustedFeature, manifest.Fingerprint, Record),
                Record, 1, null, false));
            Receipts = new ReceiptStore();
        }

        public ApplicationIdentifier Application { get; }
        public ApplicationRevision Revision { get; }
        public InMemoryApplicationRegistry Applications { get; }
        public CatalogRecordDefinition Record { get; }
        public ActiveApplicationManifest Activation { get; }
        public StateSpaceView State { get; }
        public TrustedPrincipalContext Principal { get; }
        public AllowPolicy Authorization { get; }
        public SnapshotProvider Snapshots { get; }
        public ActivationReader Activations { get; }
        public Spaces StateSpaces { get; }
        public LexicalFeatures Features { get; }
        public ReceiptStore Receipts { get; }

        public DantesRoleplay.DataAccess.Composition.InteractionProposalVerifier Verifier() =>
            new(Applications, Activations, Snapshots);

        public IInteractionGateway Gateway(
            IInteractionPlanner planner,
            RecipeStore recipes,
            out Actions actions,
            out ReceiptStore receipts)
        {
            receipts = Receipts;
            actions = new Actions();
            var verifier = Verifier();
            var learner = new InteractionRecipeLearner(recipes);
            var execution = new InteractionExecutionCoordinator(Authorization, receipts, receipts,
                Applications, Activations, StateSpaces, Snapshots, verifier, actions, learner);
            return new InteractionGateway(Features,
                new InteractionEnvelopeFactory(Applications, Activations, StateSpaces, Authorization),
                planner, verifier, Snapshots, receipts, execution);
        }

        public string Intent(string key, string actor, string preference) => JsonSerializer.Serialize(new
        {
            idempotencyKey = key,
            intentText = "Apply the current neutral fixture route.",
            maximumPlanSteps = 1,
            plannerPreference = preference,
            roleHints = new { actor }
        });

        public InteractionPlannerProposalCommand Command(string actor) => new([
            new("step.1", InteractionPlanStepKind.Action, Record.QualifiedId, Record.Version,
                Record.ContentFingerprint, [], new Dictionary<string, string> { ["actor"] = actor }, "{}")]);

        public string ProposalJson(string actor) => JsonSerializer.Serialize(new
        {
            command = "propose",
            steps = new[]
            {
                new
                {
                    stepId = "step.1", kind = "action", qualifiedId = Record.QualifiedId,
                    version = Record.Version, fingerprint = Record.ContentFingerprint,
                    dependsOn = Array.Empty<string>(),
                    roleBindings = new Dictionary<string, string> { ["actor"] = actor },
                    input = new { }
                }
            }
        });
    }

    private sealed class DisabledPlanner : IInteractionPlanner
    {
        public int Calls { get; private set; }
        public Task<InteractionPlanningOutcome> PlanAsync(AuthorizedInteractionEnvelope envelope,
            InteractionAuthorizationRequest authorizationRequest, InteractionPlannerKind plannerKind,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("No model planner is available in this acceptance path.");
        }
    }

    private sealed class AllowPolicy : IInteractionAuthorizationPolicy
    {
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) =>
            InteractionAuthorizationDecision.Allow(request, "acceptance.evidence");
    }

    private sealed class ActivationReader(ActiveApplicationManifest activation) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == activation.ApplicationId ? activation : null;
    }

    private sealed class Spaces(StateSpaceView state) : IStateSpaceRegistry
    {
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == state.StateSpaceId ? state : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId,
            string? afterStateSpaceId, int limit) =>
            applicationId == state.ApplicationRevision.ApplicationId ? new([state], null) : new([], null);
    }

    private sealed class SnapshotProvider(
        ApplicationIdentifier application,
        ActiveCatalogFeatureSnapshot snapshot) : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId,
            out ActiveCatalogFeatureSnapshot value)
        {
            value = snapshot;
            return applicationId == application;
        }
    }

    private sealed class LexicalFeatures(InteractionFeatureHit hit) : IInteractionFeatureRetriever
    {
        public InteractionFeatureSearchInput? LastInput { get; private set; }

        public Task<InteractionFeatureSearchResult> SearchAsync(InteractionFeatureRetrievalScope scope,
            InteractionFeatureSearchInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Lexical, [hit]));
        }
        public Task<InteractionFeatureRebuildResult> RebuildAsync(InteractionFeatureRetrievalScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InteractionFeatureRebuildResult(false, 0));
    }

    private sealed class Actions : IApplicationActionRunner
    {
        public int Calls { get; private set; }
        public ApplicationActionExecutionRequest? LastRequest { get; private set; }
        public Task<ApplicationActionExecutionResult> RunAsync(ApplicationActionExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new ApplicationActionExecutionResult(
                ApplicationActionExecutionDisposition.Succeeded,
                request.ExecutionIdentity.OperationId, request.QualifiedMechanicId,
                request.ContentFingerprint, request.Seed, "Neutral action completed.", 1, []));
        }
    }

    private sealed class ReceiptStore : IInteractionReceiptStore, IInteractionExecutionAuthorityStore
    {
        private int nextId;
        public InteractionReceiptProjection? LastResolution { get; private set; }
        public InteractionReceiptProjection? LastExecution { get; private set; }
        public InteractionResolutionExecutionAuthority? Authority { get; private set; }

        public Task<InteractionReceiptWriteResult> AppendResolutionAsync(
            InteractionResolutionReceiptDraft draft, CancellationToken cancellationToken = default)
        {
            var id = NextId();
            LastResolution = new(id, "resolution", draft.Envelope.Host.Principal.PrincipalId,
                draft.Envelope.Host.ApplicationRevision.ApplicationId,
                draft.Envelope.Host.StateSpaceId, draft.Envelope.Intent.IdempotencyKey,
                draft.Envelope.Fingerprint, InteractionResolutionStatusNames.Get(draft.Result.Status),
                draft.Result.Code, draft.Result.Proposal?.Fingerprint, draft.Result.SafeSummary,
                draft.Result.Evidence, DateTime.UnixEpoch, RecipeReference: draft.Result.RecipeReference);
            if (draft.Result.Proposal is not null)
            {
                var host = draft.Envelope.Host;
                Authority = new(id, host.Principal.PrincipalId, host.ApplicationRevision.ApplicationId,
                    host.ApplicationRevision.Revision, host.ApplicationRevision.Fingerprint,
                    host.StateSpaceId, host.SessionContextId, host.StateRevision,
                    host.EffectiveSetFingerprint, host.RoleProfile.StableKey, host.ConversationId,
                    host.ParentDelegationId, host.Authorization.EvidenceReference,
                    draft.Envelope.Intent.IdempotencyKey, draft.Envelope.Fingerprint,
                    InteractionResolutionStatusNames.Get(draft.Result.Status),
                    draft.Result.Proposal.Fingerprint, draft.Result.RecipeReference);
            }
            return Task.FromResult(InteractionReceiptWriteResult.Appended(LastResolution));
        }

        public Task<InteractionReceiptWriteResult> AppendExecutionAsync(
            InteractionExecutionReceiptDraft draft, CancellationToken cancellationToken = default)
        {
            var status = draft.Disposition.ToString().ToLowerInvariant();
            LastExecution = new(NextId(), "execution", draft.Consent.PrincipalReference,
                draft.Consent.ApplicationId, draft.Consent.StateSpaceId, draft.Consent.IdempotencyKey,
                draft.ExecutionRequestFingerprint, status,
                draft.Disposition == InteractionExecutionReceiptDisposition.Succeeded
                    ? "INTERACTION_EXECUTION_SUCCEEDED" : "INTERACTION_EXECUTION_FAILED",
                draft.Consent.ProposalFingerprint, draft.SafeSummary, draft.Evidence,
                DateTime.UnixEpoch, draft.Consent.ResolutionReceiptId,
                draft.Steps.Select(step => new InteractionExecutionStepReceiptProjection(
                    step.Ordinal, step.ProposalStepId,
                    step.Disposition.ToString().ToLowerInvariant(), step.OperationId)).ToArray());
            return Task.FromResult(InteractionReceiptWriteResult.Appended(LastExecution));
        }

        public Task<InteractionReceiptProjection?> GetAsync(
            InteractionAuthorizationRequest authorizationRequest, string receiptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(receiptId == LastResolution?.Id ? LastResolution
                : receiptId == LastExecution?.Id ? LastExecution : null);

        Task<InteractionResolutionExecutionAuthority?> IInteractionExecutionAuthorityStore.GetAsync(
            InteractionAuthorizationRequest authorizationRequest, string resolutionReceiptId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Authority is not null
                && Authority.ResolutionReceiptId == resolutionReceiptId
                && Authority.PrincipalReference == authorizationRequest.Principal.PrincipalId
                && Authority.ApplicationId == authorizationRequest.ApplicationId
                && Authority.StateSpaceId == authorizationRequest.StateSpaceId
                    ? Authority : null);

        private string NextId() => "interaction-receipt." + (++nextId).ToString("x32");
    }

    private sealed class RecipeStore : IInteractionRecipeStore
    {
        public InteractionRecipeProjection? Verified { get; set; }
        public InteractionRecipeCandidateDraft? Candidate { get; private set; }
        public InteractionRecipeUseEvidenceDraft? UseEvidence { get; private set; }

        public Task<InteractionRecipeWriteResult> AppendCandidateAsync(
            InteractionRecipeCandidateDraft draft, CancellationToken cancellationToken = default)
        {
            Candidate = draft;
            var reference = new InteractionRecipeReference(
                InteractionRecipeIds.Create(draft.ApplicationRevision.ApplicationId,
                    draft.Template.Fingerprint), 1, draft.Template.Fingerprint);
            return Task.FromResult(new InteractionRecipeWriteResult(
                InteractionRecipeWriteDisposition.Created, reference, "RECIPE_CANDIDATE_CREATED"));
        }

        public Task<InteractionRecipeProjection?> GetAsync(ApplicationIdentifier applicationId,
            string recipeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Verified is not null && Verified.ApplicationId == applicationId
                && Verified.Reference.Id == recipeId ? Verified : null);

        public Task<IReadOnlyList<InteractionRecipeProjection>> SearchAsync(
            ApplicationIdentifier applicationId, string query, InteractionRecipeStatus? status = null,
            int limit = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InteractionRecipeProjection>>(
                Verified is not null && Verified.ApplicationId == applicationId
                && status == InteractionRecipeStatus.Verified ? [Verified] : []);

        public Task<InteractionRecipeWriteResult> AppendUseEvidenceAsync(
            InteractionRecipeUseEvidenceDraft draft, CancellationToken cancellationToken = default)
        {
            UseEvidence = draft;
            return Task.FromResult(new InteractionRecipeWriteResult(
                InteractionRecipeWriteDisposition.Created, draft.Recipe, "RECIPE_USE_RECORDED"));
        }

        public Task<InteractionRecipeWriteResult> ReviewAsync(InteractionRecipeReviewRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionRecipeWriteResult> MarkStaleAsync(InteractionRecipeStaleDraft draft,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InteractionRecipeProjection>> ListAsync(ApplicationIdentifier applicationId,
            InteractionRecipeStatus status, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InteractionRecipeProjection>>([]);
        public Task<InteractionRecipeWriteResult?> GetReviewReplayAsync(InteractionRecipeReviewRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<InteractionRecipeWriteResult?>(null);
        public Task<InteractionRecipeSearchPage> SearchPageAsync(ApplicationIdentifier applicationId,
            string query, InteractionRecipeStatus? status, int offset, int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InteractionRecipeSearchPage([], 0));
    }
}
