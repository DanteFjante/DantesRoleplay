using DantesRoleplay.Database;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Operations;

/// <summary>
/// Records what the agent did. Every MCP tool call goes through here.
///
/// ARCHITECTURE.md §P3 puts this at priority 3 for a reason: TravelRoleplay produced execution
/// traces and threw them away, so "why did that happen?" only ever worked for the action you had
/// just run. Recording is cheap; retrofitting it is not.
/// </summary>
public sealed class OperationLog(DantesRoleplayDbContext db)
{
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<Operation> RecordAsync(
        string tool,
        string summary,
        bool success,
        string intent = "",
        IEnumerable<string>? proceduresUsed = null,
        string error = "",
        CancellationToken cancellationToken = default)
    {
        var operation = new Operation
        {
            Id = Guid.NewGuid().ToString("n"),
            Timestamp = DateTimeOffset.UtcNow,
            Tool = tool,
            Intent = intent,
            ProceduresUsed = proceduresUsed is null ? string.Empty : string.Join(",", proceduresUsed),
            Summary = summary,
            Success = success,
            Error = error
        };

        _db.Operations.Add(operation);
        await _db.SaveChangesAsync(cancellationToken);

        return operation;
    }

    /// <summary>Most recent operations, newest first.</summary>
    public async Task<IReadOnlyList<Operation>> RecentAsync(
        int limit = 20,
        bool failuresOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Operations.AsNoTracking();

        if (failuresOnly)
        {
            query = query.Where(o => !o.Success);
        }

        return await query
            .OrderByDescending(o => o.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
