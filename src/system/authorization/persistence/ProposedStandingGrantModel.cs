using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

// Review-only proposal. Production DbContext does not call this configuration.
internal static class ProposedStandingGrantModel
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StandingGrantRevisionRecord>(entity =>
        {
            entity.ToTable("proposed_standing_grant_revision", table =>
            {
                table.HasCheckConstraint("CK_proposed_grant_budget", "\"MaximumOperations\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_proposed_grant_permissions", "json_valid(\"PermissionsJson\") AND length(CAST(\"PermissionsJson\" AS BLOB)) <= 16000");
                table.HasCheckConstraint("CK_proposed_grant_hash", Hash("ContentFingerprint"));
                table.HasCheckConstraint("CK_proposed_grant_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_proposed_grant_scope", "(\"Scope\" = 'application' AND \"StateSpaceId\" IS NULL) OR (\"Scope\" = 'stateSpace' AND \"StateSpaceId\" IS NOT NULL AND length(\"StateSpaceId\") > 0)");
            });
            entity.HasKey(x => new { x.GrantId, x.Revision });
            entity.Property(x => x.GrantId).HasMaxLength(200); entity.Property(x => x.GrantReference).HasMaxLength(200);
            entity.Property(x => x.PrincipalReference).HasMaxLength(200); entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.Scope).HasMaxLength(16); entity.Property(x => x.StateSpaceId).HasMaxLength(200); entity.Property(x => x.PermissionsJson).HasMaxLength(16000);
            entity.Property(x => x.ContentFingerprint).HasMaxLength(64); entity.Property(x => x.IssuedByOperationId).HasMaxLength(200);
            entity.HasIndex(x => x.GrantReference).IsUnique();
            entity.HasOne<Operation>().WithMany().HasForeignKey(x => x.IssuedByOperationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StandingGrantCurrentRecord>(entity =>
        {
            entity.ToTable("proposed_standing_grant_current"); entity.HasKey(x => x.GrantId); entity.Property(x => x.GrantId).HasMaxLength(200);
            entity.HasOne<StandingGrantRevisionRecord>().WithMany().HasForeignKey(x => new { x.GrantId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        });
    }
    private static string Hash(string column) => "length(\"" + column + "\") = 64 AND \"" + column + "\" NOT GLOB '*[^0-9A-F]*'";
}

internal sealed class StandingGrantRevisionRecord
{
    public required string GrantId { get; set; }
    public int Revision { get; set; }
    public required string GrantReference { get; set; }
    public required string PrincipalReference { get; set; }
    public required string ApplicationId { get; set; }
    public required string Scope { get; set; }
    public string? StateSpaceId { get; set; }
    public required string PermissionsJson { get; set; }
    public required string ContentFingerprint { get; set; }
    public int MaximumOperations { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool Revoked { get; set; }
    public required string IssuedByOperationId { get; set; }
}
internal sealed class StandingGrantCurrentRecord
{
    public required string GrantId { get; set; }
    public int Revision { get; set; }
}
