using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.Interactions.Tests;

public sealed class ApplicationOnlyInvocationGuardTests
{
    [Theory]
    [InlineData("legacy-read")]
    [InlineData("standing-read")]
    [InlineData("atomic-action")]
    [InlineData("workflow-action")]
    [InlineData("read-only-service")]
    public async Task Application_host_cannot_enter_state_execution(string route)
    {
        using var fixture = await InteractionInvocationAdapterTests.InvocationFixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var stateHost = fixture.ReadRequest("command.application-scope").Host;
        var profile = route switch
        {
            "atomic-action" => InteractionExecutionProfile.Atomic,
            "workflow-action" => InteractionExecutionProfile.Workflow,
            _ => InteractionExecutionProfile.ReadOnly
        };
        var host = InteractionInvocationHost.ForApplication(stateHost.Principal,
            stateHost.ApplicationRevision, stateHost.GrantReference, stateHost.CommandId,
            profile, new(8, stateHost.Budget.DeadlineUtc));
        var authority = new UnexpectedAuthority();
        var progress = new ApplicationServiceProgressChannel();
        InteractionInvocationResult result;
        switch (route)
        {
            case "legacy-read":
                result = await new ApplicationReadModelInvocationAdapter(
                    authority, fixture.StateSpaces, fixture.ReadModels).ReadAsync(
                    fixture.ReadRequest(host.CommandId) with { Host = host });
                break;
            case "standing-read":
                result = await new StandingGrantApplicationReadModelInvocationAdapter(
                    authority, authority, fixture.StateSpaces, fixture.ReadModels).ReadAsync(
                    fixture.ReadRequest(host.CommandId) with { Host = host });
                break;
            case "atomic-action":
            case "workflow-action":
                result = await new ApplicationActionInvocationAdapter(
                    authority, fixture.StateSpaces, fixture.Runner, fixture.Operations).ExecuteAsync(
                    fixture.ActionRequest(host.CommandId, "{\"value\":9}", profile) with { Host = host });
                break;
            default:
                var stateRequest = fixture.ServiceRequest(host.CommandId);
                result = await fixture.CreateServiceAdapter(authority, authority).InvokeAsync(new(
                    host, stateRequest.SelectedDefinition, stateRequest.Definition,
                    stateRequest.HostRoleBindings, stateRequest.InputJson,
                    stateRequest.ComputationLimits, progress));
                break;
        }

        Assert.Equal(route == "workflow-action" ? "ACTION_PROFILE_UNSUPPORTED" : "INVOCATION_STATE_SCOPE_REQUIRED",
            result.Code);
        Assert.Equal(8, host.Budget.RemainingOperations);
        Assert.Equal(0, authority.Calls);
        Assert.Null(result.DataJson);
        Assert.Null(result.ReadEvidence);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Null(result.Receipt);
        Assert.Null(result.TaskHandle);
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.True(progress.TryBind()); // Rejected services never take ownership of this outlet.
        Assert.False(progress.Reader.TryRead(out _));
        progress.Complete();
    }

    private sealed class UnexpectedAuthority : IInteractionAuthorizationPolicy,
        IStandingGrantPolicy, IStandingGrantTargetResolver
    {
        public int Calls { get; private set; }

        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request)
        {
            Calls++;
            throw new InvalidOperationException("State authority must not receive an application-only invocation.");
        }

        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
            StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Standing authority must not receive this state invocation.");
        }

        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("State target resolution must not run.");
        }

        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
            string exactDefinitionId, string kind, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Current state target resolution must not run.");
        }

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Candidate resolution must not run.");
        }
    }
}
