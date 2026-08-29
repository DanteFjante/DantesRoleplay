namespace DantesRoleplay.Snapshots;

/// <summary>
/// A closed, producer-created package waiting to be staged by the snapshot store. The content is
/// copied on construction so a producer cannot alter the bytes after validation.
/// </summary>
public sealed class SnapshotCaptureProposal
{
    private readonly byte[] _content;

    public SnapshotCaptureProposal(
        string scopeContractId,
        int scopeContractVersion,
        string producerId,
        int producerVersion,
        string contentEncoding,
        string boundaryFingerprint,
        ReadOnlySpan<byte> content)
    {
        ScopeContractId = scopeContractId;
        ScopeContractVersion = scopeContractVersion;
        ProducerId = producerId;
        ProducerVersion = producerVersion;
        ContentEncoding = contentEncoding;
        BoundaryFingerprint = boundaryFingerprint;
        _content = content.ToArray();
    }

    public string ScopeContractId { get; }
    public int ScopeContractVersion { get; }
    public string ProducerId { get; }
    public int ProducerVersion { get; }
    public string ContentEncoding { get; }
    public string BoundaryFingerprint { get; }
    public ReadOnlyMemory<byte> Content => _content;
}

/// <summary>Byte-free durable identity and provenance returned by the snapshot package store.</summary>
public sealed record SnapshotPackageReference(
    string Id,
    string ScopeContractId,
    int ScopeContractVersion,
    string ProducerId,
    int ProducerVersion,
    string ContentEncoding,
    string BoundaryFingerprint,
    string DigestAlgorithm,
    string ContentDigest,
    long ByteCount,
    DateTime CapturedAt,
    string Availability);

public sealed record SnapshotPackageProblem(string Code, string Path, string Reason, string Recovery);

public sealed record SnapshotPackageStageResult(
    string Status,
    SnapshotPackageReference? Reference,
    IReadOnlyList<SnapshotPackageProblem> Problems)
{
    public bool Staged => Status == "staged";
}

public sealed record SnapshotPackageVerificationResult(
    string Status,
    SnapshotPackageReference? Reference,
    IReadOnlyList<SnapshotPackageProblem> Problems)
{
    public bool Verified => Status == "verified";
}

/// <summary>
/// Generic snapshot storage deliberately accepts only a typed in-process proposal and returns no
/// content. No MCP handler receives this interface.
/// </summary>
public interface ISnapshotPackageStore
{
    Task<SnapshotPackageStageResult> StageAsync(
        SnapshotCaptureProposal proposal,
        string rootOperationId,
        CancellationToken cancellationToken = default);

    Task<SnapshotPackageVerificationResult> VerifyAsync(
        SnapshotPackageReference expected,
        CancellationToken cancellationToken = default);
}
