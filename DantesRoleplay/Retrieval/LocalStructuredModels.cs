namespace DantesRoleplay.Retrieval;

public enum LocalModelPriority
{
    Interactive,
    Background
}

public sealed record LocalModelIdentity(
    string Provider,
    string Model,
    string Revision,
    string Profile = "standard");

public sealed record LocalModelStatus(
    bool Ready,
    LocalModelIdentity? Identity,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public static LocalModelStatus Unavailable(string code, string message) =>
        new(false, null, code, message);
}

public sealed record StructuredCompletionRequest(
    string TaskClass,
    string SystemPrompt,
    string UserPrompt,
    string ResponseSchema,
    LocalModelPriority Priority = LocalModelPriority.Interactive);

public sealed record StructuredCompletionResult(
    LocalModelIdentity? Identity,
    string Json,
    long ElapsedMilliseconds,
    int PromptTokens = 0,
    int OutputTokens = 0,
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Ok => Identity is not null && ErrorCode.Length == 0;

    public static StructuredCompletionResult Failure(
        string code,
        string message,
        long elapsedMilliseconds = 0) =>
        new(null, "", elapsedMilliseconds, ErrorCode: code, ErrorMessage: message);
}

/// <summary>Host-only schema-bound completion. It exposes no model tool definitions.</summary>
public interface ILocalStructuredCompletionProvider
{
    Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default);

    Task<StructuredCompletionResult> CompleteAsync(
        StructuredCompletionRequest request,
        CancellationToken cancellationToken = default);
}
