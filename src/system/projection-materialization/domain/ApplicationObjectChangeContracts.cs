namespace DantesRoleplay.Projections;

/// <summary>Public, ruleset-neutral contract for durable committed-object invalidation.</summary>
public static class ApplicationObjectChangeContract
{
    public const int Version = 1;
    public const string ProfileId = "object-change/v1";
    public const string ObjectScope = "object";
    public const string ApplicationScope = "application";
    public const string NoChangeScope = "none";
    public const int MaximumReplayRows = 256;
    public const int RetainedRows = 10_000;
}

public sealed record ApplicationObjectChange(
    long Cursor,
    int ContractVersion,
    string ApplicationId,
    string StateSpaceId,
    string Scope,
    string? ObjectQualifiedId,
    int? ObjectVersion,
    IReadOnlyList<string> ReadPerspectives,
    string Reason,
    DateTime CreatedAtUtc);
