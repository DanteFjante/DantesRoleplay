using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Procedures;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.Tests;

/// <summary>Slice 2: the local verifier is closed and never grants write authority.</summary>
public sealed class ProcedureBoundActionVerifierTests
{
    [Fact]
    public async Task Verifier_uses_the_dedicated_closed_task_and_accepts_a_valid_verdict()
    {
        var completion = new Completion("""{"status":"ready","reason":"The supplied procedure permits this action.","missingInformation":[]}""");
        var verifier = new ProcedureBoundActionVerifier(completion);

        var result = await verifier.VerifyAsync("Confirm the signal.", Proposal(), 1, [Procedure()], []);

        Assert.True(result.Ready);
        Assert.Equal("story-plan.verify-procedures", completion.Request!.TaskClass);
        Assert.Contains("no tools", completion.Request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("untrusted data", completion.Request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"status":"ready","reason":"Okay.","missingInformation":[],"effect":"invented"}""")]
    [InlineData("""{"status":"ready","status":"blocked","reason":"Okay.","missingInformation":[]}""")]
    [InlineData("""{"status":"invented","reason":"Okay.","missingInformation":[]}""")]
    [InlineData("""{"status":"ready","reason":"Okay.","missingInformation":[1]}""")]
    public async Task Verifier_rejects_malformed_or_invented_model_output(string json)
    {
        var result = await new ProcedureBoundActionVerifier(new Completion(json)).VerifyAsync("Confirm the signal.", Proposal(), 1, [Procedure()], []);
        Assert.False(result.Ready);
        Assert.Equal("blocked", result.Status);
    }

    [Fact]
    public async Task Verifier_maps_local_unavailability_without_permitting_an_action()
    {
        var result = await new ProcedureBoundActionVerifier(new Completion(StructuredCompletionResult.Failure("LOCAL_MODEL_UNAVAILABLE", "offline")))
            .VerifyAsync("Confirm the signal.", Proposal(), 1, [Procedure()], []);

        Assert.False(result.Ready);
        Assert.Equal("STORY_LOCAL_MODEL_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public void Preparation_has_no_action_runner_or_plan_store_dependency()
    {
        var type = typeof(ProcedureBoundActionVerifier).Assembly.GetType("DantesRoleplay.DataAccess.StoryActionStepPreparer")!;
        var dependencies = type.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Single().GetParameters().Select(parameter => parameter.ParameterType.Name);

        Assert.DoesNotContain("IActionRunner", dependencies);
        Assert.DoesNotContain("IStoryPlanActionRunner", dependencies);
        Assert.DoesNotContain("IStoryPlanStore", dependencies);
        Assert.DoesNotContain("DantesRoleplayDbContext", dependencies);
    }

    private static LocalActionProposal Proposal() => new("action", "mechanic.test.action", "confirm signal",
        new Dictionary<string, string> { ["world"] = "world.test" }, "{}", null, ["procedure.test.action"]);

    private static ProcedureDetail Procedure() => new("procedure.test.action", "test", "Test action", "A test contract.",
        "commit(kind: \"action\")", "Follow it.", "No hidden state.", ProcedureStatus.Active, 1, 1, "test", "", DateTime.UtcNow)
    { SourceHash = new string('a', 64) };

    private sealed class Completion : ILocalStructuredCompletionProvider
    {
        private readonly StructuredCompletionResult _result;
        public Completion(string json) : this(new StructuredCompletionResult(new("test", "qwen3:8b", "test"), json, 1)) { }
        public Completion(StructuredCompletionResult result) => _result = result;
        public StructuredCompletionRequest? Request { get; private set; }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(_result);
        }
    }
}
