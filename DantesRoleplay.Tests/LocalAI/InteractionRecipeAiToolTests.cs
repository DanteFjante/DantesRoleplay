using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Tests;

public sealed class InteractionRecipeAiToolTests
{
    [Fact]
    public void Recursive_recipe_work_is_rejected_above_the_closed_step_limit()
    {
        var steps = Enumerable.Range(1, InteractionContractLimits.ProposalSteps + 1)
            .Select(index => new InteractionPlannerDraftStep(
                $"step-{index}",
                InteractionPlanStepKind.Action,
                $"example.step-{index}",
                1,
                new string('A', 64),
                [],
                new Dictionary<string, string>(),
                "{}"))
            .ToArray();

        var exception = Assert.Throws<InteractionContractException>(() =>
            InteractionRecipeTemplate.FromProposal(
                ApplicationIdentifier.Parse("example"),
                new InteractionPlannerProposalCommand(steps)));

        Assert.Equal("INVALID_RECIPE_TEMPLATE", exception.Code);
    }

    [Fact]
    public async Task Verified_recipe_recursively_calls_selected_local_ai_for_dependency_ordered_tasks()
    {
        var provider = new CapturingProvider();
        var ai = new AiService([provider]);
        var source = new InteractionRecipeAiToolSource(new RecipeStore(Recipe()), ai);
        IReadOnlyList<IAiTool> tools = [];
        var context = new SystemAiToolSourceContext(
            new("operator", "Operator", "Operate the application."),
            new("test", "model", [new(AiMessageRole.User, "run recipe")], AiRequestKind.Task),
            new(PrivateOperatorPrincipal.Create("test", "operator"),
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "recipe-ai-test"),
            null,
            null,
            () => tools);
        tools = source.CreateTools(context);
        var run = tools.Single(value => value.Definition.Name == "interaction_recipe_run");

        var result = await run.InvokeAsync(new("recipe-call", run.Definition.Name,
            JsonSerializer.SerializeToElement(new { applicationId = "example", query = "solve fixture" }),
            AiRequestKind.Task));

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains("example.first", provider.Requests[0].Messages.Last().Content, StringComparison.Ordinal);
        Assert.Contains("first result", provider.Requests[1].Messages.Last().Content, StringComparison.Ordinal);
        Assert.Contains("example.second", provider.Requests[1].Messages.Last().Content, StringComparison.Ordinal);
        Assert.All(provider.Requests, request => Assert.DoesNotContain(
            request.Tools, tool => tool.Name == "interaction_recipe_run"));
        Assert.Equal(16, InteractionContractLimits.ProposalSteps);
    }

    private static InteractionRecipeProjection Recipe()
    {
        var application = ApplicationIdentifier.Parse("example");
        var proposal = new InteractionPlannerProposalCommand([
            new("first", InteractionPlanStepKind.Action, "example.first", 1, new string('A', 64),
                [], new Dictionary<string, string>(), "{}"),
            new("second", InteractionPlanStepKind.Action, "example.second", 1, new string('B', 64),
                ["first"], new Dictionary<string, string>(), "{}")
        ]);
        var template = InteractionRecipeTemplate.FromProposal(application, proposal);
        return new(new(InteractionRecipeIds.Create(application, template.Fingerprint), 1, template.Fingerprint),
            application, InteractionRecipeStatus.Verified, template, 1, [], DateTime.UtcNow, DateTime.UtcNow);
    }

    private sealed class CapturingProvider : IAiProvider
    {
        public List<AiProviderRequest> Requests { get; } = [];
        public AiProviderInfo Info { get; } = new("test", "Test");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([Model()]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var text = Requests.Count == 1 ? "first result" : "second result";
            return Task.FromResult(new AiProviderResponse(true, Model(), text, "", []));
        }
        private static AiModel Model() => new("test", "model", "Test",
            AiModelCapabilities.Messages | AiModelCapabilities.Tasks | AiModelCapabilities.Tools, []);
    }

    private sealed class RecipeStore(InteractionRecipeProjection recipe) : IInteractionRecipeStore
    {
        public Task<IReadOnlyList<InteractionRecipeProjection>> SearchAsync(ApplicationIdentifier applicationId,
            string query, InteractionRecipeStatus? status = null, int limit = 20,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InteractionRecipeProjection>>([recipe]);
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
        public Task<InteractionRecipeWriteResult> AppendUseEvidenceAsync(InteractionRecipeUseEvidenceDraft draft,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionRecipeWriteResult> MarkStaleAsync(InteractionRecipeStaleDraft draft,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionRecipeWriteResult?> GetReviewReplayAsync(InteractionRecipeReviewRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult<InteractionRecipeWriteResult?>(null);
    }
}
