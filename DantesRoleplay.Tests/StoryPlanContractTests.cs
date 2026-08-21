using System.Reflection;
using System.Text.Json;
using DantesRoleplay.Story;

namespace DantesRoleplay.Tests;

/// <summary>Slice 1: closed, pure story-plan transport and validation boundaries.</summary>
public sealed class StoryPlanContractTests
{
    [Fact]
    public void Start_parser_rejects_duplicate_and_unknown_properties_at_every_object_level()
    {
        AssertInvalid("""{"operation":"start","operation":"start","requestToken":"story-plan.test-01","campaignId":"campaign.test.story","objective":"Test.","steps":[]}""");
        AssertInvalid("""{"operation":"start","requestToken":"story-plan.test-01","campaignId":"campaign.test.story","objective":"Test.","steps":[{"id":"one","kind":"knowledge","intent":"What?","hidden":true}]}""");
        AssertInvalid("""{"operation":"start","requestToken":"story-plan.test-01","campaignId":"campaign.test.story","objective":"Test.","steps":[{"id":"one","kind":"action","intent":"Do it.","roleEntityIds":{"world":"world.one","world":"world.two"}}]}""");
        AssertInvalid("""{"operation":"start","requestToken":"story-plan.test-01","campaignId":"campaign.test.story","objective":"Test.","steps":[],"mechanicId":"forbidden"}""");
    }

    [Fact]
    public void Start_parser_applies_only_documented_optional_defaults()
    {
        using var document = JsonDocument.Parse("""{"operation":"start","requestToken":"story-plan.test-01","campaignId":"campaign.test.story","objective":"Test.","steps":[{"id":"act","kind":"action","intent":"Do it."}]}""");
        var parsed = StoryPlanJsonParser.TryParseStart(document.RootElement, out var request);
        Assert.True(parsed.Valid, parsed.Problem?.Message);
        Assert.NotNull(request);
        Assert.Empty(request!.Steps[0].RoleEntityIds!);
        Assert.Equal("{}", request.Steps[0].Input);

        using var explicitNull = JsonDocument.Parse("""{"operation":"start","requestToken":"story-plan.test-02","campaignId":"campaign.test.story","objective":"Test.","steps":[{"id":"act","kind":"action","intent":"Do it.","roleEntityIds":null}]}""");
        var nullParsed = StoryPlanJsonParser.TryParseStart(explicitNull.RootElement, out var nullRequest);
        Assert.True(nullParsed.Valid, nullParsed.Problem?.Message);
        Assert.Empty(nullRequest!.Steps[0].RoleEntityIds!);
    }

    [Theory]
    [InlineData("start", "short", "campaign.test.story", "Test.")]
    [InlineData("start", "story-plan.test-01", "Campaign.Test", "Test.")]
    [InlineData("START", "story-plan.test-01", "campaign.test.story", "Test.")]
    [InlineData("start", "story-plan.test-01", "campaign.test.story", " Test.")]
    public void Validator_rejects_start_scalar_boundaries(string operation, string token, string campaign, string objective)
    {
        var request = new StoryPlanStartRequest(operation, token, campaign, objective,
            [new("one", StoryPlanStepKind.Knowledge, "What is known?")]);
        Assert.False(StoryPlanValidator.Validate(request).Valid);
    }

    [Fact]
    public void Validator_enforces_step_and_payload_limits_without_transport_state()
    {
        var tooManySteps = new StoryPlanStartRequest("start", "story-plan.test-01", "campaign.test.story", "Test.",
            Enumerable.Range(0, 7).Select(index => new StoryPlanStepRequest($"step-{index}", StoryPlanStepKind.Knowledge, "What is known?")).ToArray());
        Assert.False(StoryPlanValidator.Validate(tooManySteps).Valid);

        var invalidAction = new StoryPlanStartRequest("start", "story-plan.test-02", "campaign.test.story", "Test.",
            [new("act", StoryPlanStepKind.Action, "Do it.", new Dictionary<string, string> { [" bad"] = "world.one" }, "[]")]);
        Assert.False(StoryPlanValidator.Validate(invalidAction).Valid);

        var oversized = new StoryPlanStartRequest("start", "story-plan.test-03", "campaign.test.story", new string('x', 1_000),
            [new("act", StoryPlanStepKind.Action, "Do it.", null, "{\"note\":\"" + new string('x', 15_500) + "\"}")]);
        Assert.False(StoryPlanValidator.Validate(oversized).Valid);
    }

    [Fact]
    public void Closed_request_contract_cannot_carry_backend_or_mechanic_authority()
    {
        var startNames = typeof(StoryPlanStartRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name).Order().ToArray();
        Assert.Equal(["CampaignId", "Objective", "Operation", "RequestToken", "Steps"], startNames);
        var stepNames = typeof(StoryPlanStepRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name).Order().ToArray();
        Assert.Equal(["Id", "Input", "Intent", "Kind", "RoleEntityIds"], stepNames);
    }

    [Fact]
    public void Cancel_parser_is_closed_and_query_limits_are_pure()
    {
        using var duplicate = JsonDocument.Parse("""{"operation":"cancel","storyPlanId":"story-plan.0123456789abcdef0123456789abcdef","expectedRevision":1,"expectedRevision":2}""");
        Assert.False(StoryPlanJsonParser.TryParseCancel(duplicate.RootElement, out _).Valid);
        Assert.False(StoryPlanValidator.Validate(new StoryPlanQueryRequest("story-plan.0123456789abcdef0123456789abcdef", 0, 21)).Valid);
    }

    private static void AssertInvalid(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(StoryPlanJsonParser.TryParseStart(document.RootElement, out _).Valid);
    }
}
