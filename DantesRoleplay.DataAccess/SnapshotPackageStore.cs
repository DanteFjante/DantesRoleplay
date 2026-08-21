using System.Security.Cryptography;
using DantesRoleplay.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Stages and verifies immutable opaque packages in the transaction owned by a domain coordinator.
/// The store never exposes or interprets package content.
/// </summary>
public sealed class SnapshotPackageStore(DantesRoleplayDbContext db) : ISnapshotPackageStore
{
    private const int MaximumBytes = 1_048_576;
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<SnapshotPackageStageResult> StageAsync(SnapshotCaptureProposal proposal, string rootOperationId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is null)
            return Failure("SNAPSHOT_TRANSACTION_REQUIRED", "rootOperationId", "Snapshot staging must join an owning root transaction.", "Begin the approved checkpoint root before staging a package.");
        if (proposal is null)
            return Failure("INVALID_SNAPSHOT_PROPOSAL", "proposal", "A typed snapshot proposal is required.", "Request capture through the registered scope producer.");
        if (!OperationId(rootOperationId))
            return Failure("INVALID_ROOT_OPERATION_ID", "rootOperationId", "rootOperationId must be a canonical operation id.", "Allocate one root operation id before staging a package.");

        var content = proposal.Content.ToArray();
        if (!Id(proposal.ScopeContractId) || proposal.ScopeContractVersion < 1 || !Id(proposal.ProducerId) || proposal.ProducerVersion < 1 || proposal.ContentEncoding != "dantes-canonical-json-v1" || !Digest(proposal.BoundaryFingerprint) || content.Length is < 1 or > MaximumBytes)
            return Failure("INVALID_SNAPSHOT_PROPOSAL", "proposal", "Snapshot proposal metadata or content does not meet the closed SP1 contract.", "Use the registered v1 producer without altering its proposal.");

        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var capturedAt = DateTime.UtcNow;
        var row = new SnapshotPackage
        {
            Id = "snapshot." + Guid.NewGuid().ToString("n"),
            ScopeContractId = proposal.ScopeContractId,
            ScopeContractVersion = proposal.ScopeContractVersion,
            ProducerId = proposal.ProducerId,
            ProducerVersion = proposal.ProducerVersion,
            ContentEncoding = proposal.ContentEncoding,
            BoundaryFingerprint = proposal.BoundaryFingerprint,
            DigestAlgorithm = "sha256",
            ContentDigest = digest,
            ByteCount = content.LongLength,
            CapturedAt = capturedAt,
            RootOperationId = rootOperationId,
            Availability = "available",
            Content = content
        };

        try
        {
            _db.SnapshotPackages.Add(row);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _db.Entry(row).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            throw;
        }

        // SQLite normalizes DateTime precision on persistence. Return the stored representation so
        // the reference issued here is exactly comparable to a later immutable read.
        var persisted = await _db.SnapshotPackages.AsNoTracking()
            .SingleAsync(package => package.Id == row.Id, cancellationToken);
        return new("staged", Reference(persisted), []);
    }

    public async Task<SnapshotPackageVerificationResult> VerifyAsync(SnapshotPackageReference expected, CancellationToken cancellationToken = default)
    {
        if (!ReferenceShape(expected))
            return VerificationFailure("INVALID_SNAPSHOT_REFERENCE", "reference", "Snapshot verification requires one complete canonical byte-free reference.", "Use the reference returned by successful staging.");

        var row = await _db.SnapshotPackages.AsNoTracking().SingleOrDefaultAsync(package => package.Id == expected.Id, cancellationToken);
        if (row is null)
            return VerificationFailure("SNAPSHOT_NOT_FOUND", "reference.id", "The named snapshot package does not exist.", "Read the checkpoint that returned the package reference again.");
        if (row.Availability != "available")
            return VerificationFailure("SNAPSHOT_UNAVAILABLE", "reference.id", "The snapshot package is not available.", "Use an available package or inspect its owning checkpoint.");
        if (!ReferenceEquals(row, expected))
            return VerificationFailure("SNAPSHOT_REFERENCE_MISMATCH", "reference", "Stored snapshot provenance does not match the expected reference.", "Use the exact unmodified reference returned by the owning coordinator.");

        var actualDigest = SHA256.HashData(row.Content);
        if (row.Content.LongLength != row.ByteCount || !Digest(row.ContentDigest) || !CryptographicOperations.FixedTimeEquals(actualDigest, Convert.FromHexString(row.ContentDigest)))
            return VerificationFailure("SNAPSHOT_CORRUPT", "reference.id", "Stored snapshot content failed integrity verification.", "Do not substitute current state; inspect the owning checkpoint evidence.");

        return new("verified", Reference(row), []);
    }

    private static SnapshotPackageStageResult Failure(string code, string path, string reason, string recovery) =>
        new("rejected", null, [new SnapshotPackageProblem(code, path, reason, recovery)]);

    private static SnapshotPackageVerificationResult VerificationFailure(string code, string path, string reason, string recovery) =>
        new("unavailable", null, [new SnapshotPackageProblem(code, path, reason, recovery)]);

    private static SnapshotPackageReference Reference(SnapshotPackage row) => new(
        row.Id, row.ScopeContractId, row.ScopeContractVersion, row.ProducerId, row.ProducerVersion,
        row.ContentEncoding, row.BoundaryFingerprint, row.DigestAlgorithm, row.ContentDigest,
        row.ByteCount, row.CapturedAt, row.Availability);

    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.Contains('.', StringComparison.Ordinal) && !value.Any(char.IsWhiteSpace);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool OperationId(string? value) => value is { Length: 32 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ReferenceShape(SnapshotPackageReference? value) => value is not null
        && value.Id is { Length: 41 } id && id.StartsWith("snapshot.", StringComparison.Ordinal) && id[9..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
        && Id(value.ScopeContractId) && value.ScopeContractVersion > 0 && Id(value.ProducerId) && value.ProducerVersion > 0
        && value.ContentEncoding == "dantes-canonical-json-v1" && Digest(value.BoundaryFingerprint)
        && value.DigestAlgorithm == "sha256" && Digest(value.ContentDigest)
        && value.ByteCount is > 0 and <= MaximumBytes && value.CapturedAt != default && value.Availability == "available";
    private static bool ReferenceEquals(SnapshotPackage row, SnapshotPackageReference expected) =>
        row.ScopeContractId == expected.ScopeContractId
        && row.ScopeContractVersion == expected.ScopeContractVersion
        && row.ProducerId == expected.ProducerId
        && row.ProducerVersion == expected.ProducerVersion
        && row.ContentEncoding == expected.ContentEncoding
        && row.BoundaryFingerprint == expected.BoundaryFingerprint
        && row.DigestAlgorithm == expected.DigestAlgorithm
        && FixedDigestEquals(row.ContentDigest, expected.ContentDigest)
        && row.ByteCount == expected.ByteCount
        && row.CapturedAt == expected.CapturedAt
        && row.Availability == expected.Availability;
    private static bool FixedDigestEquals(string left, string right) => Digest(left) && Digest(right) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
}
