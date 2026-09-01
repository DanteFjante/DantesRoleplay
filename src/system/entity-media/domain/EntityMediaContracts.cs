using DantesRoleplay.Applications;
using DantesRoleplay.Blobs;

namespace DantesRoleplay.Media;

public enum EntityMediaAudience { Player, GameMaster }

public sealed record EntityMediaAudienceContext(
    EntityMediaAudience Audience,
    string? ActorId = null);

/// <summary>
/// Supplies the host-authorized audience for direct in-process AI media reads. The model never
/// selects its own audience or actor identity.
/// </summary>
public interface IEntityMediaAudienceResolver
{
    EntityMediaAudienceContext? Resolve(ApplicationIdentifier applicationId);
}

public sealed record EntityMediaProvenance(
    string Kind, string Credit, string Source, string ReviewedOn, int Version);

public sealed record EntityMediaAttachment(
    string MediaId,
    string Role,
    IReadOnlyList<EntityMediaAudience> Visibility,
    string Sha256,
    string MediaType,
    int Width,
    int Height,
    string Alt,
    string Caption,
    int Order,
    EntityMediaProvenance Provenance);

public sealed record EntityMediaDiagnostic(
    string Code, string Message, string ComponentTypeId = "", string MediaId = "");

public sealed record EntityMediaDiscoveryResult(
    string ApplicationId,
    string StateSpaceId,
    string EntityId,
    string ResolutionFingerprint,
    IReadOnlyList<EntityMediaAttachment> Attachments,
    IReadOnlyList<EntityMediaDiagnostic> Diagnostics);

public sealed record EntityMediaReadResult(
    EntityMediaAttachment Attachment,
    BlobReadResult Blob) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Blob.DisposeAsync();
}

public interface IEntityMediaService
{
    Task<EntityMediaDiscoveryResult> DiscoverAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string entityId,
        EntityMediaAudience audience,
        bool diagnostics = false,
        CancellationToken cancellationToken = default);

    Task<EntityMediaReadResult?> OpenReadAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string entityId,
        string mediaId,
        EntityMediaAudience audience,
        CancellationToken cancellationToken = default);
}

public sealed class EntityMediaException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
