using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using System.Text.Json.Serialization;

namespace DantesRoleplay.Interactions;

public sealed record ApplicationReadModelRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string QualifiedQueryId,
    IReadOnlyDictionary<string, string> RoleBindings,
    MechanicAudienceContext? Audience = null,
    string InputJson = "{}",
    string? Cursor = null,
    int? PageSize = null)
{
    /// <summary>Host-only AI context enrichment; never populated from public query input.</summary>
    [JsonIgnore]
    public bool IncludeObjectReadEvidence { get; init; }

    /// <summary>Host-only planned-query mode; keeps object materialization exact rather than display-shaped.</summary>
    [JsonIgnore]
    public bool ExactObjectRead { get; init; }

    /// <summary>Host-only exact query authority carried by a previously verified interaction plan.</summary>
    [JsonIgnore]
    public InteractionQueryContractReference? ExpectedContract { get; init; }
}

public sealed record ApplicationReadModelResult(
    string ApplicationId,
    string StateSpaceId,
    string QualifiedQueryId,
    string StateSpaceFingerprint,
    string ResolutionFingerprint,
    string OutputSchemaHash,
    string ResultFingerprint,
    string SourceRevisionFingerprint,
    string DataJson)
{
    [JsonIgnore]
    public ApplicationObjectReadEvidence? ObjectReadEvidence { get; init; }
}

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

/// <summary>Trusted, transport-neutral inputs available while resolving declared query roles.</summary>
public sealed record ApplicationQueryRoleBindingContext(
    string RouteEntityId,
    IReadOnlyDictionary<string, string> AuthorizedRoleEntityIds);

public interface IApplicationQueryRoleBindingResolver
{
    IReadOnlyDictionary<string, string> Resolve(
        ApplicationQueryContract contract,
        string inputJson,
        ApplicationQueryRoleBindingContext context);
}

/// <summary>
/// Supplies host-authorized opaque role/entity bindings. Implementations must bind an exact
/// application state space and must never populate this map from caller query input.
/// </summary>
public interface IApplicationQueryAuthorizedContextProvider
{
    Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        CancellationToken cancellationToken = default);
}
