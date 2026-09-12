using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Web.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.Web.Persistence;

public sealed record WebPageResourceIdentityStageRequest(
    string ContentPageId,
    string QualifiedTargetId,
    string OwnerApplicationId,
    string SourceOperationId,
    DateTime CreatedAtUtc);

/// <summary>
/// Stages an insert in the caller's current transaction. The caller must have already reserved an
/// immediate SQLite writer transaction; transaction equality proves the shared staging scope, not begin mode.
/// It neither saves nor commits, and its result is never a shared invocation receipt.
/// </summary>
public sealed class WebPageResourceIdentityStore(WebContentDbContext db)
{
    public async Task<WebPageResourceIdentityStageResult> StageAsync(
        WebPageResourceIdentityStageRequest request,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(db.Database.CurrentTransaction, transaction))
            throw new WebPageStoreException("RESOURCE_IDENTITY_TRANSACTION_REQUIRED",
                "Resource identity staging requires the caller's current web write transaction.");
        Validate(request);

        var set = db.Set<WebPageResourceIdentity>();
        var existing = set.Local.SingleOrDefault(value => value.ContentPageId == request.ContentPageId)
            ?? await set.SingleOrDefaultAsync(value => value.ContentPageId == request.ContentPageId, cancellationToken);
        if (existing is not null)
        {
            var exact = existing.QualifiedTargetId == request.QualifiedTargetId &&
                existing.OwnerApplicationId == request.OwnerApplicationId;
            if (!exact)
                throw new WebPageStoreException("RESOURCE_IDENTITY_CONFLICT",
                    "The content page already has a different immutable resource identity.");
            return new(existing, false, existing.SourceOperationId == request.SourceOperationId);
        }

        var identity = new WebPageResourceIdentity
        {
            ContentPageId = request.ContentPageId,
            QualifiedTargetId = request.QualifiedTargetId,
            OwnerApplicationId = request.OwnerApplicationId,
            SourceOperationId = request.SourceOperationId,
            CreatedAtUtc = request.CreatedAtUtc
        };
        set.Add(identity);
        return new(identity, true, true);
    }

    private static void Validate(WebPageResourceIdentityStageRequest request)
    {
        if (!WebPageId.IsValid(request.ContentPageId))
            throw new WebPageStoreException("RESOURCE_IDENTITY_PAGE_INVALID", "The content page ID is invalid.");
        try { CatalogNamespaceIdentity.ValidateRecordId(request.QualifiedTargetId); }
        catch (ArgumentException exception)
        {
            throw new WebPageStoreException("RESOURCE_IDENTITY_TARGET_INVALID", exception.Message);
        }
        try { _ = ApplicationIdentifier.Parse(request.OwnerApplicationId); }
        catch (ArgumentException exception)
        {
            throw new WebPageStoreException("RESOURCE_IDENTITY_OWNER_INVALID", exception.Message);
        }
        if (!OperationId(request.SourceOperationId))
            throw new WebPageStoreException("RESOURCE_IDENTITY_OPERATION_INVALID", "The source operation ID is invalid.");
        if (request.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new WebPageStoreException("RESOURCE_IDENTITY_TIMESTAMP_INVALID", "The source timestamp must be UTC.");
    }

    private static bool OperationId(string? value) => value is { Length: 32 } &&
        value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
}

public sealed record WebPageResourceIdentityStageResult(
    WebPageResourceIdentity Identity,
    bool StagedNew,
    bool IsOriginalSourceOperation);
