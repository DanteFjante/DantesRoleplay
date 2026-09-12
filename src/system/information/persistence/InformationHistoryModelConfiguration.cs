using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

// Frozen owner model. The coordinator owns DbContext registration and migrations.
internal static class InformationHistoryModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InformationContentRevisionRecord>(entity =>
        {
            entity.ToTable("system_information_content_revision", table =>
            {
                table.HasCheckConstraint("CK_system_information_content_kind", "\"Kind\" IN ('source','record')");
                table.HasCheckConstraint("CK_system_information_content_origin", "\"Origin\" IN ('baseline-retained','conditional-write')");
                table.HasCheckConstraint("CK_system_information_content_json", "json_valid(\"ContentJson\") AND length(CAST(\"ContentJson\" AS BLOB)) <= 65536");
                table.HasCheckConstraint("CK_system_information_content_hash", Hash("ContentFingerprint"));
                table.HasCheckConstraint("CK_system_information_content_revision", "\"Revision\" > 0");
            });
            entity.HasKey(x => new { x.Kind, x.Id, x.Revision });
            entity.Property(x => x.Kind).HasMaxLength(10); entity.Property(x => x.Id).HasMaxLength(200);
            entity.Property(x => x.ContentJson).HasMaxLength(65536); entity.Property(x => x.ContentFingerprint).HasMaxLength(64);
            entity.Property(x => x.RetainedByOperationId).HasMaxLength(200); entity.Property(x => x.Origin).HasMaxLength(32);
            entity.HasOne<Operation>().WithMany().HasForeignKey(x => x.RetainedByOperationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
    private static string Hash(string column) => "length(\"" + column + "\") = 64 AND \"" + column + "\" NOT GLOB '*[^0-9A-F]*'";
}

internal sealed class InformationContentRevisionRecord
{
    public required string Kind { get; set; }
    public required string Id { get; set; }
    public int Revision { get; set; }
    public required string ContentJson { get; set; }
    public required string ContentFingerprint { get; set; }
    public required string RetainedByOperationId { get; set; }
    public required string Origin { get; set; }
}
