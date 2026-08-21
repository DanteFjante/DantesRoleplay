namespace DantesRoleplay.Snapshots;

/// <summary>
/// Generic immutable persistence record for one producer-owned package. It contains no game
/// vocabulary: scope and producer identifiers preserve the domain provenance without making the
/// database model decide what any payload means.
/// </summary>
public sealed class SnapshotPackage
{
    public required string Id { get; set; }
    public required string ScopeContractId { get; set; }
    public int ScopeContractVersion { get; set; }
    public required string ProducerId { get; set; }
    public int ProducerVersion { get; set; }
    public required string ContentEncoding { get; set; }
    public required string BoundaryFingerprint { get; set; }
    public required string DigestAlgorithm { get; set; }
    public required string ContentDigest { get; set; }
    public long ByteCount { get; set; }
    public DateTime CapturedAt { get; set; }
    public required string RootOperationId { get; set; }
    public required string Availability { get; set; }
    public required byte[] Content { get; set; }
}
