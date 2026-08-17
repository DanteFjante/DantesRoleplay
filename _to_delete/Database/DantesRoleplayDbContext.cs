using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Database;

/// <summary>
/// The only type in the kernel that knows a database exists.
///
/// Per ARCHITECTURE.md §3.4, nothing outside DantesRoleplay.Database writes SQL, and per §3.11
/// nothing in here knows anything about a game.
/// </summary>
public sealed class DantesRoleplayDbContext(DbContextOptions<DantesRoleplayDbContext> options)
    : DbContext(options)
{
    public DbSet<ProcedureContract> ProcedureContracts => Set<ProcedureContract>();

    public DbSet<ProcedureContractVersion> ProcedureContractVersions => Set<ProcedureContractVersion>();

    public DbSet<ProcedureRelation> ProcedureRelations => Set<ProcedureRelation>();

    public DbSet<Operation> Operations => Set<Operation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcedureContract>(entity =>
        {
            entity.ToTable("procedure_contract");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(200);
            entity.Property(e => e.Category).HasMaxLength(100).IsRequired();
            // Stored as text so the database stays readable in any SQLite viewer.
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<ProcedureContractVersion>(entity =>
        {
            entity.ToTable("procedure_contract_version");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContractId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Instructions).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasOne(e => e.Contract)
                  .WithMany(c => c.Versions)
                  .HasForeignKey(e => e.ContractId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Two revisions can never claim the same version number for one contract.
            entity.HasIndex(e => new { e.ContractId, e.Version }).IsUnique();
        });

        modelBuilder.Entity<ProcedureRelation>(entity =>
        {
            entity.ToTable("procedure_relation");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FromContractId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ToContractId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();

            entity.HasOne(e => e.FromContract)
                  .WithMany()
                  .HasForeignKey(e => e.FromContractId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ToContract)
                  .WithMany()
                  .HasForeignKey(e => e.ToContractId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.FromContractId, e.ToContractId, e.Kind }).IsUnique();
        });

        modelBuilder.Entity<Operation>(entity =>
        {
            entity.ToTable("operation");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(40);
            entity.Property(e => e.Tool).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
