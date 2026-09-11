using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.ApplicationActivation;
using System.Text.Json;

namespace DantesRoleplay.Tests;

public sealed class SystemInnerWorkerContractTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Candidate_subject_has_no_executable_definition_and_requires_its_application_scope()
    {
        var subject = CandidateSubject();
        var request = new SystemInnerWorkerRequest(ApplicationHost(), subject, "{}", "{}");
        Assert.Same(subject, request.Subject);
        Assert.Null(request.ProcedureVersion);
        Assert.Null(request.InvocationHost.StateSpaceId);
        Assert.Null(request.InvocationHost.StateRevision);
        Assert.Equal("WORKER_VALIDATION_SCOPE_INVALID", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerRequest(Host(), subject, "{}", "{}")).Code);
        Assert.Equal("WORKER_VALIDATION_SCOPE_INVALID", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerRequest(ApplicationHost("other-app"), subject, "{}", "{}")).Code);
        Assert.Equal("WORKER_VALIDATION_SCOPE_INVALID", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerRequest(ApplicationHost(profile: InteractionExecutionProfile.Workflow), subject, "{}", "{}")).Code);
    }

    [Fact]
    public void Legacy_constructor_preserves_exact_procedure_and_rejects_absent_state()
    {
        var procedure = new SystemTaskSelectedDefinition("procedure.fixture", 1, Hash);
        var legacy = new SystemInnerWorkerRequest(Host(), procedure, "{}", "{}");
        Assert.Same(procedure, legacy.ProcedureVersion);
        Assert.Same(procedure, Assert.IsType<SystemInnerWorkerSubject.ProcedureWorkflow>(legacy.Subject).ProcedureVersion);
        Assert.Equal("WORKER_STATE_SCOPE_REQUIRED", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerRequest(ApplicationHost(), procedure, "{}", "{}")).Code);
    }

    [Theory]
    [InlineData("short", 1, false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 1, false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0, false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1, true)]
    public void Candidate_subject_rejects_inexact_tuple(string id, int revision, bool badHash) =>
        Assert.Throws<InteractionContractException>(() => new SystemInnerWorkerSubject.ApplicationCandidateValidation(
            new(ApplicationIdentifier.Parse("fixture-app"), id, revision, badHash ? "bad-hash" : Hash)));

    [Fact]
    public void Both_subject_variants_reject_json_through_base_and_concrete_types()
    {
        var workflow = new SystemInnerWorkerSubject.ProcedureWorkflow(new("procedure.fixture", 1, Hash));
        var candidate = CandidateSubject();
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<SystemInnerWorkerSubject>(workflow));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<SystemInnerWorkerSubject>(candidate));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(workflow));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(candidate));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemInnerWorkerSubject>("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemInnerWorkerSubject.ProcedureWorkflow>("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemInnerWorkerSubject.ApplicationCandidateValidation>("{}"));
    }

    private static SystemInnerWorkerSubject.ApplicationCandidateValidation CandidateSubject() => new(
        new ApplicationCandidateReference(ApplicationIdentifier.Parse("fixture-app"), new string('a', 32), 1, Hash));

    private static InteractionInvocationHost ApplicationHost(string application = "fixture-app",
        InteractionExecutionProfile profile = InteractionExecutionProfile.ReadOnly) => InteractionInvocationHost.ForApplication(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse(application), 1, Hash, []), "grant.1", "command.1", profile,
        new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1)));

    [Fact]
    public async Task Unavailable_inner_worker_never_claims_provider_execution_or_durability()
    {
        var service = new UnavailableSystemInnerWorkerService();
        var result = await service.SubmitAsync(new SystemInnerWorkerRequest(Host(), new("procedure.fixture", 1, Hash),
            "{\"work\":true}", "{\"type\":\"object\"}"));

        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Null(result.TaskHandle);
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task Cancellation_is_reported_without_starting_worker_execution()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new UnavailableSystemInnerWorkerService().SubmitAsync(
            new SystemInnerWorkerRequest(Host(), new("procedure.fixture", 1, Hash), "{}", "{\"type\":\"object\"}"), cancellation.Token);

        Assert.Equal(InteractionInvocationResultTag.Cancelled, result.Tag);
    }

    private static InteractionInvocationHost Host() => new(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
        "state.1", "grant.1", "command.1", "revision.1", InteractionExecutionProfile.Workflow,
        new InteractionInvocationBudget(2, new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc)));
}
