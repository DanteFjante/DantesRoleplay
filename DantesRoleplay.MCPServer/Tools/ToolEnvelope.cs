using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The shape every tool returns, success or failure.
///
/// ARCHITECTURE.md §7.3: uniformity means the model learns the shape once. §7.4: a failure is an
/// instruction, not a complaint — <see cref="ToolError.Fix"/> names the exact next call, so a
/// model that gets something wrong recovers by itself instead of giving up or inventing.
/// </summary>
public sealed record ToolEnvelope
{
    public required bool Ok { get; init; }

    public object? Data { get; init; }

    /// <summary>
    /// Literal calls that make sense from here. Not prose — the model should be able to pick one
    /// and run it. This is what keeps a low-context session moving without a system prompt.
    /// </summary>
    public IReadOnlyList<string> NextSteps { get; init; } = [];

    /// <summary>Id of the audit row this call produced. Quote it when reporting what you did.</summary>
    public string OperationId { get; init; } = string.Empty;

    public ToolError? Error { get; init; }

    public static ToolEnvelope Success(object? data, string operationId, params string[] nextSteps) =>
        new() { Ok = true, Data = data, OperationId = operationId, NextSteps = nextSteps };

    public static ToolEnvelope Failure(string code, string why, string fix, string operationId = "") =>
        new()
        {
            Ok = false,
            OperationId = operationId,
            Error = new ToolError(code, why, fix),
            NextSteps = [fix]
        };
}

/// <param name="Code">Stable, machine-readable, e.g. UNKNOWN_PROCEDURE.</param>
/// <param name="Why">What went wrong, in one sentence.</param>
/// <param name="Fix">The next call to make. A concrete invocation, never advice.</param>
public sealed record ToolError(string Code, string Why, string Fix);

/// <summary>
/// Wraps a tool body so that every call — including one that throws — lands in the audit log and
/// comes back in the standard envelope.
///
/// §P3 says record everything, and the only way that stays true is if recording is not something
/// each tool has to remember to do.
/// </summary>
internal static class ToolRunner
{
    /// <summary>For read-only tools. Records the call but does not consume read evidence.</summary>
    public static Task<ToolEnvelope> RunAsync(
        IOperationLog log,
        string tool,
        Func<Task<ToolOutcome>> body) =>
        RunAsync(log, tool, string.Empty, string.Empty, null, body, consumesReadEvidence: false);

    /// <summary>
    /// For tools that change something. <paramref name="consumesReadEvidence"/> defaults true:
    /// the operation records which procedures were demonstrably read beforehand, and spends them.
    /// </summary>
    public static async Task<ToolEnvelope> RunAsync(
        IOperationLog log,
        string tool,
        string intent,
        string subject,
        IEnumerable<string>? proceduresCited,
        Func<Task<ToolOutcome>> body,
        bool consumesReadEvidence = true)
    {
        try
        {
            var outcome = await body();

            var operation = await log.RecordAsync(
                tool,
                outcome.Summary,
                outcome.Error is null,
                intent,
                // An outcome may name its own subject — get_procedure records which contract it
                // fetched, which is what makes the observed-procedures derivation possible.
                string.IsNullOrEmpty(outcome.Subject) ? subject : outcome.Subject,
                proceduresCited,
                outcome.Error?.Code ?? string.Empty,
                consumesReadEvidence);

            return outcome.Error is null
                ? ToolEnvelope.Success(outcome.Data, operation.Id, [.. outcome.NextSteps])
                : ToolEnvelope.Failure(
                    outcome.Error.Code,
                    outcome.Error.Why,
                    outcome.Error.Fix,
                    operation.Id);
        }
        catch (Exception ex)
        {
            // An unhandled exception is still an event worth recording, and the model still needs
            // somewhere to go next.
            var operation = await log.RecordAsync(
                tool,
                $"Unhandled failure: {ex.Message}",
                success: false,
                intent,
                subject,
                proceduresCited,
                "UNHANDLED",
                consumesReadEvidence);

            return ToolEnvelope.Failure(
                "UNHANDLED",
                ex.Message,
                "Call orient() to re-check the system state, then retry with corrected arguments.",
                operation.Id);
        }
    }
}

internal sealed record ToolOutcome(
    object? Data,
    string Summary,
    IReadOnlyList<string> NextSteps,
    ToolError? Error = null,
    string Subject = "")
{
    public static ToolOutcome Ok(object? data, string summary, params string[] nextSteps) =>
        new(data, summary, nextSteps);

    public static ToolOutcome OkAbout(string subject, object? data, string summary, params string[] nextSteps) =>
        new(data, summary, nextSteps, null, subject);

    public static ToolOutcome Fail(string code, string why, string fix, string summary) =>
        new(null, summary, [fix], new ToolError(code, why, fix));
}
