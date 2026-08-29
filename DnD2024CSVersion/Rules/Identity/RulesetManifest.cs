namespace DnD2024CSVersion.Rules.Identity;

/// <summary>
/// The complete identity, version, source, and licensing envelope for one ruleset release.
/// </summary>
public sealed record RulesetManifest
{
    public required RulesetIdentity Identity { get; init; }
    public required RulesetVersion Version { get; init; }
    public IReadOnlyList<RulesetSourceReference> Sources { get; init; } = [];
    public IReadOnlyList<RulesetLicense> Licenses { get; init; } = [];
}
