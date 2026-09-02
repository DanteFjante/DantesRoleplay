using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

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
    /// <summary>
    /// Runs one of the preserved implementation handlers behind the public query/commit adapter.
    /// The handler still owns its behaviour and transaction boundaries; this scope only gives its
    /// existing audit wrapper the public protocol identity — the tool name history records, the
    /// subject when the outcome does not name one, and whether the call spends read evidence.
    ///
    /// It deliberately does NOT rewrite the handlers' literal recovery calls. An earlier version
    /// did, by prefix substitution, and turned a `write_procedure` next-step carrying `id:` and
    /// the rest of its named arguments into `commit(kind: "procedure", id: "x", ...)` — a call with
    /// no `payload` argument, which the protocol rejects. The handlers now write the public call
    /// form themselves, which is the only version that can be checked by reading it.
    /// </summary>
    public static IDisposable EnterProtocol(
        string tool,
        string kind,
        bool? consumesReadEvidenceOverride = null) =>
        new DispatchScope(
            ToolRunnerDispatch.Value,
            new ProtocolDispatch(tool, kind, consumesReadEvidenceOverride));

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
            var dispatch = ToolRunnerDispatch.Value;

            var effectiveTool = dispatch?.Tool ?? tool;
            var effectiveSubject = string.IsNullOrEmpty(outcome.Subject) ? subject : outcome.Subject;

            if (dispatch is not null && string.IsNullOrEmpty(effectiveSubject))
            {
                effectiveSubject = $"{dispatch.Tool}:{dispatch.Kind}";
            }

            var operation = await log.RecordAsync(
                effectiveTool,
                outcome.Summary,
                outcome.Error is null,
                intent,
                // An outcome may name its own subject — get_procedure records which contract it
                // fetched, which is what makes the observed-procedures derivation possible.
                effectiveSubject,
                proceduresCited,
                outcome.Error?.Code ?? string.Empty,
                dispatch?.ConsumesReadEvidenceOverride ?? consumesReadEvidence,
                guardEvidenceJson: outcome.GuardEvidenceJson);

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
            var dispatch = ToolRunnerDispatch.Value;
            var effectiveTool = dispatch?.Tool ?? tool;

            // A rejection thrown from deep inside a store still carries a stable code. Passing it
            // through is the difference between "NAMESPACE_UNKNOWN: record 'x' uses unregistered
            // namespace 'y'" and a caller spending an afternoon ruling out payload size.
            var code = ex is CatalogNamespaceException typed ? typed.Code : "UNHANDLED";
            var fix = ex is CatalogNamespaceException
                // The contract, not the capability catalog: capabilities lists what the verbs take,
                // and the caller's problem here is where an identity is allowed to live.
                ? "query(kind: \"procedures\", id: \"procedure.system.namespace\") — where an id may live, and how to open somewhere new."
                : "Call orient() to re-check the system state, then retry with corrected arguments.";

            // Recording is attempted, not assumed.
            //
            // The store and the audit log share one DbContext. When a write is rejected during
            // SaveChanges, its entities stay tracked as Added, so the audit row's own SaveChanges
            // retries the rejected write and throws again — and that second throw escaped this
            // method entirely, which is how a typed refusal reached a caller as an unstructured
            // tool-invocation error with no audit row at all. The caller gets a typed answer
            // either way now; losing the audit row is bad, losing the error as well is worse.
            var operationId = string.Empty;
            try
            {
                var operation = await log.RecordAsync(
                    effectiveTool,
                    $"Unhandled failure: {ex.Message}",
                    success: false,
                    intent,
                    dispatch is null || !string.IsNullOrEmpty(subject)
                        ? subject
                        : $"{dispatch.Tool}:{dispatch.Kind}",
                    proceduresCited,
                    code,
                    consumesReadEvidence);
                operationId = operation.Id;
            }
            catch
            {
                // Deliberately swallowed: the failure being reported is the one worth returning.
            }

            return ToolEnvelope.Failure(code, ex.Message, fix, operationId);
        }
    }
}

/// <summary>
/// Which public verb and kind the current call is being served as. Nothing more: the mapping from
/// old tool name to new call form lives in `VERB_HISTORY.md` for reading old audit rows, not in
/// running code.
/// </summary>
internal sealed record ProtocolDispatch(
    string Tool,
    string Kind,
    bool? ConsumesReadEvidenceOverride);

internal sealed class DispatchScope : IDisposable
{
    private readonly ProtocolDispatch? _previous;
    private bool _disposed;

    public DispatchScope(ProtocolDispatch? previous, ProtocolDispatch current)
    {
        _previous = previous;
        ToolRunnerDispatch.Set(current);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ToolRunnerDispatch.Set(_previous);
    }
}

internal static class ToolRunnerDispatch
{
    private static readonly AsyncLocal<ProtocolDispatch?> Current = new();

    public static void Set(ProtocolDispatch? dispatch) => Current.Value = dispatch;

    public static ProtocolDispatch? Value => Current.Value;
}

internal sealed record ToolOutcome(
    object? Data,
    string Summary,
    IReadOnlyList<string> NextSteps,
    ToolError? Error = null,
    string Subject = "",
    string GuardEvidenceJson = "")
{
    public static ToolOutcome Ok(object? data, string summary, params string[] nextSteps) =>
        new(data, summary, nextSteps);

    public static ToolOutcome OkAbout(string subject, object? data, string summary, params string[] nextSteps) =>
        new(data, summary, nextSteps, null, subject);

    public static ToolOutcome Fail(string code, string why, string fix, string summary) =>
        new(null, summary, [fix], new ToolError(code, why, fix));
}
