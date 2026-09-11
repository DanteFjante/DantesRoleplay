using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Pages;

/// <summary>
/// Produces ownership evidence for one retained web-page revision. This unregistered owner grants
/// nothing: the standing-grant resolver and policy revalidate its complete returned evidence.
/// </summary>
public sealed class WebPageStandingGrantResourceTargetOwner(
    WebContentDbContext web,
    IWebPageStore pages,
    IApplicationRegistry applications,
    ICatalogNamespaceRegistry namespaces) : IStandingGrantResourceTargetOwner
{
    public const string ResourceKind = CatalogNamespaceKinds.WebPage;
    private const string EvidenceDomain = "dantes-roleplay/web-page-standing-grant-owner/v1";

    public string Kind => ResourceKind;

    public async Task<StandingGrantTargetResolution> ResolveAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Kind != Kind || selection.Revision < 1 || !UpperHash(selection.ContentFingerprint))
            return Denied("STANDING_GRANT_WEB_PAGE_SELECTION_INVALID");
        if (!ValidTarget(selection.DefinitionId)) return Denied("STANDING_GRANT_WEB_PAGE_TARGET_INVALID");

        var mapping = await MappingAsync(selection.DefinitionId, cancellationToken);
        if (mapping is null) return Unavailable("STANDING_GRANT_WEB_PAGE_UNAVAILABLE");
        var context = ValidateContext(host, mapping);
        if (context.Result is not null) return context.Result;

        var document = await pages.GetRevisionAsync(mapping.ContentPageId, selection.Revision, cancellationToken);
        if (document is null) return Unavailable("STANDING_GRANT_WEB_PAGE_REVISION_UNAVAILABLE");
        WebPageContentReference pin;
        try { pin = WebPageContentReference.FromRevision(document); }
        catch (WebPageStoreException) { return Denied("STANDING_GRANT_WEB_PAGE_CONTENT_CORRUPT"); }
        if (pin.PageId != mapping.ContentPageId || pin.Revision != selection.Revision ||
            pin.ContentHash != selection.ContentFingerprint)
            return Denied("STANDING_GRANT_WEB_PAGE_SELECTION_STALE");

        var rechecked = await MappingAsync(selection.DefinitionId, cancellationToken);
        if (!SameMapping(mapping, rechecked)) return Denied("STANDING_GRANT_WEB_PAGE_MAPPING_STALE");
        var after = ValidateContext(host, mapping);
        if (after.Result is not null) return after.Result;
        if (!SameChain(context.Chain!, after.Chain!)) return Denied("STANDING_GRANT_WEB_PAGE_CONTEXT_STALE");
        return Available(host, mapping, pin, after.Chain!);
    }

    /// <summary>Returns the latest retained revision for inspection only; it is not a publication selection.</summary>
    public async Task<StandingGrantTargetResolution> ResolveCurrentAsync(
        InteractionInvocationHost host,
        string exactDefinitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!ValidTarget(exactDefinitionId)) return Denied("STANDING_GRANT_WEB_PAGE_TARGET_INVALID");
        var mapping = await MappingAsync(exactDefinitionId, cancellationToken);
        if (mapping is null) return Unavailable("STANDING_GRANT_WEB_PAGE_UNAVAILABLE");
        var context = ValidateContext(host, mapping);
        if (context.Result is not null) return context.Result;
        var summary = await pages.GetSummaryAsync(mapping.ContentPageId, cancellationToken);
        if (summary is null || summary.LatestRevision < 1)
            return Unavailable("STANDING_GRANT_WEB_PAGE_REVISION_UNAVAILABLE");
        var document = await pages.GetRevisionAsync(mapping.ContentPageId, summary.LatestRevision, cancellationToken);
        if (document is null) return Unavailable("STANDING_GRANT_WEB_PAGE_REVISION_UNAVAILABLE");
        WebPageContentReference pin;
        try { pin = WebPageContentReference.FromRevision(document); }
        catch (WebPageStoreException) { return Denied("STANDING_GRANT_WEB_PAGE_CONTENT_CORRUPT"); }
        if (pin.PageId != mapping.ContentPageId || pin.Revision != summary.LatestRevision)
            return Denied("STANDING_GRANT_WEB_PAGE_SELECTION_STALE");
        var rechecked = await MappingAsync(exactDefinitionId, cancellationToken);
        if (!SameMapping(mapping, rechecked)) return Denied("STANDING_GRANT_WEB_PAGE_MAPPING_STALE");
        var after = ValidateContext(host, mapping);
        if (after.Result is not null) return after.Result;
        var latest = await pages.GetSummaryAsync(mapping.ContentPageId, cancellationToken);
        if (latest is null || latest.LatestRevision != summary.LatestRevision)
            return Denied("STANDING_GRANT_WEB_PAGE_CURRENT_STALE");
        if (!SameChain(context.Chain!, after.Chain!)) return Denied("STANDING_GRANT_WEB_PAGE_CONTEXT_STALE");
        return Available(host, mapping, pin, after.Chain!);
    }

    private async Task<WebPageResourceIdentity?> MappingAsync(string target, CancellationToken cancellationToken) =>
        await web.Set<WebPageResourceIdentity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.QualifiedTargetId == target, cancellationToken);

    private ContextCheck ValidateContext(InteractionInvocationHost host, WebPageResourceIdentity mapping)
    {
        if (host.StateSpaceId is not null || host.StateRevision is not null)
            return new(Denied("STANDING_GRANT_WEB_PAGE_SCOPE_DENIED"), null);
        ApplicationIdentifier owner;
        try { owner = ApplicationIdentifier.Parse(mapping.OwnerApplicationId); }
        catch (ArgumentException) { return new(Denied("STANDING_GRANT_WEB_PAGE_OWNER_INVALID"), null); }
        if (owner.IsSystem || owner != host.ApplicationRevision.ApplicationId)
            return new(Denied("STANDING_GRANT_WEB_PAGE_OWNER_MISMATCH"), null);
        var registered = applications.Get(owner);
        if (registered is null || registered.Revision != host.ApplicationRevision.Revision ||
            registered.Fingerprint != host.ApplicationRevision.Fingerprint ||
            !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
            return new(Denied("STANDING_GRANT_APPLICATION_STALE"), null);
        var chain = CatalogNamespaceIdentity.NamespaceChain(mapping.QualifiedTargetId)
            .Select(id => namespaces.Get(id)).ToArray();
        if (chain.Length == 0 || chain.Any(value => value is null || !value.IsEnabled ||
                value.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed))
            return new(Denied("STANDING_GRANT_NAMESPACE_UNREVIEWED"), null);
        var leaf = chain[^1]!;
        if (leaf.Id == CatalogNamespaceIdentity.RootNamespaceId ||
            !leaf.AllowedKinds.Contains(Kind, StringComparer.Ordinal))
            return new(Denied("STANDING_GRANT_NAMESPACE_KIND_DENIED"), null);
        return new(null, chain.Select(value => value!).ToArray());
    }

    private static StandingGrantTargetResolution Available(InteractionInvocationHost host,
        WebPageResourceIdentity mapping, WebPageContentReference pin, IReadOnlyList<CatalogNamespaceDefinition> chain)
    {
        var evidence = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            mapping.ContentPageId, mapping.QualifiedTargetId, mapping.OwnerApplicationId,
            mapping.SourceOperationId, mapping.CreatedAtUtc,
            pin.PageId, pin.Revision, pin.ContentFormat, pin.ContentHash, pin.AssetInventoryFingerprint,
            applicationRevision = host.ApplicationRevision.Revision,
            applicationFingerprint = host.ApplicationRevision.Fingerprint,
            baseApplications = host.ApplicationRevision.BaseApplications.Select(value => value.Value).ToArray(),
            namespaceChain = chain
        }));
        var target = new StandingGrantDefinitionTarget(mapping.QualifiedTargetId, ResourceKind,
            host.ApplicationRevision.ApplicationId,
            chain[^1].Id, "web-page-owner:" + InteractionCanonicalJson.Fingerprint(EvidenceDomain, evidence),
            pin.Revision!.Value, pin.ContentHash!);
        return new(StandingGrantTargetResolutionStatus.Available, "STANDING_GRANT_TARGET_AVAILABLE", target);
    }

    private static bool SameMapping(WebPageResourceIdentity first, WebPageResourceIdentity? second) => second is not null &&
        first.ContentPageId == second.ContentPageId && first.QualifiedTargetId == second.QualifiedTargetId &&
        first.OwnerApplicationId == second.OwnerApplicationId && first.SourceOperationId == second.SourceOperationId &&
        first.CreatedAtUtc.Ticks == second.CreatedAtUtc.Ticks;

    private static bool SameChain(IReadOnlyList<CatalogNamespaceDefinition> first,
        IReadOnlyList<CatalogNamespaceDefinition> second) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { chain = first })) ==
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { chain = second }));

    private static bool ValidTarget(string? value)
    {
        try
        {
            CatalogNamespaceIdentity.ValidateRecordId(value!);
            return !value!.Contains('*') && !value.Contains('?');
        }
        catch (ArgumentException) { return false; }
    }

    private static bool UpperHash(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static StandingGrantTargetResolution Denied(string code) =>
        new(StandingGrantTargetResolutionStatus.Denied, code, null);
    private static StandingGrantTargetResolution Unavailable(string code) =>
        new(StandingGrantTargetResolutionStatus.Unavailable, code, null);
    private sealed record ContextCheck(StandingGrantTargetResolution? Result, IReadOnlyList<CatalogNamespaceDefinition>? Chain);
}
