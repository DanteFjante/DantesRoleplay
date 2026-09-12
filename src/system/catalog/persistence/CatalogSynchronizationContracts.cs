using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.DataAccess.Catalog;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CatalogSynchronizationRecordSelection(CatalogRecordKind Kind, string Id);

/// <summary>
/// Compares a bounded reviewed record set under one host-configured source root. It never exports,
/// imports, resolves a conflict, or changes authored catalog/database content.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CatalogSynchronizationCompareRequest(
    string AllowedRootId,
    [property: JsonRequired] string? ExpectedActiveFingerprint,
    [property: JsonRequired] IReadOnlyList<CatalogSynchronizationRecordSelection> Records);

public interface ICatalogSynchronizationService
{
    Task<InteractionInvocationResult> CompareAsync(InteractionInvocationHost host,
        CatalogSynchronizationCompareRequest request, CancellationToken cancellationToken = default);
}

internal sealed record CatalogSynchronizationRecordEvidence(
    CatalogRecordKind Kind, string Id, CatalogChange Change, string? FileFingerprint,
    string? DatabaseFingerprint, string? AncestorFingerprint, IReadOnlyList<string> RelativePaths);

internal sealed record CatalogSynchronizationPlanEvidence(
    string AllowedRootId, string? ManifestFingerprint, bool Complete,
    IReadOnlyList<CatalogSynchronizationRecordEvidence> Records);

public interface IApplicationCatalogSynchronizationEvidenceReader
{
    Task<string?> ValidateCandidateAsync(InteractionInvocationHost host,
        ApplicationCandidateWriteRequest request, CancellationToken cancellationToken = default);
}
