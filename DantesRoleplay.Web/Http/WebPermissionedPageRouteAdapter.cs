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
    WebPagePermissionedReader reader,
    WebPagePublicationService publication,
    CompositionPageBindingCoordinator bindings)
{
    public Task<WebPermissionedPageRouteResult> ReadPageAsync(TrustedPrincipalContext principal, string slug,
        CancellationToken cancellationToken = default) => ReadAsync(principal, slug, null, cancellationToken);

    public Task<WebPermissionedPageRouteResult> ReadAssetAsync(TrustedPrincipalContext principal, string slug,
        string path, CancellationToken cancellationToken = default) => ReadAsync(principal, slug, path, cancellationToken);

    public Task<WebPermissionedPageRouteResult> ReadVersionedAssetAsync(TrustedPrincipalContext principal, string slug,
        string pageId, int revision, string path, CancellationToken cancellationToken = default) =>
        ReadAsync(principal, slug, path, cancellationToken, pageId, revision);

    public async Task<InteractionInvocationResult> InvokeActionAsync(TrustedPrincipalContext principal,
        string slug, string bindingName, string commandId, string inputJson,
        CancellationToken cancellationToken = default)
    {
        var deadlineUtc = DateTime.UtcNow.AddSeconds(10);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(deadlineUtc - DateTime.UtcNow);
        var token = deadline.Token;
        try
        {
            var route = await discovery.ResolveHostRouteAsync(slug, token);
            if (route.Status != "candidate" || route.Candidate is not { } candidate || !principal.Verified)
                return InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "The page action is not authorized.");
            var application = applications.Get(candidate.ApplicationId);
            if (application is null) return UnavailableAction();
            var selected = await publication.SelectPublishedAsync(candidate.ApplicationId, candidate.EntityId, token);
            if (selected.Content != candidate.ActiveContentReference) return UnavailableAction();
            var retained = await publication.RevalidateSelectionAsync(selected, token);
            if (retained.ContentFormat != WebPageContentFormat.Composition)
                return InteractionInvocationResult.Failed("COMPOSITION_ACTION_BINDING_UNKNOWN", "The selected page has no actions.");
            var parsed = new WebCompositionParser().Parse(retained.CompositionJson!, retained.Assets.Select(asset => asset.Path));
            if (!parsed.IsValid) return UnavailableAction();

            var mapping = await content.Set<WebPageResourceIdentity>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.ContentPageId == selected.Content.PageId, token);
            if (mapping is null) return UnavailableAction();
            var listed = await candidates.ReadAsync(principal, candidate.ApplicationId, token);
            if (listed.Status != StandingGrantReadCandidateStatus.Available) return UnavailableAction();
            foreach (var grant in listed.Candidates)
            {
                var pageHost = InteractionInvocationHost.ForApplication(principal, application, grant.GrantReference,
                    Guid.NewGuid().ToString("N") + ".page", InteractionExecutionProfile.ReadOnly,
                    new InteractionInvocationBudget(1, deadlineUtc));
                var resolved = await targets.ResolveAsync(pageHost,
                    new(mapping.QualifiedTargetId, "web-page", selected.Content.Revision!.Value,
                        selected.Content.ContentHash!), token);
                if (resolved is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }
                    || target.DefinitionId != mapping.QualifiedTargetId || target.Kind != "web-page"
                    || target.OwnerApplicationId != application.ApplicationId
                    || target.Revision != selected.Content.Revision
                    || target.ContentFingerprint != selected.Content.ContentHash
                    || target.Candidate is not null || target.RetainedActivation is not null)
                    continue;
                var requirement = new StandingGrantRequirement(StandingGrantCapability.Read,
                    StandingGrantScope.Application, [target], []);
                var initial = await grants.EvaluateAsync(pageHost, requirement, token);
                if (!initial.Allowed || initial.Grant is null) continue;
                var result = await bindings.InvokeActionAsync(selected, parsed.Document!, principal,
                    bindingName, commandId, inputJson, deadlineUtc, token);
                var final = await grants.EvaluateAsync(pageHost, requirement, token);
                var current = await discovery.ResolveHostRouteAsync(slug, token);
                _ = await publication.RevalidateSelectionAsync(selected, token);
                if (!final.Allowed || final.Grant is null
                    || final.Grant.GrantReference != initial.Grant.GrantReference
                    || final.Grant.GrantId != initial.Grant.GrantId
                    || final.Grant.Revision != initial.Grant.Revision
                    || final.Grant.ContentFingerprint != initial.Grant.ContentFingerprint
                    || current.Status != "candidate" || !Same(candidate, current.Candidate))
                    return result.Receipt is not null || result.PreviousCommits.Count != 0
                        ? result
                        : InteractionInvocationResult.Failed("INVOCATION_AUTHORITY_CHANGED",
                            "Page action authority changed before the result could be returned.");
                return result;
            }
            return InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "The page action is not authorized.");
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled("INVOCATION_CANCELLED", "The page action was cancelled.");
        }
        catch (Exception exception) when (exception is WebPageStoreException or ArgumentException
            or InteractionContractException)
        {
            return UnavailableAction();
        }
    }

    private async Task<WebPermissionedPageRouteResult> ReadAsync(TrustedPrincipalContext principal, string slug,
        string? assetPath, CancellationToken cancellationToken, string? expectedPageId = null, int? expectedRevision = null)
    {
        var deadlineUtc = DateTime.UtcNow.AddSeconds(10);
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
        var operations = 1;
        if (assetPath is null)
        {
            try
            {
                var selected = await publication.SelectPublishedAsync(candidate.ApplicationId, candidate.EntityId, token);
                if (selected.Content != candidate.ActiveContentReference) return new("unavailable", null, null);
                var retained = await publication.RevalidateSelectionAsync(selected, token);
                if (retained.ContentFormat == WebPageContentFormat.Composition)
                {
                    var parsed = new WebCompositionParser().Parse(retained.CompositionJson!, retained.Assets.Select(asset => asset.Path));
                    if (!parsed.IsValid || parsed.Document!.QueryBindings.Count > 15)
                        return new("unavailable", null, null);
                    operations += parsed.Document.QueryBindings.Count;
                }
            }
            catch (WebPageStoreException) { return new("unavailable", null, null); }
        }
        var budget = new InteractionInvocationBudget(operations, deadlineUtc);
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

    private static InteractionInvocationResult UnavailableAction() =>
        InteractionInvocationResult.Unavailable("COMPOSITION_ACTION_UNAVAILABLE", "The page action is unavailable.");
}

internal sealed record WebPermissionedPageRouteResult(string Status, string? Html, WebPageAssetDocument? Asset);
