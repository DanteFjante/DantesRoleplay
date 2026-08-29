using DantesRoleplay.Information;

namespace DantesRoleplay.MCPServer;

/// <summary>One fixed loopback development namespace selector; replace with identity-backed policy before publishing.</summary>
public sealed class DevelopmentInformationScopePolicy(string scopeSelector) : IInformationScopePolicy
{
    private readonly string _scopeSelector = scopeSelector;

    public Task<InformationScopeResolution> ResolveAsync(string scopeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InformationScopes.Contains(_scopeSelector, scopeId)
            ? new InformationScopeResolution(true, _scopeSelector, "development-static-v2")
            : InformationScopeResolution.Denied(scopeId));
}
