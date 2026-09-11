using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Tests;

public sealed class SystemInnerWorkerContractTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
