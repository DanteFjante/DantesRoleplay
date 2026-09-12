using DantesRoleplay.Web.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DantesRoleplay.Web.Persistence;

/// <summary>Root DbContext integration is intentionally owned by the coordinator.</summary>
public sealed class WebPageResourceIdentityConfiguration : IEntityTypeConfiguration<WebPageResourceIdentity>
{
    public void Configure(EntityTypeBuilder<WebPageResourceIdentity> entity)
    {
        entity.ToTable("web_page_resource_identity");
        entity.HasKey(value => value.ContentPageId);
        entity.Property(value => value.ContentPageId).HasMaxLength(WebPageId.MaximumLength).IsRequired();
        entity.Property(value => value.QualifiedTargetId).HasMaxLength(200).IsRequired();
        entity.Property(value => value.OwnerApplicationId).HasMaxLength(63).IsRequired();
        entity.Property(value => value.SourceOperationId).HasMaxLength(32).IsRequired();
        entity.Property(value => value.CreatedAtUtc).IsRequired();
        entity.HasIndex(value => value.QualifiedTargetId).IsUnique();
        entity.HasOne<WebPage>().WithMany().HasForeignKey(value => value.ContentPageId).OnDelete(DeleteBehavior.Restrict);
    }
}
