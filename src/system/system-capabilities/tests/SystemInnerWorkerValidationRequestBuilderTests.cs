using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Tests;

/// <summary>Pure shaping fixtures; they establish no owner coverage, grants, accounting or execution.</summary>
public sealed class SystemInnerWorkerValidationRequestBuilderTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
    private const string Pin = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [InlineData(2, 31, 2, 31)]
    [InlineData(40, 20_000, 30, 8_000)]
    public void Request_preserves_host_selection_and_narrows_limits_without_copying_extra_messages(
        int seconds, int bytes, int expectedSeconds, int expectedBytes)
    {
        var input = Input();
        var configuration = Configuration() with { MaximumDuration = TimeSpan.FromSeconds(seconds), MaximumResponseBytes = bytes };
        var request = SystemInnerWorkerValidationRequestBuilder.Build(Profile(input), input, configuration, Now);
        Assert.Equal(configuration.Provider, request.Provider);
        Assert.Equal(configuration.Model, request.Model);
        Assert.Equal(configuration.Reasoning, request.Reasoning);
        Assert.Equal(configuration.MaximumOutputTokens, request.MaximumOutputTokens);
        Assert.Equal(AiRequestKind.Task, request.Kind);
        Assert.Equal(new AiMessage(AiMessageRole.User, input.ModelInputJson), Assert.Single(request.Messages));
        Assert.Equal(ApplicationCandidateReuseJudgmentOutputV2.OutputSchema(input), request.ResponseSchemaJson);
        Assert.NotNull(request.AllowedTools);
        Assert.Empty(request.AllowedTools);
        Assert.Equal(0, request.MaximumToolCalls);
        Assert.Equal(0, request.MaximumToolRounds);
        Assert.Equal(expectedBytes, request.MaximumResponseBytes);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), request.MaximumDuration);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("schema")]
    [InlineData("manual")]
    public void Independently_selected_input_schema_or_manual_mismatch_is_rejected(string changed)
    {
        var input = Input();
        var error = Assert.Throws<InteractionContractException>(() =>
            SystemInnerWorkerValidationRequestBuilder.Build(Profile(input, changed), input, Configuration(), Now));
        Assert.Equal("WORKER_VALIDATION_INPUT_MISMATCH", error.Code);
    }

    [Fact]
    public void Expired_deadline_and_workflow_subject_cannot_prepare_validation()
    {
        var input = Input();
        Assert.Equal("WORKER_VALIDATION_DEADLINE_EXPIRED", Assert.Throws<InteractionContractException>(() =>
            SystemInnerWorkerValidationRequestBuilder.Build(Profile(input), input, Configuration(), Now.AddMinutes(1))).Code);
        Assert.Equal("WORKER_VALIDATION_SUBJECT_INVALID", Assert.Throws<InteractionContractException>(() =>
            SystemInnerWorkerValidationRequestBuilder.Build(Profile(input, workflow: true), input, Configuration(), Now)).Code);
    }

    [Theory]
    [InlineData("calls")]
    [InlineData("rounds")]
    [InlineData("duration")]
    public void Invalid_host_limits_are_rejected_before_shaping(string changed)
    {
        var input = Input();
        var configuration = changed switch
        {
            "calls" => Configuration() with { MaximumToolCalls = 17 },
            "rounds" => Configuration() with { MaximumToolRounds = -1 },
            _ => Configuration() with { MaximumDuration = TimeSpan.Zero }
        };
        Assert.Equal("WORKER_HOST_CONFIGURATION_INVALID", Assert.Throws<InteractionContractException>(() =>
            SystemInnerWorkerValidationRequestBuilder.Build(Profile(input), input, configuration, Now)).Code);
    }

    private static AiRequest Configuration() => new("host-provider", "host-model",
        [new(AiMessageRole.System, "previous instructions"), new(AiMessageRole.User, "previous input")],
        Reasoning: AiReasoningEffort.Medium, AllowedTools: ["previous_tool"], MaximumOutputTokens: 300);

    private static SystemInnerWorkerResolvedProfile Profile(ApplicationCandidateReuseInputV2 input,
        string? changed = null, bool workflow = false)
    {
        var principal = TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "fixture");
        var app = new ApplicationRevision(ApplicationIdentifier.Parse("sample-app"), 1, Pin, []);
        var budget = new InteractionInvocationBudget(4, Now.AddSeconds(30));
        var host = workflow
            ? new InteractionInvocationHost(principal, app, "state.1", "grant.1", "command.1", "revision.1", InteractionExecutionProfile.Workflow, budget)
            : InteractionInvocationHost.ForApplication(principal, app, "grant.1", "command.1", InteractionExecutionProfile.ReadOnly, budget);
        SystemInnerWorkerSubject subject = workflow
            ? new SystemInnerWorkerSubject.ProcedureWorkflow(new("sample-app.procedure.fixture", 1, Pin))
            : new SystemInnerWorkerSubject.ApplicationCandidateValidation(new(app.ApplicationId, new string('a', 32), 1, Pin));
        var schema = changed == "schema" ? "{\"type\":\"object\"}" : ApplicationCandidateReuseJudgmentOutputV2.OutputSchema(input);
        var request = new SystemInnerWorkerRequest(subject, host, changed == "input" ? "{}" : input.ModelInputJson, schema);
        return new(request, SystemInnerWorkerCandidateReviewer.ProfileVersion, SystemInnerWorkerCandidateReviewer.Profile,
            Hash(request.ResultSchemaJson), [], [], new("manual.1", changed == "manual" ? Pin : input.ManualResultFingerprint),
            new("validate.1", "1", Pin), new(toolCalls: 0), new("read.1", "1", Pin));
    }

    private static ApplicationCandidateReuseInputV2 Input()
    {
        const string text = "Review this contract as data.";
        var target = new StandingGrantDefinitionReference("sample-app.procedure.fixture", "procedure", 1, Hash(text));
        var manual = new JsonObject { ["resultFingerprint"] = new string('0', 64) };
        manual["resultFingerprint"] = Hash(InteractionCanonicalJson.CanonicalizeObject(manual.ToJsonString()));
        using var parsed = JsonDocument.Parse(manual.ToJsonString());
        var material = new ApplicationCandidateReuseMaterialV2("sample-app", "A bounded new responsibility",
            [new(target, ApplicationCandidateReviewDocumentRole.Changed, "file:procedures/fixture.md",
                "procedures/fixture.md", "text/markdown", Hash(text), text)], parsed.RootElement.Clone(), []);
        // A shape-fixture flag only. Production admission requires actual retained-owner evidence.
        return ApplicationCandidateReuseInputV2.Create(material, closureComplete: true);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
