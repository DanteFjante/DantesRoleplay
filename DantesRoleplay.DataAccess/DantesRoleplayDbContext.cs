using DantesRoleplay.Mechanics;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Snapshots;
using DantesRoleplay.SystemFeedback;
using DantesRoleplay.Information;
using DantesRoleplay.World;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Projections;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.HostSettings;
using DantesRoleplay.Assistants;
using DantesRoleplay.LegacyStateAdoption;
using DantesRoleplay.Interactions;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.Blobs;
using DantesRoleplay.Play;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.CatalogNamespaces;
using System.Text.Json;
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

    public DbSet<HostSettingOverride> HostSettingOverrides => Set<HostSettingOverride>();
    public DbSet<HostSettingOverrideVersion> HostSettingOverrideVersions => Set<HostSettingOverrideVersion>();
    public DbSet<AssistantConversation> AssistantConversations => Set<AssistantConversation>();
    public DbSet<AssistantTurn> AssistantTurns => Set<AssistantTurn>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();
    public DbSet<AssistantTurnActivity> AssistantTurnActivities => Set<AssistantTurnActivity>();
    public DbSet<AssistantTurnApproval> AssistantTurnApprovals => Set<AssistantTurnApproval>();
    public DbSet<ApplicationPlayConversationRecord> ApplicationPlayConversations => Set<ApplicationPlayConversationRecord>();
    public DbSet<ApplicationPlayMessageRecord> ApplicationPlayMessages => Set<ApplicationPlayMessageRecord>();
    public DbSet<ApplicationPlaySituationRecord> ApplicationPlaySituations => Set<ApplicationPlaySituationRecord>();
    public DbSet<ApplicationPlayTruthRecord> ApplicationPlayTruths => Set<ApplicationPlayTruthRecord>();
    public DbSet<SystemTaskRecord> SystemTasks => Set<SystemTaskRecord>();
    public DbSet<SystemTaskRoundRecord> SystemTaskRounds => Set<SystemTaskRoundRecord>();
    public DbSet<SystemTaskStepRecord> SystemTaskSteps => Set<SystemTaskStepRecord>();
    public DbSet<SystemTaskConfirmationRecord> SystemTaskConfirmations => Set<SystemTaskConfirmationRecord>();
    public DbSet<SystemTaskExecutionRecord> SystemTaskExecutions => Set<SystemTaskExecutionRecord>();
    public DbSet<SystemTaskExecutionStepRecord> SystemTaskExecutionSteps => Set<SystemTaskExecutionStepRecord>();
    public DbSet<BlobAsset> BlobAssets => Set<BlobAsset>();
    public DbSet<BlobUploadSession> BlobUploadSessions => Set<BlobUploadSession>();

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
    public DbSet<InformationSource> InformationSources => Set<InformationSource>();
    public DbSet<InformationRecord> InformationRecords => Set<InformationRecord>();
    public DbSet<InformationActionContract> InformationActionContracts => Set<InformationActionContract>();
    public DbSet<InteractionResolutionReceipt> InteractionResolutionReceipts => Set<InteractionResolutionReceipt>();
    public DbSet<InteractionExecutionReceipt> InteractionExecutionReceipts => Set<InteractionExecutionReceipt>();
    public DbSet<InteractionExecutionReceiptStep> InteractionExecutionReceiptSteps => Set<InteractionExecutionReceiptStep>();
    public DbSet<InteractionExecutionQueryResult> InteractionExecutionQueryResults => Set<InteractionExecutionQueryResult>();
    public DbSet<InteractionRecipe> InteractionRecipes => Set<InteractionRecipe>();
    public DbSet<InteractionRecipeRevision> InteractionRecipeRevisions => Set<InteractionRecipeRevision>();
    public DbSet<InteractionRecipeEvidence> InteractionRecipeEvidence => Set<InteractionRecipeEvidence>();
    public DbSet<TriggerObservationStructureRecord> TriggerObservationStructures => Set<TriggerObservationStructureRecord>();
    public DbSet<TriggerObservationStructureCurrentRecord> TriggerObservationStructureCurrent => Set<TriggerObservationStructureCurrentRecord>();
    public DbSet<TriggerObservationSourceRecord> TriggerObservationSources => Set<TriggerObservationSourceRecord>();
    public DbSet<TriggerObservationSourceCurrentRecord> TriggerObservationSourceCurrent => Set<TriggerObservationSourceCurrentRecord>();
    public DbSet<TriggerObservationSourceStructureRecord> TriggerObservationSourceStructures => Set<TriggerObservationSourceStructureRecord>();
    public DbSet<TriggerObservationSourcePrincipalRecord> TriggerObservationSourcePrincipals => Set<TriggerObservationSourcePrincipalRecord>();
    public DbSet<OneTimeTriggerRecord> OneTimeTriggers => Set<OneTimeTriggerRecord>();
    public DbSet<OneTimeTriggerNotificationEntityRecord> OneTimeTriggerNotificationEntities => Set<OneTimeTriggerNotificationEntityRecord>();
    public DbSet<OneTimeTriggerCurrentRecord> OneTimeTriggerCurrent => Set<OneTimeTriggerCurrentRecord>();
    public DbSet<TriggerObservationRecord> TriggerObservations => Set<TriggerObservationRecord>();
    public DbSet<TriggerFireReceiptRecord> TriggerFireReceipts => Set<TriggerFireReceiptRecord>();
    public DbSet<TriggerFireWorkRecord> TriggerFireWork => Set<TriggerFireWorkRecord>();
    public DbSet<TriggerNotificationLinkRecord> TriggerNotificationLinks => Set<TriggerNotificationLinkRecord>();
    public DbSet<RecurringTriggerRecord> RecurringTriggers => Set<RecurringTriggerRecord>();
    public DbSet<RecurringTriggerNotificationEntityRecord> RecurringTriggerNotificationEntities => Set<RecurringTriggerNotificationEntityRecord>();
    public DbSet<RecurringTriggerCurrentRecord> RecurringTriggerCurrent => Set<RecurringTriggerCurrentRecord>();
    public DbSet<RecurringTriggerStateRecord> RecurringTriggerState => Set<RecurringTriggerStateRecord>();
    public DbSet<RecurringTriggerFireWorkRecord> RecurringTriggerFireWork => Set<RecurringTriggerFireWorkRecord>();
    public DbSet<RecurringTriggerFireReceiptRecord> RecurringTriggerFireReceipts => Set<RecurringTriggerFireReceiptRecord>();
    public DbSet<RecurringTriggerNotificationLinkRecord> RecurringTriggerNotificationLinks => Set<RecurringTriggerNotificationLinkRecord>();
    public DbSet<ConditionalTriggerRecord> ConditionalTriggers => Set<ConditionalTriggerRecord>();
    public DbSet<ConditionalTriggerDependencyRecord> ConditionalTriggerDependencies => Set<ConditionalTriggerDependencyRecord>();
    public DbSet<ConditionalTriggerNotificationEntityRecord> ConditionalTriggerNotificationEntities => Set<ConditionalTriggerNotificationEntityRecord>();
    public DbSet<ConditionalTriggerCurrentRecord> ConditionalTriggerCurrent => Set<ConditionalTriggerCurrentRecord>();
    public DbSet<ConditionalTriggerStateRecord> ConditionalTriggerState => Set<ConditionalTriggerStateRecord>();
    public DbSet<ConditionalTriggerFireWorkRecord> ConditionalTriggerFireWork => Set<ConditionalTriggerFireWorkRecord>();
    public DbSet<ConditionalTriggerFireReceiptRecord> ConditionalTriggerFireReceipts => Set<ConditionalTriggerFireReceiptRecord>();
    public DbSet<ConditionalTriggerNotificationLinkRecord> ConditionalTriggerNotificationLinks => Set<ConditionalTriggerNotificationLinkRecord>();
    public DbSet<ObservationTriggerRecord> ObservationTriggers => Set<ObservationTriggerRecord>();
    public DbSet<ObservationTriggerNotificationEntityRecord> ObservationTriggerNotificationEntities => Set<ObservationTriggerNotificationEntityRecord>();
    public DbSet<ObservationTriggerCurrentRecord> ObservationTriggerCurrent => Set<ObservationTriggerCurrentRecord>();
    public DbSet<ObservationTriggerMatchWorkRecord> ObservationTriggerMatchWork => Set<ObservationTriggerMatchWorkRecord>();
    public DbSet<ObservationTriggerMatchReceiptRecord> ObservationTriggerMatchReceipts => Set<ObservationTriggerMatchReceiptRecord>();
    public DbSet<ObservationTriggerNotificationLinkRecord> ObservationTriggerNotificationLinks => Set<ObservationTriggerNotificationLinkRecord>();
    public DbSet<PhoneCompanionDeviceRecord> PhoneCompanionDevices => Set<PhoneCompanionDeviceRecord>();
    public DbSet<PhoneCompanionDeviceStructureRecord> PhoneCompanionDeviceStructures => Set<PhoneCompanionDeviceStructureRecord>();
    public DbSet<PhoneCompanionDeviceStatusRecord> PhoneCompanionDeviceStatuses => Set<PhoneCompanionDeviceStatusRecord>();
    public DbSet<PhoneCompanionDeviceCurrentRecord> PhoneCompanionDeviceCurrent => Set<PhoneCompanionDeviceCurrentRecord>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardCatalogNamespaceAssignments();
        GuardImmutableTriggerSchedulingRows();
        GuardImmutableNotificationContent();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardCatalogNamespaceAssignments();
        GuardImmutableTriggerSchedulingRows();
        GuardImmutableNotificationContent();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProcedures(modelBuilder);
        ConfigureOperations(modelBuilder);
        ConfigureInteractionReceipts(modelBuilder);
        ConfigureInteractionRecipes(modelBuilder);
        ConfigureTriggerScheduling(modelBuilder);
        ConfigureHostSettings(modelBuilder);
        ConfigureAssistantConversations(modelBuilder);
        ConfigurePlayRecording(modelBuilder);
        ConfigureSystemTasks(modelBuilder);
        ConfigureBlobStorage(modelBuilder);
        ConfigureWorld(modelBuilder);
        ConfigureMechanics(modelBuilder);
        ConfigureEventTypes(modelBuilder);
        ConfigureSubscriptions(modelBuilder);
        ConfigureEventLedger(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureSnapshots(modelBuilder);
        ConfigureSystemFeedback(modelBuilder);
        ConfigureRetainedWorkflowTables(modelBuilder);
        ConfigureInformation(modelBuilder);
        ConfigureApplicationRegistry(modelBuilder);
        ConfigureSourceRegistry(modelBuilder);
        ConfigureCatalogNamespaces(modelBuilder);
        ConfigureComponentTypes(modelBuilder);
        ConfigureApplicationScopedEcs(modelBuilder);
        ConfigureProjectionMaterialization(modelBuilder);
        ConfigureApplicationActivation(modelBuilder);
        ConfigureLegacyStateAdoption(modelBuilder);
    }

    private static void ConfigurePlayRecording(ModelBuilder modelBuilder)
    {
        const string conversationStatuses = "'ready', 'planning', 'awaiting-confirmation', 'needs-attention', 'unavailable'";
        const string situationKinds = "'out-of-character', 'conversation', 'combat', 'exploration', 'investigation', 'travel', 'rest', 'downtime', 'other'";
        modelBuilder.Entity<ApplicationPlayConversationRecord>(entity =>
        {
            entity.ToTable("application_play_conversation", table =>
            {
                table.HasCheckConstraint("CK_application_play_conversation_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_application_play_conversation_status", $"\"Status\" IN ({conversationStatuses})");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.PrincipalId).HasMaxLength(100).IsRequired();
            entity.Property(value => value.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(value => value.StateSpaceId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.SessionContextId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(30).IsRequired();
            entity.Property(value => value.Revision).IsConcurrencyToken();
            entity.Property(value => value.CurrentSituationId).HasMaxLength(80);
            entity.HasIndex(value => new
            {
                value.PrincipalId,
                value.ApplicationId,
                value.StateSpaceId,
                value.SessionContextId
            }).IsUnique();
            entity.HasIndex(value => new { value.PrincipalId, value.ApplicationId, value.UpdatedAtUtc, value.Id });
            entity.HasOne<ApplicationStateSpaceRecord>().WithMany().HasForeignKey(value => value.StateSpaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationPlayMessageRecord>(entity =>
        {
            entity.ToTable("application_play_message", table =>
            {
                table.HasCheckConstraint("CK_application_play_message_ordinal", "\"Ordinal\" > 0");
                table.HasCheckConstraint("CK_application_play_message_role", "\"Role\" IN ('player', 'assistant')");
                table.HasCheckConstraint("CK_application_play_message_text", "length(\"Text\") BETWEEN 1 AND 8000");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.ConversationId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.Role).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Text).HasMaxLength(8_000).IsRequired();
            entity.Property(value => value.Code).HasMaxLength(100).IsRequired();
            entity.Property(value => value.SituationId).HasMaxLength(80);
            entity.HasIndex(value => new { value.ConversationId, value.Ordinal }).IsUnique();
            entity.HasIndex(value => new { value.ConversationId, value.CreatedAtUtc, value.Id });
            entity.HasOne(value => value.Conversation).WithMany(value => value.Messages)
                .HasForeignKey(value => value.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationPlaySituationRecord>(entity =>
        {
            entity.ToTable("application_play_situation", table =>
            {
                table.HasCheckConstraint("CK_application_play_situation_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_application_play_situation_kind", $"\"Kind\" IN ({situationKinds})");
                table.HasCheckConstraint("CK_application_play_situation_status", "\"Status\" IN ('active', 'completed')");
                table.HasCheckConstraint("CK_application_play_situation_json", "json_valid(\"ParticipantsJson\") AND (\"LocationJson\" = '' OR json_valid(\"LocationJson\"))");
                table.HasCheckConstraint("CK_application_play_situation_summary", "length(\"Summary\") BETWEEN 1 AND 1000");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.ConversationId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.Kind).HasMaxLength(30).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Summary).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.ParticipantsJson).HasMaxLength(16_000).IsRequired();
            entity.Property(value => value.LocationJson).HasMaxLength(1_000).IsRequired();
            entity.HasIndex(value => new { value.ConversationId, value.StartedAtUtc, value.Id });
            entity.HasOne(value => value.Conversation).WithMany(value => value.Situations)
                .HasForeignKey(value => value.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationPlayTruthRecord>(entity =>
        {
            entity.ToTable("application_play_truth", table =>
            {
                table.HasCheckConstraint("CK_application_play_truth_ordinal", "\"Ordinal\" > 0");
                table.HasCheckConstraint("CK_application_play_truth_statement", "length(\"Statement\") BETWEEN 1 AND 1000");
                table.HasCheckConstraint("CK_application_play_truth_hash", "length(\"NormalizedHash\") = 64 AND \"NormalizedHash\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_application_play_truth_subjects", "json_valid(\"SubjectEntityIdsJson\") AND json_type(\"SubjectEntityIdsJson\") = 'array'");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.ConversationId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.Statement).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.NormalizedHash).HasMaxLength(64).IsRequired();
            entity.Property(value => value.SubjectEntityIdsJson).HasMaxLength(8_000).IsRequired();
            entity.Property(value => value.SourceMessageId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.SituationId).HasMaxLength(80);
            entity.HasIndex(value => new { value.ConversationId, value.Ordinal }).IsUnique();
            entity.HasIndex(value => new { value.ConversationId, value.NormalizedHash }).IsUnique();
            entity.HasOne(value => value.Conversation).WithMany(value => value.Truths)
                .HasForeignKey(value => value.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void GuardCatalogNamespaceAssignments()
    {
        var assignments = ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity switch
            {
                Mechanic value => (value.Id, CatalogNamespaceKinds.Mechanic),
                ProcedureContract value => (value.Id, CatalogNamespaceKinds.Procedure),
                ComponentDefinition value => (value.Id, CatalogNamespaceKinds.ComponentDefinition),
                ComponentTypeRecord value => (value.QualifiedId, CatalogNamespaceKinds.ComponentType),
                EventType value => (value.Id, CatalogNamespaceKinds.EventType),
                Subscription value => (value.Id, CatalogNamespaceKinds.Subscription),
                Entity value => (value.Id, CatalogNamespaceKinds.Entity),
                _ => default
            })
            .Where(value => value.Item1 is not null)
            .ToArray();
        if (assignments.Length == 0) return;

        var definitions = Set<CatalogNamespaceRecord>().AsNoTracking().ToArray()
            .ToDictionary(value => value.Id, StringComparer.Ordinal);
        foreach (var entry in ChangeTracker.Entries<CatalogNamespaceRecord>()
                     .Where(value => value.State is EntityState.Added or EntityState.Modified))
            definitions[entry.Entity.Id] = entry.Entity;

        // An empty registry is the explicit adoption boundary for an existing database. Once its
        // first namespace is registered, every new authored identity is checked here regardless of
        // which store or service attempted the write.
        if (definitions.Count == 0) return;

        foreach (var (id, kind) in assignments)
        {
            var namespaceId = CatalogNamespaceIdentity.NamespaceOf(id!);
            if (!definitions.TryGetValue(namespaceId, out var definition))
                throw new CatalogNamespaceException("NAMESPACE_UNKNOWN",
                    $"Record '{id}' uses unregistered namespace '{namespaceId}'.");
            if (!AllowUnreviewedNamespaceWrites
                && definition.ReviewStatus != CatalogNamespaceReviewStatuses.Reviewed)
                throw new CatalogNamespaceException("NAMESPACE_UNREVIEWED",
                    $"Record '{id}' uses namespace '{namespaceId}', which still needs review.");
            var current = definition;
            while (true)
            {
                if (current.DisabledAtUtc is not null)
                    throw new CatalogNamespaceException("NAMESPACE_DISABLED",
                        $"Record '{id}' uses disabled namespace '{current.Id}'.");
                if (current.ParentId is null) break;
                var parentId = current.ParentId;
                if (!definitions.TryGetValue(parentId, out current!))
                    throw new CatalogNamespaceException("NAMESPACE_PARENT_UNKNOWN",
                        $"Namespace '{namespaceId}' has missing parent '{parentId}'.");
            }
            var allowed = JsonSerializer.Deserialize<string[]>(definition.AllowedKindsJson) ?? [];
            if (!allowed.Contains(kind, StringComparer.Ordinal))
                throw new CatalogNamespaceException("NAMESPACE_KIND_FORBIDDEN",
                    $"Namespace '{namespaceId}' does not allow '{kind}' records such as '{id}'.");
        }
    }

    private static void ConfigureCatalogNamespaces(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogNamespaceRecord>(entity =>
        {
            entity.ToTable("system_catalog_namespace", table =>
            {
                table.HasCheckConstraint("CK_system_catalog_namespace_kinds", "json_valid(\"AllowedKindsJson\") AND json_type(\"AllowedKindsJson\") = 'array'");
                table.HasCheckConstraint("CK_system_catalog_namespace_aliases", "json_valid(\"AliasesJson\") AND json_type(\"AliasesJson\") = 'array'");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(200);
            entity.Property(value => value.ParentId).HasMaxLength(200);
            entity.Property(value => value.Owner).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Description).HasMaxLength(2_000).IsRequired();
            entity.Property(value => value.AllowedKindsJson).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.AliasesJson).HasMaxLength(4_000).IsRequired();
            entity.Property(value => value.ReviewStatus).HasMaxLength(20).IsRequired();
            entity.Property(value => value.ReviewNote).HasMaxLength(2_000).IsRequired();
            entity.HasIndex(value => new { value.DisabledAtUtc, value.Id });
            entity.HasOne<CatalogNamespaceRecord>().WithMany().HasForeignKey(value => value.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CatalogNamespaceOverlayProfileRecord>(entity =>
        {
            entity.ToTable("system_catalog_namespace_overlay_profile");
            entity.HasKey(value => new { value.ApplicationId, value.ProfileId });
            entity.Property(value => value.ApplicationId).HasMaxLength(63);
            entity.Property(value => value.ProfileId).HasMaxLength(63);
            entity.Property(value => value.Description).HasMaxLength(2_000).IsRequired();
        });

        modelBuilder.Entity<CatalogResolutionKeyRecord>(entity =>
        {
            entity.ToTable("system_catalog_namespace_resolution_key");
            entity.HasKey(value => new { value.ApplicationId, value.ProfileId, value.ResolutionKey });
            entity.Property(value => value.ApplicationId).HasMaxLength(63);
            entity.Property(value => value.ProfileId).HasMaxLength(63);
            entity.Property(value => value.ResolutionKey).HasMaxLength(200);
            entity.Property(value => value.RecordKind).HasMaxLength(40).IsRequired();
            entity.Property(value => value.Description).HasMaxLength(2_000).IsRequired();
            entity.HasOne<CatalogNamespaceOverlayProfileRecord>().WithMany()
                .HasForeignKey(value => new { value.ApplicationId, value.ProfileId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CatalogNamespaceOverlayRecord>(entity =>
        {
            entity.ToTable("system_catalog_namespace_overlay");
            entity.HasKey(value => new
                { value.ApplicationId, value.ProfileId, value.HigherNamespaceId, value.LowerNamespaceId, value.RecordKind });
            entity.Property(value => value.ApplicationId).HasMaxLength(63);
            entity.Property(value => value.ProfileId).HasMaxLength(63);
            entity.Property(value => value.HigherNamespaceId).HasMaxLength(200);
            entity.Property(value => value.LowerNamespaceId).HasMaxLength(200);
            entity.Property(value => value.RecordKind).HasMaxLength(40);
            entity.HasOne<CatalogNamespaceOverlayProfileRecord>().WithMany()
                .HasForeignKey(value => new { value.ApplicationId, value.ProfileId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CatalogNamespaceRecord>().WithMany().HasForeignKey(value => value.HigherNamespaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CatalogNamespaceRecord>().WithMany().HasForeignKey(value => value.LowerNamespaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal bool AllowUnreviewedNamespaceWrites { get; set; }

    internal IDisposable PermitUnreviewedNamespaceImport()
    {
        var previous = AllowUnreviewedNamespaceWrites;
        AllowUnreviewedNamespaceWrites = true;
        return new NamespaceImportScope(this, previous);
    }

    private sealed class NamespaceImportScope(DantesRoleplayDbContext db, bool previous) : IDisposable
    {
        public void Dispose() => db.AllowUnreviewedNamespaceWrites = previous;
    }

    private static void ConfigureBlobStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlobAsset>(entity =>
        {
            entity.ToTable("blob_asset", table =>
            {
                table.HasCheckConstraint("CK_blob_asset_sha256",
                    "length(\"Sha256\") = 64 AND \"Sha256\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_blob_asset_media_type",
                    "\"MediaType\" IN ('image/png', 'image/jpeg', 'image/webp')");
                table.HasCheckConstraint("CK_blob_asset_byte_length",
                    $"\"ByteLength\" BETWEEN 1 AND {BlobStorageOptions.MaximumByteLength}");
            });
            entity.HasKey(value => value.Sha256);
            entity.Property(value => value.Sha256).HasMaxLength(64);
            entity.Property(value => value.MediaType).HasMaxLength(20).IsRequired();
            entity.Ignore(value => value.AssetKey);
            entity.Ignore(value => value.ResourceUri);
            entity.Ignore(value => value.DownloadPath);
        });

        modelBuilder.Entity<BlobUploadSession>(entity =>
        {
            entity.ToTable("blob_upload_session", table =>
            {
                table.HasCheckConstraint("CK_blob_upload_session_id",
                    "length(\"Id\") = 44 AND substr(\"Id\", 1, 12) = 'blob-upload.' AND substr(\"Id\", 13) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_blob_upload_session_hashes",
                    "length(\"TokenHash\") = 64 AND \"TokenHash\" NOT GLOB '*[^0-9a-f]*' AND length(\"ExpectedSha256\") = 64 AND \"ExpectedSha256\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_blob_upload_session_state",
                    "\"State\" IN ('pending', 'uploaded', 'finalized')");
                table.HasCheckConstraint("CK_blob_upload_session_media_type",
                    "\"MediaType\" IN ('image/png', 'image/jpeg', 'image/webp')");
                table.HasCheckConstraint("CK_blob_upload_session_byte_length",
                    $"\"ExpectedByteLength\" BETWEEN 1 AND {BlobStorageOptions.MaximumByteLength}");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(44);
            entity.Property(value => value.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(value => value.ExpectedSha256).HasMaxLength(64).IsRequired();
            entity.Property(value => value.MediaType).HasMaxLength(20).IsRequired();
            entity.Property(value => value.State).HasMaxLength(10).IsRequired();
            entity.HasIndex(value => value.ExpiresAtUtc);
            entity.HasIndex(value => new { value.ExpectedSha256, value.State });
        });
    }

    // The compiled Story adapter is no longer part of this host, but these two tables already
    // belong to its migration history. Keep an untyped mapping so EF preserves that history and
    // can initialize an existing database without importing a Story CLR contract or generating a
    // destructive migration. No generic service reads or writes these retained records.
    private static void ConfigureRetainedWorkflowTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("DantesRoleplay.Story.StoryPlanRun", entity =>
        {
            entity.ToTable("story_plan_run", table =>
            {
                table.HasCheckConstraint("CK_story_plan_run_id", "length(\"Id\") = 43 AND substr(\"Id\", 1, 11) = 'story-plan.' AND substr(\"Id\", 12) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_story_plan_run_status", "\"Status\" IN ('pending', 'running', 'completed', 'blocked', 'failed', 'cancelled')");
                table.HasCheckConstraint("CK_story_plan_run_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_story_plan_run_step_counts", "\"NextStepIndex\" >= 0 AND \"CompletedStepCount\" >= 0");
            });
            entity.Property<string>("Id").HasMaxLength(43);
            entity.Property<string>("CampaignId").IsRequired().HasMaxLength(200);
            entity.Property<bool>("CancelRequested");
            entity.Property<int>("CompletedStepCount");
            entity.Property<DateTime>("CreatedAtUtc");
            entity.Property<string>("HandoffJson").HasMaxLength(32000);
            entity.Property<string>("LeaseOwner").HasMaxLength(100);
            entity.Property<DateTime?>("LeaseUntilUtc");
            entity.Property<int>("NextStepIndex");
            entity.Property<string>("Objective").IsRequired().HasMaxLength(1000);
            entity.Property<string>("PlanJson").IsRequired().HasMaxLength(16000);
            entity.Property<string>("PolicyRevision").IsRequired().HasMaxLength(200);
            entity.Property<string>("PrincipalId").IsRequired().HasMaxLength(200);
            entity.Property<string>("RequestToken").IsRequired().HasMaxLength(100);
            entity.Property<int>("Revision").IsConcurrencyToken();
            entity.Property<string>("Status").IsRequired().HasMaxLength(20);
            entity.Property<string>("StopCode").IsRequired().HasMaxLength(100);
            entity.Property<string>("StopMessage").IsRequired().HasMaxLength(1000);
            entity.Property<DateTime>("UpdatedAtUtc");
            entity.HasKey("Id");
            entity.HasIndex("RequestToken").IsUnique();
            entity.HasIndex("Status", "LeaseUntilUtc", "UpdatedAtUtc");
        });

        modelBuilder.Entity("DantesRoleplay.Story.StoryPlanStepRun", entity =>
        {
            entity.ToTable("story_plan_step_run", table =>
            {
                table.HasCheckConstraint("CK_story_plan_step_run_index", "\"StepIndex\" BETWEEN 0 AND 5");
                table.HasCheckConstraint("CK_story_plan_step_run_kind", "\"Kind\" IN ('campaign-context', 'knowledge', 'action')");
                table.HasCheckConstraint("CK_story_plan_step_run_status", "\"Status\" IN ('pending', 'running', 'completed', 'blocked', 'failed', 'skipped')");
            });
            entity.Property<string>("StoryPlanId").HasMaxLength(43);
            entity.Property<int>("StepIndex");
            entity.Property<string>("ActionOperationId").IsRequired().HasMaxLength(32);
            entity.Property<DateTime?>("CompletedAtUtc");
            entity.Property<string>("ErrorCode").IsRequired().HasMaxLength(100);
            entity.Property<string>("ErrorMessage").IsRequired().HasMaxLength(1000);
            entity.Property<string>("InputJson").IsRequired().HasMaxLength(4000);
            entity.Property<string>("Intent").IsRequired().HasMaxLength(500);
            entity.Property<string>("Kind").IsRequired().HasMaxLength(20);
            entity.Property<string>("MechanicId").IsRequired().HasMaxLength(200);
            entity.Property<int?>("MechanicVersion");
            entity.Property<string>("ProcedureEvidenceJson").IsRequired().HasMaxLength(4000);
            entity.Property<string>("ResultJson").IsRequired().HasMaxLength(32000);
            entity.Property<string>("RoleEntityIdsJson").IsRequired().HasMaxLength(4000);
            entity.Property<DateTime?>("StartedAtUtc");
            entity.Property<string>("Status").IsRequired().HasMaxLength(20);
            entity.Property<string>("StepId").IsRequired().HasMaxLength(40);
            entity.HasKey("StoryPlanId", "StepIndex");
            entity.HasOne("DantesRoleplay.Story.StoryPlanRun", "StoryPlan")
                .WithMany("Steps")
                .HasForeignKey("StoryPlanId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });
    }

    private static void ConfigureApplicationRegistry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationRegistryRecord>(entity =>
        {
            entity.ToTable("system_application", table =>
            {
                table.HasCheckConstraint("CK_system_application_id", "\"Id\" <> 'system'");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(63);
            entity.Property(x => x.DisplayName).IsRequired();
            entity.Property(x => x.Description).IsRequired();
        });

        modelBuilder.Entity<ApplicationRevisionRecord>(entity =>
        {
            entity.ToTable("system_application_revision", table =>
            {
                table.HasCheckConstraint("CK_system_application_revision_number", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_system_application_revision_fingerprint", "length(\"Fingerprint\") = 64 AND \"Fingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.Revision });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.HasOne<ApplicationRegistryRecord>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationRevisionBaseRecord>(entity =>
        {
            entity.ToTable("system_application_revision_base", table =>
            {
                table.HasCheckConstraint("CK_system_application_revision_base_ordinal", "\"Ordinal\" >= 0");
            });
            entity.HasKey(x => new { x.ApplicationId, x.Revision, x.Ordinal });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.BaseApplicationId).HasMaxLength(63);
            entity.HasIndex(x => new { x.ApplicationId, x.Revision, x.BaseApplicationId }).IsUnique();
            entity.HasOne<ApplicationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.Revision })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationRegistryRecord>()
                .WithMany()
                .HasForeignKey(x => x.BaseApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureApplicationActivation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationActivationRevisionRecord>(entity =>
        {
            entity.ToTable("system_application_activation_revision", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_revision_number", "\"ActivationRevision\" > 0 AND \"ApplicationRevision\" > 0");
                table.HasCheckConstraint("CK_system_application_activation_revision_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"PreviewFingerprint\") = 64 AND \"PreviewFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ScannedDocumentsFingerprint\") = 64 AND \"ScannedDocumentsFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"CandidateManifestFingerprint\") = 64 AND \"CandidateManifestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"DependencyGraphFingerprint\") = 64 AND \"DependencyGraphFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ResolutionFingerprint\") = 64 AND \"ResolutionFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ActivationFingerprint\") = 64 AND \"ActivationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.ApplicationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PreviewFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ScannedDocumentsFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CandidateManifestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DependencyGraphFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResolutionFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ActivationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DependencyCoverageVersion).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ActivatedByOperationId).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationFingerprint });
            entity.HasOne<ApplicationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, Revision = x.ApplicationRevision })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Operation>()
                .WithMany()
                .HasForeignKey(x => x.ActivatedByOperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationActivationCurrentRecord>(entity =>
        {
            entity.ToTable("system_application_activation_current", table =>
                table.HasCheckConstraint("CK_system_application_activation_current_revision", "\"ActivationRevision\" > 0"));
            entity.HasKey(x => x.ApplicationId);
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationActivationSourceRecord>(entity =>
        {
            entity.ToTable("system_application_activation_source", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_source_counts", "\"Ordinal\" >= 0 AND \"DocumentCount\" >= 0 AND \"ProblemCount\" >= 0");
                table.HasCheckConstraint("CK_system_application_activation_source_hash", "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RegistrationFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationRevision, x.SourceId }).IsUnique();
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationActivationExtensionRecord>(entity =>
        {
            entity.ToTable("system_application_activation_extension", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_extension_values", "\"Ordinal\" >= 0 AND length(\"SourceIdsJson\") >= 2 AND json_valid(\"SourceIdsJson\") AND length(\"NamespaceIdsJson\") >= 2 AND json_valid(\"NamespaceIdsJson\") AND length(\"HigherPriorityThanJson\") >= 2 AND json_valid(\"HigherPriorityThanJson\")");
                table.HasCheckConstraint("CK_system_application_activation_extension_hash", "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.ExtensionId).HasMaxLength(63).IsRequired();
            entity.Property(x => x.RegistrationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SourceIdsJson).IsRequired();
            entity.Property(x => x.NamespaceIdsJson).IsRequired();
            entity.Property(x => x.HigherPriorityThanJson).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationRevision, x.ExtensionId }).IsUnique();
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationActivationDocumentRecord>(entity =>
        {
            entity.ToTable("system_application_activation_document", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_document_values", "\"Ordinal\" >= 0 AND \"Trust\" IN (0, 1) AND \"Length\" >= 0");
                table.HasCheckConstraint("CK_system_application_activation_document_hash", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.LogicalIdentity).HasMaxLength(1200).IsRequired();
            entity.Property(x => x.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RelativePath).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.MediaType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContentFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationRevision, x.LogicalIdentity }).IsUnique();
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationActivationReceiptRecord>(entity =>
        {
            entity.ToTable("system_application_activation_receipt", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_receipt_hash", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_application_activation_receipt_outcome", "\"Outcome\" IN ('activated', 'unchanged')");
            });
            entity.HasKey(x => x.OperationId);
            entity.Property(x => x.OperationId).HasMaxLength(200);
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
            entity.HasOne<Operation>()
                .WithMany()
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSourceRegistry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationSourceRecord>(entity =>
        {
            entity.ToTable("system_application_source", table =>
            {
                table.HasCheckConstraint("CK_system_application_source_trust", "\"Trust\" IN (0, 1)");
            });
            entity.HasKey(x => new { x.ApplicationId, x.SourceId });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.SourceId).IsRequired();
            entity.Property(x => x.AllowedRootId).IsRequired();
            entity.Property(x => x.RelativePathOrGlob).IsRequired();
            entity.Property(x => x.LogicalIdentity).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.LogicalIdentity, x.Precedence }).IsUnique();
            entity.HasOne<ApplicationRegistryRecord>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationSourceScanRecord>(entity =>
        {
            entity.ToTable("system_application_source_scan", table =>
            {
                table.HasCheckConstraint("CK_system_application_source_scan_generation", "\"Generation\" > 0");
                table.HasCheckConstraint("CK_system_application_source_scan_status", "\"Status\" IN (0, 1)");
                table.HasCheckConstraint("CK_system_application_source_scan_fingerprint", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.SourceId, x.Generation });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.SourceId).IsRequired();
            entity.Property(x => x.ContentFingerprint).HasMaxLength(64).IsRequired();
            entity.HasOne<ApplicationSourceRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.SourceId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationExtensionRecord>(entity =>
        {
            entity.ToTable("system_application_extension", table =>
            {
                table.HasCheckConstraint("CK_system_application_extension_id",
                    "length(\"ExtensionId\") BETWEEN 1 AND 63 AND \"ExtensionId\" <> 'base'");
                table.HasCheckConstraint("CK_system_application_extension_json",
                    "json_valid(\"SourceIdsJson\") AND json_valid(\"NamespaceIdsJson\") AND json_valid(\"DependenciesJson\") AND json_valid(\"ConflictsWithJson\") AND json_valid(\"HigherPriorityThanJson\")");
                table.HasCheckConstraint("CK_system_application_extension_fingerprint",
                    "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(value => new { value.ApplicationId, value.ExtensionId });
            entity.Property(value => value.ApplicationId).HasMaxLength(63);
            entity.Property(value => value.ExtensionId).HasMaxLength(63);
            entity.Property(value => value.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(value => value.Description).HasMaxLength(2_000).IsRequired();
            entity.Property(value => value.Classification).HasMaxLength(20).IsRequired();
            entity.Property(value => value.SourceIdsJson).HasMaxLength(20_000).IsRequired();
            entity.Property(value => value.NamespaceIdsJson).HasMaxLength(20_000).IsRequired();
            entity.Property(value => value.DependenciesJson).HasMaxLength(10_000).IsRequired();
            entity.Property(value => value.ConflictsWithJson).HasMaxLength(10_000).IsRequired();
            entity.Property(value => value.HigherPriorityThanJson).HasMaxLength(10_000).IsRequired();
            entity.Property(value => value.RegistrationFingerprint).HasMaxLength(64).IsRequired();
            entity.HasOne<ApplicationRegistryRecord>().WithMany()
                .HasForeignKey(value => value.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureComponentTypes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ComponentTypeRecord>(entity =>
        {
            entity.ToTable("system_component_type");
            entity.HasKey(x => x.QualifiedId);
            entity.Property(x => x.QualifiedId).HasMaxLength(200);
            entity.Property(x => x.ApplicationId).HasMaxLength(63).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.DisabledAtUtc, x.QualifiedId });
            entity.HasOne<ApplicationRegistryRecord>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ComponentTypeVersionRecord>(entity =>
        {
            entity.ToTable("system_component_type_version", table =>
            {
                table.HasCheckConstraint("CK_system_component_type_version_number", "\"Version\" > 0");
                table.HasCheckConstraint("CK_system_component_type_version_hash", "length(\"SchemaHash\") = 64 AND \"SchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_component_type_version_profile", "\"ProfileId\" IN ('system-json-schema-2020-12/v1', 'system-json-schema-2020-12/v2')");
                table.HasCheckConstraint("CK_system_component_type_version_schema_json", "json_valid(\"SchemaJson\")");
            });
            entity.HasKey(x => new { x.QualifiedId, x.Version });
            entity.Property(x => x.QualifiedId).HasMaxLength(200);
            entity.Property(x => x.ProfileId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SchemaJson).HasMaxLength(SystemJsonSchemaProfile.MaximumSchemaBytes).IsRequired();
            entity.Property(x => x.SchemaHash).HasMaxLength(64).IsRequired();
            entity.HasOne<ComponentTypeRecord>()
                .WithMany()
                .HasForeignKey(x => x.QualifiedId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureApplicationScopedEcs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationStateSpaceRecord>(entity =>
        {
            entity.ToTable("system_state_space", table =>
            {
                table.HasCheckConstraint("CK_system_state_space_revision", "\"ApplicationRevision\" > 0");
                table.HasCheckConstraint("CK_system_state_space_manifest", "length(\"ManifestFingerprint\") = 64 AND \"ManifestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ResolutionFingerprint\") = 64 AND \"ResolutionFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_state_space_scope", "\"Scope\" IN ('runtime-state-space', 'application-publication')");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(200);
            entity.Property(x => x.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(x => x.ManifestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResolutionFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BindingRevision).HasDefaultValue(1);
            entity.HasIndex(x => x.ApplicationId).IsUnique()
                .HasFilter("\"Scope\" = 'application-publication'");
            entity.HasOne<ApplicationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, Revision = x.ApplicationRevision })
                .HasPrincipalKey(x => new { x.ApplicationId, x.Revision })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StateSpaceBindingRevisionRecord>(entity =>
        {
            entity.ToTable("system_state_space_binding_revision", table =>
            {
                table.HasCheckConstraint("CK_system_state_space_binding_revision", "\"BindingRevision\" > 0");
                table.HasCheckConstraint("CK_system_state_space_binding_application_revision", "\"ApplicationRevision\" > 0");
                table.HasCheckConstraint("CK_system_state_space_binding_application_fingerprint", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_state_space_binding_active_fingerprint", "length(\"ActiveFingerprint\") = 64 AND \"ActiveFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_state_space_binding_resolution_fingerprint", "length(\"ResolutionFingerprint\") = 64 AND \"ResolutionFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_state_space_binding_fingerprint", "length(\"BindingFingerprint\") = 64 AND \"BindingFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_state_space_binding_previous", "\"PreviousBindingFingerprint\" IS NULL OR (length(\"PreviousBindingFingerprint\") = 64 AND \"PreviousBindingFingerprint\" NOT GLOB '*[^0-9A-F]*')");
                table.HasCheckConstraint("CK_system_state_space_binding_counts", "\"EntityCount\" >= 0 AND \"ComponentCount\" >= 0");
                table.HasCheckConstraint("CK_system_state_space_binding_scope", "\"Scope\" IN ('runtime-state-space', 'application-publication')");
            });
            entity.HasKey(x => new { x.StateSpaceId, x.BindingRevision });
            entity.Property(x => x.StateSpaceId).HasMaxLength(200);
            entity.Property(x => x.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(x => x.ApplicationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ActiveFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResolutionFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BindingFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PreviousBindingFingerprint).HasMaxLength(64);
            entity.Property(x => x.CompatibilityCode).HasMaxLength(80).IsRequired();
            entity.Property(x => x.DependencyCoverageVersion).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OperationId).HasMaxLength(32);
            entity.HasIndex(x => x.OperationId).IsUnique();
            entity.HasOne<ApplicationStateSpaceRecord>()
                .WithMany()
                .HasForeignKey(x => x.StateSpaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, Revision = x.ApplicationRevision })
                .HasPrincipalKey(x => new { x.ApplicationId, x.Revision })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Operation>()
                .WithMany()
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationEcsEntityRecord>(entity =>
        {
            entity.ToTable("system_ecs_entity", table =>
            {
                table.HasCheckConstraint("CK_system_ecs_entity_revision", "\"Revision\" > 0");
            });
            entity.HasKey(x => new { x.StateSpaceId, x.Id });
            entity.Property(x => x.StateSpaceId).HasMaxLength(200);
            entity.Property(x => x.Id).HasMaxLength(200);
            entity.Property(x => x.Name).HasMaxLength(400).IsRequired();
            entity.HasOne<ApplicationStateSpaceRecord>()
                .WithMany()
                .HasForeignKey(x => x.StateSpaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationEcsComponentRecord>(entity =>
        {
            entity.ToTable("system_ecs_component", table =>
            {
                table.HasCheckConstraint("CK_system_ecs_component_type_version", "\"TypeVersion\" > 0");
                table.HasCheckConstraint("CK_system_ecs_component_hash", "length(\"SchemaHash\") = 64 AND \"SchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_ecs_component_data", "json_valid(\"Data\")");
                table.HasCheckConstraint("CK_system_ecs_component_revision", "\"Revision\" > 0");
            });
            entity.HasKey(x => new { x.StateSpaceId, x.EntityId, x.QualifiedTypeId });
            entity.Property(x => x.StateSpaceId).HasMaxLength(200);
            entity.Property(x => x.EntityId).HasMaxLength(200);
            entity.Property(x => x.QualifiedTypeId).HasMaxLength(200);
            entity.Property(x => x.SchemaHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Data).HasMaxLength(SystemJsonSchemaProfile.MaximumValueBytes).IsRequired();
            entity.HasIndex(x => new { x.StateSpaceId, x.QualifiedTypeId });
            entity.HasOne<ApplicationEcsEntityRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.StateSpaceId, Id = x.EntityId })
                .HasPrincipalKey(x => new { x.StateSpaceId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ComponentTypeVersionRecord>()
                .WithMany()
                .HasForeignKey(x => new { QualifiedId = x.QualifiedTypeId, Version = x.TypeVersion })
                .HasPrincipalKey(x => new { x.QualifiedId, x.Version })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationEcsContainmentRecord>(entity =>
        {
            entity.ToTable("system_ecs_containment", table =>
                table.HasCheckConstraint("CK_system_ecs_containment_revision", "\"Revision\" > 0"));
            entity.HasKey(value => new { value.StateSpaceId, value.ContainedEntityId });
            entity.Property(value => value.StateSpaceId).HasMaxLength(200);
            entity.Property(value => value.ContainedEntityId).HasMaxLength(200);
            entity.Property(value => value.ContainerEntityId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Slot).HasMaxLength(100).IsRequired();
            entity.HasIndex(value => new { value.StateSpaceId, value.ContainerEntityId });
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(value => new { value.StateSpaceId, Id = value.ContainedEntityId })
                .HasPrincipalKey(value => new { value.StateSpaceId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(value => new { value.StateSpaceId, Id = value.ContainerEntityId })
                .HasPrincipalKey(value => new { value.StateSpaceId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationEcsRelationshipRecord>(entity =>
        {
            entity.ToTable("system_ecs_relationship", table =>
            {
                table.HasCheckConstraint("CK_system_ecs_relationship_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_system_ecs_relationship_data", "json_valid(\"Data\")");
            });
            entity.HasKey(value => new
                { value.StateSpaceId, value.FromEntityId, value.ToEntityId, value.QualifiedKind });
            entity.Property(value => value.StateSpaceId).HasMaxLength(200);
            entity.Property(value => value.FromEntityId).HasMaxLength(200);
            entity.Property(value => value.ToEntityId).HasMaxLength(200);
            entity.Property(value => value.QualifiedKind).HasMaxLength(200);
            entity.Property(value => value.Data).HasMaxLength(SystemJsonSchemaProfile.MaximumValueBytes).IsRequired();
            entity.HasIndex(value => new { value.StateSpaceId, value.QualifiedKind });
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(value => new { value.StateSpaceId, Id = value.FromEntityId })
                .HasPrincipalKey(value => new { value.StateSpaceId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(value => new { value.StateSpaceId, Id = value.ToEntityId })
                .HasPrincipalKey(value => new { value.StateSpaceId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureLegacyStateAdoption(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyStateAdoptionRecord>(entity =>
        {
            entity.ToTable("system_legacy_state_adoption", table =>
            {
                table.HasCheckConstraint("CK_system_legacy_state_adoption_fingerprints",
                    "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"SourceFingerprint\") = 64 AND \"SourceFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"EvidenceFingerprint\") = 64 AND \"EvidenceFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_legacy_state_adoption_counts",
                    "\"EntityCount\" >= 0 AND \"ComponentCount\" >= 0 AND \"ContainmentCount\" >= 0 AND \"RelationshipCount\" >= 0");
            });
            entity.HasKey(value => value.StateSpaceId);
            entity.Property(value => value.StateSpaceId).HasMaxLength(200);
            entity.Property(value => value.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(value => value.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.SourceFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.EvidenceFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.OperationId).HasMaxLength(32).IsRequired();
            entity.HasIndex(value => value.OperationId).IsUnique();
            entity.HasOne<ApplicationStateSpaceRecord>().WithMany()
                .HasForeignKey(value => value.StateSpaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationRegistryRecord>().WithMany()
                .HasForeignKey(value => value.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Operation>().WithMany()
                .HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureProjectionMaterialization(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectionDefinitionRecord>(entity =>
        {
            entity.ToTable("system_projection_definition"); entity.HasKey(x => x.QualifiedId);
            entity.Property(x => x.QualifiedId).HasMaxLength(200); entity.Property(x => x.ApplicationId).HasMaxLength(63).IsRequired();
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProjectionDefinitionVersionRecord>(entity =>
        {
            entity.ToTable("system_projection_definition_version", table =>
            {
                table.HasCheckConstraint("CK_system_projection_definition_version_number", "\"Version\" > 0");
                table.HasCheckConstraint("CK_system_projection_definition_version_output_hash", "length(\"OutputSchemaHash\") = 64 AND \"OutputSchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_projection_definition_version_content_hash", "length(\"ContentHash\") = 64 AND \"ContentHash\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_projection_definition_version_schema", "json_valid(\"OutputSchemaJson\")");
            });
            entity.HasKey(x => new { x.QualifiedId, x.Version }); entity.Property(x => x.QualifiedId).HasMaxLength(200); entity.Property(x => x.ProfileId).HasMaxLength(64).IsRequired(); entity.Property(x => x.OutputSchemaJson).HasMaxLength(SystemJsonSchemaProfile.MaximumSchemaBytes).IsRequired(); entity.Property(x => x.OutputSchemaHash).HasMaxLength(64).IsRequired(); entity.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.QualifiedId, x.ContentHash }).IsUnique(); entity.HasOne<ProjectionDefinitionRecord>().WithMany().HasForeignKey(x => x.QualifiedId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProjectionComponentInputRecord>(entity =>
        {
            entity.ToTable("system_projection_component_input"); entity.HasKey(x => new { x.QualifiedId, x.Version, x.InputId }); entity.Property(x => x.QualifiedId).HasMaxLength(200); entity.Property(x => x.InputId).HasMaxLength(200); entity.Property(x => x.EntityRole).HasMaxLength(200).IsRequired(); entity.Property(x => x.QualifiedTypeId).HasMaxLength(200).IsRequired(); entity.Property(x => x.SchemaHash).HasMaxLength(64).IsRequired();
            entity.HasOne<ProjectionDefinitionVersionRecord>().WithMany().HasForeignKey(x => new { x.QualifiedId, x.Version }).OnDelete(DeleteBehavior.Restrict); entity.HasOne<ComponentTypeVersionRecord>().WithMany().HasForeignKey(x => new { QualifiedId = x.QualifiedTypeId, Version = x.TypeVersion }).HasPrincipalKey(x => new { x.QualifiedId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProjectionDependencyInputRecord>(entity =>
        {
            entity.ToTable("system_projection_dependency_input", table => table.HasCheckConstraint("CK_system_projection_dependency_input_role_bindings", "json_valid(\"RoleBindingsJson\")")); entity.HasKey(x => new { x.QualifiedId, x.Version, x.InputId }); entity.Property(x => x.QualifiedId).HasMaxLength(200); entity.Property(x => x.InputId).HasMaxLength(200); entity.Property(x => x.DependencyQualifiedId).HasMaxLength(200).IsRequired(); entity.Property(x => x.DependencyContentHash).HasMaxLength(64).IsRequired(); entity.Property(x => x.RoleBindingsJson).IsRequired();
            entity.HasOne<ProjectionDefinitionVersionRecord>().WithMany().HasForeignKey(x => new { x.QualifiedId, x.Version }).OnDelete(DeleteBehavior.Restrict); entity.HasOne<ProjectionDefinitionVersionRecord>().WithMany().HasForeignKey(x => new { QualifiedId = x.DependencyQualifiedId, Version = x.DependencyVersion }).HasPrincipalKey(x => new { x.QualifiedId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProjectionMappingRecord>(entity =>
        {
            entity.ToTable("system_projection_mapping"); entity.HasKey(x => new { x.QualifiedId, x.Version, x.TargetPointer }); entity.Property(x => x.QualifiedId).HasMaxLength(200); entity.Property(x => x.TargetPointer).HasMaxLength(1000); entity.Property(x => x.InputId).HasMaxLength(200).IsRequired(); entity.Property(x => x.SourcePointer).HasMaxLength(1000).IsRequired(); entity.HasIndex(x => new { x.QualifiedId, x.Version, x.Ordinal }).IsUnique(); entity.HasOne<ProjectionDefinitionVersionRecord>().WithMany().HasForeignKey(x => new { x.QualifiedId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureInformation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InformationSource>(entity =>
        {
            entity.ToTable("information_source", table =>
            {
                table.HasCheckConstraint("CK_information_source_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_information_source_metadata_schema", "json_valid(\"MetadataSchemaJson\")");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(200);
            entity.Property(x => x.ScopeId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.MetadataSchemaJson).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
            entity.HasIndex(x => new { x.ScopeId, x.Id });
        });
        modelBuilder.Entity<InformationRecord>(entity =>
        {
            entity.ToTable("information_record", table =>
            {
                table.HasCheckConstraint("CK_information_record_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_information_record_metadata", "json_valid(\"MetadataJson\")");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(200);
            entity.Property(x => x.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Content).HasMaxLength(16000).IsRequired();
            entity.Property(x => x.MetadataJson).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
            entity.HasOne(x => x.Source).WithMany(x => x.Records).HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.SourceId, x.Id });
        });
        modelBuilder.Entity<InformationActionContract>(entity =>
        {
            entity.ToTable("information_action_contract", table =>
            {
                table.HasCheckConstraint("CK_information_action_contract_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_information_action_contract_input_schema", "json_valid(\"InputSchemaJson\")");
                table.HasCheckConstraint("CK_information_action_contract_rule_records", "json_valid(\"RuleRecordIdsJson\")");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(200);
            entity.Property(x => x.ScopeId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ExecutorId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.InputSchemaJson).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.RuleRecordIdsJson).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
            entity.HasIndex(x => new { x.ScopeId, x.Id });
        });
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
            entity.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.Property(x => x.FixedRoleEntityIdsJson).IsRequired(); entity.Property(x => x.RoleFromEventPayloadJson).HasDefaultValue("{}").IsRequired(); entity.Property(x => x.FanoutSelectorJson).HasDefaultValue("{}").IsRequired(); entity.Property(x => x.TrackedEntityIdsJson).IsRequired(); entity.Property(x => x.PayloadEqualsJson).IsRequired();
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
            entity.Property(e => e.Matches).HasMaxLength(2000);

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

    private static void ConfigureInteractionReceipts(ModelBuilder modelBuilder)
    {
        const string hash = "length(\"{0}\") = 64 AND \"{0}\" NOT GLOB '*[^0-9A-F]*'";
        modelBuilder.Entity<InteractionResolutionReceipt>(entity =>
        {
            entity.ToTable("interaction_resolution_receipt", table =>
            {
                table.HasCheckConstraint("CK_interaction_resolution_receipt_id", "length(\"Id\") = 52 AND substr(\"Id\", 1, 20) = 'interaction-receipt.' AND substr(\"Id\", 21) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_interaction_resolution_receipt_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_interaction_resolution_receipt_status", "\"Status\" IN ('resolved', 'needs-input', 'ambiguous', 'unknown', 'unsupported', 'unavailable', 'unsafe', 'stale')");
                table.HasCheckConstraint("CK_interaction_resolution_receipt_proposal", "(\"Status\" = 'resolved' AND \"ProposalFingerprint\" IS NOT NULL) OR (\"Status\" <> 'resolved' AND \"ProposalFingerprint\" IS NULL)");
                table.HasCheckConstraint("CK_interaction_resolution_receipt_evidence", "length(\"EvidenceJson\") <= 16384 AND json_valid(\"EvidenceJson\") AND json_type(\"EvidenceJson\") = 'array' AND json_array_length(\"EvidenceJson\") BETWEEN 0 AND 16");
                table.HasCheckConstraint("CK_interaction_resolution_receipt_hashes", string.Join(" AND ", string.Format(hash, "ApplicationFingerprint"), string.Format(hash, "EffectiveSetFingerprint"), string.Format(hash, "EnvelopeFingerprint"), "(\"QueryFingerprint\" IS NULL OR (length(\"QueryFingerprint\") = 64 AND \"QueryFingerprint\" NOT GLOB '*[^0-9A-F]*'))", "(\"ProposalFingerprint\" IS NULL OR (length(\"ProposalFingerprint\") = 64 AND \"ProposalFingerprint\" NOT GLOB '*[^0-9A-F]*'))"));
                table.HasCheckConstraint("CK_interaction_resolution_receipt_bounds", "\"ApplicationRevision\" > 0 AND length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"SessionContextId\") BETWEEN 1 AND 200 AND length(\"StateRevision\") BETWEEN 1 AND 200 AND length(\"RoleProfile\") BETWEEN 1 AND 300 AND (\"ConversationId\" IS NULL OR length(\"ConversationId\") BETWEEN 1 AND 200) AND (\"ParentDelegationId\" IS NULL OR length(\"ParentDelegationId\") BETWEEN 1 AND 200) AND length(\"AuthorizationEvidenceReference\") BETWEEN 1 AND 200 AND length(\"IdempotencyKey\") BETWEEN 1 AND 128 AND length(\"Code\") BETWEEN 1 AND 200 AND length(\"SafeSummary\") <= 1000");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(52);
            entity.Property(row => row.PrincipalReference).HasMaxLength(74).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.ApplicationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.StateSpaceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.SessionContextId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StateRevision).HasMaxLength(200).IsRequired();
            entity.Property(row => row.EffectiveSetFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RoleProfile).HasMaxLength(300).IsRequired();
            entity.Property(row => row.ConversationId).HasMaxLength(200);
            entity.Property(row => row.ParentDelegationId).HasMaxLength(200);
            entity.Property(row => row.AuthorizationEvidenceReference).HasMaxLength(200).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(row => row.EnvelopeFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.QueryFingerprint).HasMaxLength(64);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Code).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ProposalFingerprint).HasMaxLength(64);
            entity.Property(row => row.SafeSummary).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.EvidenceJson).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.RecipeId).HasMaxLength(102);
            entity.Property(row => row.RecipeTemplateFingerprint).HasMaxLength(64);
            entity.HasIndex(row => new { row.PrincipalReference, row.ApplicationId, row.StateSpaceId, row.IdempotencyKey }).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.StateSpaceId, row.CreatedAtUtc, row.Id });
        });

        modelBuilder.Entity<InteractionExecutionReceipt>(entity =>
        {
            entity.ToTable("interaction_execution_receipt", table =>
            {
                table.HasCheckConstraint("CK_interaction_execution_receipt_id", "length(\"Id\") = 52 AND substr(\"Id\", 1, 20) = 'interaction-receipt.' AND substr(\"Id\", 21) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_interaction_execution_receipt_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_interaction_execution_receipt_disposition", "\"Disposition\" IN ('succeeded', 'failed', 'partial', 'skipped', 'stale', 'unauthorized', 'cancelled', 'timed-out')");
                table.HasCheckConstraint("CK_interaction_execution_receipt_evidence", "length(\"EvidenceJson\") <= 16384 AND json_valid(\"EvidenceJson\") AND json_type(\"EvidenceJson\") = 'array' AND json_array_length(\"EvidenceJson\") BETWEEN 0 AND 16");
                table.HasCheckConstraint("CK_interaction_execution_receipt_hashes", string.Join(" AND ", string.Format(hash, "ExecutionRequestFingerprint"), string.Format(hash, "ProposalFingerprint")));
                table.HasCheckConstraint("CK_interaction_execution_receipt_bounds", "length(\"ResolutionReceiptId\") = 52 AND length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"IdempotencyKey\") BETWEEN 1 AND 128 AND length(\"SafeSummary\") <= 1000");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(52);
            entity.Property(row => row.ResolutionReceiptId).HasMaxLength(52).IsRequired();
            entity.Property(row => row.PrincipalReference).HasMaxLength(74).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.StateSpaceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(row => row.ExecutionRequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ProposalFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.Property(row => row.SafeSummary).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.EvidenceJson).HasMaxLength(16384).IsRequired();
            entity.HasIndex(row => new { row.PrincipalReference, row.ApplicationId, row.StateSpaceId, row.ResolutionReceiptId, row.IdempotencyKey }).IsUnique();
            entity.HasOne<InteractionResolutionReceipt>().WithMany().HasForeignKey(row => row.ResolutionReceiptId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InteractionExecutionReceiptStep>(entity =>
        {
            entity.ToTable("interaction_execution_receipt_step", table =>
            {
                table.HasCheckConstraint("CK_interaction_execution_receipt_step_ordinal", "\"Ordinal\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_interaction_execution_receipt_step_disposition", "\"Disposition\" IN ('succeeded', 'failed', 'skipped')");
                table.HasCheckConstraint("CK_interaction_execution_receipt_step_bounds", "length(\"ProposalStepId\") BETWEEN 1 AND 200 AND (\"OperationId\" IS NULL OR length(\"OperationId\") BETWEEN 1 AND 40)");
            });
            entity.HasKey(row => new { row.ExecutionReceiptId, row.Ordinal });
            entity.Property(row => row.ExecutionReceiptId).HasMaxLength(52);
            entity.Property(row => row.ProposalStepId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.Property(row => row.OperationId).HasMaxLength(40);
            entity.HasIndex(row => new { row.ExecutionReceiptId, row.ProposalStepId }).IsUnique();
            entity.HasOne(row => row.ExecutionReceipt).WithMany(row => row.Steps).HasForeignKey(row => row.ExecutionReceiptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Operation>().WithMany().HasForeignKey(row => row.OperationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InteractionExecutionQueryResult>(entity =>
        {
            entity.ToTable("interaction_execution_query_result", table =>
            {
                table.HasCheckConstraint("CK_interaction_execution_query_result_ordinal", "\"Ordinal\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_interaction_execution_query_result_exposure", "\"Exposure\" IN ('model-visible', 'binding-only')");
                table.HasCheckConstraint("CK_interaction_execution_query_result_hashes", string.Join(" AND ",
                    string.Format(hash, "OutputSchemaHash"), string.Format(hash, "ResultFingerprint"),
                    string.Format(hash, "SourceRevisionFingerprint")));
                table.HasCheckConstraint("CK_interaction_execution_query_result_output",
                    "(\"Exposure\" = 'binding-only' AND \"OutputJson\" IS NULL) OR (\"Exposure\" = 'model-visible' AND length(\"OutputJson\") BETWEEN 1 AND 65536 AND json_valid(\"OutputJson\"))");
                table.HasCheckConstraint("CK_interaction_execution_query_result_bounds",
                    "length(\"ProposalStepId\") BETWEEN 1 AND 200 AND length(\"QualifiedId\") BETWEEN 3 AND 400");
            });
            entity.HasKey(row => new { row.ExecutionReceiptId, row.Ordinal });
            entity.Property(row => row.ExecutionReceiptId).HasMaxLength(52);
            entity.Property(row => row.ProposalStepId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.QualifiedId).HasMaxLength(400).IsRequired();
            entity.Property(row => row.OutputSchemaHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ResultFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.SourceRevisionFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Exposure).HasMaxLength(20).IsRequired();
            entity.Property(row => row.OutputJson).HasMaxLength(65_536);
            entity.HasIndex(row => new { row.ExecutionReceiptId, row.ProposalStepId }).IsUnique();
            entity.HasOne(row => row.ExecutionReceipt).WithMany(row => row.QueryResults)
                .HasForeignKey(row => row.ExecutionReceiptId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureInteractionRecipes(ModelBuilder modelBuilder)
    {
        const string hash = "length(\"{0}\") = 64 AND \"{0}\" NOT GLOB '*[^0-9A-F]*'";
        modelBuilder.Entity<InteractionRecipe>(entity =>
        {
            entity.ToTable("interaction_recipe", table =>
            {
                table.HasCheckConstraint("CK_interaction_recipe_id", "length(\"Id\") BETWEEN 41 AND 102 AND \"Id\" GLOB '*.recipe.[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]' ");
                table.HasCheckConstraint("CK_interaction_recipe_template", "length(\"TemplateJson\") BETWEEN 2 AND 65536 AND json_valid(\"TemplateJson\") AND json_type(\"TemplateJson\") = 'object'");
                table.HasCheckConstraint("CK_interaction_recipe_hash", string.Format(hash, "TemplateFingerprint"));
                table.HasCheckConstraint("CK_interaction_recipe_application", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system'");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(102);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TemplateFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.TemplateJson).HasMaxLength(65536).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TemplateFingerprint }).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.CreatedAtUtc, row.Id });
        });

        modelBuilder.Entity<InteractionRecipeRevision>(entity =>
        {
            entity.ToTable("interaction_recipe_revision", table =>
            {
                table.HasCheckConstraint("CK_interaction_recipe_revision_status", "\"Status\" IN ('candidate', 'verified', 'stale', 'retired')");
                table.HasCheckConstraint("CK_interaction_recipe_revision_version", "\"Version\" > 0 AND \"ApplicationRevision\" > 0");
                table.HasCheckConstraint("CK_interaction_recipe_revision_hashes", string.Join(" AND ", string.Format(hash, "ApplicationFingerprint"), string.Format(hash, "EffectiveSetFingerprint"), string.Format(hash, "ResolutionFingerprint"), string.Format(hash, "RequestFingerprint")));
                table.HasCheckConstraint("CK_interaction_recipe_revision_bounds", "length(\"RecipeId\") BETWEEN 41 AND 102 AND length(\"ReviewerPrincipalReference\") <= 74 AND length(\"Reason\") <= 1000 AND length(\"RequestToken\") BETWEEN 1 AND 128");
            });
            entity.HasKey(row => new { row.RecipeId, row.Version });
            entity.Property(row => row.RecipeId).HasMaxLength(102);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ApplicationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.EffectiveSetFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ResolutionFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ReviewerPrincipalReference).HasMaxLength(74).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.RequestToken).HasMaxLength(128).IsRequired();
            entity.Property(row => row.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.ReviewerPrincipalReference, row.RequestToken }).IsUnique();
            entity.HasOne(row => row.Recipe).WithMany(row => row.Revisions).HasForeignKey(row => row.RecipeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InteractionRecipeEvidence>(entity =>
        {
            entity.ToTable("interaction_recipe_evidence", table =>
            {
                table.HasCheckConstraint("CK_interaction_recipe_evidence_kind", "\"Kind\" IN ('derived', 'use-success', 'use-failure')");
                table.HasCheckConstraint("CK_interaction_recipe_evidence_hash", string.Format(hash, "IntentFingerprint"));
                table.HasCheckConstraint("CK_interaction_recipe_evidence_bounds", "length(\"RecipeId\") BETWEEN 41 AND 102 AND length(\"ExecutionReceiptId\") = 52 AND length(\"ResolutionReceiptId\") = 52 AND length(\"IntentText\") <= 500 AND length(\"RoleProfile\") BETWEEN 1 AND 300");
            });
            entity.HasKey(row => new { row.RecipeId, row.ExecutionReceiptId, row.Kind });
            entity.Property(row => row.RecipeId).HasMaxLength(102);
            entity.Property(row => row.ExecutionReceiptId).HasMaxLength(52);
            entity.Property(row => row.ResolutionReceiptId).HasMaxLength(52).IsRequired();
            entity.Property(row => row.Kind).HasMaxLength(20).IsRequired();
            entity.Property(row => row.IntentText).HasMaxLength(500).IsRequired();
            entity.Property(row => row.IntentFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RoleProfile).HasMaxLength(300).IsRequired();
            entity.HasOne(row => row.Recipe).WithMany(row => row.Evidence).HasForeignKey(row => row.RecipeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InteractionExecutionReceipt>().WithMany().HasForeignKey(row => row.ExecutionReceiptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InteractionResolutionReceipt>().WithMany().HasForeignKey(row => row.ResolutionReceiptId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTriggerScheduling(ModelBuilder modelBuilder)
    {
        const string hash = "length(\"{0}\") = 64 AND \"{0}\" NOT GLOB '*[^0-9A-F]*'";
        const string application = "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system'";
        const string identifier = "length(\"Id\") BETWEEN 3 AND 200";

        modelBuilder.Entity<TriggerObservationStructureRecord>(entity =>
        {
            entity.ToTable("trigger_observation_structure", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_structure_values", $"{application} AND {identifier} AND \"Version\" > 0 AND \"Status\" IN ('active', 'retired') AND \"DataClassification\" IN ('general', 'privacy-minimized-signal', 'raw-location', 'third-party-notification-content') AND length(\"SchemaProfileId\") BETWEEN 1 AND 200 AND length(\"NormalizedSchema\") BETWEEN 2 AND 65536 AND json_valid(\"NormalizedSchema\") AND json_type(\"NormalizedSchema\") = 'object' AND length(\"Description\") BETWEEN 1 AND 1024");
                table.HasCheckConstraint("CK_trigger_observation_structure_hash", string.Format(hash, "SchemaHash"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.SchemaProfileId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NormalizedSchema).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.SchemaHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Description).HasMaxLength(1024).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.DataClassification).HasMaxLength(40).IsRequired();
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationStructureCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_observation_structure_current", table =>
                table.HasCheckConstraint("CK_trigger_observation_structure_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourceRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_source_values", $"{application} AND {identifier} AND \"Version\" > 0 AND \"Status\" IN ('enabled', 'disabled') AND \"ReplayWindowSeconds\" BETWEEN 1 AND 604800 AND \"RequestsPerMinute\" BETWEEN 1 AND 10");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourceCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source_current", table =>
                table.HasCheckConstraint("CK_trigger_observation_source_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<TriggerObservationSourceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourceStructureRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source_structure", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_source_structure_versions", "\"SourceVersion\" > 0 AND \"StructureVersion\" > 0");
                table.HasCheckConstraint("CK_trigger_observation_source_structure_ids", "length(\"SourceId\") BETWEEN 3 AND 200 AND length(\"StructureId\") BETWEEN 3 AND 200");
            });
            entity.HasKey(row => new { row.ApplicationId, row.SourceId, row.SourceVersion, row.StructureId, row.StructureVersion });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.SourceId).HasMaxLength(200);
            entity.Property(row => row.StructureId).HasMaxLength(200);
            entity.HasOne(row => row.Source).WithMany(row => row.AllowedStructures)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourcePrincipalRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source_principal", table =>
                table.HasCheckConstraint("CK_trigger_observation_source_principal_id",
                    "length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*'"));
            entity.HasKey(row => new { row.ApplicationId, row.SourceId, row.SourceVersion, row.PrincipalId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.SourceId).HasMaxLength(200);
            entity.Property(row => row.PrincipalId).HasMaxLength(74);
            entity.HasOne(row => row.Source).WithMany(row => row.AllowedPrincipals)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device", table =>
            {
                table.HasCheckConstraint("CK_trigger_phone_device_values",
                    $"{application} AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND substr(\"DeviceId\", 14) NOT GLOB '*[^0-9a-f]*' AND length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*' AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND \"PermissionProfile\" = 'privacy-minimized-signals'");
                table.HasCheckConstraint("CK_trigger_phone_device_verifier", string.Format(hash, "CredentialVerifier"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.Property(row => row.PrincipalId).HasMaxLength(74).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.CredentialVerifier).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PermissionProfile).HasMaxLength(40).IsRequired();
            entity.HasIndex(row => row.PrincipalId).IsUnique();
            entity.HasIndex(row => row.CredentialVerifier).IsUnique();
            entity.HasOne<TriggerObservationSourceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceStructureRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device_structure", table =>
            {
                table.HasCheckConstraint("CK_trigger_phone_device_structure_values",
                    $"{application} AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND \"Ordinal\" BETWEEN 0 AND 7 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0");
                table.HasCheckConstraint("CK_trigger_phone_device_structure_hash", string.Format(hash, "StructureHash"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.Property(row => row.StructureId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.DeviceId, row.StructureId, row.StructureVersion }).IsUnique();
            entity.HasOne(row => row.Device).WithMany(row => row.Structures)
                .HasForeignKey(row => new { row.ApplicationId, row.DeviceId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceStatusRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device_status", table =>
                table.HasCheckConstraint("CK_trigger_phone_device_status_values",
                    $"{application} AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND \"Revision\" BETWEEN 1 AND 2 AND \"Status\" IN ('active', 'revoked')"));
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId, row.Revision });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.HasOne(row => row.Device).WithMany(row => row.StatusRevisions)
                .HasForeignKey(row => new { row.ApplicationId, row.DeviceId }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device_current", table =>
                table.HasCheckConstraint("CK_trigger_phone_device_current_revision",
                    "\"CurrentRevision\" BETWEEN 1 AND 2"));
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.HasOne<PhoneCompanionDeviceStatusRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.DeviceId, Revision = row.CurrentRevision })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OneTimeTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_one_time_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_one_time_definition_values", $"{application} AND {identifier} AND \"Version\" > 0 AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" = 'notification-only' AND \"Lifecycle\" IN ('active', 'cancelled')");
                table.HasCheckConstraint("CK_trigger_one_time_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.MisfirePolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OneTimeTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_one_time_notification_entity", table =>
            {
                table.HasCheckConstraint("CK_trigger_one_time_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200");
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OneTimeTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_one_time_current", table =>
                table.HasCheckConstraint("CK_trigger_one_time_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<OneTimeTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationRecord>(entity =>
        {
            entity.ToTable("trigger_observation", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_id", "length(\"Id\") = 44 AND substr(\"Id\", 1, 12) = 'observation.' AND substr(\"Id\", 13) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_request", "length(\"RequestId\") = 52 AND substr(\"RequestId\", 1, 20) = 'observation-request.' AND substr(\"RequestId\", 21) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_values", $"{application} AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"SourceInstanceId\") BETWEEN 1 AND 128 AND length(\"OccurrenceId\") BETWEEN 1 AND 200 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"DataJson\") BETWEEN 2 AND 65536 AND json_valid(\"DataJson\") AND json_type(\"DataJson\") = 'object'");
                table.HasCheckConstraint("CK_trigger_observation_hashes", string.Join(" AND ", string.Format(hash, "StructureHash"), string.Format(hash, "DataHash"), string.Format(hash, "RequestFingerprint")));
                table.HasCheckConstraint("CK_trigger_observation_principal",
                    "\"PrincipalId\" IS NULL OR (length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(44);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.RequestId).HasMaxLength(52).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.SourceInstanceId).HasMaxLength(128).IsRequired();
            entity.Property(row => row.OccurrenceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.DataJson).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.DataHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PrincipalId).HasMaxLength(74);
            entity.HasIndex(row => new { row.ApplicationId, row.RequestId }).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.SourceId, row.SourceVersion, row.SourceInstanceId, row.OccurrenceId }).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.ReceivedAtUtc, row.Id });
            entity.HasOne<TriggerObservationSourceRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationSourceStructureRecord>().WithMany().HasForeignKey(row => new
            {
                row.ApplicationId,
                row.SourceId,
                row.SourceVersion,
                row.StructureId,
                row.StructureVersion
            }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationSourcePrincipalRecord>().WithMany().HasForeignKey(row => new
            {
                row.ApplicationId,
                row.SourceId,
                row.SourceVersion,
                row.PrincipalId
            }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerFireReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_fire_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_fire_receipt_id", "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_fire_receipt_values", $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Disposition\" IN ('due', 'missed')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<OneTimeTriggerRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerFireWorkRecord>(entity =>
        {
            entity.ToTable("trigger_fire_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_fire_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_fire_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_fire_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'missed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" IN ('completed', 'missed') AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_fire_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_fire_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<OneTimeTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc });
            entity.HasOne<TriggerFireReceiptRecord>().WithOne().HasForeignKey<TriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne().HasForeignKey<TriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OneTimeTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_definition_values",
                    $"{application} AND {identifier} AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND \"Kind\" IN ('daily', 'weekly', 'monthly') AND \"Interval\" BETWEEN 1 AND 365 AND \"LocalTimeSeconds\" BETWEEN 0 AND 86399 AND length(\"TimeZoneId\") BETWEEN 3 AND 100 AND \"GapPolicy\" IN ('skip', 'next-valid') AND \"OverlapPolicy\" IN ('earlier', 'later') AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" = 'notification-only'");
                table.HasCheckConstraint("CK_trigger_recurring_definition_shape",
                    "(\"Kind\" = 'daily' AND \"WeekdaysMask\" = 0 AND \"DayOfMonth\" IS NULL) OR " +
                    "(\"Kind\" = 'weekly' AND \"WeekdaysMask\" BETWEEN 1 AND 127 AND \"DayOfMonth\" IS NULL) OR " +
                    "(\"Kind\" = 'monthly' AND \"WeekdaysMask\" = 0 AND \"DayOfMonth\" BETWEEN 1 AND 31)");
                table.HasCheckConstraint("CK_trigger_recurring_definition_dates",
                    "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                table.HasCheckConstraint("CK_trigger_recurring_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Kind).HasMaxLength(20).IsRequired();
            entity.Property(row => row.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(row => row.GapPolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.OverlapPolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.MisfirePolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_notification_entity", table =>
                table.HasCheckConstraint("CK_trigger_recurring_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200"));
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_current", table =>
                table.HasCheckConstraint("CK_trigger_recurring_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerStateRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_state", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_state_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"CurrentVersion\" > 0 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_recurring_state_disposition",
                    "\"LastDisposition\" IS NULL OR \"LastDisposition\" IN ('due', 'missed')");
                table.HasCheckConstraint("CK_trigger_recurring_state_failure",
                    "\"LastFailureKind\" IS NULL OR \"LastFailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted')");
                table.HasCheckConstraint("CK_trigger_recurring_state_last",
                    "(\"LastOccurrenceAtUtc\" IS NULL AND \"LastDisposition\" IS NULL AND \"LastFailureKind\" IS NULL) OR " +
                    "(\"LastOccurrenceAtUtc\" IS NOT NULL AND ((\"LastDisposition\" IS NULL) <> (\"LastFailureKind\" IS NULL)))");
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.LastDisposition).HasMaxLength(20);
            entity.Property(row => row.LastFailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.NextOccurrenceAtUtc, row.ApplicationId, row.TriggerId });
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerFireWorkRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_fire_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'missed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" IN ('completed', 'missed') AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerFireReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_fire_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_fire_receipt_id",
                    "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_fire_receipt_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Disposition\" IN ('due', 'missed')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc });
            entity.HasOne<RecurringTriggerFireReceiptRecord>().WithOne()
                .HasForeignKey<RecurringTriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne()
                .HasForeignKey<RecurringTriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_definition_values",
                    $"{application} AND {identifier} AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND \"Kind\" IN ('world-clock-threshold', 'state-condition') AND \"Activation\" IN ('rising-edge', 'level') AND \"Rearm\" IN ('on-false', 'manual') AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" = 'notification-only'");
                table.HasCheckConstraint("CK_trigger_conditional_definition_clock_policy",
                    "\"Kind\" <> 'world-clock-threshold' OR (\"Activation\" = 'rising-edge' AND \"Rearm\" = 'manual')");
                table.HasCheckConstraint("CK_trigger_conditional_definition_config",
                    "length(\"AdapterConfigurationJson\") BETWEEN 2 AND 65536 AND json_valid(\"AdapterConfigurationJson\") AND json_type(\"AdapterConfigurationJson\") = 'object'");
                table.HasCheckConstraint("CK_trigger_conditional_definition_config_hash",
                    string.Format(hash, "AdapterConfigurationHash"));
                table.HasCheckConstraint("CK_trigger_conditional_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Kind).HasMaxLength(30).IsRequired();
            entity.Property(row => row.Activation).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Rearm).HasMaxLength(20).IsRequired();
            entity.Property(row => row.StateSpaceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.AdapterId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.AdapterConfigurationJson).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.AdapterConfigurationHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationStateSpaceRecord>().WithMany().HasForeignKey(row => row.StateSpaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerDependencyRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_dependency", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_dependency_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 15 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200 AND length(\"QualifiedTypeId\") BETWEEN 3 AND 200 AND \"TypeVersion\" > 0");
                table.HasCheckConstraint("CK_trigger_conditional_dependency_hash", string.Format(hash, "SchemaHash"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.Property(row => row.QualifiedTypeId).HasMaxLength(200);
            entity.Property(row => row.SchemaHash).HasMaxLength(64);
            entity.HasIndex(row => new { row.StateSpaceId, row.EntityId, row.QualifiedTypeId });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId, row.QualifiedTypeId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.Dependencies)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(row => new { row.StateSpaceId, Id = row.EntityId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_notification_entity", table =>
                table.HasCheckConstraint("CK_trigger_conditional_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200"));
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(row => new { row.StateSpaceId, Id = row.EntityId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_current", table =>
                table.HasCheckConstraint("CK_trigger_conditional_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerStateRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_state", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_state_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"CurrentVersion\" > 0 AND \"EvaluationRevision\" >= 0");
                table.HasCheckConstraint("CK_trigger_conditional_state_operations",
                    "(\"LastOperationId\" IS NULL OR (length(\"LastOperationId\") = 32 AND \"LastOperationId\" NOT GLOB '*[^0-9a-f]*')) AND (\"LastFiredOperationId\" IS NULL OR (length(\"LastFiredOperationId\") = 32 AND \"LastFiredOperationId\" NOT GLOB '*[^0-9a-f]*'))");
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.LastOperationId).HasMaxLength(32);
            entity.Property(row => row.LastFiredOperationId).HasMaxLength(32);
            entity.Property(row => row.EvaluationRevision).IsConcurrencyToken();
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerFireWorkRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_fire_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*' AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" = 'completed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ChangeOperationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ChangeOperationId }).IsUnique();
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerFireReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_fire_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_fire_receipt_id",
                    "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_fire_receipt_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*' AND \"Disposition\" = 'due'");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ChangeOperationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ChangeOperationId }).IsUnique();
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*'");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ChangeOperationId).HasMaxLength(32).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasOne<ConditionalTriggerFireReceiptRecord>().WithOne()
                .HasForeignKey<ConditionalTriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne()
                .HasForeignKey<ConditionalTriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_definition_values",
                    $"{application} AND {identifier} AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" = 'notification-only'");
                table.HasCheckConstraint("CK_trigger_observation_match_definition_hashes",
                    string.Format(hash, "StructureHash") + " AND " + string.Format(hash, "AdapterConfigurationHash"));
                table.HasCheckConstraint("CK_trigger_observation_match_definition_config",
                    "length(\"AdapterConfigurationJson\") BETWEEN 2 AND 65536 AND json_valid(\"AdapterConfigurationJson\") AND json_type(\"AdapterConfigurationJson\") = 'object'");
                table.HasCheckConstraint("CK_trigger_observation_match_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.AdapterId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.AdapterConfigurationJson).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.AdapterConfigurationHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.SourceId, row.SourceVersion,
                row.StructureId, row.StructureVersion, row.StructureHash, row.Lifecycle });
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationSourceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_notification_entity", table =>
                table.HasCheckConstraint("CK_trigger_observation_match_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200"));
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(row => new { row.StateSpaceId, Id = row.EntityId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_current", table =>
                table.HasCheckConstraint("CK_trigger_observation_match_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<ObservationTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerMatchWorkRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.' AND substr(\"ObservationId\", 13) NOT GLOB '*[^0-9a-f]*' AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_observation_match_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" = 'completed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_observation_match_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_observation_match_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ObservationId).HasMaxLength(44).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ObservationId }).IsUnique();
            entity.HasOne<ObservationTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationRecord>().WithMany().HasForeignKey(row => row.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerMatchReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_receipt_id",
                    "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_receipt_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.' AND \"Disposition\" IN ('matched', 'not-matched')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ObservationId).HasMaxLength(44).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ObservationId }).IsUnique();
            entity.HasOne<ObservationTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationRecord>().WithMany().HasForeignKey(row => row.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.'");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ObservationId).HasMaxLength(44).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasOne<ObservationTriggerMatchReceiptRecord>().WithOne()
                .HasForeignKey<ObservationTriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne()
                .HasForeignKey<ObservationTriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationRecord>().WithMany().HasForeignKey(row => row.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void GuardImmutableTriggerSchedulingRows()
    {
        var forbidden = ChangeTracker.Entries().FirstOrDefault(entry =>
            entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity is
                TriggerObservationStructureRecord or
                TriggerObservationSourceRecord or
                TriggerObservationSourceStructureRecord or
                TriggerObservationSourcePrincipalRecord or
                OneTimeTriggerRecord or
                OneTimeTriggerNotificationEntityRecord or
                TriggerObservationRecord or
                TriggerFireReceiptRecord or
                TriggerNotificationLinkRecord or
                RecurringTriggerRecord or
                RecurringTriggerNotificationEntityRecord or
                RecurringTriggerFireReceiptRecord or
                RecurringTriggerNotificationLinkRecord or
                ConditionalTriggerRecord or
                ConditionalTriggerDependencyRecord or
                ConditionalTriggerNotificationEntityRecord or
                ConditionalTriggerFireReceiptRecord or
                ConditionalTriggerNotificationLinkRecord or
                ObservationTriggerRecord or
                ObservationTriggerNotificationEntityRecord or
                ObservationTriggerMatchReceiptRecord or
                ObservationTriggerNotificationLinkRecord or
                PhoneCompanionDeviceRecord or
                PhoneCompanionDeviceStructureRecord or
                PhoneCompanionDeviceStatusRecord);
        if (forbidden is not null)
            throw new InvalidOperationException("TRIGGER_SCHEDULING_IMMUTABLE");
    }

    private void GuardImmutableNotificationContent()
    {
        var forbiddenLink = ChangeTracker.Entries<NotificationEntity>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        var forbiddenNotification = ChangeTracker.Entries<Notification>().Any(entry =>
            entry.State == EntityState.Deleted ||
            entry.State == EntityState.Modified && entry.Properties.Any(property =>
                property.IsModified && property.Metadata.Name is not
                    (nameof(Notification.State) or nameof(Notification.ReadAt) or nameof(Notification.ArchivedAt))));
        if (forbiddenLink || forbiddenNotification)
            throw new InvalidOperationException("NOTIFICATION_CONTENT_IMMUTABLE");
    }

    private static void ConfigureHostSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HostSettingOverride>(entity =>
        {
            entity.ToTable("host_setting_override", table =>
            {
                table.HasCheckConstraint("CK_host_setting_override_versions",
                    "\"CurrentVersion\" > 0 AND \"AppliedVersion\" >= 0 AND \"AppliedVersion\" <= \"CurrentVersion\"");
            });
            entity.HasKey(row => row.Key);
            entity.Property(row => row.Key).HasMaxLength(100);
            entity.Property(row => row.CurrentVersion).IsConcurrencyToken();
        });

        modelBuilder.Entity<HostSettingOverrideVersion>(entity =>
        {
            entity.ToTable("host_setting_override_version", table =>
            {
                table.HasCheckConstraint("CK_host_setting_override_version_number", "\"Version\" > 0");
                table.HasCheckConstraint("CK_host_setting_override_version_operation",
                    "length(\"OperationId\") = 32 AND \"OperationId\" NOT GLOB '*[^0-9a-f]*'");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.SettingKey).HasMaxLength(100);
            entity.Property(row => row.ValueJson).HasMaxLength(16000);
            entity.Property(row => row.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(row => row.OperationId).HasMaxLength(32).IsRequired();
            entity.HasIndex(row => new { row.SettingKey, row.Version }).IsUnique();
            entity.HasIndex(row => row.OperationId).IsUnique();
            entity.HasOne(row => row.Setting)
                .WithMany(row => row.Versions)
                .HasForeignKey(row => row.SettingKey)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Operation>()
                .WithMany()
                .HasForeignKey(row => row.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAssistantConversations(ModelBuilder modelBuilder)
    {
        const string statuses = "'pending', 'running', 'awaiting-approval', 'completed', 'failed', 'cancelled'";
        modelBuilder.Entity<AssistantConversation>(entity =>
        {
            entity.ToTable("assistant_conversation", table =>
            {
                table.HasCheckConstraint("CK_assistant_conversation_id",
                    "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'conversation.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_assistant_conversation_operator",
                    "length(\"OperatorId\") = 74 AND substr(\"OperatorId\", 1, 10) = 'principal.' AND substr(\"OperatorId\", 11) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_assistant_conversation_provider", "\"Provider\" IN ('local', 'codex')");
                table.HasCheckConstraint("CK_assistant_conversation_scope", "\"Scope\" IN ('advisory', 'system')");
                table.HasCheckConstraint("CK_assistant_conversation_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_assistant_conversation_status", $"\"Status\" IN ({statuses})");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(45);
            entity.Property(item => item.OperatorId).HasMaxLength(74).IsRequired();
            entity.Property(item => item.Provider).HasMaxLength(20).IsRequired();
            entity.Property(item => item.Scope).HasMaxLength(20).HasDefaultValue(AssistantConversationScopes.Advisory).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Revision).IsConcurrencyToken();
            entity.Property(item => item.Status).HasMaxLength(20).IsRequired();
            entity.Property(item => item.ExternalThreadId).HasMaxLength(200);
            entity.HasIndex(item => item.ExternalThreadId).IsUnique();
            entity.HasIndex(item => new { item.OperatorId, item.Scope, item.Provider, item.UpdatedAtUtc, item.Id });
        });

        modelBuilder.Entity<AssistantTurn>(entity =>
        {
            entity.ToTable("assistant_turn", table =>
            {
                table.HasCheckConstraint("CK_assistant_turn_id",
                    "length(\"Id\") = 37 AND substr(\"Id\", 1, 5) = 'turn.' AND substr(\"Id\", 6) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_assistant_turn_number", "\"TurnNumber\" > 0");
                table.HasCheckConstraint("CK_assistant_turn_provider", "\"Provider\" IN ('local', 'codex')");
                table.HasCheckConstraint("CK_assistant_turn_status", $"\"Status\" IN ({statuses})");
                table.HasCheckConstraint("CK_assistant_turn_hash",
                    "length(\"RequestHash\") = 64 AND \"RequestHash\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_assistant_turn_metrics",
                    "\"ElapsedMilliseconds\" >= 0 AND \"PromptTokens\" >= 0 AND \"OutputTokens\" >= 0");
                table.HasCheckConstraint("CK_assistant_turn_context_profile",
                    "\"ContextProfile\" IN ('', 'system-read-v1')");
                table.HasCheckConstraint("CK_assistant_turn_context_fingerprint",
                    "\"ContextFingerprint\" = '' OR (length(\"ContextFingerprint\") = 64 AND \"ContextFingerprint\" NOT GLOB '*[^0-9A-F]*')");
                table.HasCheckConstraint("CK_assistant_turn_context_references",
                    "length(\"ContextSourceReferencesJson\") <= 8000");
                table.HasCheckConstraint("CK_assistant_turn_response_disposition",
                    "\"ResponseDisposition\" IN ('', 'answered', 'unknown', 'unsupported', 'needs-input', 'needs-application', 'unavailable')");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(37);
            entity.Property(item => item.ConversationId).HasMaxLength(45).IsRequired();
            entity.Property(item => item.OperatorId).HasMaxLength(74).IsRequired();
            entity.Property(item => item.Provider).HasMaxLength(20).IsRequired();
            entity.Property(item => item.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(item => item.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(20).IsRequired();
            entity.Property(item => item.ExternalTurnId).HasMaxLength(200);
            entity.Property(item => item.ExternalStatus).HasMaxLength(30);
            entity.HasIndex(item => item.ExternalTurnId).IsUnique();
            entity.Property(item => item.ErrorCode).HasMaxLength(100);
            entity.Property(item => item.ErrorMessage).HasMaxLength(500);
            entity.Property(item => item.ModelProvider).HasMaxLength(50);
            entity.Property(item => item.Model).HasMaxLength(200);
            entity.Property(item => item.ModelRevision).HasMaxLength(200);
            entity.Property(item => item.ModelProfile).HasMaxLength(100);
            entity.Property(item => item.ContextProfile).HasMaxLength(40);
            entity.Property(item => item.ContextFingerprint).HasMaxLength(64);
            entity.Property(item => item.ContextSourceReferencesJson).HasMaxLength(8_000);
            entity.Property(item => item.ResponseDisposition).HasMaxLength(30);
            entity.HasIndex(item => new { item.ConversationId, item.TurnNumber }).IsUnique();
            entity.HasIndex(item => new { item.OperatorId, item.Provider, item.IdempotencyKey }).IsUnique();
            entity.HasOne(item => item.Conversation).WithMany(item => item.Turns)
                .HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssistantMessage>(entity =>
        {
            entity.ToTable("assistant_message", table =>
            {
                table.HasCheckConstraint("CK_assistant_message_id",
                    "length(\"Id\") = 40 AND substr(\"Id\", 1, 8) = 'message.' AND substr(\"Id\", 9) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_assistant_message_ordinal", "\"Ordinal\" > 0");
                table.HasCheckConstraint("CK_assistant_message_role", "\"Role\" IN ('user', 'assistant')");
                table.HasCheckConstraint("CK_assistant_message_content", "length(\"Content\") BETWEEN 1 AND 8000");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(40);
            entity.Property(item => item.ConversationId).HasMaxLength(45).IsRequired();
            entity.Property(item => item.TurnId).HasMaxLength(37).IsRequired();
            entity.Property(item => item.Role).HasMaxLength(20).IsRequired();
            entity.Property(item => item.Content).HasMaxLength(8_000).IsRequired();
            entity.HasIndex(item => new { item.ConversationId, item.Ordinal }).IsUnique();
            entity.HasOne(item => item.Conversation).WithMany(item => item.Messages)
                .HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Turn).WithMany(item => item.Messages)
                .HasForeignKey(item => item.TurnId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssistantTurnActivity>(entity =>
        {
            entity.ToTable("assistant_turn_activity", table =>
            {
                table.HasCheckConstraint("CK_assistant_turn_activity_id",
                    "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'activity.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_assistant_turn_activity_sequence", "\"Sequence\" > 0");
                table.HasCheckConstraint("CK_assistant_turn_activity_kind",
                    "\"Kind\" IN ('command', 'file-change', 'mcp-tool', 'dynamic-tool', 'web-search', 'warning', 'error')");
                table.HasCheckConstraint("CK_assistant_turn_activity_content",
                    "length(\"ExternalItemId\") BETWEEN 1 AND 200 AND length(\"Summary\") BETWEEN 1 AND 500");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(41);
            entity.Property(item => item.ConversationId).HasMaxLength(45).IsRequired();
            entity.Property(item => item.TurnId).HasMaxLength(37).IsRequired();
            entity.Property(item => item.ExternalItemId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Kind).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Summary).HasMaxLength(500).IsRequired();
            entity.HasIndex(item => new { item.TurnId, item.ExternalItemId }).IsUnique();
            entity.HasIndex(item => new { item.TurnId, item.Sequence }).IsUnique();
            entity.HasOne(item => item.Conversation).WithMany(item => item.Activities)
                .HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Turn).WithMany(item => item.Activities)
                .HasForeignKey(item => item.TurnId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssistantTurnApproval>(entity =>
        {
            entity.ToTable("assistant_turn_approval", table =>
            {
                table.HasCheckConstraint("CK_assistant_turn_approval_id",
                    "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'approval.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_assistant_turn_approval_kind",
                    "\"Kind\" IN ('command', 'file-change', 'network', 'permissions')");
                table.HasCheckConstraint("CK_assistant_turn_approval_status",
                    "\"Status\" IN ('pending', 'decided', 'dispatched', 'resolved', 'expired', 'cancelled', 'failed')");
                table.HasCheckConstraint("CK_assistant_turn_approval_decision",
                    "\"Decision\" IS NULL OR \"Decision\" IN ('accept', 'decline', 'cancel')");
                table.HasCheckConstraint("CK_assistant_turn_approval_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_assistant_turn_approval_hash",
                    "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_assistant_turn_approval_content",
                    "length(\"ExternalRequestId\") BETWEEN 1 AND 200 AND length(\"ExternalItemId\") BETWEEN 1 AND 200 AND length(\"Summary\") BETWEEN 1 AND 500 AND length(\"DetailsJson\") BETWEEN 2 AND 8192");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(41);
            entity.Property(item => item.ConversationId).HasMaxLength(45).IsRequired();
            entity.Property(item => item.TurnId).HasMaxLength(37).IsRequired();
            entity.Property(item => item.OperatorId).HasMaxLength(74).IsRequired();
            entity.Property(item => item.ExternalRequestId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ExternalItemId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ExternalApprovalId).HasMaxLength(200);
            entity.Property(item => item.Kind).HasMaxLength(30).IsRequired();
            entity.Property(item => item.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Summary).HasMaxLength(500).IsRequired();
            entity.Property(item => item.DetailsJson).HasMaxLength(8_192).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(20).IsRequired();
            entity.Property(item => item.Decision).HasMaxLength(20);
            entity.Property(item => item.Revision).IsConcurrencyToken();
            entity.HasIndex(item => new { item.TurnId, item.ExternalRequestId }).IsUnique();
            entity.HasIndex(item => new { item.OperatorId, item.TurnId, item.Status });
            entity.HasOne(item => item.Conversation).WithMany(item => item.Approvals)
                .HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Turn).WithMany(item => item.Approvals)
                .HasForeignKey(item => item.TurnId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSystemTasks(ModelBuilder modelBuilder)
    {
        const string hash = "length(\"{0}\") = 64 AND \"{0}\" NOT GLOB '*[^0-9A-F]*'";
        modelBuilder.Entity<SystemTaskRecord>(entity =>
        {
            entity.ToTable("system_task", table =>
            {
                table.HasCheckConstraint("CK_system_task_id",
                    "length(\"Id\") = 44 AND substr(\"Id\", 1, 12) = 'system-task.' AND substr(\"Id\", 13) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_task_principal",
                    "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_task_operation", "\"Operation\" IN ('resolve', 'submit')");
                table.HasCheckConstraint("CK_system_task_status",
                    "\"Status\" IN ('planning', 'prepared', 'completed', 'needs-input', 'unknown', 'unsupported', 'unavailable', 'failed')");
                table.HasCheckConstraint("CK_system_task_hashes", string.Join(" AND ",
                    string.Format(hash, "RequestFingerprint"),
                    "(\"PlanFingerprint\" = '' OR (" + string.Format(hash, "PlanFingerprint") + "))",
                    "(\"ContextFingerprint\" = '' OR (" + string.Format(hash, "ContextFingerprint") + "))"));
                table.HasCheckConstraint("CK_system_task_bounds",
                    "length(\"Intent\") BETWEEN 1 AND 8000 AND length(\"IdempotencyKey\") BETWEEN 1 AND 100 AND length(\"SafeSummary\") <= 1000 AND length(\"ContextSourceReferencesJson\") <= 16000 AND length(\"ErrorCode\") <= 100 AND length(\"ErrorMessage\") <= 500");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(44);
            entity.Property(value => value.PrincipalReference).HasMaxLength(74).IsRequired();
            entity.Property(value => value.ConversationId).HasMaxLength(45).IsRequired();
            entity.Property(value => value.Operation).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Intent).HasMaxLength(8_000).IsRequired();
            entity.Property(value => value.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(value => value.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.SafeSummary).HasMaxLength(1_000);
            entity.Property(value => value.PlanFingerprint).HasMaxLength(64);
            entity.Property(value => value.ContextProfile).HasMaxLength(40);
            entity.Property(value => value.ContextFingerprint).HasMaxLength(64);
            entity.Property(value => value.ContextSourceReferencesJson).HasMaxLength(16_000);
            entity.Property(value => value.ErrorCode).HasMaxLength(100);
            entity.Property(value => value.ErrorMessage).HasMaxLength(500);
            entity.HasIndex(value => new
                { value.PrincipalReference, value.ConversationId, value.IdempotencyKey }).IsUnique();
            entity.HasIndex(value => new
                { value.PrincipalReference, value.ConversationId, value.CreatedAtUtc, value.Id });
            entity.HasOne<AssistantConversation>().WithMany().HasForeignKey(value => value.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SystemTaskRoundRecord>(entity =>
        {
            entity.ToTable("system_task_round", table =>
            {
                table.HasCheckConstraint("CK_system_task_round_ordinal", "\"Ordinal\" BETWEEN 1 AND 3");
                table.HasCheckConstraint("CK_system_task_round_disposition",
                    "\"Disposition\" IN ('continue', 'prepared', 'completed', 'needs-input', 'unknown', 'unsupported', 'unavailable')");
                table.HasCheckConstraint("CK_system_task_round_hashes", string.Join(" AND ",
                    string.Format(hash, "ContextFingerprint"), string.Format(hash, "ResponseFingerprint")));
                table.HasCheckConstraint("CK_system_task_round_bounds",
                    "length(\"Summary\") BETWEEN 1 AND 1000 AND length(\"EvidenceJson\") <= 16000 AND length(\"OutputJson\") <= 524288");
            });
            entity.HasKey(value => new { value.TaskId, value.Ordinal });
            entity.Property(value => value.TaskId).HasMaxLength(44);
            entity.Property(value => value.Disposition).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Summary).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.ContextFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.ResponseFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.ModelProvider).HasMaxLength(50).IsRequired();
            entity.Property(value => value.Model).HasMaxLength(200).IsRequired();
            entity.Property(value => value.ModelRevision).HasMaxLength(200).IsRequired();
            entity.Property(value => value.ModelProfile).HasMaxLength(100).IsRequired();
            entity.Property(value => value.EvidenceJson).HasMaxLength(16_000).IsRequired();
            entity.Property(value => value.OutputJson).HasMaxLength(524_288).IsRequired();
            entity.HasOne(value => value.Task).WithMany(value => value.Rounds)
                .HasForeignKey(value => value.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemTaskStepRecord>(entity =>
        {
            entity.ToTable("system_task_step", table =>
            {
                table.HasCheckConstraint("CK_system_task_step_ordinal", "\"Ordinal\" BETWEEN 1 AND 12");
                table.HasCheckConstraint("CK_system_task_step_id",
                    "length(\"StepId\") = 8 AND substr(\"StepId\", 1, 5) = 'step-' AND substr(\"StepId\", 6) NOT GLOB '*[^0-9]*'");
                table.HasCheckConstraint("CK_system_task_step_mode", "\"Mode\" IN ('read', 'write')");
                table.HasCheckConstraint("CK_system_task_step_preflight", "\"PreflightStatus\" IN ('read', 'ready', 'deferred')");
                table.HasCheckConstraint("CK_system_task_step_hashes", string.Join(" AND ",
                    string.Format(hash, "DescriptorFingerprint"), string.Format(hash, "InputFingerprint"),
                    "(\"PreconditionFingerprint\" = '' OR (" + string.Format(hash, "PreconditionFingerprint") + "))",
                    "(\"ResultFingerprint\" = '' OR (" + string.Format(hash, "ResultFingerprint") + "))"));
                table.HasCheckConstraint("CK_system_task_step_bounds",
                    "\"CapabilityVersion\" > 0 AND length(\"InputJson\") BETWEEN 2 AND 98304 AND length(\"SafeSummary\") BETWEEN 1 AND 1000 AND length(\"AffectedReferencesJson\") <= 16000 AND length(\"DeferredStepIdsJson\") <= 1024 AND length(\"ResultJson\") <= 1048576");
            });
            entity.HasKey(value => new { value.TaskId, value.Ordinal });
            entity.Property(value => value.TaskId).HasMaxLength(44);
            entity.Property(value => value.StepId).HasMaxLength(8).IsRequired();
            entity.Property(value => value.CapabilityId).HasMaxLength(120).IsRequired();
            entity.Property(value => value.DescriptorFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Owner).HasMaxLength(80).IsRequired();
            entity.Property(value => value.Mode).HasMaxLength(10).IsRequired();
            entity.Property(value => value.InputJson).HasMaxLength(98_304).IsRequired();
            entity.Property(value => value.InputFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.PreflightStatus).HasMaxLength(20).IsRequired();
            entity.Property(value => value.PreconditionFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.SafeSummary).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.AffectedReferencesJson).HasMaxLength(16_000).IsRequired();
            entity.Property(value => value.DeferredStepIdsJson).HasMaxLength(1_024).IsRequired();
            entity.Property(value => value.ResultJson).HasMaxLength(1_048_576).IsRequired();
            entity.Property(value => value.ResultFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(value => new { value.TaskId, value.StepId }).IsUnique();
            entity.HasOne(value => value.Task).WithMany(value => value.Steps)
                .HasForeignKey(value => value.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemTaskConfirmationRecord>(entity =>
        {
            entity.ToTable("system_task_confirmation", table =>
            {
                table.HasCheckConstraint("CK_system_task_confirmation_id",
                    "length(\"Id\") = 57 AND substr(\"Id\", 1, 25) = 'system-task-confirmation.' AND substr(\"Id\", 26) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_task_confirmation_hashes", string.Join(" AND ",
                    string.Format(hash, "PlanFingerprint"), string.Format(hash, "RequestFingerprint")));
                table.HasCheckConstraint("CK_system_task_confirmation_bounds",
                    "length(\"IdempotencyKey\") BETWEEN 1 AND 100 AND length(\"AuthorizationEvidenceJson\") BETWEEN 2 AND 4000 AND \"ExpiresAtUtc\" > \"ConfirmedAtUtc\"");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(57);
            entity.Property(value => value.TaskId).HasMaxLength(44).IsRequired();
            entity.Property(value => value.PrincipalReference).HasMaxLength(74).IsRequired();
            entity.Property(value => value.PlanFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(value => value.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.AuthorizationEvidenceJson).HasMaxLength(4_000).IsRequired();
            entity.HasIndex(value => new { value.PrincipalReference, value.TaskId, value.IdempotencyKey }).IsUnique();
            entity.HasOne(value => value.Task).WithMany(value => value.Confirmations)
                .HasForeignKey(value => value.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemTaskExecutionRecord>(entity =>
        {
            entity.ToTable("system_task_execution", table =>
            {
                table.HasCheckConstraint("CK_system_task_execution_id",
                    "length(\"Id\") = 52 AND substr(\"Id\", 1, 20) = 'system-task-receipt.' AND substr(\"Id\", 21) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_system_task_execution_status",
                    "\"Status\" IN ('running', 'succeeded', 'partial', 'failed', 'stale', 'unauthorized', 'cancelled', 'timed-out', 'indeterminate')");
                table.HasCheckConstraint("CK_system_task_execution_hashes", string.Join(" AND ",
                    string.Format(hash, "RequestFingerprint"), string.Format(hash, "PlanFingerprint")));
                table.HasCheckConstraint("CK_system_task_execution_bounds",
                    "length(\"IdempotencyKey\") BETWEEN 1 AND 100 AND length(\"SafeSummary\") <= 1000 AND length(\"ErrorCode\") <= 100 AND length(\"ErrorMessage\") <= 500 AND length(\"AuthorizationEvidenceJson\") BETWEEN 2 AND 4000");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(52);
            entity.Property(value => value.TaskId).HasMaxLength(44).IsRequired();
            entity.Property(value => value.ConfirmationId).HasMaxLength(57).IsRequired();
            entity.Property(value => value.PrincipalReference).HasMaxLength(74).IsRequired();
            entity.Property(value => value.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(value => value.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.PlanFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.SafeSummary).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.ErrorCode).HasMaxLength(100).IsRequired();
            entity.Property(value => value.ErrorMessage).HasMaxLength(500).IsRequired();
            entity.Property(value => value.AuthorizationEvidenceJson).HasMaxLength(4_000).IsRequired();
            entity.HasIndex(value => new { value.PrincipalReference, value.TaskId, value.IdempotencyKey }).IsUnique();
            entity.HasIndex(value => value.ConfirmationId).IsUnique();
            entity.HasOne(value => value.Task).WithMany(value => value.Executions)
                .HasForeignKey(value => value.TaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Confirmation).WithMany(value => value.Executions)
                .HasForeignKey(value => value.ConfirmationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SystemTaskExecutionStepRecord>(entity =>
        {
            entity.ToTable("system_task_execution_step", table =>
            {
                table.HasCheckConstraint("CK_system_task_execution_step_ordinal", "\"Ordinal\" BETWEEN 1 AND 12");
                table.HasCheckConstraint("CK_system_task_execution_step_status",
                    "\"Status\" IN ('running', 'succeeded', 'failed', 'stale', 'unauthorized', 'cancelled', 'timed-out', 'indeterminate', 'skipped')");
                table.HasCheckConstraint("CK_system_task_execution_step_hashes",
                    "(\"OutputFingerprint\" = '' OR (" + string.Format(hash, "OutputFingerprint") + ")) AND (\"ReadBackFingerprint\" = '' OR (" + string.Format(hash, "ReadBackFingerprint") + "))");
                table.HasCheckConstraint("CK_system_task_execution_step_bounds",
                    "length(\"TaskStepId\") = 8 AND length(\"ExecutionEvidenceJson\") BETWEEN 2 AND 16000 AND length(\"OperationId\") <= 100 AND length(\"OutputJson\") <= 1048576 AND length(\"ErrorCode\") <= 100 AND length(\"ErrorMessage\") <= 500");
                table.HasCheckConstraint("CK_system_task_execution_step_completion",
                    "(\"Status\" = 'running' AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" <> 'running' AND \"CompletedAtUtc\" IS NOT NULL)");
            });
            entity.HasKey(value => new { value.ExecutionId, value.Ordinal });
            entity.Property(value => value.ExecutionId).HasMaxLength(52);
            entity.Property(value => value.TaskStepId).HasMaxLength(8).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.ExecutionEvidenceJson).HasMaxLength(16_000).IsRequired();
            entity.Property(value => value.OperationId).HasMaxLength(100).IsRequired();
            entity.Property(value => value.OutputJson).HasMaxLength(1_048_576).IsRequired();
            entity.Property(value => value.OutputFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.ReadBackFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.ErrorCode).HasMaxLength(100).IsRequired();
            entity.Property(value => value.ErrorMessage).HasMaxLength(500).IsRequired();
            entity.HasOne(value => value.Execution).WithMany(value => value.Steps)
                .HasForeignKey(value => value.ExecutionId).OnDelete(DeleteBehavior.Cascade);
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
            entity.HasIndex(e => new { e.FromEntityId, e.Kind, e.ToEntityId });
            entity.HasIndex(e => new { e.ToEntityId, e.Kind, e.FromEntityId });
        });
    }
}
