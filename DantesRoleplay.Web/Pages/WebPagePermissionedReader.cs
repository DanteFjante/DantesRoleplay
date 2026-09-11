using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Pages;

/// <summary>
/// Reads one live publication using a host-created invocation and current Application Read grant.
/// This owner neither constructs authority nor executes composition bindings. Registration and
/// transport are coordinator-owned; a successful read is not publication or mutation evidence.
/// </summary>
public sealed class WebPagePermissionedReader(
    WebPagePublicationService publication,
    WebContentDbContext content,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants)
{
    public Task<WebPagePermissionedReadResult> ReadPageAsync(InteractionInvocationHost host,
        string entityId, CancellationToken cancellationToken = default) =>
        ReadAsync(host, entityId, null, null, null, cancellationToken);

    public Task<WebPagePermissionedReadResult> ReadAssetAsync(InteractionInvocationHost host,
        string entityId, string contentPageId, int revision, string path,
        CancellationToken cancellationToken = default) =>
        ReadAsync(host, entityId, contentPageId, revision, path, cancellationToken);

    private async Task<WebPagePermissionedReadResult> ReadAsync(InteractionInvocationHost host,
        string entityId, string? contentPageId, int? revision, string? path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Profile != InteractionExecutionProfile.ReadOnly)
            return Failed("WEB_READ_ONLY_REQUIRED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow)
            return Failed("INVOCATION_DEADLINE_EXCEEDED");
        if (!host.Budget.TryConsumeOperation())
            return Failed("INVOCATION_BUDGET_EXHAUSTED");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
        deadline.CancelAfter(remaining > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, uint.MaxValue - 1d)) : TimeSpan.Zero);
        var token = deadline.Token;
        try
        {
            var selected = await publication.SelectPublishedAsync(host.ApplicationRevision.ApplicationId, entityId, token);
            var application = selected.Publication.ApplicationRevision;
            if (application.Revision != host.ApplicationRevision.Revision
                || application.Fingerprint != host.ApplicationRevision.Fingerprint
                || !application.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Failed("WEB_APPLICATION_GENERATION_STALE");
            if (path is not null && (contentPageId != selected.Content.PageId || revision != selected.Content.Revision
                || !WebPageAssetPath.TryValidate(path, out var normalized) || normalized != path))
                return Failed("WEB_ASSET_SELECTION_MISMATCH");

            var identity = await content.Set<WebPageResourceIdentity>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.ContentPageId == selected.Content.PageId, token);
            if (identity is null)
                return Unavailable("WEB_RESOURCE_IDENTITY_UNAVAILABLE");
            if (identity.OwnerApplicationId != application.ApplicationId.Value)
                return Failed("WEB_RESOURCE_OWNER_MISMATCH");
            var resolved = await targets.ResolveAsync(host, new(identity.QualifiedTargetId, "web-page",
                selected.Content.Revision!.Value, selected.Content.ContentHash!), token);
            if (resolved.Status == StandingGrantTargetResolutionStatus.Unavailable)
                return Unavailable("WEB_RESOURCE_AUTHORIZATION_UNAVAILABLE");
            if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is not { } target
                || target.DefinitionId != identity.QualifiedTargetId || target.Kind != "web-page"
                || target.OwnerApplicationId != application.ApplicationId
                || target.Revision != selected.Content.Revision || target.ContentFingerprint != selected.Content.ContentHash
                || target.Candidate is not null || target.RetainedActivation is not null)
                return Failed("WEB_RESOURCE_NOT_AUTHORIZED");

            var requirement = new StandingGrantRequirement(StandingGrantCapability.Read,
                StandingGrantScope.Application, [target], []);
            var admission = await grants.EvaluateAsync(host, requirement, token);
            if (!admission.Allowed || admission.Grant is null)
                return GrantFailure(admission);

            string? html = null;
            WebPageAssetDocument? asset = null;
            if (path is not null)
            {
                asset = await publication.ReadSelectedAssetAsync(selected, path, token);
                if (asset is null) return Failed("WEB_ASSET_UNAVAILABLE");
            }
            else
            {
                var retained = await publication.RevalidateSelectionAsync(selected, token);
                if (retained.ContentFormat == WebPageContentFormat.Html)
                    html = retained.Html;
                else
                {
                    var rendered = await publication.RenderLiteralSelectionAsync(selected, token);
                    if (!rendered.IsSuccess)
                        return Unavailable(rendered.Errors.Any(error => error.Code == "COMPOSITION_BINDINGS_UNAVAILABLE")
                            ? "COMPOSITION_BINDINGS_UNAVAILABLE" : "WEB_COMPOSITION_INVALID");
                    html = rendered.Html;
                }
            }

            // A previous grant decision cannot authorize a later response. Re-read the current
            // grant/owner and the publication after materialization; never consume a second slot.
            var final = await grants.EvaluateAsync(host, requirement, token);
            if (!final.Allowed || final.Grant is null || final.Grant.GrantReference != admission.Grant.GrantReference
                || final.Grant.GrantId != admission.Grant.GrantId || final.Grant.Revision != admission.Grant.Revision
                || final.Grant.ContentFingerprint != admission.Grant.ContentFingerprint)
                return Failed("WEB_READ_AUTHORITY_CHANGED");
            _ = await publication.RevalidateSelectionAsync(selected, token);
            token.ThrowIfCancellationRequested();
            if (host.Budget.DeadlineUtc <= DateTime.UtcNow)
                return Failed("INVOCATION_DEADLINE_EXCEEDED");
            return new(selected.Content, html, asset, null);
        }
        catch (OperationCanceledException)
        {
            return new(null, null, null, InteractionInvocationResult.Cancelled(
                cancellationToken.IsCancellationRequested ? "WEB_READ_CANCELLED" : "INVOCATION_DEADLINE_EXCEEDED",
                "The page read did not complete."));
        }
        catch (WebPageStoreException exception)
        {
            return exception.Code is "WEB_CONTENT_UNPINNED" or "WEB_PUBLICATION_TRANSACTION_UNAVAILABLE"
                ? Unavailable(exception.Code) : Failed(exception.Code);
        }
    }

    private static WebPagePermissionedReadResult GrantFailure(StandingGrantDecision decision) =>
        decision.Code is "STANDING_GRANT_TARGET_UNAVAILABLE" or "STANDING_GRANT_RESOURCE_OWNER_UNAVAILABLE"
            ? Unavailable("WEB_RESOURCE_AUTHORIZATION_UNAVAILABLE") : Failed("WEB_RESOURCE_NOT_AUTHORIZED");

    private static WebPagePermissionedReadResult Failed(string code) =>
        new(null, null, null, InteractionInvocationResult.Failed(code, "The page read could not be authorized or completed."));

    private static WebPagePermissionedReadResult Unavailable(string code) =>
        new(null, null, null, InteractionInvocationResult.Unavailable(code, "The requested page capability is unavailable."));
}

/// <summary>Content is present only after authorization and selection rechecks; failures disclose no payload.</summary>
public sealed record WebPagePermissionedReadResult(WebPageContentReference? Content,
    string? Html, WebPageAssetDocument? Asset, InteractionInvocationResult? Failure);
