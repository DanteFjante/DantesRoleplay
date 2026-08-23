namespace DantesRoleplay.Web.Pages;

public sealed class WebPage
{
    public required string Id { get; set; }

    public int ActiveRevision { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<WebPageRevision> Revisions { get; set; } = [];
}

public sealed class WebPageRevision
{
    public long Id { get; set; }

    public required string PageId { get; set; }

    public WebPage? Page { get; set; }

    public int Revision { get; set; }

    public required string Html { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed record WebPageDocument(
    string Id,
    int Revision,
    string Html,
    DateTime CreatedAt);
