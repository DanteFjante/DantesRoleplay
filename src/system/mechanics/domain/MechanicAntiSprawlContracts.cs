namespace DantesRoleplay.Mechanics;

/// <summary>Exact authored mechanic identity retained in anti-sprawl review evidence.</summary>
public sealed record MechanicAntiSprawlEndpoint(string QualifiedId, string ContentFingerprint);

/// <summary>
/// One explainable deterministic conflict or advisory similarity candidate. Classification and
/// reasons are strings so protocol consumers can display new reason details without owning the
/// comparison algorithm.
/// </summary>
public sealed record MechanicAntiSprawlFinding(
    string Code,
    string Classification,
    bool Blocking,
    MechanicAntiSprawlEndpoint Left,
    MechanicAntiSprawlEndpoint Right,
    IReadOnlyList<string> Reasons,
    double Similarity,
    string ReviewState,
    string? Disposition,
    string Summary);
