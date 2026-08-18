namespace DantesRoleplay.Operations;

/// <summary>
/// Records what the agent did. Every MCP tool call goes through here — reads included, because
/// which contracts were consulted before a change is half the story, and because the read
/// entries are what make the observed-procedures column possible.
/// </summary>
public interface IOperationLog
{
    /// <summary>
    /// Writes one operation. <paramref name="proceduresCited"/> is the caller's own claim; when
    /// <paramref name="consumesReadEvidence"/> is set the implementation also derives what was
    /// actually read from earlier log entries, and that evidence is then spent.
    ///
    /// Read-only tools must leave it false. A tool that records evidence also CONSUMES it, so an
    /// incidental `history` call would otherwise spend the reads a pending write was about to
    /// account for.
    /// </summary>
    Task<Operation> RecordAsync(
        string tool,
        string summary,
        bool success,
        string intent = "",
        string subject = "",
        IEnumerable<string>? proceduresCited = null,
        string error = "",
        bool consumesReadEvidence = false,
        CancellationToken cancellationToken = default,
        string mechanicId = "",
        int? mechanicVersion = null,
        long? seed = null,
        string projectionJson = "");

    Task<IReadOnlyList<Operation>> RecentAsync(
        int limit = 20,
        bool failuresOnly = false,
        string? tool = null,
        string? subject = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Contract ids demonstrably fetched via get_procedure within the recent window. Public so a
    /// dry run can tell the caller what the audit trail is about to say about it.
    /// </summary>
    Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(
        CancellationToken cancellationToken = default);
}
