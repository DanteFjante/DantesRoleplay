using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Tests;

public sealed class StandingGrantContractRulesTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("demo");
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void Candidate_origin_never_authorizes_execution_state_reads_or_task_access()
    {
        var host = Host();
        var target = Target() with
        {
            Candidate = new DantesRoleplay.ApplicationActivation.ApplicationCandidateReference(App, new string('a', 32), 1, Hash)
        };
        foreach (var capability in new[] { StandingGrantCapability.Execute, StandingGrantCapability.Read,
            StandingGrantCapability.ReadTask, StandingGrantCapability.CancelTask })
        {
            var error = Assert.Throws<InteractionContractException>(() => StandingGrantContractRules.ValidateRequirement(host,
                new(capability, StandingGrantScope.StateSpace, [target], [],
                    capability is StandingGrantCapability.ReadTask or StandingGrantCapability.CancelTask ? TaskTarget(host, target) : null)));
            Assert.Equal("STANDING_GRANT_CANDIDATE_SCOPE_DENIED", error.Code);
        }
        foreach (var capability in new[] { StandingGrantCapability.Author, StandingGrantCapability.Validate,
            StandingGrantCapability.Activate, StandingGrantCapability.Read })
            StandingGrantContractRules.ValidateRequirement(host, new(capability, StandingGrantScope.Application, [target], []));
    }

    [Fact]
    public void Issuer_transition_requires_exact_predecessor_and_explicit_revocation()
    {
        var grant = new StandingGrantRevision("grant@1", "grant", 1, Hash, "principal", App,
            StandingGrantScope.Application, null, [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ExactIds, [], []), [], 1, DateTime.UtcNow, false, "operation");
        StandingGrantContractRules.ValidateIssuerTransition(new(StandingGrantIssuerMutation.Issue, 0, grant));
        StandingGrantContractRules.ValidateIssuerTransition(new(StandingGrantIssuerMutation.Replace, 1, grant with { Revision = 2 }));
        StandingGrantContractRules.ValidateIssuerTransition(new(StandingGrantIssuerMutation.Revoke, 1, grant with { Revision = 2, Revoked = true }));
        foreach (var invalid in new StandingGrantIssuerRequirement[]
        {
            new(StandingGrantIssuerMutation.Issue, 1, grant with { Revision = 2 }),
            new(StandingGrantIssuerMutation.Replace, 0, grant),
            new(StandingGrantIssuerMutation.Revoke, 1, grant with { Revision = 2 }),
            new(StandingGrantIssuerMutation.Replace, 1, grant with { Revision = 2, Revoked = true }),
            new(StandingGrantIssuerMutation.Replace, 1, grant with { Revision = 3 }),
            new(StandingGrantIssuerMutation.Revoke, int.MaxValue, grant)
        })
            Assert.Equal("INVALID_STANDING_GRANT_TRANSITION", Assert.Throws<InteractionContractException>(
                () => StandingGrantContractRules.ValidateIssuerTransition(invalid)).Code);
    }

    [Fact]
    public void Exact_empty_denies_and_application_owned_matches_only_explicit_descendants()
    {
        var target = new StandingGrantDefinitionTarget("demo.rules.future", "mechanic", App, "demo.rules", "evidence", 1, Hash);
        var empty = new StandingGrantDefinitionAllowance(StandingGrantDefinitionMode.ExactIds, [], []);
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App, empty, target));
        var direct = new StandingGrantDefinitionAllowance(StandingGrantDefinitionMode.ApplicationOwned, [], [new("demo.rules", false, ["mechanic"])]);
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App, direct, new("demo.rules.child.future", "mechanic", App, "demo.rules.child", "evidence", 1, Hash)));
        var descendants = direct with { ApplicationOwnedNamespaces = [new("demo.rules", true, ["mechanic"])] };
        Assert.True(StandingGrantContractRules.MatchesDefinitionAllowance(App, descendants, new("demo.rules.child.future", "mechanic", App, "demo.rules.child", "evidence", 1, Hash)));
    }

    [Theory]
    [InlineData(StandingGrantCapability.Author, StandingGrantScope.StateSpace)]
    [InlineData(StandingGrantCapability.Execute, StandingGrantScope.Application)]
    [InlineData(StandingGrantCapability.ReadTask, StandingGrantScope.Application)]
    [InlineData(StandingGrantCapability.CancelTask, StandingGrantScope.Application)]
    public void Invalid_capability_scope_is_rejected(StandingGrantCapability capability, StandingGrantScope scope)
    {
        var grant = new StandingGrantRevision("grant", "family", 1, Hash, "principal", App, scope, scope == StandingGrantScope.StateSpace ? "state" : null,
            [capability], new(StandingGrantDefinitionMode.ExactIds, [], []), [], 1, DateTime.UtcNow, false, "operation");
        var error = Assert.Throws<InteractionContractException>(() => StandingGrantContractRules.ValidateConfiguration(grant));
        Assert.Equal("INVALID_STANDING_GRANT_CAPABILITIES", error.Code);
    }

    [Fact]
    public void Matcher_rejects_cross_application_sibling_prefix_and_unknown_kind()
    {
        var allowance = new StandingGrantDefinitionAllowance(StandingGrantDefinitionMode.ApplicationOwned, [], [new("demo.rules", true, ["mechanic"])]);
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App, allowance, new("other.rules.x", "mechanic", ApplicationIdentifier.Parse("other"), "other.rules", "evidence", 1, Hash)));
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App, allowance, new("demo.ruleset.x", "mechanic", App, "demo.ruleset", "evidence", 1, Hash)));
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App, allowance, new("demo.rules.x", "unknown", App, "demo.rules", "evidence", 1, Hash)));
    }

    [Fact]
    public void Mixed_or_empty_application_owned_selectors_are_rejected()
    {
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App,
            new(StandingGrantDefinitionMode.ApplicationOwned, ["demo.rules.x"], [new("demo.rules", false, ["mechanic"])]), new("demo.rules.x", "mechanic", App, "demo.rules", "evidence", 1, Hash)));
        Assert.Throws<InteractionContractException>(() => StandingGrantContractRules.ValidateConfiguration(new("grant", "family", 1, Hash, "principal", App, StandingGrantScope.Application, null,
            [StandingGrantCapability.Read], new(StandingGrantDefinitionMode.ApplicationOwned, [], []), [], 1, DateTime.UtcNow, false, "operation")));
    }

    [Theory]
    [InlineData(StandingGrantCapability.Read, StandingGrantScope.Application)]
    [InlineData(StandingGrantCapability.Read, StandingGrantScope.StateSpace)]
    [InlineData(StandingGrantCapability.ReadTask, StandingGrantScope.StateSpace)]
    [InlineData(StandingGrantCapability.CancelTask, StandingGrantScope.StateSpace)]
    public void Pure_read_and_task_control_shapes_need_no_action_effects(
        StandingGrantCapability capability, StandingGrantScope scope)
    {
        var host = Host();
        var target = Target();
        var task = capability == StandingGrantCapability.Read ? null : TaskTarget(host, target);
        var requirement = new StandingGrantRequirement(capability, scope, [target], [], task);
        StandingGrantContractRules.ValidateRequirement(host, requirement);
        var error = Assert.Throws<InteractionContractException>(() => StandingGrantContractRules.ValidateRequirement(
            host, requirement with { EffectKinds = ["component.set"] }));
        Assert.Equal("INVALID_STANDING_GRANT_EFFECTS", error.Code);
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("application")]
    [InlineData("state")]
    [InlineData("definition")]
    [InlineData("revision")]
    [InlineData("fingerprint")]
    [InlineData("missing-handle")]
    [InlineData("missing-definition")]
    public void Task_control_rejects_mismatched_stored_tuple(string mismatch)
    {
        var host = Host();
        var target = Target();
        var task = TaskTarget(host, target);
        task = mismatch switch
        {
            "principal" => task with { PrincipalReference = "principal." + new string('b', 64) },
            "application" => task with { ApplicationId = ApplicationIdentifier.Parse("other") },
            "state" => task with { StateSpaceId = "other-state" },
            "definition" => task with { SelectedDefinition = new("demo.rules.other", 1, Hash) },
            "revision" => task with { SelectedDefinition = new(target.DefinitionId, 2, Hash) },
            "fingerprint" => task with { SelectedDefinition = new(target.DefinitionId, 1, new string('B', 64)) },
            "missing-handle" => task with { Handle = null! },
            _ => task with { SelectedDefinition = null! }
        };
        foreach (var capability in new[] { StandingGrantCapability.ReadTask, StandingGrantCapability.CancelTask })
        {
            var error = Assert.Throws<InteractionContractException>(() => StandingGrantContractRules.ValidateRequirement(
                host, new(capability, StandingGrantScope.StateSpace, [target], [], task)));
            Assert.Equal("INVALID_STANDING_GRANT_TASK", error.Code);
        }
    }

    [Fact]
    public void Future_identity_requires_explicit_namespace_kind_and_no_system_or_wildcard_expansion()
    {
        var target = Target();
        var allowance = new StandingGrantDefinitionAllowance(StandingGrantDefinitionMode.ApplicationOwned, [],
            [new("demo.rules", false, ["mechanic"])]);
        Assert.True(StandingGrantContractRules.MatchesDefinitionAllowance(App, allowance, target));
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App, allowance, target with { Kind = "query" }));
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(ApplicationIdentifier.System, allowance, target));
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App,
            new(StandingGrantDefinitionMode.ExactIds, ["demo.rules.*"], []), target));
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App, allowance,
            target with { DefinitionId = "demo.rules.child.future" }));
        Assert.False(StandingGrantContractRules.MatchesDefinitionAllowance(App,
            allowance with { ApplicationOwnedNamespaces = [new("demo.rules", false, [])] }, target));
    }

    private static StandingGrantDefinitionTarget Target() =>
        new("demo.rules.future", "mechanic", App, "demo.rules", "owner-evidence", 1, Hash);

    private static InteractionInvocationHost Host() => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new ApplicationRevision(App, 1, Hash, []), "state", "grant@1", "read-command", "state@1",
        InteractionExecutionProfile.ReadOnly, new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1)));

    private static StandingGrantTaskTarget TaskTarget(InteractionInvocationHost host, StandingGrantDefinitionTarget target) =>
        new(new SystemTaskDurableHandle("task", "original-command"), host.Principal.PrincipalId, App, host.StateSpaceId,
            new SystemTaskSelectedDefinition(target.DefinitionId, target.Revision, target.ContentFingerprint));
}
