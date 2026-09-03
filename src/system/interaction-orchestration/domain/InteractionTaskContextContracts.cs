using DantesRoleplay.Applications;

namespace DantesRoleplay.Interactions;

public static class InteractionTaskContextProfiles
{
    public const string Version1 = "interaction-task-context/v1";
}

/// <summary>
/// One bounded, immutable context snapshot assembled for an already-authorized interaction.
/// Every value in Json carries its own reference, revision, and fingerprint; the pack fingerprint
/// binds the complete ordered snapshot used by the planner.
/// </summary>
public sealed record InteractionTaskContextPack(
    string Profile,
    string Json,
    string Fingerprint,
    IReadOnlyList<string> SourceReferences);

public interface IInteractionTaskContextMaterializer
{
    Task<InteractionTaskContextPack> MaterializeAsync(
        AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken = default);
}

/// <summary>Receipt plus the continuity and catalog revision under which it was recorded.</summary>
public sealed record InteractionReceiptContext(
    string Reference,
    string SessionContextId,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string StateRevision,
    string EffectiveSetFingerprint,
    string AuthorizationEvidenceReference,
    InteractionReceiptProjection Receipt);

public interface IInteractionRecentReceiptReader
{
    Task<IReadOnlyList<InteractionReceiptContext>> ReadRecentAsync(
        InteractionAuthorizationRequest authorizationRequest,
        string sessionContextId,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class InteractionTaskContextException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
