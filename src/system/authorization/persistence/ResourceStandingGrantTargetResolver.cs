using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Authorization;

/// <summary>
/// Routes explicitly supported retained resources to their owner while preserving the existing
/// catalog resolver as the sole owner of catalog, candidate and historical definition resolution.
/// </summary>
public sealed class ResourceStandingGrantTargetResolver : IStandingGrantTargetResolver
{
    private const string WebPageKind = "web-page";
    private readonly IStandingGrantTargetResolver _definitions;
    private readonly IReadOnlyDictionary<string, IStandingGrantResourceTargetOwner> _owners;

    public ResourceStandingGrantTargetResolver(
        IStandingGrantTargetResolver definitions,
        IEnumerable<IStandingGrantResourceTargetOwner> owners)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        ArgumentNullException.ThrowIfNull(owners);

        // Version one deliberately admits one resource kind. Taking only two entries bounds even
        // an adversarial enumerable while still detecting every possible duplicate/unsupported set.
        var supplied = owners.Take(2).ToArray();
        var copied = new Dictionary<string, IStandingGrantResourceTargetOwner>(StringComparer.Ordinal);
        foreach (var owner in supplied)
        {
            if (owner is null)
                throw new ArgumentException("Resource target owners cannot contain null entries.", nameof(owners));
            if (owner.Kind != WebPageKind)
                throw new ArgumentException("The resource target owner kind is not supported.", nameof(owners));
            if (!copied.TryAdd(owner.Kind, owner))
                throw new ArgumentException("Resource target owner kinds must be unique.", nameof(owners));
        }
        _owners = copied;
    }

    public Task<StandingGrantTargetResolution> ResolveAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Kind == WebPageKind
            ? ResolveResourceAsync(host, selection, cancellationToken)
            : _definitions.ResolveAsync(host, selection, cancellationToken);
    }

    public Task<StandingGrantTargetResolution> ResolveCurrentAsync(
        InteractionInvocationHost host,
        string exactDefinitionId,
        string kind,
        CancellationToken cancellationToken = default) =>
        kind == WebPageKind
            ? ResolveCurrentResourceAsync(host, exactDefinitionId, kind, cancellationToken)
            : _definitions.ResolveCurrentAsync(host, exactDefinitionId, kind, cancellationToken);

    public Task<StandingGrantTargetResolution> ResolveCandidateAsync(
        InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Kind == WebPageKind
            ? Task.FromResult(Unavailable("STANDING_GRANT_RESOURCE_CANDIDATE_UNAVAILABLE"))
            : _definitions.ResolveCandidateAsync(host, candidate, selection, cancellationToken);
    }

    public Task<StandingGrantTargetResolution> ResolveRetainedAsync(
        InteractionInvocationHost host,
        StandingGrantActivationOrigin origin,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Kind == WebPageKind
            ? Task.FromResult(Unavailable("STANDING_GRANT_RESOURCE_RETAINED_UNAVAILABLE"))
            : _definitions.ResolveRetainedAsync(host, origin, selection, cancellationToken);
    }

    public async Task<StandingGrantTargetResolution> RevalidateAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind != WebPageKind)
            return await _definitions.RevalidateAsync(host, target, cancellationToken);
        if (target.Candidate is not null || target.RetainedActivation is not null)
            return Denied("STANDING_GRANT_RESOURCE_OWNER_EVIDENCE_INVALID");

        var selection = new StandingGrantDefinitionReference(
            target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint);
        var resolved = await ResolveResourceAsync(host, selection, cancellationToken);
        return resolved.Status == StandingGrantTargetResolutionStatus.Available && resolved.Target != target
            ? Denied("STANDING_GRANT_RESOURCE_OWNER_EVIDENCE_STALE")
            : resolved;
    }

    private async Task<StandingGrantTargetResolution> ResolveResourceAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!_owners.TryGetValue(selection.Kind, out var owner))
            return Unavailable("STANDING_GRANT_RESOURCE_OWNER_UNAVAILABLE");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await owner.ResolveAsync(host, selection, cancellationToken);
            return ValidateOwnerResult(result, host, selection.DefinitionId, selection.Kind,
                selection.Revision, selection.ContentFingerprint);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable("STANDING_GRANT_RESOURCE_OWNER_UNAVAILABLE"); }
    }

    private async Task<StandingGrantTargetResolution> ResolveCurrentResourceAsync(
        InteractionInvocationHost host,
        string exactDefinitionId,
        string kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!_owners.TryGetValue(kind, out var owner))
            return Unavailable("STANDING_GRANT_RESOURCE_OWNER_UNAVAILABLE");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await owner.ResolveCurrentAsync(host, exactDefinitionId, cancellationToken);
            return ValidateOwnerResult(result, host, exactDefinitionId, kind, null, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable("STANDING_GRANT_RESOURCE_OWNER_UNAVAILABLE"); }
    }

    private static StandingGrantTargetResolution ValidateOwnerResult(
        StandingGrantTargetResolution? result,
        InteractionInvocationHost host,
        string exactDefinitionId,
        string kind,
        int? revision,
        string? fingerprint)
    {
        if (result is null || !Enum.IsDefined(result.Status)
            || result.CurrentActivation is not null
            || (result.Status == StandingGrantTargetResolutionStatus.Available) != (result.Target is not null))
            return Denied("STANDING_GRANT_RESOURCE_OWNER_EVIDENCE_INVALID");
        if (result.Target is not { } target)
            return result;
        if (target.Candidate is not null || target.RetainedActivation is not null
            || target.DefinitionId != exactDefinitionId || target.Kind != kind
            || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId
            || target.Revision < 1 || !UpperSha256(target.ContentFingerprint)
            || revision.HasValue && target.Revision != revision.Value
            || fingerprint is not null && target.ContentFingerprint != fingerprint)
            return Denied("STANDING_GRANT_RESOURCE_OWNER_EVIDENCE_INVALID");
        return result;
    }

    private static bool UpperSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static StandingGrantTargetResolution Denied(string code) =>
        new(StandingGrantTargetResolutionStatus.Denied, code, null);

    private static StandingGrantTargetResolution Unavailable(string code) =>
        new(StandingGrantTargetResolutionStatus.Unavailable, code, null);
}
