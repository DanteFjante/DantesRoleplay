using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Hosting;

/// <summary>Turns a unique pinned route into one rechecked, application-scoped grant read.</summary>
internal sealed class WebPermissionedPageRouteAdapter(
    WebPublicationDiscovery discovery,
    WebContentDbContext content,
    IApplicationRegistry applications,
    IStandingGrantReadCandidateReader candidates,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants,
    WebPagePermissionedReader reader)
{
    public Task<WebPermissionedPageRouteResult> ReadPageAsync(TrustedPrincipalContext principal, string slug,
        CancellationToken cancellationToken = default) => ReadAsync(principal, slug, null, cancellationToken);

    public Task<WebPermissionedPageRouteResult> ReadAssetAsync(TrustedPrincipalContext principal, string slug,
        string path, CancellationToken cancellationToken = default) => ReadAsync(principal, slug, path, cancellationToken);

    public Task<WebPermissionedPageRouteResult> ReadVersionedAssetAsync(TrustedPrincipalContext principal, string slug,
        string pageId, int revision, string path, CancellationToken cancellationToken = default) =>
        ReadAsync(principal, slug, path, cancellationToken, pageId, revision);

    private async Task<WebPermissionedPageRouteResult> ReadAsync(TrustedPrincipalContext principal, string slug,
        string? assetPath, CancellationToken cancellationToken, string? expectedPageId = null, int? expectedRevision = null)
    {
        var deadlineUtc = DateTime.UtcNow.AddSeconds(10);
        var budget = new InteractionInvocationBudget(1, deadlineUtc);
        var commandId = Guid.NewGuid().ToString("N");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(deadlineUtc - DateTime.UtcNow);
        var token = deadline.Token;
        var route = await discovery.ResolveHostRouteAsync(slug, token);
        if (route.Status != "candidate" || route.Candidate is not { } candidate) return new(route.Status, null, null);
        if (!principal.Verified || candidate.ActiveContentReference.PageId is not { } pageId
            || candidate.ActiveContentReference.Revision is not { } revision || candidate.ActiveContentReference.ContentHash is not { } hash)
            return new("forbidden", null, null);
        if ((expectedPageId is not null && expectedPageId != pageId) || (expectedRevision is not null && expectedRevision != revision))
            return new("not-found", null, null);
        var application = applications.Get(candidate.ApplicationId);
        if (application is null) return new("application-unavailable", null, null);
        var mapping = await content.Set<WebPageResourceIdentity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ContentPageId == pageId, token);
        if (mapping is null) return new("unavailable", null, null);
        var listed = await candidates.ReadAsync(principal, candidate.ApplicationId, token);
        if (listed.Status != StandingGrantReadCandidateStatus.Available) return new("unavailable", null, null);
        foreach (var grant in listed.Candidates)
        {
            var host = InteractionInvocationHost.ForApplication(principal, application, grant.GrantReference,
                commandId, InteractionExecutionProfile.ReadOnly, budget);
            var resolved = await targets.ResolveAsync(host, new(mapping.QualifiedTargetId, "web-page", revision, hash), token);
            if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is not { } target) continue;
            var decision = await grants.EvaluateAsync(host, new(StandingGrantCapability.Read,
                StandingGrantScope.Application, [target], []), token);
            if (!decision.Allowed) continue;
            var read = assetPath is null
                ? await reader.ReadPageAsync(host, candidate.EntityId, token)
                : await reader.ReadAssetAsync(host, candidate.EntityId, pageId, revision, assetPath, token);
            if (read.Failure is not null) return new(read.Failure.Tag == InteractionInvocationResultTag.Unavailable ? "unavailable" : "forbidden", null, null);
            var final = await discovery.ResolveHostRouteAsync(slug, token);
            if (final.Status != "candidate" || !Same(candidate, final.Candidate)
                || read.Content != candidate.ActiveContentReference || DateTime.UtcNow >= deadlineUtc) return new("unavailable", null, null);
            return new("ready", read.Html, read.Asset);
        }
        return new("forbidden", null, null);
    }

    private static bool Same(WebPageHostRouteCandidate first, WebPageHostRouteCandidate? second) => second is not null
        && first.ApplicationId == second.ApplicationId && first.PublicationStateSpaceId == second.PublicationStateSpaceId
        && first.EntityId == second.EntityId && first.Slug == second.Slug && first.ActiveContentReference == second.ActiveContentReference;
}

internal sealed record WebPermissionedPageRouteResult(string Status, string? Html, WebPageAssetDocument? Asset);
