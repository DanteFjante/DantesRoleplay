using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Snapshots;
using DantesRoleplay.SystemFeedback;
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

    public DbSet<Operation> Operations => Set<Operation>();

    public DbSet<Entity> Entities => Set<Entity>();

    public DbSet<ComponentDefinition> ComponentDefinitions => Set<ComponentDefinition>();

    public DbSet<Component> Components => Set<Component>();

    public DbSet<Containment> Containments => Set<Containment>();

    public DbSet<Relationship> Relationships => Set<Relationship>();

    public DbSet<Mechanic> Mechanics => Set<Mechanic>();

    public DbSet<MechanicVersion> MechanicVersions => Set<MechanicVersion>();
    public DbSet<EventType> EventTypes => Set<EventType>();
    public DbSet<EventTypeVersion> EventTypeVersions => Set<EventTypeVersion>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionVersion> SubscriptionVersions => Set<SubscriptionVersion>();
    public DbSet<EventRecord> Events => Set<EventRecord>();
    public DbSet<EventEntity> EventEntities => Set<EventEntity>();

    public DbSet<EventExecution> EventExecutions => Set<EventExecution>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationEntity> NotificationEntities => Set<NotificationEntity>();

    public DbSet<SnapshotPackage> SnapshotPackages => Set<SnapshotPackage>();

    public DbSet<SystemFeedbackReport> SystemFeedbackReports => Set<SystemFeedbackReport>();
    public DbSet<SystemFeedbackStep> SystemFeedbackSteps => Set<SystemFeedbackStep>();
    public DbSet<SystemFeedbackOperationReference> SystemFeedbackOperationReferences => Set<SystemFeedbackOperationReference>();
    public DbSet<SystemFeedbackProcedureReference> SystemFeedbackProcedureReferences => Set<SystemFeedbackProcedureReference>();
    public DbSet<SystemFeedbackDisposition> SystemFeedbackDispositions => Set<SystemFeedbackDisposition>();
    public DbSet<SystemFeedbackRetentionAction> SystemFeedbackRetentionActions => Set<SystemFeedbackRetentionAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProcedures(modelBuilder);
        ConfigureOperations(modelBuilder);
        ConfigureWorld(modelBuilder);
        ConfigureMechanics(modelBuilder);
        ConfigureEventTypes(modelBuilder);
        ConfigureSubscriptions(modelBuilder);
        ConfigureEventLedger(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureSnapshots(modelBuilder);
        ConfigureSystemFeedback(modelBuilder);
    }

    private static void ConfigureSystemFeedback(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemFeedbackReport>(entity =>
        {
            entity.ToTable("system_feedback_report", table =>
            {
                table.HasCheckConstraint("CK_system_feedback_report_id", "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'feedback.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_feedback_report_token", "length(\"RequestToken\") = 49 AND substr(\"RequestToken\", 1, 17) = 'feedback-request.' AND substr(\"RequestToken\", 18) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_feedback_report_fingerprint", "length(\"PayloadFingerprint\") = 64 AND \"PayloadFingerprint\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_feedback_report_category", "\"Category\" IN ('Defect', 'Friction', 'Documentation', 'Suggestion', 'Positive')");
                table.HasCheckConstraint("CK_system_feedback_report_impact", "\"Impact\" IN ('Blocked', 'Degraded', 'Minor', 'None')");
                table.HasCheckConstraint("CK_system_feedback_report_state", "\"State\" IN ('Open', 'Acknowledged', 'Resolved', 'Dismissed')");
                table.HasCheckConstraint("CK_system_feedback_report_triage_revision", "\"TriageRevision\" >= 0");
                table.HasCheckConstraint("CK_system_feedback_report_retention_revision", "\"RetentionRevision\" >= 0");
                table.HasCheckConstraint("CK_system_feedback_report_hold_state", "\"HoldState\" IN ('None', 'Held')");
                table.HasCheckConstraint("CK_system_feedback_report_operation", "length(\"SubmissionOperationId\") = 32 AND \"SubmissionOperationId\" NOT GLOB '*[^0-9a-f]*'");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(41);
            entity.Property(x => x.RequestToken).HasMaxLength(49).IsRequired();
            entity.HasIndex(x => x.RequestToken).IsUnique();
            entity.Property(x => x.PayloadFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Impact).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.TriageRevision).IsConcurrencyToken().HasDefaultValue(0).IsRequired();
            entity.Property(x => x.RetentionRevision).IsConcurrencyToken().HasDefaultValue(0).IsRequired();
            entity.Property(x => x.HoldState).HasConversion<string>().HasMaxLength(20).HasDefaultValue(SystemFeedbackHoldState.None).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Observed).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Expected).HasMaxLength(1000);
            entity.Property(x => x.SubmissionOperationId).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.State, x.CreatedAt, x.Id });
            entity.HasIndex(x => new { x.Category, x.CreatedAt, x.Id });
            entity.HasIndex(x => new { x.Impact, x.CreatedAt, x.Id });
            entity.HasIndex(x => new { x.ArchivedAt, x.State, x.Category, x.CreatedAt, x.Id });
            entity.HasIndex(x => new { x.HoldState, x.State, x.CreatedAt, x.Id });
        });
        modelBuilder.Entity<SystemFeedbackStep>(entity =>
        {
            entity.ToTable("system_feedback_step", table => table.HasCheckConstraint("CK_system_feedback_step_ordinal", "\"Ordinal\" BETWEEN 0 AND 7"));
            entity.HasKey(x => x.Id); entity.Property(x => x.ReportId).HasMaxLength(41).IsRequired(); entity.Property(x => x.Text).HasMaxLength(400).IsRequired();
            entity.HasOne(x => x.Report).WithMany(x => x.Steps).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ReportId, x.Ordinal }).IsUnique();
        });
        modelBuilder.Entity<SystemFeedbackOperationReference>(entity =>
        {
            entity.ToTable("system_feedback_operation", table => table.HasCheckConstraint("CK_system_feedback_operation_ordinal", "\"Ordinal\" BETWEEN 0 AND 7"));
            entity.HasKey(x => x.Id); entity.Property(x => x.ReportId).HasMaxLength(41).IsRequired(); entity.Property(x => x.OperationId).HasMaxLength(32).IsRequired();
            entity.HasOne(x => x.Report).WithMany(x => x.OperationReferences).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ReportId, x.Ordinal }).IsUnique(); entity.HasIndex(x => new { x.ReportId, x.OperationId }).IsUnique(); entity.HasIndex(x => x.OperationId);
        });
        modelBuilder.Entity<SystemFeedbackProcedureReference>(entity =>
        {
            entity.ToTable("system_feedback_procedure", table => table.HasCheckConstraint("CK_system_feedback_procedure_ordinal", "\"Ordinal\" BETWEEN 0 AND 7"));
            entity.HasKey(x => x.Id); entity.Property(x => x.ReportId).HasMaxLength(41).IsRequired(); entity.Property(x => x.ProcedureId).HasMaxLength(200).IsRequired();
            entity.HasOne(x => x.Report).WithMany(x => x.ProcedureReferences).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ReportId, x.Ordinal }).IsUnique(); entity.HasIndex(x => new { x.ReportId, x.ProcedureId }).IsUnique(); entity.HasIndex(x => new { x.ProcedureId, x.ProcedureVersion });
        });
        modelBuilder.Entity<SystemFeedbackDisposition>(entity =>
        {
            entity.ToTable("system_feedback_disposition", table =>
            {
                table.HasCheckConstraint("CK_system_feedback_disposition_id", "length(\"Id\") = 53 AND substr(\"Id\", 1, 21) = 'feedback-disposition.' AND substr(\"Id\", 22) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_feedback_disposition_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_system_feedback_disposition_from", "\"FromState\" IN ('Open', 'Acknowledged', 'Resolved', 'Dismissed')");
                table.HasCheckConstraint("CK_system_feedback_disposition_to", "\"ToState\" IN ('Open', 'Acknowledged', 'Resolved', 'Dismissed')");
                table.HasCheckConstraint("CK_system_feedback_disposition_changed", "\"FromState\" <> \"ToState\"");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(53);
            entity.Property(x => x.ReportId).HasMaxLength(41).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FromState).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.ToState).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne(x => x.Report).WithMany(x => x.Dispositions).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ReportId, x.Revision }).IsUnique();
            entity.HasIndex(x => new { x.ToState, x.CreatedAt, x.Id });
        });
        modelBuilder.Entity<SystemFeedbackRetentionAction>(entity =>
        {
            entity.ToTable("system_feedback_retention_action", table =>
            {
                table.HasCheckConstraint("CK_system_feedback_retention_action_id", "length(\"Id\") = 51 AND substr(\"Id\", 1, 19) = 'feedback-retention.' AND substr(\"Id\", 20) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_feedback_retention_action_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_system_feedback_retention_action_action", "\"Action\" IN ('Archive', 'Restore', 'PlaceHold', 'ReleaseHold')");
                table.HasCheckConstraint("CK_system_feedback_retention_action_from_hold", "\"FromHoldState\" IN ('None', 'Held')");
                table.HasCheckConstraint("CK_system_feedback_retention_action_to_hold", "\"ToHoldState\" IN ('None', 'Held')");
                table.HasCheckConstraint("CK_system_feedback_retention_action_changed", "(\"FromArchived\" <> \"ToArchived\") <> (\"FromHoldState\" <> \"ToHoldState\")");
                table.HasCheckConstraint("CK_system_feedback_retention_action_reference", "(\"Action\" IN ('PlaceHold', 'ReleaseHold') AND \"Reference\" IS NOT NULL AND length(\"Reference\") BETWEEN 1 AND 100) OR (\"Action\" IN ('Archive', 'Restore') AND \"Reference\" IS NULL)");
                table.HasCheckConstraint("CK_system_feedback_retention_action_effective_as_of", "(\"Action\" = 'Archive' AND \"EffectiveAsOf\" IS NOT NULL) OR (\"Action\" <> 'Archive' AND \"EffectiveAsOf\" IS NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(51);
            entity.Property(x => x.ReportId).HasMaxLength(41).IsRequired();
            entity.Property(x => x.Action).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.FromHoldState).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.ToHoldState).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(100);
            entity.Property(x => x.Note).HasMaxLength(500).IsRequired();
            entity.HasOne(x => x.Report).WithMany(x => x.RetentionActions).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ReportId, x.Revision }).IsUnique();
            entity.HasIndex(x => new { x.Action, x.CreatedAt, x.Id });
        });
    }

    private static void ConfigureSnapshots(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SnapshotPackage>(entity =>
        {
            entity.ToTable("snapshot_package", table =>
            {
                table.HasCheckConstraint("CK_snapshot_package_id", "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'snapshot.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_snapshot_package_scope_version", "\"ScopeContractVersion\" > 0");
                table.HasCheckConstraint("CK_snapshot_package_producer_version", "\"ProducerVersion\" > 0");
                table.HasCheckConstraint("CK_snapshot_package_encoding", "\"ContentEncoding\" = 'dantes-canonical-json-v1'");
                table.HasCheckConstraint("CK_snapshot_package_boundary_fingerprint", "length(\"BoundaryFingerprint\") = 64 AND \"BoundaryFingerprint\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_snapshot_package_digest_algorithm", "\"DigestAlgorithm\" = 'sha256'");
                table.HasCheckConstraint("CK_snapshot_package_content_digest", "length(\"ContentDigest\") = 64 AND \"ContentDigest\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_snapshot_package_byte_count", "\"ByteCount\" BETWEEN 1 AND 1048576 AND \"ByteCount\" = length(\"Content\")");
                table.HasCheckConstraint("CK_snapshot_package_root_operation", "length(\"RootOperationId\") = 32 AND \"RootOperationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_snapshot_package_availability", "\"Availability\" = 'available'");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(200);
            entity.Property(x => x.ScopeContractId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ScopeContractVersion).IsRequired();
            entity.Property(x => x.ProducerId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProducerVersion).IsRequired();
            entity.Property(x => x.ContentEncoding).HasMaxLength(100).IsRequired();
            entity.Property(x => x.BoundaryFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DigestAlgorithm).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ContentDigest).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ByteCount).IsRequired();
            entity.Property(x => x.CapturedAt).IsRequired();
            entity.Property(x => x.RootOperationId).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Availability).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Content).IsRequired();
        });
    }

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notification");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(40);
            entity.Property(x => x.Topic).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(400).IsRequired();
            entity.Property(x => x.Body).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(40).IsRequired();
            entity.Property(x => x.EventId).HasMaxLength(40);
            entity.Property(x => x.ExecutionId).HasMaxLength(40);
            entity.Property(x => x.RootOperationId).HasMaxLength(40);

            // Stored as text rather than an integer, so a row read outside this application says
            // "unread" instead of "0". The ledger's own audience is people reading a database.
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(20);

            // The two questions actually asked: "what is waiting for me?" and "what came out of
            // this change?". Nothing indexes the body, because nothing should search it.
            entity.HasIndex(x => new { x.State, x.CreatedAt });
            entity.HasIndex(x => new { x.CorrelationId, x.Ordinal });
            entity.HasIndex(x => new { x.Topic, x.CreatedAt });
        });

        modelBuilder.Entity<NotificationEntity>(entity =>
        {
            entity.ToTable("notification_entity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NotificationId).HasMaxLength(40).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(200).IsRequired();

            entity.HasOne(x => x.Notification)
                .WithMany(x => x.Entities)
                .HasForeignKey(x => x.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.EntityId, x.Id });
            entity.HasIndex(x => new { x.NotificationId, x.Ordinal }).IsUnique();
        });
    }

    private static void ConfigureEventLedger(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventRecord>(entity => { entity.ToTable("event"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasMaxLength(40); entity.Property(x => x.TypeId).HasMaxLength(200).IsRequired(); entity.Property(x => x.CorrelationId).HasMaxLength(40).IsRequired(); entity.Property(x => x.CausationId).HasMaxLength(40); entity.Property(x => x.RootOperationId).HasMaxLength(40); entity.Property(x => x.PayloadJson).IsRequired(); entity.HasIndex(x => new { x.CorrelationId, x.Sequence }); entity.HasIndex(x => x.RootOperationId); entity.HasIndex(x => new { x.TypeId, x.Timestamp }); });
        modelBuilder.Entity<EventExecution>(entity =>
        {
            entity.ToTable("event_execution"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(40);
            entity.Property(x => x.EventId).HasMaxLength(40).IsRequired();
            entity.Property(x => x.SubscriptionId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.MechanicId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProjectionJson).IsRequired(); entity.Property(x => x.OutputJson).IsRequired();
            entity.Property(x => x.LogJson).IsRequired(); entity.Property(x => x.LimitHit).HasMaxLength(40);
            entity.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);

            // Ordinal is unique per event so two executions of one chain can never claim the same
            // slot, and the pair is how a chain is read back in order.
            entity.HasIndex(x => new { x.EventId, x.Ordinal }).IsUnique();
            entity.HasIndex(x => x.SubscriptionId);
        });
        modelBuilder.Entity<EventEntity>(entity => { entity.ToTable("event_entity"); entity.HasKey(x => x.Id); entity.Property(x => x.EventId).HasMaxLength(40).IsRequired(); entity.Property(x => x.EntityId).HasMaxLength(200).IsRequired(); entity.HasOne(x => x.Event).WithMany(x => x.Entities).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.EntityId, x.Id }); entity.HasIndex(x => new { x.EventId, x.Ordinal }).IsUnique(); });
    }

    private static void ConfigureSubscriptions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("subscription"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(200); entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(200); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.Category); entity.HasIndex(x => x.Scope); entity.HasIndex(x => x.Status);
        });
        modelBuilder.Entity<SubscriptionVersion>(entity =>
        {
            entity.ToTable("subscription_version"); entity.HasKey(x => x.Id);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200).IsRequired(); entity.Property(x => x.EventTypeId).HasMaxLength(200).IsRequired(); entity.Property(x => x.EventMechanicId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.Property(x => x.FixedRoleEntityIdsJson).IsRequired(); entity.Property(x => x.TrackedEntityIdsJson).IsRequired(); entity.Property(x => x.PayloadEqualsJson).IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired(); entity.Property(x => x.SourceHash).HasMaxLength(64);
            entity.HasOne(x => x.Subscription).WithMany(x => x.Versions).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.SubscriptionId, x.Version }).IsUnique(); entity.HasIndex(x => x.EventTypeId); entity.HasIndex(x => x.EventMechanicId); entity.HasIndex(x => new { x.Mode, x.Order });
        });
    }

    private static void ConfigureEventTypes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventType>(entity => { entity.ToTable("event_type"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasMaxLength(200); entity.Property(x => x.Category).HasMaxLength(100).IsRequired(); entity.Property(x => x.Scope).HasMaxLength(200); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.HasIndex(x => x.Category); entity.HasIndex(x => x.Scope); entity.HasIndex(x => x.Status); });
        modelBuilder.Entity<EventTypeVersion>(entity => { entity.ToTable("event_type_version"); entity.HasKey(x => x.Id); entity.Property(x => x.EventTypeId).HasMaxLength(200).IsRequired(); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.Property(x => x.PayloadSchema).IsRequired(); entity.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired(); entity.Property(x => x.SourceHash).HasMaxLength(64); entity.HasOne(x => x.EventType).WithMany(x => x.Versions).HasForeignKey(x => x.EventTypeId).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.EventTypeId, x.Version }).IsUnique(); });
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
            entity.Property(e => e.MechanicId).HasMaxLength(200);
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
