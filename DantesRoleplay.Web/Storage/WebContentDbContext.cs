using DantesRoleplay.Web.Pages;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Persistence;

public sealed class WebContentDbContext(DbContextOptions<WebContentDbContext> options)
    : DbContext(options)
{
    public DbSet<WebPage> Pages => Set<WebPage>();

    public DbSet<WebPageRevision> PageRevisions => Set<WebPageRevision>();

    public DbSet<WebPageAsset> PageAssets => Set<WebPageAsset>();

    public DbSet<WebPageMigrationReportRecord> PageMigrationReports => Set<WebPageMigrationReportRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebPage>(entity =>
        {
            entity.ToTable("web_page", table =>
            {
                table.HasCheckConstraint(
                    "CK_web_page_active_revision",
                    "\"ActiveRevision\" > 0");
            });
            entity.HasKey(page => page.Id);
            entity.Property(page => page.Id).HasMaxLength(WebPageId.MaximumLength);
            entity.Property(page => page.ActiveRevision).IsRequired();
            entity.Property(page => page.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<WebPageRevision>(entity =>
        {
            entity.ToTable("web_page_revision", table =>
            {
                table.HasCheckConstraint(
                    "CK_web_page_revision_revision",
                    "\"Revision\" > 0");
            });
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.PageId)
                .HasMaxLength(WebPageId.MaximumLength)
                .IsRequired();
            entity.Property(revision => revision.Revision).IsRequired();
            entity.Property(revision => revision.Html).IsRequired();
            entity.Property(revision => revision.CreatedAt).IsRequired();
            entity.HasOne(revision => revision.Page)
                .WithMany(page => page.Revisions)
                .HasForeignKey(revision => revision.PageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(revision => new { revision.PageId, revision.Revision }).IsUnique();
        });

        modelBuilder.Entity<WebPageAsset>(entity =>
        {
            entity.ToTable("web_page_asset");
            entity.HasKey(asset => asset.Id);
            entity.Property(asset => asset.PageRevisionId).IsRequired();
            entity.Property(asset => asset.Path)
                .HasMaxLength(WebPageBundleLimits.MaximumAssetPathLength)
                .IsRequired();
            entity.Property(asset => asset.ContentType).HasMaxLength(127).IsRequired();
            entity.Property(asset => asset.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(asset => asset.Content).IsRequired();
            entity.HasOne(asset => asset.PageRevision)
                .WithMany(revision => revision.Assets)
                .HasForeignKey(asset => asset.PageRevisionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(asset => new { asset.PageRevisionId, asset.Path }).IsUnique();
        });

        modelBuilder.Entity<WebPageMigrationReportRecord>(entity =>
        {
            entity.ToTable("web_page_migration_report");
            entity.HasKey(report => report.Id);
            entity.Property(report => report.Id).HasMaxLength(100);
            entity.Property(report => report.ReportJson).IsRequired();
            entity.Property(report => report.UpdatedAtUtc).IsRequired();
        });
    }
}
