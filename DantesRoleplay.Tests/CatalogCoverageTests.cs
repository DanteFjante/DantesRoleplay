using System.Data;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Every table and every column is either carried by the catalog or deliberately left out, and this
/// is where "deliberately" is written down.
///
/// The catalog answers one question — can the database be extracted and put back? — and that answer
/// decays silently. Add a column next month and nothing fails: export keeps working, import keeps
/// working, and the new field is quietly dropped on every round trip. Nobody finds out until
/// somebody restores from a catalog and the data is subtly wrong.
///
/// So the lists below are not a description of the code. They are the specification, and the code
/// is checked against them. A new column fails this test until someone classifies it, and
/// classifying it means writing the sentence explaining the choice.
///
/// This is the same reasoning as <see cref="MigrationDriftTests"/>, one layer up: that one asserts
/// the migrations match the model, this one asserts the catalog matches the model.
/// </summary>
public sealed class CatalogCoverageTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ---- tables --------------------------------------------------------------------------

    /// <summary>Tables the catalog writes out, and where they land.</summary>
    private static readonly Dictionary<string, string> CarriedTables = new(StringComparer.Ordinal)
    {
        ["entity"] = "world/entities/<id>.json",
        ["component"] = "folded into the entity file that carries it",
        ["containment"] = "folded into the entity file — a thing is inside at most one thing",
        ["relationship"] = "world/relationships.json, as one set",
        ["component_definition"] = "components/<id>.json, schema in a sibling .schema.json",
        ["mechanic"] = "mechanics/<category>/<id>.md + .js",
        ["mechanic_version"] = "the current version only; the chain stays in the database",
        ["procedure_contract"] = "procedures/<category>/<id>.md",
        ["procedure_contract_version"] = "the current version only",
        ["event_type"] = "event-types/<id>.json, schema in a sibling .schema.json",
        ["event_type_version"] = "the current version only; the chain stays in the database",
        ["subscription"] = "subscriptions/<id>.json",
        ["subscription_version"] = "the current version only; the chain stays in the database",
        ["operation"] = "history/operations.jsonl, with --with-history. EXPORT ONLY — nothing imports it"
    };

    /// <summary>Tables the catalog does not write out, and why not.</summary>
    private static readonly Dictionary<string, string> SkippedTables = new(StringComparer.Ordinal)
    {
        // Runtime evidence, like the operation log — but unlike the log, not even exportable.
        // An event asserts that a particular world change committed. Writing one from a file would
        // be asserting that something happened which did not, so there is deliberately no export,
        // no import, and no commit kind anywhere that can produce one.
        ["event"] = "Runtime evidence. An event exists only because a world change committed; a "
            + "catalog that could write one would be able to assert a change that never happened.",
        ["event_entity"] = "Join rows for the above.",
        ["event_execution"] = "Runtime evidence: one reaction subscription that ran against one "
            + "accepted event. Same reasoning as the event itself — it records something that "
            + "happened, so it cannot be authored.",

        ["notification"] = "Runtime evidence. A notice records that a rule, at a version, inside "
            + "one committed change, decided something was worth telling a person. Authoring one "
            + "from a file would be putting words in that rule's mouth.",
        ["notification_entity"] = "Join rows for the above.",

        // Feedback is operational test evidence. It is intentionally durable and queryable in a
        // running system, but catalog import/export must not manufacture reports or their audit
        // references.
        ["system_feedback_report"] = "Runtime system-feedback evidence; catalog files must not author reports.",
        ["system_feedback_step"] = "Ordered reproduction steps for runtime system feedback.",
        ["system_feedback_operation"] = "Runtime audit references attached to system feedback.",
        ["system_feedback_procedure"] = "Frozen procedure revisions attached to system feedback.",
        ["system_feedback_disposition"] = "Immutable local developer-triage history for runtime system feedback.",
        ["system_feedback_retention_action"] = "Immutable local developer-retention history for runtime system feedback.",

        // Snapshot packages are immutable runtime evidence produced from one committed session.
        // Their opaque bytes and provenance cannot be file-authored without allowing a catalog to
        // claim that a capture happened, so the snapshot feature intentionally has no catalog
        // export/import route.
        ["snapshot_package"] = "Immutable runtime snapshot evidence. A catalog must not author or "
            + "restore opaque captured bytes and their operation provenance.",

        // Story-plan runs are resumable runtime orchestration state.  They contain a principal's
        // request, local-model preparation, leases, and action receipts; importing them would
        // recreate work that was never actually scheduled or committed in this database.
        ["story_plan_run"] = "Resumable runtime orchestration state, not authored catalog content.",
        ["story_plan_step_run"] = "Per-step runtime execution evidence for a story-plan run.",

        // Generic information is MCP-authored live database content. It is deliberately not part
        // of the bootstrap catalog: importing a catalog must not overwrite a user's independent
        // information namespace, its rules, or the action contracts it has enabled.
        ["information_source"] = "Live MCP-authored generic information, not bootstrap catalog content.",
        ["information_record"] = "Live MCP-authored generic information, not bootstrap catalog content.",
        ["information_action_contract"] = "Live MCP-authored action-contract configuration, not bootstrap catalog content.",

        // Application/source registration controls which application declarations may later be
        // scanned and activated. A catalog import must not create a host-local allowed root,
        // change precedence, or manufacture scan evidence.
        ["system_application"] = "Live generic application-registry configuration, not catalog content.",
        ["system_application_revision"] = "Immutable live application-registry revision evidence, not catalog content.",
        ["system_application_revision_base"] = "Immutable live base-application relationship evidence, not catalog content.",
        ["system_application_source"] = "Live allowed-root/source registration, not catalog content.",
        ["system_application_source_scan"] = "Runtime source-scan evidence, not catalog content.",
        ["system_component_type"] = "Live application component-type identity, not bootstrap catalog content.",
        ["system_component_type_version"] = "Immutable validated component schema history, not bootstrap catalog content.",
        ["system_state_space"] = "Live application-scoped runtime state binding, not bootstrap catalog content.",
        ["system_state_space_binding_revision"] = "Immutable live state-space binding, compatibility, and audit history, not bootstrap catalog content.",
        ["system_ecs_entity"] = "Live application-scoped entity state, not bootstrap catalog content.",
        ["system_ecs_component"] = "Live application-scoped component state, not bootstrap catalog content.",
        ["system_ecs_containment"] = "Live application-scoped exclusive containment state, not bootstrap catalog content.",
        ["system_ecs_relationship"] = "Live application-scoped directed relationship state, not bootstrap catalog content.",
        ["system_legacy_state_adoption"] = "Immutable runtime legacy-adoption and replay evidence, not authored catalog content.",
        ["system_projection_definition"] = "Live application-owned projection identity, not bootstrap catalog content.",
        ["system_projection_definition_version"] = "Immutable live structural projection history, not bootstrap catalog content.",
        ["system_projection_component_input"] = "Immutable projection component input declaration, not bootstrap catalog content.",
        ["system_projection_dependency_input"] = "Immutable projection dependency declaration, not bootstrap catalog content.",
        ["system_projection_mapping"] = "Immutable structural projection mapping, not bootstrap catalog content.",
        ["system_application_activation_revision"] = "Immutable runtime application-overlay activation evidence, not authored catalog content.",
        ["system_application_activation_current"] = "Live pointer to one activated application overlay, not authored catalog content.",
        ["system_application_activation_source"] = "Retained source evidence for an activated overlay, not authored catalog content.",
        ["system_application_activation_document"] = "Retained redacted winner evidence for an activated overlay, not authored catalog content.",
        ["system_application_activation_receipt"] = "Immutable operation-linked activation replay evidence, not authored catalog content.",

        ["host_setting_override"] = "Host-local operational configuration; catalog import must not change process settings.",
        ["host_setting_override_version"] = "Immutable host-local setting and audit history, not authored game content.",

        ["assistant_conversation"] = "Operator-scoped live assistant history, not authored game content.",
        ["assistant_turn"] = "Runtime assistant request, outcome, and provider evidence, not authored game content.",
        ["assistant_turn_activity"] = "Runtime Codex progress and external-item evidence, not authored game content.",
        ["assistant_turn_approval"] = "Operator-scoped Codex approval and reconciliation evidence, not authored game content.",
        ["assistant_message"] = "Immutable operator/assistant transcript content, not authored game content.",

        ["interaction_resolution_receipt"] = "Immutable runtime interaction-resolution evidence; catalog files must not assert an intent was resolved.",
        ["interaction_execution_receipt"] = "Immutable runtime interaction-execution evidence; catalog files must not assert an execution was attempted.",
        ["interaction_execution_receipt_step"] = "Ordered runtime interaction execution-step evidence, not authored catalog content.",
        ["interaction_recipe"] = "Private learned interaction-route identity and inert template, not authored catalog content.",
        ["interaction_recipe_revision"] = "Append-only private review and invalidation history for learned routes.",
        ["interaction_recipe_evidence"] = "Private runtime execution evidence for learned and reused routes.",

        ["__EFMigrationsHistory"] = "Schema bookkeeping, not content. A catalog describes what the "
            + "database holds, not which migrations built it.",
        ["__EFMigrationsLock"] = "Schema bookkeeping.",
        ["sqlite_sequence"] = "SQLite internal."
    };

    /// <summary>
    /// Tables that exist in a migrated database but not in one built by EnsureCreated, which is how
    /// the test fixture builds its own. They stay classified above — a real database has them — but
    /// their absence here is not evidence that the list has gone stale.
    /// </summary>
    private static readonly HashSet<string> OnlyInAMigratedDatabase = new(StringComparer.Ordinal)
    {
        "__EFMigrationsHistory",
        "__EFMigrationsLock"
    };

    [Fact]
    public void Every_table_is_either_carried_by_the_catalog_or_deliberately_skipped()
    {
        var actual = TableNames();
        var classified = CarriedTables.Keys.Concat(SkippedTables.Keys).ToHashSet(StringComparer.Ordinal);

        var unclassified = actual.Except(classified, StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();

        Assert.True(
            unclassified.Count == 0,
            $"These tables are in the database and in neither list: {string.Join(", ", unclassified)}. "
            + "Add them to the catalog, or to SkippedTables with the reason. A table nobody decided "
            + "about is one that gets silently dropped on every round trip.");

        // And the reverse: a list that names a table which no longer exists is a list nobody is
        // maintaining, which is worse than no list.
        var stale = classified
            .Except(actual, StringComparer.Ordinal)
            .Except(OnlyInAMigratedDatabase, StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"These tables are named in the coverage lists but no longer exist: {string.Join(", ", stale)}.");
    }

    // ---- columns -------------------------------------------------------------------------

    /// <summary>Columns whose value survives a round trip through the catalog.</summary>
    private static readonly HashSet<string> Carried = new(StringComparer.Ordinal)
    {
        "entity.Id", "entity.Name",

        "component.EntityId", "component.DefinitionId", "component.Data",

        "containment.ContainerId", "containment.ContainedId", "containment.Slot",

        "relationship.FromEntityId", "relationship.ToEntityId", "relationship.Kind", "relationship.Data",

        "component_definition.Id", "component_definition.Name",
        "component_definition.Description", "component_definition.Schema",

        "mechanic.Id", "mechanic.Category", "mechanic.Status", "mechanic.Scope",
        "mechanic_version.MechanicId", "mechanic_version.Name", "mechanic_version.Description",
        "mechanic_version.Matches", "mechanic_version.Requirements", "mechanic_version.Source",
        "mechanic_version.CreatedBy", "mechanic_version.ChangeNote",

        "procedure_contract.Id", "procedure_contract.Category", "procedure_contract.Status",
        "procedure_contract_version.ContractId", "procedure_contract_version.Name",
        "procedure_contract_version.Description", "procedure_contract_version.Instructions",
        "procedure_contract_version.Constraints", "procedure_contract_version.Governs",
        "procedure_contract_version.CreatedBy", "procedure_contract_version.ChangeNote",

        "event_type.Id", "event_type.Category", "event_type.Status", "event_type.Scope",
        "event_type_version.EventTypeId", "event_type_version.Name", "event_type_version.Description",
        "event_type_version.PayloadSchema", "event_type_version.CreatedBy", "event_type_version.ChangeNote",

        "subscription.Id", "subscription.Category", "subscription.Status", "subscription.Scope",
        "subscription_version.SubscriptionId", "subscription_version.EventTypeId", "subscription_version.EventMechanicId",
        "subscription_version.Mode", "subscription_version.Order", "subscription_version.FixedRoleEntityIdsJson", "subscription_version.RoleFromEventPayloadJson", "subscription_version.FanoutSelectorJson",
        "subscription_version.TrackedEntityIdsJson", "subscription_version.PayloadEqualsJson",
        "subscription_version.MaxExecutionsPerChain", "subscription_version.CreatedBy", "subscription_version.ChangeNote",

        // The operation log is serialised whole, field for field. It is export only.
        "operation.Id", "operation.Error", "operation.Intent", "operation.ProceduresCited",
        "operation.ProceduresRead", "operation.Subject", "operation.Success", "operation.Summary",
        "operation.Timestamp", "operation.Tool", "operation.ConsumedReadEvidence",
        "operation.MechanicId", "operation.MechanicVersion", "operation.ProjectionJson", "operation.Seed"
    };

    /// <summary>Columns the catalog does not carry, and why each one is fine to lose.</summary>
    private static readonly Dictionary<string, string> NotCarried = new(StringComparer.Ordinal)
    {
        ["assistant_conversation.Id"] = "Live assistant conversation identity, not carried by the game catalog.",
        ["assistant_conversation.OperatorId"] = "Private operator scope, not carried by the game catalog.",
        ["assistant_conversation.Provider"] = "Runtime assistant provider selection, not carried by the game catalog.",
        ["assistant_conversation.ExternalThreadId"] = "Runtime provider thread identity, not carried by the game catalog.",
        ["assistant_conversation.Title"] = "Server-derived assistant conversation display text, not carried by the game catalog.",
        ["assistant_conversation.Revision"] = "Live assistant concurrency state, not carried by the game catalog.",
        ["assistant_conversation.Status"] = "Live assistant conversation status, not carried by the game catalog.",
        ["assistant_conversation.CreatedAtUtc"] = "Live assistant conversation timestamp, not carried by the game catalog.",
        ["assistant_conversation.UpdatedAtUtc"] = "Live assistant conversation timestamp, not carried by the game catalog.",
        ["assistant_turn.Id"] = "Runtime assistant turn identity, not carried by the game catalog.",
        ["assistant_turn.ConversationId"] = "Runtime assistant conversation ownership, not carried by the game catalog.",
        ["assistant_turn.OperatorId"] = "Private operator scope, not carried by the game catalog.",
        ["assistant_turn.Provider"] = "Runtime assistant provider evidence, not carried by the game catalog.",
        ["assistant_turn.TurnNumber"] = "Runtime assistant transcript ordering, not carried by the game catalog.",
        ["assistant_turn.IdempotencyKey"] = "Runtime assistant replay protection, not carried by the game catalog.",
        ["assistant_turn.RequestHash"] = "Runtime assistant replay evidence, not carried by the game catalog.",
        ["assistant_turn.Status"] = "Runtime assistant turn status, not carried by the game catalog.",
        ["assistant_turn.ExternalTurnId"] = "Runtime provider turn identity, not carried by the game catalog.",
        ["assistant_turn.ExternalStatus"] = "Runtime provider turn status, not carried by the game catalog.",
        ["assistant_turn.ErrorCode"] = "Runtime assistant failure evidence, not carried by the game catalog.",
        ["assistant_turn.ErrorMessage"] = "Runtime assistant failure evidence, not carried by the game catalog.",
        ["assistant_turn.ModelProvider"] = "Runtime local-model identity evidence, not carried by the game catalog.",
        ["assistant_turn.Model"] = "Runtime local-model identity evidence, not carried by the game catalog.",
        ["assistant_turn.ModelRevision"] = "Runtime local-model identity evidence, not carried by the game catalog.",
        ["assistant_turn.ModelProfile"] = "Runtime local-model profile evidence, not carried by the game catalog.",
        ["assistant_turn.ElapsedMilliseconds"] = "Runtime assistant timing evidence, not carried by the game catalog.",
        ["assistant_turn.PromptTokens"] = "Runtime assistant usage evidence, not carried by the game catalog.",
        ["assistant_turn.OutputTokens"] = "Runtime assistant usage evidence, not carried by the game catalog.",
        ["assistant_turn.CreatedAtUtc"] = "Runtime assistant turn timestamp, not carried by the game catalog.",
        ["assistant_turn.StartedAtUtc"] = "Runtime assistant turn timestamp, not carried by the game catalog.",
        ["assistant_turn.CompletedAtUtc"] = "Runtime assistant turn timestamp, not carried by the game catalog.",
        ["assistant_turn_activity.Id"] = "Runtime Codex activity identity, not carried by the game catalog.",
        ["assistant_turn_activity.ConversationId"] = "Runtime assistant conversation ownership, not carried by the game catalog.",
        ["assistant_turn_activity.TurnId"] = "Runtime assistant turn ownership, not carried by the game catalog.",
        ["assistant_turn_activity.Sequence"] = "Runtime Codex activity ordering, not carried by the game catalog.",
        ["assistant_turn_activity.ExternalItemId"] = "Runtime provider item identity, not carried by the game catalog.",
        ["assistant_turn_activity.Kind"] = "Runtime Codex activity kind, not carried by the game catalog.",
        ["assistant_turn_activity.Status"] = "Runtime Codex activity status, not carried by the game catalog.",
        ["assistant_turn_activity.Summary"] = "Runtime Codex activity display text, not carried by the game catalog.",
        ["assistant_turn_activity.CreatedAtUtc"] = "Runtime Codex activity timestamp, not carried by the game catalog.",
        ["assistant_turn_approval.Id"] = "Runtime Codex approval identity, not carried by the game catalog.",
        ["assistant_turn_approval.ConversationId"] = "Runtime assistant conversation ownership, not carried by the game catalog.",
        ["assistant_turn_approval.TurnId"] = "Runtime assistant turn ownership, not carried by the game catalog.",
        ["assistant_turn_approval.OperatorId"] = "Private operator scope, not carried by the game catalog.",
        ["assistant_turn_approval.ExternalRequestId"] = "Runtime provider request identity, not carried by the game catalog.",
        ["assistant_turn_approval.ExternalItemId"] = "Runtime provider item identity, not carried by the game catalog.",
        ["assistant_turn_approval.ExternalApprovalId"] = "Runtime provider approval identity, not carried by the game catalog.",
        ["assistant_turn_approval.Kind"] = "Runtime Codex approval kind, not carried by the game catalog.",
        ["assistant_turn_approval.RequestFingerprint"] = "Runtime approval replay evidence, not carried by the game catalog.",
        ["assistant_turn_approval.Summary"] = "Runtime approval display text, not carried by the game catalog.",
        ["assistant_turn_approval.DetailsJson"] = "Private normalized approval details, not carried by the game catalog.",
        ["assistant_turn_approval.CanAccept"] = "Runtime approval safety result, not carried by the game catalog.",
        ["assistant_turn_approval.Status"] = "Runtime approval lifecycle state, not carried by the game catalog.",
        ["assistant_turn_approval.Decision"] = "Private operator approval decision, not carried by the game catalog.",
        ["assistant_turn_approval.Revision"] = "Runtime approval concurrency state, not carried by the game catalog.",
        ["assistant_turn_approval.RequestedAtUtc"] = "Runtime approval timestamp, not carried by the game catalog.",
        ["assistant_turn_approval.ExpiresAtUtc"] = "Runtime approval expiry, not carried by the game catalog.",
        ["assistant_turn_approval.DecidedAtUtc"] = "Runtime approval timestamp, not carried by the game catalog.",
        ["assistant_turn_approval.DispatchedAtUtc"] = "Runtime approval timestamp, not carried by the game catalog.",
        ["assistant_turn_approval.ResolvedAtUtc"] = "Runtime approval timestamp, not carried by the game catalog.",
        ["assistant_message.Id"] = "Live assistant message identity, not carried by the game catalog.",
        ["assistant_message.ConversationId"] = "Live assistant conversation ownership, not carried by the game catalog.",
        ["assistant_message.TurnId"] = "Runtime assistant turn ownership, not carried by the game catalog.",
        ["assistant_message.Ordinal"] = "Live assistant transcript ordering, not carried by the game catalog.",
        ["assistant_message.Role"] = "Live assistant transcript role, not carried by the game catalog.",
        ["assistant_message.Content"] = "Private operator/assistant transcript content, not carried by the game catalog.",
        ["assistant_message.CreatedAtUtc"] = "Live assistant message timestamp, not carried by the game catalog.",

        ["host_setting_override.Key"] = "Host-local setting identity, not carried by the game catalog.",
        ["host_setting_override.CurrentVersion"] = "Host-local pending revision pointer, not carried by the game catalog.",
        ["host_setting_override.AppliedVersion"] = "Host-local startup application pointer, not carried by the game catalog.",
        ["host_setting_override.UpdatedAtUtc"] = "Host-local setting timestamp, not carried by the game catalog.",
        ["host_setting_override_version.Id"] = "Host-local history row identity, not carried by the game catalog.",
        ["host_setting_override_version.SettingKey"] = "Host-local setting history owner, not carried by the game catalog.",
        ["host_setting_override_version.Version"] = "Host-local setting revision, not carried by the game catalog.",
        ["host_setting_override_version.ValueJson"] = "Host-local setting value, not carried by the game catalog.",
        ["host_setting_override_version.CreatedAtUtc"] = "Host-local setting audit timestamp, not carried by the game catalog.",
        ["host_setting_override_version.CreatedBy"] = "Host-local operator identity, not carried by the game catalog.",
        ["host_setting_override_version.OperationId"] = "Host-local operation provenance, not carried by the game catalog.",
        ["system_application_activation_current.ApplicationId"] = "Live activation pointer, not carried by the catalog.",
        ["system_application_activation_current.ActivationRevision"] = "Live activation pointer revision, not carried by the catalog.",
        ["system_application_activation_revision.ApplicationId"] = "Immutable runtime activation evidence, not carried by the catalog.",
        ["system_application_activation_revision.ActivationRevision"] = "Immutable runtime activation evidence, not carried by the catalog.",
        ["system_application_activation_revision.ApplicationRevision"] = "Exact activated application revision evidence, not carried by the catalog.",
        ["system_application_activation_revision.ApplicationFingerprint"] = "Exact activated application fingerprint, not carried by the catalog.",
        ["system_application_activation_revision.PreviewFingerprint"] = "Exact activated preview fingerprint, not carried by the catalog.",
        ["system_application_activation_revision.ScannedDocumentsFingerprint"] = "Exact activated scan evidence, not carried by the catalog.",
        ["system_application_activation_revision.CandidateManifestFingerprint"] = "Exact activated candidate evidence, not carried by the catalog.",
        ["system_application_activation_revision.DependencyGraphFingerprint"] = "Declared dependency evidence at activation, not carried by the catalog.",
        ["system_application_activation_revision.ActivationFingerprint"] = "Immutable activation concurrency fingerprint, not carried by the catalog.",
        ["system_application_activation_revision.DependencyCoverageVersion"] = "Activation dependency-coverage policy evidence, not carried by the catalog.",
        ["system_application_activation_revision.DependencyCoverageComplete"] = "Activation dependency-coverage status, not carried by the catalog.",
        ["system_application_activation_revision.ActivatedByOperationId"] = "Activation audit provenance, not carried by the catalog.",
        ["system_application_activation_revision.ActivatedAtUtc"] = "Activation audit timestamp, not carried by the catalog.",
        ["system_application_activation_source.ApplicationId"] = "Activated source evidence owner, not carried by the catalog.",
        ["system_application_activation_source.ActivationRevision"] = "Activated source evidence revision, not carried by the catalog.",
        ["system_application_activation_source.Ordinal"] = "Activated source evidence order, not carried by the catalog.",
        ["system_application_activation_source.SourceId"] = "Activated source identity evidence, not carried by the catalog.",
        ["system_application_activation_source.RegistrationFingerprint"] = "Activated source registration fingerprint, not carried by the catalog.",
        ["system_application_activation_source.DocumentCount"] = "Activated source document count evidence, not carried by the catalog.",
        ["system_application_activation_source.ProblemCount"] = "Activated source problem count evidence, not carried by the catalog.",
        ["system_application_activation_document.ApplicationId"] = "Activated winner evidence owner, not carried by the catalog.",
        ["system_application_activation_document.ActivationRevision"] = "Activated winner evidence revision, not carried by the catalog.",
        ["system_application_activation_document.Ordinal"] = "Activated winner evidence order, not carried by the catalog.",
        ["system_application_activation_document.LogicalIdentity"] = "Activated winner logical identity, not carried by the catalog.",
        ["system_application_activation_document.SourceId"] = "Activated winner source identity, not carried by the catalog.",
        ["system_application_activation_document.Trust"] = "Activated winner trust evidence, not carried by the catalog.",
        ["system_application_activation_document.Precedence"] = "Activated winner precedence evidence, not carried by the catalog.",
        ["system_application_activation_document.RelativePath"] = "Redacted activated winner locator, not carried by the catalog.",
        ["system_application_activation_document.MediaType"] = "Activated winner media type evidence, not carried by the catalog.",
        ["system_application_activation_document.ContentFingerprint"] = "Activated winner content fingerprint, not carried by the catalog.",
        ["system_application_activation_document.Length"] = "Activated winner length evidence, not carried by the catalog.",
        ["system_application_activation_document.IsText"] = "Activated winner content-kind evidence, not carried by the catalog.",
        ["system_application_activation_receipt.OperationId"] = "Immutable activation replay receipt identity, not carried by the catalog.",
        ["system_application_activation_receipt.RequestFingerprint"] = "Immutable activation request evidence, not carried by the catalog.",
        ["system_application_activation_receipt.ApplicationId"] = "Activation replay receipt owner, not carried by the catalog.",
        ["system_application_activation_receipt.ActivationRevision"] = "Activation replay receipt revision, not carried by the catalog.",
        ["system_application_activation_receipt.Outcome"] = "Activation replay receipt outcome, not carried by the catalog.",
        ["system_application.Id"] = "Live generic application-registry configuration, not carried by the catalog.",
        ["system_application.DisplayName"] = "Live generic application-registry configuration, not carried by the catalog.",
        ["system_application.Description"] = "Live generic application-registry configuration, not carried by the catalog.",
        ["system_application.CreatedAtUtc"] = "Live generic application-registry evidence, not carried by the catalog.",
        ["system_application_revision.ApplicationId"] = "Immutable live application-registry revision evidence, not carried by the catalog.",
        ["system_application_revision.Revision"] = "Immutable live application-registry revision evidence, not carried by the catalog.",
        ["system_application_revision.Fingerprint"] = "Immutable live application-registry revision evidence, not carried by the catalog.",
        ["system_application_revision.CreatedAtUtc"] = "Immutable live application-registry revision evidence, not carried by the catalog.",
        ["system_application_revision_base.ApplicationId"] = "Immutable live base-application relationship evidence, not carried by the catalog.",
        ["system_application_revision_base.Revision"] = "Immutable live base-application relationship evidence, not carried by the catalog.",
        ["system_application_revision_base.Ordinal"] = "Immutable live base-application relationship evidence, not carried by the catalog.",
        ["system_application_revision_base.BaseApplicationId"] = "Immutable live base-application relationship evidence, not carried by the catalog.",
        ["system_application_source.ApplicationId"] = "Live allowed-root/source registration, not carried by the catalog.",
        ["system_application_source.SourceId"] = "Live allowed-root/source registration, not carried by the catalog.",
        ["system_application_source.AllowedRootId"] = "Host-local allowed-root reference, not carried by the catalog.",
        ["system_application_source.RelativePathOrGlob"] = "Live source registration, not carried by the catalog.",
        ["system_application_source.Trust"] = "Live source registration, not carried by the catalog.",
        ["system_application_source.Precedence"] = "Live source registration, not carried by the catalog.",
        ["system_application_source.LogicalIdentity"] = "Live source registration, not carried by the catalog.",
        ["system_application_source.CreatedAtUtc"] = "Live source registration evidence, not carried by the catalog.",
        ["system_application_source_scan.ApplicationId"] = "Runtime source-scan evidence, not carried by the catalog.",
        ["system_application_source_scan.SourceId"] = "Runtime source-scan evidence, not carried by the catalog.",
        ["system_application_source_scan.Generation"] = "Runtime source-scan evidence, not carried by the catalog.",
        ["system_application_source_scan.Status"] = "Runtime source-scan evidence, not carried by the catalog.",
        ["system_application_source_scan.ContentFingerprint"] = "Runtime source-scan evidence, not carried by the catalog.",
        ["system_application_source_scan.RecordedAtUtc"] = "Runtime source-scan evidence, not carried by the catalog.",
        ["system_component_type.QualifiedId"] = "Live application component-type identity, not carried by the catalog.",
        ["system_component_type.ApplicationId"] = "Live application component-type ownership, not carried by the catalog.",
        ["system_component_type.CreatedAtUtc"] = "Live component-type registration evidence, not carried by the catalog.",
        ["system_component_type_version.QualifiedId"] = "Immutable live component schema identity, not carried by the catalog.",
        ["system_component_type_version.Version"] = "Immutable live component schema version, not carried by the catalog.",
        ["system_component_type_version.ProfileId"] = "Bounded schema-profile identity, not carried by the catalog.",
        ["system_component_type_version.SchemaJson"] = "Validated live application schema, not carried by the bootstrap catalog.",
        ["system_component_type_version.SchemaHash"] = "Immutable live schema fingerprint, not carried by the catalog.",
        ["system_component_type_version.CreatedAtUtc"] = "Live component schema registration evidence, not carried by the catalog.",
        ["system_state_space.Id"] = "Live application-scoped state-space identity, not carried by the catalog.",
        ["system_state_space.ApplicationId"] = "Live state-space application binding, not carried by the catalog.",
        ["system_state_space.ApplicationRevision"] = "Live state-space application revision binding, not carried by the catalog.",
        ["system_state_space.ManifestFingerprint"] = "Live state-space manifest evidence, not carried by the catalog.",
        ["system_state_space.BindingRevision"] = "Live state-space binding concurrency evidence, not carried by the catalog.",
        ["system_state_space.CreatedAtUtc"] = "Live state-space creation evidence, not carried by the catalog.",
        ["system_state_space.UpdatedAtUtc"] = "Live state-space binding update evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.StateSpaceId"] = "Immutable state-space binding history, not carried by the catalog.",
        ["system_state_space_binding_revision.BindingRevision"] = "Immutable state-space binding revision evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.ApplicationId"] = "Historical state-space application ownership, not carried by the catalog.",
        ["system_state_space_binding_revision.ApplicationRevision"] = "Historical application revision binding, not carried by the catalog.",
        ["system_state_space_binding_revision.ApplicationFingerprint"] = "Historical application fingerprint evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.ActiveFingerprint"] = "Historical active-overlay binding evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.BindingFingerprint"] = "Historical state-space binding fingerprint, not carried by the catalog.",
        ["system_state_space_binding_revision.PreviousBindingFingerprint"] = "Historical binding-chain evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.CompatibilityCode"] = "Runtime compatibility decision evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.EntityCount"] = "Runtime compatibility entity-count evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.ComponentCount"] = "Runtime compatibility component-count evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.DependencyCoverageVersion"] = "Runtime dependency-coverage evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.DependencyCoverageComplete"] = "Runtime dependency-coverage boundary, not carried by the catalog.",
        ["system_state_space_binding_revision.OperationId"] = "Operation-linked state-space binding evidence, not carried by the catalog.",
        ["system_state_space_binding_revision.CreatedAtUtc"] = "Historical state-space creation timestamp, not carried by the catalog.",
        ["system_state_space_binding_revision.UpdatedAtUtc"] = "Historical state-space update timestamp, not carried by the catalog.",
        ["system_state_space_binding_revision.RecordedAtUtc"] = "Immutable binding-history timestamp, not carried by the catalog.",
        ["system_ecs_entity.StateSpaceId"] = "Live application-scoped entity state, not carried by the catalog.",
        ["system_ecs_entity.Id"] = "Live application-scoped entity identity, not carried by the catalog.",
        ["system_ecs_entity.Name"] = "Live application-scoped entity label, not carried by the catalog.",
        ["system_ecs_entity.Revision"] = "Live application-scoped entity concurrency evidence, not carried by the catalog.",
        ["system_ecs_entity.CreatedAtUtc"] = "Live application-scoped entity creation evidence, not carried by the catalog.",
        ["system_ecs_entity.DeletedAtUtc"] = "Live application-scoped entity deletion evidence, not carried by the catalog.",
        ["system_ecs_component.StateSpaceId"] = "Live application-scoped component state, not carried by the catalog.",
        ["system_ecs_component.EntityId"] = "Live application-scoped component entity identity, not carried by the catalog.",
        ["system_ecs_component.QualifiedTypeId"] = "Exact live application component type identity, not carried by the catalog.",
        ["system_ecs_component.TypeVersion"] = "Exact live immutable component type version, not carried by the catalog.",
        ["system_ecs_component.SchemaHash"] = "Exact live component schema fingerprint, not carried by the catalog.",
        ["system_ecs_component.Data"] = "Live application-scoped JSON component value, not carried by the catalog.",
        ["system_ecs_component.Revision"] = "Live application-scoped component concurrency evidence, not carried by the catalog.",
        ["system_ecs_component.CreatedAtUtc"] = "Live application-scoped component creation evidence, not carried by the catalog.",
        ["system_ecs_component.UpdatedAtUtc"] = "Live application-scoped component update evidence, not carried by the catalog.",
        ["system_ecs_containment.StateSpaceId"] = "Live application-scoped containment state, not carried by the catalog.",
        ["system_ecs_containment.ContainedEntityId"] = "Live contained entity identity, not carried by the catalog.",
        ["system_ecs_containment.ContainerEntityId"] = "Live container entity identity, not carried by the catalog.",
        ["system_ecs_containment.Slot"] = "Application-owned live containment metadata, not carried by the catalog.",
        ["system_ecs_containment.Revision"] = "Live containment concurrency evidence, not carried by the catalog.",
        ["system_ecs_containment.CreatedAtUtc"] = "Live containment creation evidence, not carried by the catalog.",
        ["system_ecs_containment.UpdatedAtUtc"] = "Live containment update evidence, not carried by the catalog.",
        ["system_ecs_relationship.StateSpaceId"] = "Live application-scoped relationship state, not carried by the catalog.",
        ["system_ecs_relationship.FromEntityId"] = "Live relationship source identity, not carried by the catalog.",
        ["system_ecs_relationship.ToEntityId"] = "Live relationship target identity, not carried by the catalog.",
        ["system_ecs_relationship.QualifiedKind"] = "Application-owned live relationship kind, not carried by the catalog.",
        ["system_ecs_relationship.Data"] = "Application-owned live relationship JSON, not carried by the catalog.",
        ["system_ecs_relationship.Revision"] = "Live relationship concurrency evidence, not carried by the catalog.",
        ["system_ecs_relationship.CreatedAtUtc"] = "Live relationship creation evidence, not carried by the catalog.",
        ["system_ecs_relationship.UpdatedAtUtc"] = "Live relationship update evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.StateSpaceId"] = "Runtime adoption target identity, not carried by the catalog.",
        ["system_legacy_state_adoption.ApplicationId"] = "Runtime adoption application ownership, not carried by the catalog.",
        ["system_legacy_state_adoption.RequestFingerprint"] = "Immutable adoption request evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.SourceFingerprint"] = "Immutable legacy source evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.EvidenceFingerprint"] = "Immutable adoption preflight evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.EntityCount"] = "Runtime adoption entity-count evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.ComponentCount"] = "Runtime adoption component-count evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.ContainmentCount"] = "Runtime adoption containment-count evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.RelationshipCount"] = "Runtime adoption relationship-count evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.OperationId"] = "Operation-linked adoption replay evidence, not carried by the catalog.",
        ["system_legacy_state_adoption.CreatedAtUtc"] = "Immutable adoption timestamp, not carried by the catalog.",
        ["system_projection_definition.QualifiedId"] = "Live projection identity, not carried by the catalog.",
        ["system_projection_definition.ApplicationId"] = "Live projection ownership, not carried by the catalog.",
        ["system_projection_definition.CreatedAtUtc"] = "Live projection registration evidence, not carried by the catalog.",
        ["system_projection_definition_version.QualifiedId"] = "Immutable projection identity, not carried by the catalog.",
        ["system_projection_definition_version.Version"] = "Immutable projection version, not carried by the catalog.",
        ["system_projection_definition_version.ProfileId"] = "Projection schema profile, not carried by the catalog.",
        ["system_projection_definition_version.OutputSchemaJson"] = "Validated projection output schema, not carried by the catalog.",
        ["system_projection_definition_version.OutputSchemaHash"] = "Projection output schema fingerprint, not carried by the catalog.",
        ["system_projection_definition_version.ContentHash"] = "Projection definition fingerprint, not carried by the catalog.",
        ["system_projection_definition_version.CreatedAtUtc"] = "Projection registration evidence, not carried by the catalog.",
        ["system_projection_component_input.QualifiedId"] = "Projection input owner, not carried by the catalog.",
        ["system_projection_component_input.Version"] = "Projection input version, not carried by the catalog.",
        ["system_projection_component_input.InputId"] = "Projection input identity, not carried by the catalog.",
        ["system_projection_component_input.EntityRole"] = "Projection entity role, not carried by the catalog.",
        ["system_projection_component_input.QualifiedTypeId"] = "Exact component type input, not carried by the catalog.",
        ["system_projection_component_input.TypeVersion"] = "Exact component type version, not carried by the catalog.",
        ["system_projection_component_input.SchemaHash"] = "Exact component schema evidence, not carried by the catalog.",
        ["system_projection_component_input.Ordinal"] = "Projection input order, not carried by the catalog.",
        ["system_projection_dependency_input.QualifiedId"] = "Projection dependency owner, not carried by the catalog.",
        ["system_projection_dependency_input.Version"] = "Projection dependency version, not carried by the catalog.",
        ["system_projection_dependency_input.InputId"] = "Projection dependency identity, not carried by the catalog.",
        ["system_projection_dependency_input.DependencyQualifiedId"] = "Exact dependency projection ID, not carried by the catalog.",
        ["system_projection_dependency_input.DependencyVersion"] = "Exact dependency projection version, not carried by the catalog.",
        ["system_projection_dependency_input.DependencyContentHash"] = "Exact dependency projection fingerprint, not carried by the catalog.",
        ["system_projection_dependency_input.RoleBindingsJson"] = "Projection role binding declaration, not carried by the catalog.",
        ["system_projection_dependency_input.Ordinal"] = "Projection dependency order, not carried by the catalog.",
        ["system_projection_mapping.QualifiedId"] = "Projection mapping owner, not carried by the catalog.",
        ["system_projection_mapping.Version"] = "Projection mapping version, not carried by the catalog.",
        ["system_projection_mapping.TargetPointer"] = "Structural projection target pointer, not carried by the catalog.",
        ["system_projection_mapping.InputId"] = "Structural projection source input, not carried by the catalog.",
        ["system_projection_mapping.SourcePointer"] = "Structural projection source pointer, not carried by the catalog.",
        ["system_projection_mapping.Ordinal"] = "Projection mapping order, not carried by the catalog.",
        ["information_source.Id"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_source.ScopeId"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_source.Name"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_source.Description"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_source.MetadataSchemaJson"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_source.ContentHash"] = "Live MCP-authored generic information revision evidence, not catalog content.",
        ["information_source.Revision"] = "Live MCP-authored generic information revision evidence, not catalog content.",
        ["information_source.CreatedAtUtc"] = "Live MCP-authored generic information timestamp, not catalog content.",
        ["information_source.UpdatedAtUtc"] = "Live MCP-authored generic information timestamp, not catalog content.",
        ["information_record.Id"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_record.SourceId"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_record.Title"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_record.Content"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_record.MetadataJson"] = "Live MCP-authored generic information, not carried by the bootstrap catalog.",
        ["information_record.ContentHash"] = "Live MCP-authored generic information revision evidence, not catalog content.",
        ["information_record.Revision"] = "Live MCP-authored generic information revision evidence, not catalog content.",
        ["information_record.CreatedAtUtc"] = "Live MCP-authored generic information timestamp, not catalog content.",
        ["information_record.UpdatedAtUtc"] = "Live MCP-authored generic information timestamp, not catalog content.",
        ["information_action_contract.Id"] = "Live MCP-authored action contract, not carried by the bootstrap catalog.",
        ["information_action_contract.ScopeId"] = "Live MCP-authored action contract, not carried by the bootstrap catalog.",
        ["information_action_contract.Name"] = "Live MCP-authored action contract, not carried by the bootstrap catalog.",
        ["information_action_contract.Description"] = "Live MCP-authored action contract, not carried by the bootstrap catalog.",
        ["information_action_contract.ExecutorId"] = "Live MCP-authored action contract, not carried by the bootstrap catalog.",
        ["information_action_contract.InputSchemaJson"] = "Live MCP-authored action contract, not carried by the bootstrap catalog.",
        ["information_action_contract.RuleRecordIdsJson"] = "Live MCP-authored action contract, not carried by the bootstrap catalog.",
        ["information_action_contract.ContentHash"] = "Live MCP-authored action contract revision evidence, not catalog content.",
        ["information_action_contract.Revision"] = "Live MCP-authored action contract revision evidence, not catalog content.",
        ["information_action_contract.CreatedAtUtc"] = "Live MCP-authored action contract timestamp, not catalog content.",
        ["information_action_contract.UpdatedAtUtc"] = "Live MCP-authored action contract timestamp, not catalog content.",
        // --- Surrogate keys. The catalog addresses records by their real identity.
        ["component.Id"] = "Surrogate key. A component is addressed by (entity, definition).",
        ["containment.Id"] = "Surrogate key. Containment is a property of the contained entity.",
        ["relationship.Id"] = "Surrogate key. An edge is addressed by (from, to, kind).",
        ["mechanic_version.Id"] = "Surrogate key.",
        ["procedure_contract_version.Id"] = "Surrogate key.",
        ["event_type_version.Id"] = "Surrogate key.",
        ["subscription_version.Id"] = "Surrogate key.",
        // --- Derived. Recomputed on write; carrying them would let a file assert something false.
        ["component.Revision"] = "Derived: a count of writes, incremented by the store.",
        ["mechanic.CurrentVersion"] = "Derived from the version rows.",
        ["procedure_contract.CurrentVersion"] = "Derived from the version rows.",
        ["mechanic_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["procedure_contract_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["event_type.CurrentVersion"] = "Derived from the version rows.",
        ["subscription.CurrentVersion"] = "Derived from the version rows.",
        ["event_type_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["subscription_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["mechanic_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["procedure_contract_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["event_type_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["subscription_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["operation.GuardEvidenceJson"] = "Runtime audit evidence, not authored catalog content.",

        // --- Story-plan orchestration. The run and its steps are durable only so a live
        //     development game can resume, cancel, and audit bounded local work. They are not
        //     world content and must never be recreated by a catalog import.
        ["story_plan_run.Id"] = "Runtime story-plan identity, not carried by the catalog.",
        ["story_plan_run.RequestToken"] = "Runtime idempotency evidence for a story-plan request.",
        ["story_plan_run.CampaignId"] = "Runtime story-plan scope, not authored catalog content.",
        ["story_plan_run.PrincipalId"] = "Runtime developer principal evidence, not catalog content.",
        ["story_plan_run.Objective"] = "Runtime local-model request text, not authored catalog content.",
        ["story_plan_run.PlanJson"] = "Runtime canonical plan request, not authored catalog content.",
        ["story_plan_run.Status"] = "Runtime worker state, not authored catalog content.",
        ["story_plan_run.NextStepIndex"] = "Runtime worker progress, not authored catalog content.",
        ["story_plan_run.CompletedStepCount"] = "Runtime worker progress, not authored catalog content.",
        ["story_plan_run.CancelRequested"] = "Runtime cancellation signal, not authored catalog content.",
        ["story_plan_run.LeaseOwner"] = "Runtime worker lease, not authored catalog content.",
        ["story_plan_run.LeaseUntilUtc"] = "Runtime worker lease, not authored catalog content.",
        ["story_plan_run.PolicyRevision"] = "Runtime policy evidence, not authored catalog content.",
        ["story_plan_run.HandoffJson"] = "Runtime story handoff, not authored catalog content.",
        ["story_plan_run.StopCode"] = "Runtime terminal outcome, not authored catalog content.",
        ["story_plan_run.StopMessage"] = "Runtime terminal outcome, not authored catalog content.",
        ["story_plan_run.Revision"] = "Runtime optimistic-concurrency evidence, not catalog content.",
        ["story_plan_run.CreatedAtUtc"] = "Runtime timestamp, not authored catalog content.",
        ["story_plan_run.UpdatedAtUtc"] = "Runtime timestamp, not authored catalog content.",
        ["story_plan_step_run.StoryPlanId"] = "Runtime story-plan step identity, not catalog content.",
        ["story_plan_step_run.StepIndex"] = "Runtime plan ordering, not authored catalog content.",
        ["story_plan_step_run.StepId"] = "Runtime plan step identity, not authored catalog content.",
        ["story_plan_step_run.Kind"] = "Runtime plan step definition, not authored catalog content.",
        ["story_plan_step_run.Intent"] = "Runtime plan step definition, not authored catalog content.",
        ["story_plan_step_run.InputJson"] = "Runtime plan step input, not authored catalog content.",
        ["story_plan_step_run.RoleEntityIdsJson"] = "Runtime plan step roles, not authored catalog content.",
        ["story_plan_step_run.Status"] = "Runtime worker state, not authored catalog content.",
        ["story_plan_step_run.MechanicId"] = "Runtime action-routing evidence, not catalog content.",
        ["story_plan_step_run.MechanicVersion"] = "Runtime action-routing evidence, not catalog content.",
        ["story_plan_step_run.ProcedureEvidenceJson"] = "Runtime procedure-read evidence, not catalog content.",
        ["story_plan_step_run.ResultJson"] = "Runtime step result, not authored catalog content.",
        ["story_plan_step_run.ErrorCode"] = "Runtime failed-step evidence, not catalog content.",
        ["story_plan_step_run.ErrorMessage"] = "Runtime failed-step evidence, not catalog content.",
        ["story_plan_step_run.ActionOperationId"] = "Runtime action receipt reference, not catalog content.",
        ["story_plan_step_run.StartedAtUtc"] = "Runtime timestamp, not authored catalog content.",
        ["story_plan_step_run.CompletedAtUtc"] = "Runtime timestamp, not authored catalog content.",

        // --- Timestamps. Provenance about when a row was touched, not what it says.
        ["entity.CreatedAt"] = "Timestamp.",
        ["component.CreatedAt"] = "Timestamp.",
        ["component.UpdatedAt"] = "Timestamp.",
        ["containment.CreatedAt"] = "Timestamp.",
        ["relationship.CreatedAt"] = "Timestamp.",
        ["component_definition.CreatedAt"] = "Timestamp.",
        ["component_definition.UpdatedAt"] = "Timestamp.",
        ["mechanic.CreatedAt"] = "Timestamp.",
        ["mechanic.UpdatedAt"] = "Timestamp.",
        ["mechanic_version.CreatedAt"] = "Timestamp.",
        ["procedure_contract.CreatedAt"] = "Timestamp.",
        ["procedure_contract.UpdatedAt"] = "Timestamp.",
        ["procedure_contract_version.CreatedAt"] = "Timestamp.",
        ["event_type.CreatedAt"] = "Timestamp.",
        ["event_type.UpdatedAt"] = "Timestamp.",
        ["event_type_version.CreatedAt"] = "Timestamp.",
        ["subscription.CreatedAt"] = "Timestamp.",
        ["subscription.UpdatedAt"] = "Timestamp.",
        ["subscription_version.CreatedAt"] = "Timestamp.",

        // --- Tombstones. A catalog states what the world IS; re-importing one would resurrect a
        //     row somebody deleted on purpose, so deleted entities are not exported at all.
        ["entity.DeletedAt"] = "Tombstone. Soft-deleted entities are excluded from the export.",

        // --- The event ledger. Runtime evidence; see the note in SkippedTables.
        ["event.Id"] = "Runtime evidence, not carried by the catalog.",
        ["event.TypeId"] = "Runtime evidence, not carried by the catalog.",
        ["event.TypeVersion"] = "Runtime evidence, not carried by the catalog.",
        ["event.Scope"] = "Runtime evidence, not carried by the catalog.",
        ["event.PayloadJson"] = "Runtime evidence, not carried by the catalog.",
        ["event.Timestamp"] = "Runtime evidence, not carried by the catalog.",
        ["event.CorrelationId"] = "Runtime evidence, not carried by the catalog.",
        ["event.CausationId"] = "Runtime evidence, not carried by the catalog.",
        ["event.Depth"] = "Runtime evidence, not carried by the catalog.",
        ["event.Sequence"] = "Runtime evidence, not carried by the catalog.",
        ["event.RootOperationId"] = "Runtime evidence, not carried by the catalog.",
        ["event.ProducerExecutionId"] = "Runtime evidence, not carried by the catalog.",
        ["notification.Id"] = "Runtime evidence, not carried by the catalog.",
        ["notification.Topic"] = "Runtime evidence, not carried by the catalog.",
        ["notification.Subject"] = "Runtime evidence, not carried by the catalog.",
        ["notification.Body"] = "Runtime evidence, not carried by the catalog.",
        ["notification.CorrelationId"] = "Runtime evidence, not carried by the catalog.",
        ["notification.EventId"] = "Runtime evidence, not carried by the catalog.",
        ["notification.ExecutionId"] = "Runtime evidence, not carried by the catalog.",
        ["notification.RootOperationId"] = "Runtime evidence, not carried by the catalog.",
        ["notification.Ordinal"] = "Runtime evidence, not carried by the catalog.",
        ["notification.CreatedAt"] = "Runtime evidence, not carried by the catalog.",
        ["notification.State"] = "Delivery state, changed only by commit(kind: \"notification\").",
        ["notification.ReadAt"] = "Delivery state, changed only by commit(kind: \"notification\").",
        ["notification.ArchivedAt"] = "Delivery state, changed only by commit(kind: \"notification\").",
        ["notification_entity.Id"] = "Runtime evidence, not carried by the catalog.",
        ["notification_entity.NotificationId"] = "Runtime evidence, not carried by the catalog.",
        ["notification_entity.EntityId"] = "Runtime evidence, not carried by the catalog.",
        ["notification_entity.Ordinal"] = "Runtime evidence, not carried by the catalog.",
        ["system_feedback_report.Id"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.RequestToken"] = "Idempotency evidence for a runtime feedback submission.",
        ["system_feedback_report.PayloadFingerprint"] = "Idempotency evidence for a runtime feedback submission.",
        ["system_feedback_report.Category"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.Impact"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.State"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.TriageRevision"] = "Runtime optimistic-concurrency evidence for local feedback triage.",
        ["system_feedback_report.RetentionRevision"] = "Runtime optimistic-concurrency evidence for local feedback retention.",
        ["system_feedback_report.ArchivedAt"] = "Reversible local feedback-retention projection, not catalog content.",
        ["system_feedback_report.HoldState"] = "Runtime local feedback-retention projection, not catalog content.",
        ["system_feedback_report.Summary"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.Observed"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.Expected"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.CreatedAt"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_report.SubmissionOperationId"] = "Runtime audit evidence, not carried by the catalog.",
        ["system_feedback_step.Id"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_step.ReportId"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_step.Ordinal"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_step.Text"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_operation.Id"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_operation.ReportId"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_operation.OperationId"] = "Runtime audit evidence, not carried by the catalog.",
        ["system_feedback_operation.Ordinal"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_procedure.Id"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_procedure.ReportId"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_procedure.ProcedureId"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_procedure.ProcedureVersion"] = "Frozen runtime procedure revision, not carried by the catalog.",
        ["system_feedback_procedure.Ordinal"] = "Runtime system-feedback evidence, not carried by the catalog.",
        ["system_feedback_disposition.Id"] = "Runtime local feedback-triage evidence, not carried by the catalog.",
        ["system_feedback_disposition.ReportId"] = "Runtime local feedback-triage evidence, not carried by the catalog.",
        ["system_feedback_disposition.Revision"] = "Runtime local feedback-triage evidence, not carried by the catalog.",
        ["system_feedback_disposition.FromState"] = "Runtime local feedback-triage evidence, not carried by the catalog.",
        ["system_feedback_disposition.ToState"] = "Runtime local feedback-triage evidence, not carried by the catalog.",
        ["system_feedback_disposition.Note"] = "Runtime local feedback-triage evidence, not carried by the catalog.",
        ["system_feedback_disposition.CreatedAt"] = "Runtime local feedback-triage evidence, not carried by the catalog.",
        ["system_feedback_retention_action.Id"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.ReportId"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.Revision"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.Action"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.FromArchived"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.ToArchived"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.FromHoldState"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.ToHoldState"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.Reference"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.Note"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.EffectiveAsOf"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["system_feedback_retention_action.CreatedAt"] = "Runtime local feedback-retention evidence, not carried by the catalog.",
        ["event_entity.Id"] = "Runtime evidence, not carried by the catalog.",
        ["event_entity.EventId"] = "Runtime evidence, not carried by the catalog.",
        ["event_entity.EntityId"] = "Runtime evidence, not carried by the catalog.",
        ["event_entity.Ordinal"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.Id"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.EventId"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.Ordinal"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.SubscriptionId"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.SubscriptionVersion"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.MechanicId"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.MechanicVersion"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.Seed"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.ProjectionJson"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.OutputJson"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.EffectCount"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.EventCount"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.Narration"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.LogJson"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.ElapsedMilliseconds"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.LimitHit"] = "Runtime evidence, not carried by the catalog.",
        ["event_execution.CreatedAt"] = "Runtime evidence, not carried by the catalog.",

        // --- Snapshot package. The entire immutable evidence package is intentionally absent
        //     from catalog export/import; see the corresponding skipped-table decision above.
        ["snapshot_package.Id"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.ScopeContractId"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.ScopeContractVersion"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.ProducerId"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.ProducerVersion"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.ContentEncoding"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.BoundaryFingerprint"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.DigestAlgorithm"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.ContentDigest"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.ByteCount"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.CapturedAt"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.RootOperationId"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.Availability"] = "Runtime evidence, not carried by the catalog.",
        ["snapshot_package.Content"] = "Runtime evidence, not carried by the catalog.",

        ["interaction_resolution_receipt.Id"] = "Immutable runtime interaction receipt identity, not carried by the catalog.",
        ["interaction_resolution_receipt.PrincipalReference"] = "Runtime authorization evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.ApplicationId"] = "Runtime application scope evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.ApplicationRevision"] = "Runtime application revision evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.ApplicationFingerprint"] = "Runtime application fingerprint evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.StateSpaceId"] = "Runtime state-space scope evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.SessionContextId"] = "Runtime session scope evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.StateRevision"] = "Runtime state revision evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.EffectiveSetFingerprint"] = "Runtime effective-overlay evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.RoleProfile"] = "Runtime fixed AI-role evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.ConversationId"] = "Opaque runtime conversation reference, not carried by the catalog.",
        ["interaction_resolution_receipt.ParentDelegationId"] = "Opaque runtime delegation reference, not carried by the catalog.",
        ["interaction_resolution_receipt.AuthorizationEvidenceReference"] = "Runtime authorization evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.IdempotencyKey"] = "Runtime replay evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.EnvelopeFingerprint"] = "Runtime intent-envelope fingerprint, not carried by the catalog.",
        ["interaction_resolution_receipt.QueryFingerprint"] = "Runtime redacted retrieval-query fingerprint, not carried by the catalog.",
        ["interaction_resolution_receipt.Status"] = "Runtime resolution outcome, not carried by the catalog.",
        ["interaction_resolution_receipt.Code"] = "Runtime resolution outcome code, not carried by the catalog.",
        ["interaction_resolution_receipt.ProposalFingerprint"] = "Runtime inert proposal fingerprint, not carried by the catalog.",
        ["interaction_resolution_receipt.SafeSummary"] = "Runtime redacted outcome summary, not carried by the catalog.",
        ["interaction_resolution_receipt.EvidenceJson"] = "Runtime redacted evidence, not carried by the catalog.",
        ["interaction_resolution_receipt.CreatedAtUtc"] = "Runtime receipt timestamp, not carried by the catalog.",
        ["interaction_resolution_receipt.RecipeId"] = "Optional private learned-route provenance, not carried by the catalog.",
        ["interaction_resolution_receipt.RecipeVersion"] = "Optional private learned-route revision provenance, not carried by the catalog.",
        ["interaction_resolution_receipt.RecipeTemplateFingerprint"] = "Optional private learned-route template provenance, not carried by the catalog.",

        ["interaction_execution_receipt.Id"] = "Immutable runtime execution receipt identity, not carried by the catalog.",
        ["interaction_execution_receipt.ResolutionReceiptId"] = "Runtime parent receipt evidence, not carried by the catalog.",
        ["interaction_execution_receipt.PrincipalReference"] = "Runtime authorization evidence, not carried by the catalog.",
        ["interaction_execution_receipt.ApplicationId"] = "Runtime application scope evidence, not carried by the catalog.",
        ["interaction_execution_receipt.StateSpaceId"] = "Runtime state-space scope evidence, not carried by the catalog.",
        ["interaction_execution_receipt.IdempotencyKey"] = "Runtime replay evidence, not carried by the catalog.",
        ["interaction_execution_receipt.ExecutionRequestFingerprint"] = "Runtime execution-request fingerprint, not carried by the catalog.",
        ["interaction_execution_receipt.ProposalFingerprint"] = "Runtime proposal fingerprint, not carried by the catalog.",
        ["interaction_execution_receipt.Disposition"] = "Runtime execution disposition, not carried by the catalog.",
        ["interaction_execution_receipt.SafeSummary"] = "Runtime redacted outcome summary, not carried by the catalog.",
        ["interaction_execution_receipt.EvidenceJson"] = "Runtime redacted evidence, not carried by the catalog.",
        ["interaction_execution_receipt.CreatedAtUtc"] = "Runtime receipt timestamp, not carried by the catalog.",

        ["interaction_execution_receipt_step.ExecutionReceiptId"] = "Runtime parent execution receipt identity, not carried by the catalog.",
        ["interaction_execution_receipt_step.Ordinal"] = "Runtime execution-step ordering evidence, not carried by the catalog.",
        ["interaction_execution_receipt_step.ProposalStepId"] = "Runtime inert proposal-step reference, not carried by the catalog.",
        ["interaction_execution_receipt_step.Disposition"] = "Runtime execution-step disposition, not carried by the catalog.",
        ["interaction_execution_receipt_step.OperationId"] = "Runtime link to existing operation audit evidence, not carried by the catalog.",

        ["interaction_recipe.Id"] = "Private learned-route identity, not carried by the catalog.",
        ["interaction_recipe.ApplicationId"] = "Private learned-route application scope, not carried by the catalog.",
        ["interaction_recipe.TemplateFingerprint"] = "Private inert-template identity, not carried by the catalog.",
        ["interaction_recipe.TemplateJson"] = "Private inert learned template, not carried by the catalog.",
        ["interaction_recipe.CreatedAtUtc"] = "Private learned-route creation timestamp, not carried by the catalog.",
        ["interaction_recipe_revision.RecipeId"] = "Private learned-route revision owner, not carried by the catalog.",
        ["interaction_recipe_revision.Version"] = "Append-only private learned-route revision number, not carried by the catalog.",
        ["interaction_recipe_revision.Status"] = "Private learned-route review status, not carried by the catalog.",
        ["interaction_recipe_revision.ApplicationRevision"] = "Runtime application authority evidence, not carried by the catalog.",
        ["interaction_recipe_revision.ApplicationFingerprint"] = "Runtime application authority fingerprint, not carried by the catalog.",
        ["interaction_recipe_revision.EffectiveSetFingerprint"] = "Runtime overlay authority fingerprint, not carried by the catalog.",
        ["interaction_recipe_revision.ReviewerPrincipalReference"] = "Opaque private reviewer evidence, not carried by the catalog.",
        ["interaction_recipe_revision.Reason"] = "Private bounded review reason, not carried by the catalog.",
        ["interaction_recipe_revision.RequestToken"] = "Private review replay token, not carried by the catalog.",
        ["interaction_recipe_revision.RequestFingerprint"] = "Private review replay fingerprint, not carried by the catalog.",
        ["interaction_recipe_revision.CreatedAtUtc"] = "Private revision timestamp, not carried by the catalog.",
        ["interaction_recipe_evidence.RecipeId"] = "Private learned-route evidence owner, not carried by the catalog.",
        ["interaction_recipe_evidence.ExecutionReceiptId"] = "Runtime execution provenance, not carried by the catalog.",
        ["interaction_recipe_evidence.ResolutionReceiptId"] = "Runtime resolution provenance, not carried by the catalog.",
        ["interaction_recipe_evidence.Kind"] = "Closed runtime learning/use evidence kind, not carried by the catalog.",
        ["interaction_recipe_evidence.IntentText"] = "Private bounded lexical example, never catalog-exported.",
        ["interaction_recipe_evidence.IntentFingerprint"] = "Private intent evidence fingerprint, not carried by the catalog.",
        ["interaction_recipe_evidence.RoleProfile"] = "Runtime AI-role evidence, not carried by the catalog.",
        ["interaction_recipe_evidence.CreatedAtUtc"] = "Private evidence timestamp, not carried by the catalog.",

    };

    [Fact]
    public void Every_column_is_either_carried_by_the_catalog_or_deliberately_left_out()
    {
        using var db = _fixture.CreateContext();

        var actual = db.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties()
                .Select(property => $"{type.GetTableName()}.{property.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var classified = Carried.Concat(NotCarried.Keys).ToHashSet(StringComparer.Ordinal);

        var unclassified = actual.Except(classified, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "These columns exist and nobody has decided whether the catalog carries them:\n  "
            + string.Join("\n  ", unclassified)
            + "\n\nAdd each to Carried, or to NotCarried with the sentence explaining why losing it "
            + "is fine. A column nobody decided about is one that gets silently dropped on every "
            + "round trip, and the loss shows up long after the change that caused it.");

        var stale = classified.Except(actual, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These columns are named in the coverage lists but no longer exist:\n  "
            + string.Join("\n  ", stale)
            + "\n\nRemove them. A list that names things which are gone is one nobody is "
            + "maintaining, which is worse than having no list.");
    }

    /// <summary>
    /// There are no intentionally uncarried authored fields. A new one must either become a real
    /// catalog field or be documented with a reason it is safe to lose.
    /// </summary>
    [Fact]
    public void There_are_no_remaining_authored_catalog_gaps()
    {
        var gaps = NotCarried
            .Where(entry => entry.Value.StartsWith("GAP:", StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(gaps);

    }

    private List<string> TableNames()
    {
        using var db = _fixture.CreateContext();
        var connection = db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "select name from sqlite_master where type = 'table' order by name";

        var names = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
