using DantesRoleplay.Web.Pages;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Persistence;

public sealed class WebPageStore(WebContentDbContext db) : IWebPageStore
{
    public async Task<WebPageDocument> SaveAndActivateAsync(
        string id,
        string html,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(id))
        {
            throw new ArgumentException("Page ID is invalid.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ArgumentException("HTML cannot be empty.", nameof(html));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var page = await db.Pages
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        var nextRevision = page is null
            ? 1
            : await db.PageRevisions
                .Where(revision => revision.PageId == id)
                .MaxAsync(revision => revision.Revision, cancellationToken) + 1;
        var now = DateTime.UtcNow;

        if (page is null)
        {
            page = new WebPage
            {
                Id = id,
                ActiveRevision = nextRevision,
                UpdatedAt = now
            };
            db.Pages.Add(page);
        }
        else
        {
            page.ActiveRevision = nextRevision;
            page.UpdatedAt = now;
        }

        var revision = new WebPageRevision
        {
            PageId = id,
            Revision = nextRevision,
            Html = html,
            CreatedAt = now
        };
        db.PageRevisions.Add(revision);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WebPageDocument(id, nextRevision, html, now);
    }

    public Task<WebPageDocument?> GetActiveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!WebPageId.IsValid(id))
        {
            return Task.FromResult<WebPageDocument?>(null);
        }

        return db.Pages
            .AsNoTracking()
            .Where(page => page.Id == id)
            .Join(
                db.PageRevisions.AsNoTracking(),
                page => new { PageId = page.Id, Revision = page.ActiveRevision },
                revision => new { revision.PageId, revision.Revision },
                (_, revision) => new WebPageDocument(
                    revision.PageId,
                    revision.Revision,
                    revision.Html,
                    revision.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
