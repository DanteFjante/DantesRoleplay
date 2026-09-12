using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Authorization.Tests;

public sealed class StandingGrantAuthoredResourceKindTests
{
    [Theory]
    [InlineData(CatalogNamespaceKinds.InformationSource)]
    [InlineData(CatalogNamespaceKinds.WebPage)]
    public void Resource_kinds_have_only_their_declared_application_capabilities(string kind)
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var hash = new string('A', 64);
        var host = new InteractionInvocationHost(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new(app, 1, hash, []), "state", "grant", "command", "state-revision", InteractionExecutionProfile.ReadOnly,
            new(1, DateTime.UtcNow.AddMinutes(1)));
        var target = new StandingGrantDefinitionTarget("fixture.content.entry", kind, app, "fixture.content", "owner", 1, hash);
        Assert.Contains(kind, CatalogNamespaceKinds.All);
        foreach (var capability in Enum.GetValues<StandingGrantCapability>())
        foreach (var scope in Enum.GetValues<StandingGrantScope>())
        {
            var allowed = scope == StandingGrantScope.Application && (capability is StandingGrantCapability.Read or StandingGrantCapability.Author
                || kind == CatalogNamespaceKinds.WebPage && capability is StandingGrantCapability.Validate or StandingGrantCapability.Activate);
            var requirement = new StandingGrantRequirement(capability, scope, [target], []);
            if (allowed) StandingGrantContractRules.ValidateRequirement(host, requirement);
            else Assert.Throws<InteractionContractException>(() => StandingGrantContractRules.ValidateRequirement(host, requirement));
        }
        Assert.Equal(7, StandingGrantLimits.DefinitionKindsPerNamespace);
    }
}
