using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Web.Persistence;

namespace DantesRoleplay.Web.Pages;

/// <summary>
/// An exact read selection, not a permission token. The calling coordinator must resolve the
/// web-page resource and authorize its Application-scoped operation on every use. Query reads
/// need their separate StateSpace-scoped host; neither host is constructed by this owner.
/// </summary>
public sealed class WebPagePublicationSelection
{
    internal WebPagePublicationSelection(StateSpaceView publication, EcsEntityView entity,
        EcsComponentView component, WebPageContentReference content, bool draft,
        string? activationFingerprint, string slug)
    {
        Publication = publication with
        {
            ApplicationRevision = publication.ApplicationRevision with
            { BaseApplications = Array.AsReadOnly(publication.ApplicationRevision.BaseApplications.ToArray()) }
        };
        Entity = entity;
        PageComponent = component;
        Content = content;
        IsDraft = draft;
        ActivationFingerprint = activationFingerprint;
        PageComponentFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(component.ValueJson)));
        AssetBasePath = $"/ui/{slug}/content/{content.PageId}/revisions/{content.Revision}/";
    }

    public StateSpaceView Publication { get; }
    public EcsEntityView Entity { get; }
    public EcsComponentView PageComponent { get; }
    public string PageComponentFingerprint { get; }
    public WebPageContentReference Content { get; }
    public bool IsDraft { get; }
    public string? ActivationFingerprint { get; }
    public string AssetBasePath { get; }
}

/// <summary>ECS write evidence only; this is not a shared invocation commit receipt.</summary>
public sealed record WebPagePublicationPinResult(WebPageContentReference Content, int PageComponentRevision);

/// <summary>A read-only observation of the separate legacy pointer, not a cross-database transaction.</summary>
public sealed record WebPageCompatibilityPointer(string ContentPageId, int PinnedRevision, int ActiveRevision)
{
    public bool RequiresReconciliation => PinnedRevision != ActiveRevision;
}

public sealed partial class WebPagePublicationService
{
    public Task<WebPagePublicationSelection> SelectDraftAsync(ApplicationIdentifier applicationId,
        string entityId, int revision, CancellationToken cancellationToken = default)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        return SelectContentAsync(applicationId, entityId, revision, cancellationToken);
    }

    public Task<WebPagePublicationSelection> SelectPublishedAsync(ApplicationIdentifier applicationId,
        string entityId, CancellationToken cancellationToken = default) =>
        SelectContentAsync(applicationId, entityId, null, cancellationToken);

    private async Task<WebPagePublicationSelection> SelectContentAsync(ApplicationIdentifier applicationId,
        string entityId, int? draftRevision, CancellationToken cancellationToken)
    {
        var identity = await SelectionIdentityAsync(applicationId, entityId, cancellationToken);
        var reference = identity.Value.ActiveContentReference;
        if (draftRevision is null && !reference.IsPinned)
            throw SelectionError("WEB_CONTENT_UNPINNED", "Legacy page content has no exact publication pin.");
        var revision = draftRevision ?? reference.Revision!.Value;
        var retained = await pages.GetRevisionAsync(reference.PageId, revision, cancellationToken)
            ?? throw SelectionError("WEB_CONTENT_REVISION_UNKNOWN", "The exact retained content revision is unavailable.");
        var pin = WebPageContentReference.FromRevision(retained);
        if (draftRevision is null && reference != pin)
            throw SelectionError("WEB_CONTENT_PIN_MISMATCH", "The retained content no longer matches the publication pin.");
        var selection = new WebPagePublicationSelection(identity.Publication, identity.Entity,
            identity.Component, pin, draftRevision is not null, identity.ActivationFingerprint, identity.Value.Slug);
        await RecheckIdentityAsync(selection, cancellationToken);
        return selection;
    }

    /// <summary>Rechecks current publication identity and exact retained bytes, with no active fallback.</summary>
    public async Task<WebPageRevisionDocument> RevalidateSelectionAsync(WebPagePublicationSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        await RecheckIdentityAsync(selection, cancellationToken);
        var retained = await pages.GetRevisionAsync(selection.Content.PageId,
            selection.Content.Revision!.Value, cancellationToken)
            ?? throw SelectionError("WEB_CONTENT_REVISION_UNKNOWN", "The selected retained revision is unavailable.");
        if (!selection.Content.MatchesRevision(retained))
            throw SelectionError("WEB_CONTENT_PIN_MISMATCH", "The selected content or asset inventory changed.");
        await RecheckIdentityAsync(selection, cancellationToken);
        return retained;
    }

    /// <summary>The transport must reauthorize this specific content resource on every asset request.</summary>
    public async Task<WebPageAssetDocument?> ReadSelectedAssetAsync(WebPagePublicationSelection selection,
        string path, CancellationToken cancellationToken = default)
    {
        var retained = await RevalidateSelectionAsync(selection, cancellationToken);
        var expected = retained.Assets.SingleOrDefault(asset => asset.Path == path);
        if (expected is null) return null;
        var asset = await pages.GetRevisionAssetAsync(selection.Content.PageId,
            selection.Content.Revision!.Value, path, cancellationToken);
        if (asset is null || asset.PageId != selection.Content.PageId || asset.Revision != selection.Content.Revision ||
            asset.Path != expected.Path || asset.ContentType != expected.ContentType ||
            asset.ContentHash != expected.ContentHash || asset.Content.Length != expected.Content.Length ||
            Convert.ToHexString(SHA256.HashData(asset.Content)) != expected.ContentHash)
            throw SelectionError("WEB_CONTENT_PIN_MISMATCH", "The selected asset does not match its retained inventory.");
        await RecheckIdentityAsync(selection, cancellationToken);
        return asset;
    }

    /// <summary>Literal rendering only. Missing query/action owners are not replaced by synthetic hosts.</summary>
    public async Task<WebCompositionRenderResult> RenderLiteralSelectionAsync(WebPagePublicationSelection selection,
        CancellationToken cancellationToken = default)
    {
        var retained = await RevalidateSelectionAsync(selection, cancellationToken);
        if (retained.ContentFormat != WebPageContentFormat.Composition)
            throw SelectionError("WEB_COMPOSITION_REQUIRED", "The selected revision is not a composition.");
        var parsed = new WebCompositionParser().Parse(retained.CompositionJson!, retained.Assets.Select(asset => asset.Path));
        if (!parsed.IsValid) return new(null, parsed.Errors);
        if (parsed.Document!.QueryBindings.Count != 0 || parsed.Document.ActionBindings.Count != 0)
            return new(null, [new("$", "COMPOSITION_BINDINGS_UNAVAILABLE", "Exact authorized query and action bindings are not available for this selection.")]);
        var rendered = new WebCompositionRenderer().Render(parsed.Document,
            new Dictionary<string, JsonElement>(), selection.AssetBasePath);
        await RecheckIdentityAsync(selection, cancellationToken);
        return rendered;
    }

    /// <summary>
    /// Unregistered owner operation. The caller must authorize publication of this retained resource
    /// and validate the real candidate first. Only the ECS reference is transacted here; the separate
    /// web ActiveRevision compatibility pointer is deliberately not written. No authorization or
    /// candidate acceptance is inferred from a selection, principal flag, or successful storage read.
    /// </summary>
    public async Task<WebPagePublicationPinResult> CompareExchangeContentReferenceAsync(
        WebPagePublicationSelection expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!expected.IsDraft)
            throw SelectionError("WEB_DRAFT_SELECTION_REQUIRED", "Publishing requires an exact retained draft selection.");
        _ = await RevalidateSelectionAsync(expected, cancellationToken);
        if (transactions is null)
            throw SelectionError("WEB_PUBLICATION_TRANSACTION_UNAVAILABLE", "Transactional publication is unavailable.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        try
        {
            var identity = await RecheckIdentityAsync(expected, cancellationToken);
            var value = identity.Value with { ActiveContentReference = expected.Content };
            var written = await entities.SetComponentAsync(new(identity.Publication.StateSpaceId,
                identity.Entity.EntityId, identity.Component.Type, JsonSerializer.Serialize(value, Json),
                expected.PageComponent.Revision), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(expected.Content, written.Revision);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<WebPageCompatibilityPointer> ReadCompatibilityPointerAsync(
        WebPagePublicationSelection published, CancellationToken cancellationToken = default)
    {
        if (published.IsDraft)
            throw SelectionError("WEB_PUBLISHED_SELECTION_REQUIRED", "Compatibility comparison requires a published pin.");
        _ = await RevalidateSelectionAsync(published, cancellationToken);
        var summary = await pages.GetSummaryAsync(published.Content.PageId, cancellationToken)
            ?? throw SelectionError("WEB_CONTENT_REVISION_UNKNOWN", "The retained content page is unavailable.");
        await RecheckIdentityAsync(published, cancellationToken);
        return new(published.Content.PageId, published.Content.Revision!.Value, summary.ActiveRevision);
    }

    private async Task<SelectionIdentity> RecheckIdentityAsync(WebPagePublicationSelection expected,
        CancellationToken cancellationToken)
    {
        var current = await SelectionIdentityAsync(expected.Publication.ApplicationRevision.ApplicationId,
            expected.Entity.EntityId, cancellationToken);
        var before = expected.Publication;
        var after = current.Publication;
        if (before.StateSpaceId != after.StateSpaceId || before.BindingRevision != after.BindingRevision ||
            before.ManifestFingerprint != after.ManifestFingerprint || before.ResolutionFingerprint != after.ResolutionFingerprint ||
            before.ApplicationRevision.Revision != after.ApplicationRevision.Revision ||
            before.ApplicationRevision.Fingerprint != after.ApplicationRevision.Fingerprint ||
            expected.Entity.Revision != current.Entity.Revision || expected.Entity.CreatedAtUtc != current.Entity.CreatedAtUtc ||
            expected.PageComponent.Type != current.Component.Type || expected.PageComponent.Revision != current.Component.Revision ||
            expected.PageComponent.ValueJson != current.Component.ValueJson ||
            expected.PageComponent.CreatedAtUtc != current.Component.CreatedAtUtc ||
            expected.ActivationFingerprint != current.ActivationFingerprint)
            throw SelectionError("WEB_PAGE_SELECTION_STALE", "The application publication or page component changed. Select it again.");
        return current;
    }

    private async Task<SelectionIdentity> SelectionIdentityAsync(ApplicationIdentifier applicationId,
        string entityId, CancellationToken cancellationToken)
    {
        var registered = applications.Get(applicationId)
            ?? throw SelectionError("WEB_APPLICATION_UNKNOWN", "The owning application is unavailable.");
        var batch = stateSpaces.ListPage(applicationId, null, 100);
        var publications = batch.StateSpaces.Where(value => value.Scope == EcsStateSpaceScope.ApplicationPublication).ToArray();
        if (batch.NextStateSpaceId is not null || publications.Length != 1)
            throw SelectionError("WEB_PUBLICATION_INVALID", "An exact bounded application publication binding is required.");
        var publication = publications[0];
        if (publication.ApplicationRevision.ApplicationId != applicationId ||
            publication.ApplicationRevision.Revision != registered.Revision ||
            publication.ApplicationRevision.Fingerprint != registered.Fingerprint)
            throw SelectionError("WEB_APPLICATION_GENERATION_STALE", "The publication is bound to a different application generation.");
        var active = activations?.Current(applicationId);
        if (active is not null && (active.ApplicationRevision != registered.Revision ||
            active.ApplicationFingerprint != registered.Fingerprint ||
            active.ActivationFingerprint != publication.ManifestFingerprint ||
            active.ResolutionFingerprint != publication.ResolutionFingerprint))
            throw SelectionError("WEB_APPLICATION_GENERATION_STALE", "The publication binding differs from the active application evidence.");
        var entity = await entities.GetEntityAsync(publication.StateSpaceId, entityId, cancellationToken);
        if (entity is null || entity.DeletedAtUtc is not null)
            throw SelectionError("WEB_PAGE_UNKNOWN", "The enabled page entity is unavailable.");
        var component = await entities.GetComponentAsync(publication.StateSpaceId, entityId,
            WebPageComponentTypes.Page, cancellationToken)
            ?? throw SelectionError("WEB_PAGE_UNKNOWN", "The exact page component is unavailable.");
        var type = componentTypes.Get(component.Type.QualifiedTypeId, component.Type.TypeVersion);
        if (component.StateSpaceId != publication.StateSpaceId || component.EntityId != entityId ||
            type is null || type.QualifiedId != WebPageComponentTypes.Page || type.SchemaHash != component.Type.SchemaHash)
            throw SelectionError("WEB_PAGE_COMPONENT_MISMATCH", "The exact registered page component type is unavailable.");
        var value = JsonSerializer.Deserialize<PageValue>(component.ValueJson, Json)
            ?? throw SelectionError("WEB_PAGE_COMPONENT_MISMATCH", "The page component is empty.");
        if (value.ActiveContentReference is null || !WebPageId.IsValid(value.Slug))
            throw SelectionError("WEB_PAGE_COMPONENT_MISMATCH", "The page component has no valid content link or slug.");
        value.ActiveContentReference.Validate();
        return new(publication, entity, component, value, active?.ActivationFingerprint);
    }

    private static WebPageStoreException SelectionError(string code, string message) => new(code, message);
    private sealed record SelectionIdentity(StateSpaceView Publication, EcsEntityView Entity,
        EcsComponentView Component, PageValue Value, string? ActivationFingerprint);
}
