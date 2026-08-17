using System.ComponentModel;
using DantesRoleplay.Operations;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The audit trail, readable from inside the system.
///
/// Not only for the human in the control room: an agent picking up work mid-thread can read the
/// last few operations and reconstruct where things stand, which is usually cheaper and more
/// reliable than inferring it from current state.
/// </summary>
[McpServerToolType]
public sealed class HistoryTool
{
    [McpServerTool(Name = "history")]
    [Description(
        "Recent operations against this system: what was called, on what, what it was trying to " +
        "achieve, which procedures were cited AND which were demonstrably read, and whether it " +
        "worked. Filter by tool or subject to answer questions like 'has anyone written this " +
        "contract before?'. Read this when diagnosing, or when you need to know what happened " +
        "before you arrived.")]
    public async Task<ToolEnvelope> HistoryAsync(
        IOperationLog log,
        [Description("How many to return, newest first. Default 20, max 200.")] int limit = 20,
        [Description("Only operations that failed. Useful when something is not working.")]
        bool failuresOnly = false,
        [Description("Only this tool, e.g. \"write_procedure\".")] string? tool = null,
        [Description("Only operations acting on this id, e.g. a contract id.")] string? subject = null,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "history", async () =>
        {
            var clamped = Math.Clamp(limit, 1, 200);

            var operations = await log.RecentAsync(clamped, failuresOnly, tool, subject, cancellationToken);

            var view = operations.Select(o => new
            {
                o.Id,
                o.Timestamp,
                o.Tool,
                o.Subject,
                o.Intent,
                Cited = Split(o.ProceduresCited),
                Read = Split(o.ProceduresRead),
                o.ConsumedReadEvidence,
                o.Summary,
                o.Success,
                o.Error
            }).ToList();

            if (view.Count == 0)
            {
                return ToolOutcome.Ok(
                    new { Operations = view },
                    "No matching operations.",
                    failuresOnly || tool is not null || subject is not null
                        ? "history() — drop the filters to see everything."
                        : "orient() — nothing has happened yet; start there.");
            }

            var failures = view.Count(o => !o.Success);

            // A citation with no matching read is the one discrepancy worth surfacing unprompted:
            // it means an agent claimed a procedure it never opened.
            //
            // Only operations that CONSUMED evidence can be judged this way. A dry run cites
            // without consuming, and counting it produced a false accusation against an agent
            // that had read the manual properly moments earlier.
            var unbacked = view.Count(o =>
                o.ConsumedReadEvidence && o.Cited.Except(o.Read, StringComparer.Ordinal).Any());

            var notes = new List<string>();

            if (failures > 0 && !failuresOnly)
            {
                notes.Add("history(failuresOnly: true) — look at just the failures.");
            }

            if (unbacked > 0)
            {
                notes.Add($"{unbacked} operation(s) cited a procedure that was not read in the preceding window.");
            }

            notes.Add("orient() — check the current state of the system.");

            return ToolOutcome.Ok(
                new { Operations = view, CitedWithoutReading = unbacked },
                $"Returned {view.Count} operation(s), {failures} failed.",
                [.. notes]);
        });

    private static string[] Split(string value) =>
        string.IsNullOrEmpty(value) ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
