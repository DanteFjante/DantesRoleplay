using DantesRoleplay.Interactions;
using System.Text.Json.Serialization;

namespace DantesRoleplay.Information;

// Coordinator review proposal. Separate request types prevent today's store from silently ignoring
// an expected revision. Nothing implements or registers this interface before storage integration.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InformationSourceConditionalWriteRequest(
    [property: JsonRequired] InformationSourceWriteRequest Value, [property: JsonRequired] int ExpectedRevision,
    string? QualifiedTargetId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InformationRecordConditionalWriteRequest(
    [property: JsonRequired] InformationRecordWriteRequest Value, [property: JsonRequired] int ExpectedRevision);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InformationRecordRevisionReadRequest(
    [property: JsonRequired] string RecordId, int? Revision = null);

/// <summary>
/// Mode is registered or inline for exact retained bindings. The corresponding *-current-unpinned
/// modes expose only the live row after validating it against the current source schema; they do
/// not assert which schema revision originally accepted legacy content.
/// </summary>
public sealed record InformationMetadataSchemaBinding(
    string Mode, int SourceRevision, string ProfileId, string SchemaJson, string SchemaHash,
    Ecs.EcsComponentReference? RegisteredSchema);

public sealed record InformationRecordRevisionView(
    string Id, int Revision, string SourceId, string Title, string Content, string MetadataJson,
    string ContentHash, InformationMetadataSchemaBinding MetadataSchema);

public sealed record InformationRecordRevisionReadResult(
    string Status, InformationRecordRevisionView? Record, string ErrorCode = "", string ErrorMessage = "");

/// <summary>Explicit installation-operator adoption or movement; never inferred from a source's textual scope.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InformationSourceOwnershipWriteRequest(
    string SourceId, Applications.ApplicationIdentifier NewApplicationId, string QualifiedTargetId,
    [property: JsonRequired] int ExpectedOwnershipRevision, [property: JsonRequired] string? ExpectedOwnershipFingerprint);

public interface IInformationSourceOwnershipAdministration
{
    Task<InteractionInvocationResult> BindAsync(InteractionInvocationHost host,
        InformationSourceOwnershipWriteRequest request, CancellationToken cancellationToken = default);
}

public interface IConditionalInformationStore
{
    /// <summary>
    /// ExpectedRevision is required: zero means absent, positive means that exact existing revision;
    /// negative is invalid. There is no null/unconditional variant. Under the SQLite writer lock,
    /// first reconcile the host command's canonical payload, then compare/write, retain the old and
    /// new exact content and record the receipt in the existing operation log in one transaction.
    /// Same command/payload replays its receipt; another command with a stale expectation conflicts
    /// even when its desired content matches. Existing unchecked methods do not implement this ABI.
    /// The source namespace must have a verified owner/scope mapping before a grant can authorize
    /// it; absent mapping returns unavailable rather than treating an app grant as global authority.
    /// </summary>
    Task<InteractionInvocationResult> WriteSourceAsync(InteractionInvocationHost host,
        InformationSourceConditionalWriteRequest request, CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> WriteRecordAsync(InteractionInvocationHost host,
        InformationRecordConditionalWriteRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads a current or retained record only after current Read authority and exact schema verification.</summary>
    Task<InformationRecordRevisionReadResult> ReadRecordRevisionAsync(InteractionInvocationHost host,
        InformationRecordRevisionReadRequest request, CancellationToken cancellationToken = default);
}
