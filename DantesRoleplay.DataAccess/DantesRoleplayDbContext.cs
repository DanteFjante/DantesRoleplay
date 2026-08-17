using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// The only type in the solution that knows a database exists.
///
/// Per ARCHITECTURE.md §3.4 nothing outside this project writes SQL, and per §3.11 nothing in
/// here knows anything about a game: the world tables are Entity, ComponentDefinition,
/// Component, Containment and Relationship, and every game concept is a row in them.
/// </summary>
public sealed class DantesRoleplayDbContext(DbContextOptions<DantesRoleplayDbContext> options)
    : DbContext(options)
{
    public DbSet<ProcedureContract> ProcedureContracts => Set<ProcedureContract>();

    public DbSet<ProcedureContractVersion> ProcedureContractVersions => Set<ProcedureContractVersion>();

    public DbSet<ProcedureRelation> ProcedureRelations => Set<ProcedureRelation>();

    public DbSet<Operation> Operations => Set<Operation>();

    public DbSet<Entity> Entities => Set<Entity>();

    public DbSet<ComponentDefinition> ComponentDefinitions => Set<ComponentDefinition>();

    public DbSet<Component> Components => Set<Component>();

    public DbSet<Containment> Containments => Set<Containment>();

    public DbSet<Relationship> Relationships => Set<Relationship>();

    public DbSet<Mechanic> Mechanics => Set<Mechanic>();

    public DbSet<MechanicVersion> MechanicVersions => Set<MechanicVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProcedures(modelBuilder);
        ConfigureOperations(modelBuilder);
        ConfigureWorld(modelBuilder);
        ConfigureMechanics(modelBuilder);
    }

    /// <summary>
    /// Shaped exactly like the procedure tables, because a mechanic is the same kind of object:
    /// authored content with an identity row and append-only versions.
    ///
    /// Note what is NOT here — no table for what a mechanic does, what it affects, or what kind of
    /// rule it is. That would be the game leaking into the schema (§3.11). The source is text and
    /// the requirements are JSON, and neither is something the database understands.
    /// </summary>
    private static void ConfigureMechanics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mechanic>(entity =>
        {
            entity.ToTable("mechanic");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(200);
            entity.Property(e => e.Category).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Scope).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.Status);
            // Retrieval always filters on scope: this campaign's rules, plus the shared ones.
            entity.HasIndex(e => e.Scope);
        });

        modelBuilder.Entity<MechanicVersion>(entity =>
        {
            entity.ToTable("mechanic_version");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MechanicId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Matches).IsRequired();
            entity.Property(e => e.Requirements).IsRequired();
            entity.Property(e => e.Source).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.SourceHash).HasMaxLength(64);

            entity.HasOne(e => e.Mechanic)
                  .WithMany(m => m.Versions)
                  .HasForeignKey(e => e.MechanicId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.MechanicId, e.Version }).IsUnique();
        });
    }

    private static void ConfigureProcedures(ModelBuilder modelBuilder)
    {
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
            entity.Property(e => e.SourceHash).HasMaxLength(64);
            entity.Property(e => e.Governs).HasMaxLength(500);

            entity.HasOne(e => e.Contract)
                  .WithMany(c => c.Versions)
                  .HasForeignKey(e => e.ContractId)
                  .OnDelete(DeleteBehavior.Cascade);

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
    }

    private static void ConfigureOperations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Operation>(entity =>
        {
            entity.ToTable("operation");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(40);
            entity.Property(e => e.Tool).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(200);
            entity.Property(e => e.ConsumedReadEvidence).HasDefaultValue(false);
            entity.HasIndex(e => e.Timestamp);
            // Supports both the history filters and the observed-procedures derivation, which
            // queries by tool + timestamp on every write.
            entity.HasIndex(e => new { e.Tool, e.Timestamp });
            entity.HasIndex(e => e.Subject);
        });
    }

    private static void ConfigureWorld(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entity>(entity =>
        {
            entity.ToTable("entity");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(200);
            entity.Property(e => e.Name).HasMaxLength(400).IsRequired();
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.DeletedAt);
        });

        modelBuilder.Entity<ComponentDefinition>(entity =>
        {
            entity.ToTable("component_definition");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(200);
            entity.Property(e => e.Name).HasMaxLength(400).IsRequired();
            entity.Property(e => e.Description).IsRequired();
        });

        modelBuilder.Entity<Component>(entity =>
        {
            entity.ToTable("component");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DefinitionId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Data).IsRequired();

            entity.HasOne(e => e.Entity)
                  .WithMany(e => e.Components)
                  .HasForeignKey(e => e.EntityId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Definition)
                  .WithMany()
                  .HasForeignKey(e => e.DefinitionId)
                  .OnDelete(DeleteBehavior.Restrict);

            // One component per definition per entity — "Orban's stats" is singular.
            entity.HasIndex(e => new { e.EntityId, e.DefinitionId }).IsUnique();

            // Supports "find every entity that has a position".
            entity.HasIndex(e => e.DefinitionId);
        });

        modelBuilder.Entity<Containment>(entity =>
        {
            entity.ToTable("containment");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContainerId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ContainedId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Slot).HasMaxLength(100);

            entity.HasOne(e => e.Container)
                  .WithMany()
                  .HasForeignKey(e => e.ContainerId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Contained)
                  .WithMany()
                  .HasForeignKey(e => e.ContainedId)
                  .OnDelete(DeleteBehavior.Cascade);

            // A thing is in at most one place. This is the constraint, not a convention.
            entity.HasIndex(e => e.ContainedId).IsUnique();
            entity.HasIndex(e => e.ContainerId);
        });

        modelBuilder.Entity<Relationship>(entity =>
        {
            entity.ToTable("relationship");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FromEntityId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ToEntityId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Kind).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Data).IsRequired();

            entity.HasOne(e => e.FromEntity)
                  .WithMany()
                  .HasForeignKey(e => e.FromEntityId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ToEntity)
                  .WithMany()
                  .HasForeignKey(e => e.ToEntityId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.FromEntityId, e.ToEntityId, e.Kind }).IsUnique();
            entity.HasIndex(e => e.ToEntityId);
        });
    }
}
