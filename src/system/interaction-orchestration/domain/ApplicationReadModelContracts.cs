using DantesRoleplay.Applications;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Interactions;

public sealed record ApplicationReadModelRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string QualifiedQueryId,
    IReadOnlyDictionary<string, string> RoleBindings,
    MechanicAudienceContext? Audience = null,
    string InputJson = "{}");

public sealed record ApplicationReadModelResult(
    string ApplicationId,
    string StateSpaceId,
    string QualifiedQueryId,
    string StateSpaceFingerprint,
    string ResolutionFingerprint,
    string OutputSchemaHash,
    string ResultFingerprint,
    string SourceRevisionFingerprint,
    string DataJson);

public sealed class ApplicationReadModelException(
    string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Executes one registered application-owned read model against an exact state-space binding.
/// Rules and projection shaping remain in catalog JavaScript; this host only resolves, sandboxes,
/// validates, fingerprints, and returns the closed result.
/// </summary>
public interface IApplicationReadModelService
{
    Task<ApplicationReadModelResult> ReadAsync(
        ApplicationReadModelRequest request,
        CancellationToken cancellationToken = default);
}
