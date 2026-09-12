namespace DantesRoleplay.TriggerScheduling;

public sealed class ConditionalTriggerRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int Version { get; set; }
    public required string Lifecycle { get; set; }
    public required string Kind { get; set; }
    public required string Activation { get; set; }
    public required string Rearm { get; set; }
    public required string StateSpaceId { get; set; }
    public required string AdapterId { get; set; }
    public int AdapterVersion { get; set; }
    public required string AdapterConfigurationJson { get; set; }
    public required string AdapterConfigurationHash { get; set; }
    public required string Target { get; set; }
    public required string NotificationTopic { get; set; }
    public required string NotificationSubject { get; set; }
    public required string NotificationBody { get; set; }
    public string? NotificationStateSpaceId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public ICollection<ConditionalTriggerDependencyRecord> Dependencies { get; } =
        new List<ConditionalTriggerDependencyRecord>();
    public ICollection<ConditionalTriggerRelationshipDependencyRecord> RelationshipDependencies { get; } =
        new List<ConditionalTriggerRelationshipDependencyRecord>();
    public ICollection<ConditionalTriggerNotificationEntityRecord> NotificationEntities { get; } =
        new List<ConditionalTriggerNotificationEntityRecord>();
    public ConditionalTriggerWorkflowBindingRecord? WorkflowBinding { get; set; }
    public ConditionalTriggerPredicateBindingRecord? PredicateBinding { get; set; }
}

public sealed class ConditionalTriggerDependencyRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public required string QualifiedTypeId { get; set; }
    public int TypeVersion { get; set; }
    public required string SchemaHash { get; set; }
    public ConditionalTriggerRecord? Trigger { get; set; }
}

public sealed class ConditionalTriggerRelationshipDependencyRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string QualifiedKind { get; set; }
    public required string AnchorEntityId { get; set; }
    public bool Incoming { get; set; }
    public ConditionalTriggerRecord? Trigger { get; set; }
}

public sealed class ConditionalTriggerWorkflowBindingRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string PrincipalReference { get; set; }
    public required string AuthenticationMethod { get; set; }
    public int ApplicationRevision { get; set; }
    public required string ApplicationFingerprint { get; set; }
    public required string BaseApplicationsJson { get; set; }
    public required string StateSpaceId { get; set; }
    public required string GrantReference { get; set; }
    public required string StateRevision { get; set; }
    public required string DefinitionId { get; set; }
    public int DefinitionVersion { get; set; }
    public required string DefinitionFingerprint { get; set; }
    public required string ExecutionRequestJson { get; set; }
    public string? ResultSchemaJson { get; set; }
    public string? ResultSchemaFingerprint { get; set; }
    public int MaximumOperations { get; set; }
    public int RuntimeWindowSeconds { get; set; }
    public required string BindingFingerprint { get; set; }
    public ConditionalTriggerRecord? Trigger { get; set; }
}

public sealed class ConditionalTriggerPredicateBindingRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string MechanicId { get; set; }
    public int MechanicVersion { get; set; }
    public required string MechanicFingerprint { get; set; }
    public int ActivationRevision { get; set; }
    public required string ActivationFingerprint { get; set; }
    public int ActivationApplicationRevision { get; set; }
    public required string ActivationApplicationFingerprint { get; set; }
    public required string SourceRegistrationFingerprint { get; set; }
    public required string RequirementsJson { get; set; }
    public required string RequirementsFingerprint { get; set; }
    public required string RoleEntityIdsJson { get; set; }
    public required string RoleEntityIdsFingerprint { get; set; }
    public required string Coalescing { get; set; }
    public int MaximumOperationsPerFire { get; set; }
    public required string BindingFingerprint { get; set; }
    public ConditionalTriggerRecord? Trigger { get; set; }
}

public sealed class ConditionalTriggerNotificationEntityRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public int Ordinal { get; set; }
    public required string StateSpaceId { get; set; }
    public required string EntityId { get; set; }
    public ConditionalTriggerRecord? Trigger { get; set; }
}

public sealed class ConditionalTriggerCurrentRecord
{
    public required string ApplicationId { get; set; }
    public required string Id { get; set; }
    public int CurrentVersion { get; set; }
}

public sealed class ConditionalTriggerStateRecord
{
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int CurrentVersion { get; set; }
    public bool? CurrentTruth { get; set; }
    public bool Armed { get; set; }
    public int EvaluationRevision { get; set; }
    public string? LastOperationId { get; set; }
    public string? LastFiredOperationId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ConditionalTriggerFireWorkRecord
{
    public required string FireId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ChangeOperationId { get; set; }
    public string? CausalAllowanceId { get; set; }
    public string? PredicateCaptureJson { get; set; }
    public string? PredicateCaptureFingerprint { get; set; }
    public bool? PredicatePriorTruth { get; set; }
    public bool? PredicatePriorArmed { get; set; }
    public required string State { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? FailureKind { get; set; }
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class TriggerCausalAllowanceRecord
{
    public required string Id { get; set; }
    public required string SourceKind { get; set; }
    public required string SourceId { get; set; }
    public int MaximumOperations { get; set; }
    public int ReservedOperations { get; set; }
    public required string IdentityFingerprint { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class TriggerCausalReservationRecord
{
    public required string CausalAllowanceId { get; set; }
    public required string FireId { get; set; }
    public int Operations { get; set; }
    public required string CommandId { get; set; }
    public required string RequestFingerprint { get; set; }
    public DateTime ReservedAtUtc { get; set; }
}

public sealed class ConditionalTriggerFireReceiptRecord
{
    public required string Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ChangeOperationId { get; set; }
    public required string Disposition { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class ConditionalTriggerNotificationLinkRecord
{
    public required string FireId { get; set; }
    public required string NotificationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string TriggerId { get; set; }
    public int TriggerVersion { get; set; }
    public required string ChangeOperationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
