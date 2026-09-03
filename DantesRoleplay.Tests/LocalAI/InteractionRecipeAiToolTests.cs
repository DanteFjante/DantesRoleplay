using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Tests;

public sealed class InteractionRecipeAiToolTests
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("example");
    private static readonly string CatalogFingerprint = Hash("catalog");

    [Fact]
    public void Mechanic_opportunity_queue_is_a_separate_read_only_ai_tool()
    {
        var fixture = Fixture();
        var source = new InteractionRecipeAiToolSource(fixture.Store, interactions: fixture.Gateway,
            applications: fixture.Applications, snapshots: fixture.Snapshots);

        var tool = Assert.Single(source.CreateTools(fixture.Context), value =>
            value.Definition.Name == "interaction_mechanic_opportunities_list");

        Assert.Contains("inert", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("applicationId", tool.Definition.InputSchemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Recipe_work_is_rejected_above_the_closed_step_limit()
    {
        var steps = Enumerable.Range(1, InteractionContractLimits.ProposalSteps + 1)
            .Select(index => new InteractionPlannerDraftStep(
                $"step-{index}", InteractionPlanStepKind.Action, $"example.step-{index}",
                1, new string('A', 64), [], new Dictionary<string, string>(), "{}"))
            .ToArray();

        var exception = Assert.Throws<InteractionContractException>(() =>
            InteractionRecipeTemplate.FromProposal(App, new InteractionPlannerProposalCommand(steps)));

        Assert.Equal("INVALID_RECIPE_TEMPLATE", exception.Code);
    }

    [Fact]
    public async Task Current_recipe_compiles_and_executes_without_any_ai_call()
    {
        var fixture = Fixture();
        var provider = new CapturingProvider();
        var source = new InteractionRecipeAiToolSource(fixture.Store, new AiService([provider]),
            fixture.Gateway, fixture.Applications, fixture.Snapshots);
        var run = Tool(source, fixture.Context);

        var result = await run.InvokeAsync(new("recipe-call", run.Definition.Name,
            JsonSerializer.SerializeToElement(new
            {
                applicationId = App.Value,
                query = "solve fixture",
                roleBindings = new { actor = "entity.actor" },
                stepInputs = new Dictionary<string, object> { ["step.2"] = new { amount = 2 } }
            }), AiRequestKind.Task));

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Empty(provider.Requests);
        Assert.Equal(1, fixture.Gateway.PlanCalls);
        Assert.Equal(1, fixture.Gateway.ExecuteCalls);
        Assert.True(fixture.Approval.Confirmed);
        using var proposal = JsonDocument.Parse(fixture.Gateway.SubmittedProposal!);
        var steps = proposal.RootElement.GetProperty("steps");
        Assert.Equal("step.1", steps[0].GetProperty("stepId").GetString());
        Assert.Equal("step.1", steps[1].GetProperty("dependsOn")[0].GetString());
        Assert.Equal("entity.actor", steps[0].GetProperty("roleBindings").GetProperty("actor").GetString());
        Assert.Equal(2, steps[1].GetProperty("input").GetProperty("amount").GetInt32());
        using var output = JsonDocument.Parse(result.Content);
        var efficiency = output.RootElement.GetProperty("efficiency");
        Assert.Equal(2, efficiency.GetProperty("baselineAiCalls").GetInt32());
        Assert.Equal(0, efficiency.GetProperty("actualAiCalls").GetInt32());
        Assert.Equal(2, efficiency.GetProperty("savedAiCalls").GetInt32());
        Assert.Equal(0, efficiency.GetProperty("totalTokens").GetInt32());
        Assert.NotNull(fixture.Store.UseEvidence);
        Assert.True(fixture.Store.UseEvidence!.Successful);
        Assert.Equal(2, fixture.Store.UseEvidence.ReplayPerformance!.BaselineAiCalls);
        Assert.Equal(0, fixture.Store.UseEvidence.ReplayPerformance.ActualAiCalls);
        Assert.Equal(2, fixture.Store.UseEvidence.ReplayPerformance.SavedAiCalls);
    }

    [Fact]
    public async Task Missing_role_choices_use_one_read_only_ai_pass_instead_of_one_call_per_step()
    {
        var fixture = Fixture();
        var provider = new CapturingProvider(resolveActor: true);
        var source = new InteractionRecipeAiToolSource(fixture.Store, new AiService([provider]),
            fixture.Gateway, fixture.Applications, fixture.Snapshots);
        var run = Tool(source, fixture.Context);

        var result = await run.InvokeAsync(new("recipe-call", run.Definition.Name,
            JsonSerializer.SerializeToElement(new { applicationId = App.Value, query = "solve fixture" }),
            AiRequestKind.Task));

        Assert.True(result.Ok, result.ErrorMessage);
        var request = Assert.Single(provider.Requests);
        Assert.All(request.Tools, tool => Assert.True(
            tool.Name.StartsWith("read_", StringComparison.Ordinal)
            || tool.Description.StartsWith("Read ", StringComparison.Ordinal)
            || tool.Name == "interaction_recipes_find"));
        Assert.Contains("Missing roles: actor", request.Messages.Last().Content, StringComparison.Ordinal);
        using var output = JsonDocument.Parse(result.Content);
        var efficiency = output.RootElement.GetProperty("efficiency");
        Assert.Equal(1, efficiency.GetProperty("actualAiCalls").GetInt32());
        Assert.Equal(1, efficiency.GetProperty("savedAiCalls").GetInt32());
        Assert.Equal(30, efficiency.GetProperty("promptTokens").GetInt32());
        Assert.Equal(5, efficiency.GetProperty("outputTokens").GetInt32());
        Assert.Equal(1, fixture.Store.UseEvidence!.ReplayPerformance!.ActualAiCalls);
        Assert.Equal(30, fixture.Store.UseEvidence.ReplayPerformance.PromptTokens);
        Assert.Equal(5, fixture.Store.UseEvidence.ReplayPerformance.OutputTokens);
        using var proposal = JsonDocument.Parse(fixture.Gateway.SubmittedProposal!);
        Assert.Equal("entity.resolved", proposal.RootElement.GetProperty("steps")[0]
            .GetProperty("roleBindings").GetProperty("actor").GetString());
    }

    [Fact]
    public async Task Stale_mechanic_is_rejected_before_confirmation_or_partial_execution()
    {
        var fixture = Fixture(staleSecondStep: true);
        var source = new InteractionRecipeAiToolSource(fixture.Store, null,
            fixture.Gateway, fixture.Applications, fixture.Snapshots);
        var run = Tool(source, fixture.Context);

        var result = await run.InvokeAsync(new("recipe-call", run.Definition.Name,
            JsonSerializer.SerializeToElement(new
            {
                applicationId = App.Value,
                query = "solve fixture",
                roleBindings = new { actor = "entity.actor" }
            }), AiRequestKind.Task));

        Assert.False(result.Ok);
        Assert.Equal("INTERACTION_RECIPE_STALE", result.ErrorCode);
        Assert.Equal(0, fixture.Gateway.PlanCalls);
        Assert.Equal(0, fixture.Gateway.ExecuteCalls);
        Assert.False(fixture.Approval.Confirmed);
        Assert.True(fixture.Store.MarkedStale);
    }

    [Fact]
    public async Task Compiled_proposal_does_not_execute_without_the_existing_host_confirmation()
    {
        var fixture = Fixture();
        var source = new InteractionRecipeAiToolSource(fixture.Store, null,
            fixture.Gateway, fixture.Applications, fixture.Snapshots);
        var run = Tool(source, fixture.Context with { ToolApproval = null });

        var result = await run.InvokeAsync(new("recipe-call", run.Definition.Name,
            JsonSerializer.SerializeToElement(new
            {
                applicationId = App.Value,
                query = "solve fixture",
                roleBindings = new { actor = "entity.actor" }
            }), AiRequestKind.Task));

        Assert.False(result.Ok);
        Assert.Equal("AI_TOOL_CONFIRMATION_REQUIRED", result.ErrorCode);
        Assert.Equal(1, fixture.Gateway.PlanCalls);
        Assert.Equal(0, fixture.Gateway.ExecuteCalls);
        Assert.Null(fixture.Store.UseEvidence);
    }

    private static IAiTool Tool(InteractionRecipeAiToolSource source, SystemAiToolSourceContext context) =>
        source.CreateTools(context).Single(value => value.Definition.Name == "interaction_recipe_run");

    private static FixtureData Fixture(bool staleSecondStep = false)
    {
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(App, "Example", "Recipe replay fixture.", []));
        var first = Mechanic("example.first", "First");
        var second = Mechanic("example.second", "Second");
        var proposal = new InteractionPlannerProposalCommand([
            new("first", InteractionPlanStepKind.Action, first.QualifiedId, first.Version,
                first.ContentFingerprint, [], new Dictionary<string, string> { ["actor"] = "slot" }, "{}"),
            new("second", InteractionPlanStepKind.Action, second.QualifiedId, second.Version,
                second.ContentFingerprint, ["first"], new Dictionary<string, string>(), "{}")
        ]);
        var template = InteractionRecipeTemplate.FromProposal(App, proposal);
        var recipe = new InteractionRecipeProjection(
            new(InteractionRecipeIds.Create(App, template.Fingerprint), 1, template.Fingerprint),
            App, InteractionRecipeStatus.Verified, template, 1, [], DateTime.UnixEpoch, DateTime.UnixEpoch,
            revision.Revision, revision.Fingerprint, CatalogFingerprint, [], CatalogFingerprint);
        var records = staleSecondStep ? new[] { first, second with { Version = 2 } } : [first, second];
        var snapshots = new FixedSnapshots(Snapshot(records));
        var store = new RecipeStore(recipe);
        var gateway = new Gateway();
        var approval = new Approval();
        IReadOnlyList<IAiTool> tools = [new ReadTool()];
        var invocation = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope, "recipe-ai-test")
        {
            ApplicationId = App,
            StateSpaceId = "state.1",
            ResolutionFingerprint = CatalogFingerprint
        };
        var context = new SystemAiToolSourceContext(
            new("operator", "Operator", "Operate the application."),
            new("test", "model", [new(AiMessageRole.User, "run recipe")], AiRequestKind.Task),
            invocation, null, approval, () => tools);
        return new(applications, snapshots, store, gateway, approval, context);
    }

    private static CatalogRecordDefinition Mechanic(string id, string name)
    {
        var content = JsonSerializer.Serialize(new
        {
            id,
            category = "fixture",
            name,
            description = name + " step.",
            matches = name.ToLowerInvariant(),
            requirements = id.EndsWith("first", StringComparison.Ordinal)
                ? "{\"roles\":{\"actor\":{\"components\":[]}}}"
                : "{\"roles\":{}}",
            source = "return { effects: [] };",
            scope = "fixture",
            status = "active"
        });
        return new("example", "mechanic", id, name, name + " step.", [], [], "", "active", 1,
            content, Hash(content), "fixture", id + ".json");
    }

    private static ActiveCatalogFeatureSnapshot Snapshot(IReadOnlyList<CatalogRecordDefinition> records)
    {
        var manifest = CatalogNavigationManifest.Create(App, CatalogFingerprint, "catalog-lexical-v1",
            [new("example", "Example", "Recipe replay fixture.")],
            [new("example", "", "Example", "Recipe replay fixture.", CatalogDescriptionStatus.Authored)], records);
        return new(manifest, records.Select(value => new ActiveCatalogFeatureDocument(
            value, SourceTrust.Trusted)).ToArray());
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record FixtureData(
        InMemoryApplicationRegistry Applications,
        FixedSnapshots Snapshots,
        RecipeStore Store,
        Gateway Gateway,
        Approval Approval,
        SystemAiToolSourceContext Context);

    private sealed class FixedSnapshots(ActiveCatalogFeatureSnapshot snapshot)
        : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot value)
        {
            value = snapshot;
            return applicationId == App;
        }
    }

    private sealed class Gateway : IInteractionGateway
    {
        public int PlanCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public string? SubmittedProposal { get; private set; }

        public Task<InteractionPlanGatewayResult> PlanAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, string stateSpaceId, string sessionContextId,
            string intentJson, string? submittedProposalJson = null, string? conversationId = null,
            InteractionAiRole role = InteractionAiRole.Outer, string? parentDelegationId = null,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            SubmittedProposal = submittedProposalJson;
            return Task.FromResult(new InteractionPlanGatewayResult(
                InteractionResolutionStatus.Resolved, "INTERACTION_RESOLVED", "Resolved.", [],
                Hash("proposal"), null, InteractionReceiptWriteResult.Appended(Receipt('a', "resolution")),
                Hash("trace")));
        }

        public Task<InteractionExecutionOutcome> ExecuteAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, string stateSpaceId, string executionRequestJson,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            var actions = new[]
            {
                new ApplicationActionExecutionResult(ApplicationActionExecutionDisposition.Succeeded,
                    "operation.1", "example.first", Hash("first"), 1, "First.", 0, []),
                new ApplicationActionExecutionResult(ApplicationActionExecutionDisposition.Succeeded,
                    "operation.2", "example.second", Hash("second"), 2, "Second.", 0, [])
            };
            return Task.FromResult(new InteractionExecutionOutcome(
                InteractionExecutionReceiptDisposition.Succeeded, "INTERACTION_EXECUTION_SUCCEEDED",
                "Completed.", actions, InteractionReceiptWriteResult.Appended(Receipt('b', "execution")),
                Hash("execution")));
        }

        public Task<InteractionFeatureSearchResult> SearchFeaturesAsync(ApplicationIdentifier applicationId,
            string? query, string? qualifiedId, int limit = 10, string? namespaceId = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InteractionReceiptProjection?> GetReceiptAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, string stateSpaceId, string receiptId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static InteractionReceiptProjection Receipt(char marker, string kind) => new(
            "interaction-receipt." + new string(marker, 32), kind, Principal, App, "state.1",
            "recipe.1", Hash(kind), kind == "execution" ? "succeeded" : "resolved",
            "OK", Hash("proposal"), "Completed.", [], DateTime.UnixEpoch);
    }

    private sealed class Approval : IAiToolApprovalGate
    {
        public bool Confirmed { get; private set; }
        public Task<bool> ConfirmAsync(AiToolApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            Confirmed = true;
            return Task.FromResult(true);
        }
    }

    private sealed class CapturingProvider(bool resolveActor = false) : IAiProvider
    {
        public List<AiProviderRequest> Requests { get; } = [];
        public AiProviderInfo Info { get; } = new("test", "Test");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([Model()]);

        public Task<AiProviderResponse> SendAsync(AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(resolveActor
                ? new AiProviderResponse(true, Model(), "", "{\"roleBindings\":{\"actor\":\"entity.resolved\"}}",
                    [], 30, 5)
                : new AiProviderResponse(true, Model(), "", "{}", []));
        }

        private static AiModel Model() => new("test", "model", "Test",
            AiModelCapabilities.Messages | AiModelCapabilities.Tasks | AiModelCapabilities.Tools
            | AiModelCapabilities.StructuredOutput, []);
    }

    private sealed class ReadTool : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            "read_entities", "Read entities.", "{\"type\":\"object\"}");
        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default) => Task.FromResult(AiToolResult.Success("{}"));
    }

    private sealed class RecipeStore(InteractionRecipeProjection recipe) : IInteractionRecipeStore
    {
        public InteractionRecipeUseEvidenceDraft? UseEvidence { get; private set; }
        public bool MarkedStale { get; private set; }

        public Task<IReadOnlyList<InteractionRecipeProjection>> SearchAsync(ApplicationIdentifier applicationId,
            string query, InteractionRecipeStatus? status = null, int limit = 20,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InteractionRecipeProjection>>([recipe]);
        public Task<InteractionRecipeWriteResult> AppendUseEvidenceAsync(InteractionRecipeUseEvidenceDraft draft,
            CancellationToken cancellationToken = default)
        {
            UseEvidence = draft;
            return Task.FromResult(new InteractionRecipeWriteResult(
                InteractionRecipeWriteDisposition.Created, recipe.Reference, "RECIPE_USE_RECORDED"));
        }
        public Task<InteractionRecipeWriteResult> MarkStaleAsync(InteractionRecipeStaleDraft draft,
            CancellationToken cancellationToken = default)
        {
            MarkedStale = true;
            return Task.FromResult(new InteractionRecipeWriteResult(
                InteractionRecipeWriteDisposition.Created, recipe.Reference, "RECIPE_MARKED_STALE"));
        }
        public Task<InteractionRecipeProjection?> GetAsync(ApplicationIdentifier applicationId, string recipeId,
            CancellationToken cancellationToken = default) => Task.FromResult<InteractionRecipeProjection?>(recipe);
        public Task<IReadOnlyList<InteractionRecipeProjection>> ListAsync(ApplicationIdentifier applicationId,
            InteractionRecipeStatus status, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InteractionRecipeProjection>>([recipe]);
        public Task<InteractionRecipeSearchPage> SearchPageAsync(ApplicationIdentifier applicationId, string query,
            InteractionRecipeStatus? status, int offset, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InteractionRecipeSearchPage([recipe], 1));
        public Task<InteractionRecipeWriteResult> AppendCandidateAsync(InteractionRecipeCandidateDraft draft,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionRecipeWriteResult> ReviewAsync(InteractionRecipeReviewRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionRecipeWriteResult?> GetReviewReplayAsync(InteractionRecipeReviewRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult<InteractionRecipeWriteResult?>(null);
    }
}
